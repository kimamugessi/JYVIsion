using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms.PropertyGridInternal;
using MvCameraControl;
using OpenCvSharp.Flann;
using WeifenLuo.WinFormsUI.Docking;

namespace JYVision.Grab
{
    internal class HikRobotCam : GrabModel
    {      
        private IDevice _device = null;

        void FrameGrabedEventHandler(object sender, FrameGrabbedEventArgs e)
        {
            Console.WriteLine("Get one frame : Width[{0}],Height[{1}],ImageSize[{2}],FramNum[{3}]", e.FrameOut.Image.Width, e.FrameOut.Image.Height, e.FrameOut.Image.ImageSize, e.FrameOut.FrameNum);

            IFrameOut frameOut = e.FrameOut;
            OnGrabCompleted(BufferIndex);

            if (_userImageBuffer[BufferIndex].ImageBuffer != null) 
            {
                if (frameOut.Image.PixelType == MvGvspPixelType.PixelType_Gvsp_Mono8)
                {
                    if (_userImageBuffer[BufferIndex].ImageBuffer != null)
                    {
                        IntPtr ptrSourceTemp = frameOut.Image.PixelDataPtr;
                        Marshal.Copy(ptrSourceTemp, _userImageBuffer[BufferIndex].ImageBuffer, 0, (int)frameOut.Image.ImageSize);
                    }
                }
                else
                {
                    IImage inputImage = frameOut.Image;
                    IImage outImage;
                    MvGvspPixelType dstPixelType = MvGvspPixelType.PixelType_Gvsp_BGR8_Packed;

                    int result = _device.PixelTypeConverter.ConvertPixelType(inputImage, out outImage, dstPixelType);
                    if (result != MvError.MV_OK)
                    {
                        Console.WriteLine("Image Convert failed:{0:x8}", result);
                        return;
                    }

                    if (_userImageBuffer[BufferIndex].ImageBuffer != null)
                    {
                        IntPtr ptrSourceTemp = outImage.PixelDataPtr;
                        Marshal.Copy(ptrSourceTemp, _userImageBuffer[BufferIndex].ImageBuffer, 0, (int)outImage.ImageSize);
                    }
                }
            }

            OnTransferCompleted(BufferIndex);

            if (IncreaseBufferIndex)
            {
                BufferIndex++;
                if (BufferIndex >= _userImageBuffer.Count()) BufferIndex = 0;
            }
        }


        #region Method

        internal override bool Create(string strIpAddr = null)
        {
            SDKSystem.Initialize();

            _strIpAddr = strIpAddr;

            try
            {
                const DeviceTLayerType devLayerType = DeviceTLayerType.MvGigEDevice;
                
                List<IDeviceInfo> devInfoList;

                // Enum device
                int ret = DeviceEnumerator.EnumDevices(devLayerType, out devInfoList);
                if (ret != MvError.MV_OK)
                {
                    Console.WriteLine("Enum device failed:{0:x8}", ret);
                    return false;
                }

                Console.WriteLine("Enum device count : {0}", devInfoList.Count);

                if (0 == devInfoList.Count)
                {
                    return false;
                }

                int selDevIndex = -1;

                int devIndex = 0;
                foreach (var devInfo in devInfoList)
                {
                    Console.WriteLine("[Device {0}]:", devIndex);
                    if (devInfo.TLayerType == DeviceTLayerType.MvGigEDevice || devInfo.TLayerType == DeviceTLayerType.MvVirGigEDevice || devInfo.TLayerType == DeviceTLayerType.MvGenTLGigEDevice)
                    {
                        IGigEDeviceInfo gigeDevInfo = devInfo as IGigEDeviceInfo;
                        uint nIp1 = ((gigeDevInfo.CurrentIp & 0xff000000) >> 24);
                        uint nIp2 = ((gigeDevInfo.CurrentIp & 0x00ff0000) >> 16);
                        uint nIp3 = ((gigeDevInfo.CurrentIp & 0x0000ff00) >> 8);
                        uint nIp4 = (gigeDevInfo.CurrentIp & 0x000000ff);

                        string strIP = nIp1 + "." + nIp2 + "." + nIp3 + "." + nIp4;
                        Console.WriteLine("DevIP" + strIP);

                        if (_strIpAddr == null || strIP == strIpAddr)
                        {
                            selDevIndex = devIndex;
                            break;
                        }
                    }

                    Console.WriteLine("ModelName:" + devInfo.ModelName);
                    Console.WriteLine("SerialNumber:" + devInfo.SerialNumber);
                    Console.WriteLine();
                    devIndex++;
                }

                if (selDevIndex < 0 || selDevIndex > devInfoList.Count - 1)
                {
                    Console.WriteLine("Invalid selected device number:{0}", selDevIndex);
                    return false;
                }

                _device = DeviceFactory.CreateDevice(devInfoList[selDevIndex]);

                _disposed = false;
            }
            catch (Exception ex)
            {
                ex.ToString();
                return false;
            }
            return true;
        }

