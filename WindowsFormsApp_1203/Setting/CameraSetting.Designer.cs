namespace JYVision4.Setting
{
    partial class CameraSetting
    {
        /// <summary> 
        /// 필수 디자이너 변수입니다.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// 사용 중인 모든 리소스를 정리합니다.
        /// </summary>
        /// <param name="disposing">관리되는 리소스를 삭제해야 하면 true이고, 그렇지 않으면 false입니다.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region 구성 요소 디자이너에서 생성한 코드

        /// <summary> 
        /// 디자이너 지원에 필요한 메서드입니다. 
        /// 이 메서드의 내용을 코드 편집기로 수정하지 마세요.
        /// </summary>
        private void InitializeComponent()
        {
            this.btnApply = new System.Windows.Forms.Button();
            this.cbCameraType = new System.Windows.Forms.ComboBox();
            this.lbCameraType = new System.Windows.Forms.Label();
            this.lbExposure = new System.Windows.Forms.Label();
            this.tbExposure = new System.Windows.Forms.TextBox();
            this.lbExpUnit = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // btnApply
            // 
            this.btnApply.Font = new System.Drawing.Font("Segoe UI", 9.163636F);
            this.btnApply.Location = new System.Drawing.Point(146, 73);
            this.btnApply.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.btnApply.Name = "btnApply";
            this.btnApply.Size = new System.Drawing.Size(104, 30);
            this.btnApply.TabIndex = 5;
            this.btnApply.Text = "적용";
            this.btnApply.UseVisualStyleBackColor = true;
            this.btnApply.Click += new System.EventHandler(this.btnApply_Click);
            // 
            // cbCameraType
            // 
            this.cbCameraType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cbCameraType.Font = new System.Drawing.Font("Segoe UI", 9.163636F);
            this.cbCameraType.FormattingEnabled = true;
            this.cbCameraType.Location = new System.Drawing.Point(89, 7);
            this.cbCameraType.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.cbCameraType.Name = "cbCameraType";
            this.cbCameraType.Size = new System.Drawing.Size(161, 27);
            this.cbCameraType.TabIndex = 4;
            // 
            // lbCameraType
            // 
            this.lbCameraType.AutoSize = true;
            this.lbCameraType.Font = new System.Drawing.Font("Segoe UI", 9.163636F);
            this.lbCameraType.Location = new System.Drawing.Point(3, 10);
            this.lbCameraType.Name = "lbCameraType";
            this.lbCameraType.Size = new System.Drawing.Size(83, 19);
            this.lbCameraType.TabIndex = 3;
            this.lbCameraType.Text = "카메라 종료";
            // 
            // lbExposure
            // 
            this.lbExposure.AutoSize = true;
            this.lbExposure.Font = new System.Drawing.Font("Segoe UI", 9.163636F);
            this.lbExposure.Location = new System.Drawing.Point(3, 44);
            this.lbExposure.Name = "lbExposure";
            this.lbExposure.Size = new System.Drawing.Size(65, 19);
            this.lbExposure.TabIndex = 3;
            this.lbExposure.Text = "노출시간";
            // 
            // tbExposure
            // 
            this.tbExposure.Font = new System.Drawing.Font("Segoe UI", 9.163636F);
            this.tbExposure.Location = new System.Drawing.Point(88, 41);
            this.tbExposure.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.tbExposure.Name = "tbExposure";
            this.tbExposure.Size = new System.Drawing.Size(122, 26);
            this.tbExposure.TabIndex = 6;
            // 
            // lbExpUnit
            // 
            this.lbExpUnit.AutoSize = true;
            this.lbExpUnit.Font = new System.Drawing.Font("Segoe UI", 9.163636F);
            this.lbExpUnit.Location = new System.Drawing.Point(217, 45);
            this.lbExpUnit.Name = "lbExpUnit";
            this.lbExpUnit.Size = new System.Drawing.Size(27, 19);
            this.lbExpUnit.TabIndex = 3;
            this.lbExpUnit.Text = "ms";
            // 
            // CameraSetting
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.tbExposure);
            this.Controls.Add(this.btnApply);
            this.Controls.Add(this.cbCameraType);
            this.Controls.Add(this.lbExpUnit);
            this.Controls.Add(this.lbExposure);
            this.Controls.Add(this.lbCameraType);
            this.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.Name = "CameraSetting";
            this.Size = new System.Drawing.Size(283, 144);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btnApply;
        private System.Windows.Forms.ComboBox cbCameraType;
        private System.Windows.Forms.Label lbCameraType;
        private System.Windows.Forms.Label lbExposure;
        private System.Windows.Forms.TextBox tbExposure;
        private System.Windows.Forms.Label lbExpUnit;
    }
}
