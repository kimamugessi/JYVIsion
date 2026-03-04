namespace JYVision.Property
{
    partial class BoltROIProp
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
            this.btnSetROI = new System.Windows.Forms.Button();
            this.btnBoltROI = new System.Windows.Forms.Button();
            this.btnCheckMark = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // btnSetROI
            // 
            this.btnSetROI.Location = new System.Drawing.Point(16, 16);
            this.btnSetROI.Name = "btnSetROI";
            this.btnSetROI.Size = new System.Drawing.Size(106, 22);
            this.btnSetROI.TabIndex = 1;
            this.btnSetROI.Text = "Matching";
            this.btnSetROI.UseVisualStyleBackColor = true;
            this.btnSetROI.Click += new System.EventHandler(this.btnKeyMatch_Click);
            // 
            // btnBoltROI
            // 
            this.btnBoltROI.Location = new System.Drawing.Point(16, 47);
            this.btnBoltROI.Name = "btnBoltROI";
            this.btnBoltROI.Size = new System.Drawing.Size(106, 22);
            this.btnBoltROI.TabIndex = 1;
            this.btnBoltROI.Text = "Bolt ROI";
            this.btnBoltROI.UseVisualStyleBackColor = true;
            this.btnBoltROI.Click += new System.EventHandler(this.btnBoltROI_Click);
            // 
            // btnCheckMark
            // 
            this.btnCheckMark.Location = new System.Drawing.Point(16, 75);
            this.btnCheckMark.Name = "btnCheckMark";
            this.btnCheckMark.Size = new System.Drawing.Size(106, 22);
            this.btnCheckMark.TabIndex = 1;
            this.btnCheckMark.Text = "Check Mark";
            this.btnCheckMark.UseVisualStyleBackColor = true;
            this.btnCheckMark.Click += new System.EventHandler(this.btnCheckMark_Click);
            // 
            // BoltROIProp
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.btnBoltROI);
            this.Controls.Add(this.btnCheckMark);
            this.Controls.Add(this.btnSetROI);
            this.Name = "BoltROIProp";
            this.Size = new System.Drawing.Size(264, 416);
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.Button btnSetROI;
        private System.Windows.Forms.Button btnBoltROI;
        private System.Windows.Forms.Button btnCheckMark;
    }
}