        internal override bool Grab(int bufferIndex, bool waitDone)
        {
            if (_device == null) return false;
            BufferIndex = bufferIndex;
            bool ret = true;

            if (!HardwareTrigger)
            {
                try
                {
                    int result = _device.Parameters.SetCommandValue("TriggerSoftware");
                    if (result != MvError.MV_OK) ret = false;
                }
                catch { ret = false; }
            }

            return ret;
        }

        internal override bool Close()
        {
            if (_device != null)
            {
                _device.StreamGrabber.StopGrabbing();
                _device.Close();
            }

            return true;
        }

        internal override bool Open()
        {
            try
            {
                if (_device == null) return false;
                if (!_device.IsConnected)
                {
                    int ret = _device.Open();
                    if (MvError.MV_OK != ret)
                    {
                        _device.Dispose();
                        Console.WriteLine("Device open fail!", ret);
                        return false;
                    }

                    if (_device is IGigEDevice)
                    {
                        int packetSize;
                        ret = (_device as IGigEDevice).GetOptimalPacketSize(out packetSize);
                        if (packetSize > 0)
                        {
                            ret = _device.Parameters.SetIntValue("GevSCPSPacketSize", packetSize);
                            if (ret != MvError.MV_OK)
                            {
                                Console.WriteLine("Warning: Set Packet Size failed {0:x8}", ret);
                            }
                            else
                            {
                                Console.WriteLine("Set PacketSize to {0}", packetSize);
                            }
                        }
                        else
                        {
                            Console.WriteLine("Set PacketSize to {0}", packetSize);
                        }
                    }
                    ret = _device.Parameters.SetEnumValue("TriggerMode", 1);
                    if (ret != MvError.MV_OK)
                    {
                        Console.WriteLine("Set TriggerMode failed:{0:x8}", ret);
                        return false;
                    }

                    if (HardwareTrigger)
                    {
                        _device.Parameters.SetEnumValueByString("TriggerSource", "Line0");                        
                    }
                    else
                    {
                        _device.Parameters.SetEnumValueByString("TriggerSource", "Software");                        
                    }
                    _device.StreamGrabber.FrameGrabedEvent += FrameGrabedEventHandler;

                    ret = _device.StreamGrabber.StartGrabbing();
                    if (ret != MvError.MV_OK)
                    {
                        Console.WriteLine("Start grabbing failed:{0:x8}", ret);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return false;
            }

            return true;
        }

        internal override bool Reconnect()
        {
            if (_device == null)
            {
                Console.WriteLine("_camera is null");
                return false;
            }
            Close();
            return Open();
        }
        
        internal override bool GetPixelBpp(out int pixelBpp) //카메라의 정보
        {
            pixelBpp = 8;
            if(_device== null) return false;

            IEnumValue enumValue;
            int result = _device.Parameters.GetEnumValue("PixelFormat", out enumValue);
            if (result != MvError.MV_OK)
            {
                Console.WriteLine("Get PixelFormat failed: nRet {0:x8}", result);
                return false;
            }
            if (MvGvspPixelType.PixelType_Gvsp_Mono8 == (MvGvspPixelType)enumValue.CurEnumEntry.Value) pixelBpp = 8;
            else pixelBpp = 24;
            return true;
        }
        #endregion


        #region Parameter Setting
        internal override bool SetExposureTime(long exposure)
        {
            if (_device == null) return false;

            _device.Parameters.SetEnumValue("ExposureAuto", 0);
            int result = _device.Parameters.SetFloatValue("ExposureTime", exposure);
            if (result != MvError.MV_OK)
            {
                Console.WriteLine("Set Exposure Time Fail!", result);
                return false;
            }

            return true;
        }

        internal override bool GetExposureTime(out long exposure)
        {
            exposure = 0;
            if (_device == null)
                return false;

            IFloatValue floatValue;
            int result = _device.Parameters.GetFloatValue("ExposureTime", out floatValue);
            if (result == MvError.MV_OK)
            {
                exposure = (long)floatValue.CurValue;
            }

            return true;
        }

        internal override bool SetGain(float gain)
        {
            if (_device == null)
                return false;

            _device.Parameters.SetEnumValue("GainAuto", 0);
            int result = _device.Parameters.SetFloatValue("Gain", gain);
            if (result != MvError.MV_OK)
            {
                Console.WriteLine("Set Gain Time Fail!", result);
                return false;
            }

            return true;
        }

        internal override bool GetGain(out float gain)
        {
            gain = 0;
            if (_device == null)
                return false;

            IFloatValue floatValue;
            int result = _device.Parameters.GetFloatValue("Gain", out floatValue);
            if (result == MvError.MV_OK)
            {
                gain = (long)floatValue.CurValue;
            }

            return true;
        }

        internal override bool GetResolution(out int width, out int height, out int stride)  //카메라 해상도
        {
            width = 0;
            height = 0;
            stride = 0;

            if (_device == null)
                return false;

            IIntValue intValue;
            IEnumValue enumValue;
            MvGvspPixelType pixelType;

            int result;

            result = _device.Parameters.GetIntValue("Width", out intValue);
            if (result != MvError.MV_OK)
            {
                Console.WriteLine("Get Width failed: nRet {0:x8}", result);
                return false;
            }
            width = (int)intValue.CurValue;

            result = _device.Parameters.GetIntValue("Height", out intValue);
            if (result != MvError.MV_OK)
            {
                Console.WriteLine("Get Height failed: nRet {0:x8}", result);
                return false;
            }
            height = (int)intValue.CurValue;

            result = _device.Parameters.GetEnumValue("PixelFormat", out enumValue);
            if (result != MvError.MV_OK)
            {
                Console.WriteLine("Get PixelFormat failed: nRet {0:x8}", result);
                return false;
            }
            pixelType = (MvGvspPixelType)enumValue.CurEnumEntry.Value;

            if (pixelType == MvGvspPixelType.PixelType_Gvsp_Mono8)
                stride = width * 1;
            else
                stride = width * 3;

            return true;
        }

        internal override bool SetTriggerMode(bool hardwareTrigger)
        {
            if (_device == null)
                return false;

            HardwareTrigger = hardwareTrigger;

            if (HardwareTrigger)
            {
                _device.Parameters.SetEnumValueByString("TriggerSource", "Line0");
            }
            else
            {
                _device.Parameters.SetEnumValueByString("TriggerSource", "Software");
            }

            return true;
        }

        #endregion

        #region Dispose

        private bool _disposed = false;

        protected void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                if(_device != null)
                {
                    _device.StreamGrabber.FrameGrabedEvent -= FrameGrabedEventHandler;
                    _device.StreamGrabber.StopGrabbing();
                    _device.Close();
                    _device.Dispose();
                    _device = null;

                    // Finalize SDK
                    SDKSystem.Finalize();
                }
            }
            _disposed = true;
        }

        internal override void Dispose()
        {
            Dispose(disposing: true);
        }
        #endregion //Disposable
    }
}
