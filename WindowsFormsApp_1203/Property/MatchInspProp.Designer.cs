namespace JYVision.Property
{
    partial class MatchInspProp
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
                txtExtendX.Leave -= OnUpdateValue;
                txtExtendY.Leave -= OnUpdateValue;
                txtScore.Leave -= OnUpdateValue;

                patternImageEditor.ButtonChanged -= PatternImage_ButtonChanged;

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
            this.grpMatch = new System.Windows.Forms.GroupBox();
            this.chkInvertResult = new System.Windows.Forms.CheckBox();
            this.lbScore = new System.Windows.Forms.Label();
            this.txtExtendY = new System.Windows.Forms.TextBox();
            this.txtScore = new System.Windows.Forms.TextBox();
            this.txtExtendX = new System.Windows.Forms.TextBox();
            this.lbX = new System.Windows.Forms.Label();
            this.lbExtent = new System.Windows.Forms.Label();
            this.chkUse = new System.Windows.Forms.CheckBox();
            this.patternImageEditor = new JYVision.UIControl.PatternImageEditor();
            this.grpMatch.SuspendLayout();
            this.SuspendLayout();
            // 
            // grpMatch
            // 
            this.grpMatch.Controls.Add(this.chkInvertResult);
            this.grpMatch.Controls.Add(this.lbScore);
            this.grpMatch.Controls.Add(this.txtExtendY);
            this.grpMatch.Controls.Add(this.txtScore);
            this.grpMatch.Controls.Add(this.txtExtendX);
            this.grpMatch.Controls.Add(this.lbX);
            this.grpMatch.Controls.Add(this.lbExtent);
            this.grpMatch.Location = new System.Drawing.Point(7, 41);
            this.grpMatch.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.grpMatch.Name = "grpMatch";
            this.grpMatch.Padding = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.grpMatch.Size = new System.Drawing.Size(269, 120);
            this.grpMatch.TabIndex = 5;
            this.grpMatch.TabStop = false;
            // 
            // chkInvertResult
            // 
            this.chkInvertResult.AutoSize = true;
            this.chkInvertResult.Font = new System.Drawing.Font("Segoe UI", 9.163636F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.chkInvertResult.Location = new System.Drawing.Point(15, 92);
            this.chkInvertResult.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.chkInvertResult.Name = "chkInvertResult";
            this.chkInvertResult.Size = new System.Drawing.Size(88, 23);
            this.chkInvertResult.TabIndex = 3;
            this.chkInvertResult.Text = "결과 반전";
            this.chkInvertResult.UseVisualStyleBackColor = true;
            this.chkInvertResult.CheckedChanged += new System.EventHandler(this.chkInvertResult_CheckedChanged);
            // 
            // lbScore
            // 
            this.lbScore.AutoSize = true;
            this.lbScore.Font = new System.Drawing.Font("Segoe UI", 9.163636F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lbScore.Location = new System.Drawing.Point(7, 58);
            this.lbScore.Name = "lbScore";
            this.lbScore.Size = new System.Drawing.Size(79, 19);
            this.lbScore.TabIndex = 2;
            this.lbScore.Text = "매칭스코어";
            // 
            // txtExtendY
            // 
            this.txtExtendY.Font = new System.Drawing.Font("Segoe UI", 9.163636F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.txtExtendY.Location = new System.Drawing.Point(183, 20);
            this.txtExtendY.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.txtExtendY.Name = "txtExtendY";
            this.txtExtendY.Size = new System.Drawing.Size(57, 26);
            this.txtExtendY.TabIndex = 1;
            // 
            // txtScore
            // 
            this.txtScore.Font = new System.Drawing.Font("Segoe UI", 9.163636F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.txtScore.Location = new System.Drawing.Point(98, 55);
            this.txtScore.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.txtScore.Name = "txtScore";
            this.txtScore.Size = new System.Drawing.Size(57, 26);
            this.txtScore.TabIndex = 1;
            // 
            // txtExtendX
            // 
            this.txtExtendX.Font = new System.Drawing.Font("Segoe UI", 9.163636F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.txtExtendX.Location = new System.Drawing.Point(98, 20);
            this.txtExtendX.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.txtExtendX.Name = "txtExtendX";
            this.txtExtendX.Size = new System.Drawing.Size(57, 26);
            this.txtExtendX.TabIndex = 1;
            // 
            // lbX
            // 
            this.lbX.AutoSize = true;
            this.lbX.Font = new System.Drawing.Font("Segoe UI", 9.163636F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lbX.Location = new System.Drawing.Point(162, 27);
            this.lbX.Name = "lbX";
            this.lbX.Size = new System.Drawing.Size(15, 19);
            this.lbX.TabIndex = 0;
            this.lbX.Text = "x";
            // 
            // lbExtent
            // 
            this.lbExtent.AutoSize = true;
            this.lbExtent.Font = new System.Drawing.Font("Segoe UI", 9.163636F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lbExtent.Location = new System.Drawing.Point(7, 30);
            this.lbExtent.Name = "lbExtent";
            this.lbExtent.Size = new System.Drawing.Size(65, 19);
            this.lbExtent.TabIndex = 0;
            this.lbExtent.Text = "확장영역";
            // 
            // chkUse
            // 
            this.chkUse.AutoSize = true;
            this.chkUse.Checked = true;
            this.chkUse.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkUse.Font = new System.Drawing.Font("Segoe UI", 9.163636F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.chkUse.Location = new System.Drawing.Point(16, 15);
            this.chkUse.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.chkUse.Name = "chkUse";
            this.chkUse.Size = new System.Drawing.Size(56, 23);
            this.chkUse.TabIndex = 6;
            this.chkUse.Text = "검사";
            this.chkUse.UseVisualStyleBackColor = true;
            this.chkUse.CheckedChanged += new System.EventHandler(this.chkUse_CheckedChanged);
            // 
            // patternImageEditor
            // 
            this.patternImageEditor.Font = new System.Drawing.Font("굴림", 9.163636F);
            this.patternImageEditor.Location = new System.Drawing.Point(7, 170);
            this.patternImageEditor.Margin = new System.Windows.Forms.Padding(3, 5, 3, 5);
            this.patternImageEditor.Name = "patternImageEditor";
            this.patternImageEditor.Size = new System.Drawing.Size(269, 149);
            this.patternImageEditor.TabIndex = 4;
            // 
            // MatchInspProp
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.patternImageEditor);
            this.Controls.Add(this.grpMatch);
            this.Controls.Add(this.chkUse);
            this.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.Name = "MatchInspProp";
            this.Size = new System.Drawing.Size(357, 368);
            this.grpMatch.ResumeLayout(false);
            this.grpMatch.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.GroupBox grpMatch;
        private System.Windows.Forms.CheckBox chkInvertResult;
        private System.Windows.Forms.Label lbScore;
        private System.Windows.Forms.TextBox txtExtendY;
        private System.Windows.Forms.TextBox txtScore;
        private System.Windows.Forms.TextBox txtExtendX;
        private System.Windows.Forms.Label lbX;
        private System.Windows.Forms.Label lbExtent;
        private System.Windows.Forms.CheckBox chkUse;
        private UIControl.PatternImageEditor patternImageEditor;
    }
}
