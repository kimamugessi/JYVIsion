using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using JYVision.Util;
using WeifenLuo.WinFormsUI.Docking;

namespace JYVision
{
    //===== 시스템 로그 출력 및 실시간 모니터링 폼 =====
    public partial class LogForm : DockContent
    {
        //===== [그룹 1] 초기화 =====

        //------- 폼 생성자 및 로그 수신 이벤트 연결 -------
        public LogForm()
        {
            InitializeComponent();

            // 폼 종료 시 메모리 누수 방지를 위한 이벤트 등록
            this.FormClosed += LogForm_FormClosed;

            // 정적 로거 클래스의 로그 업데이트 이벤트 구독
            SLogger.LogUpdated += OnLogUpdated;
        }

        //===== [그룹 2] 로그 수신 및 UI 제어 =====

        //------- 로그 이벤트 발생 시 UI 스레드 동기화 처리 (Cross-Thread 방지) -------
        private void OnLogUpdated(string logMessage)
        {
            // 백그라운드 스레드에서 호출될 경우 Invoke를 통해 UI 스레드로 마샬링
            if (listBoxLogs.InvokeRequired)
            {
                listBoxLogs.Invoke(new Action(() => AddLog(logMessage)));
            }
            else
            {
                AddLog(logMessage);
            }
        }

        //------- 실제 리스트박스에 로그 메시지 추가 및 개수 관리 -------
        private void AddLog(string logMessage)
        {
            // 메모리 과부하 방지: 로그가 1000개 이상 쌓이면 가장 오래된 로그 삭제
            if (listBoxLogs.Items.Count > 1000)
            {
                listBoxLogs.Items.RemoveAt(0);
            }

            listBoxLogs.Items.Add(logMessage);

            // 자동 스크롤: 항상 가장 최신 로그(마지막 인덱스)를 화면에 표시
            listBoxLogs.TopIndex = listBoxLogs.Items.Count - 1;
        }

        //===== [그룹 3] 리소스 해제 =====

        //------- 폼이 닫힐 때 전역 이벤트 구독 해제 (Memory Leak 방지) -------
        private void LogForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            // 구독을 해제하지 않으면 로거가 살아있는 동안 폼 객체가 메모리에서 해제되지 않음
            SLogger.LogUpdated -= OnLogUpdated;
            this.FormClosed -= LogForm_FormClosed;
        }
    }
}