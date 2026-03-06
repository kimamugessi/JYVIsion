namespace JYVision.Property
{
    partial class xylophone
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
            this.btnBoltROI = new System.Windows.Forms.Button();
            this.cbKeybord = new System.Windows.Forms.CheckBox();
            this.cbBolt = new System.Windows.Forms.CheckBox();
            this.cbMark = new System.Windows.Forms.CheckBox();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.backgroundWorker1 = new System.ComponentModel.BackgroundWorker();
            this.groupBox1.SuspendLayout();
            this.SuspendLayout();
            // 
            // btnBoltROI
            // 
            this.btnBoltROI.Location = new System.Drawing.Point(16, 25);
            this.btnBoltROI.Name = "btnBoltROI";
            this.btnBoltROI.Size = new System.Drawing.Size(57, 22);
            this.btnBoltROI.TabIndex = 1;
            this.btnBoltROI.Text = "검사";
            this.btnBoltROI.UseVisualStyleBackColor = true;
            this.btnBoltROI.Click += new System.EventHandler(this.btnBoltROI_Click);
            // 
            // cbKeybord
            // 
            this.cbKeybord.AutoSize = true;
            this.cbKeybord.Enabled = false;
            this.cbKeybord.Location = new System.Drawing.Point(20, 29);
            this.cbKeybord.Name = "cbKeybord";
            this.cbKeybord.Size = new System.Drawing.Size(79, 18);
            this.cbKeybord.TabIndex = 3;
            this.cbKeybord.Text = "Keybord";
            this.cbKeybord.UseVisualStyleBackColor = true;
            // 
            // cbBolt
            // 
            this.cbBolt.AutoSize = true;
            this.cbBolt.Enabled = false;
            this.cbBolt.Location = new System.Drawing.Point(20, 53);
            this.cbBolt.Name = "cbBolt";
            this.cbBolt.Size = new System.Drawing.Size(52, 18);
            this.cbBolt.TabIndex = 3;
            this.cbBolt.Text = "Bolt";
            this.cbBolt.UseVisualStyleBackColor = true;
            // 
            // cbMark
            // 
            this.cbMark.AutoSize = true;
            this.cbMark.Enabled = false;
            this.cbMark.Location = new System.Drawing.Point(20, 77);
            this.cbMark.Name = "cbMark";
            this.cbMark.Size = new System.Drawing.Size(57, 18);
            this.cbMark.TabIndex = 3;
            this.cbMark.Text = "Mark";
            this.cbMark.UseVisualStyleBackColor = true;
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.cbBolt);
            this.groupBox1.Controls.Add(this.cbMark);
            this.groupBox1.Controls.Add(this.cbKeybord);
            this.groupBox1.Location = new System.Drawing.Point(16, 63);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(178, 120);
            this.groupBox1.TabIndex = 4;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "ROI";
            // 
            // xylophone
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.btnBoltROI);
            this.Name = "xylophone";
            this.Size = new System.Drawing.Size(264, 416);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.Button btnBoltROI;
        private System.Windows.Forms.CheckBox cbKeybord;
        private System.Windows.Forms.CheckBox cbBolt;
        private System.Windows.Forms.CheckBox cbMark;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.ComponentModel.BackgroundWorker backgroundWorker1;
    }
}
