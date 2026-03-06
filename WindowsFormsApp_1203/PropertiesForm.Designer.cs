namespace JYVision
{
    partial class PropertiesForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(PropertiesForm));
            this.tabPropControl = new System.Windows.Forms.TabControl();
            this.SuspendLayout();
            // 
            // tabPropControl
            // 
            this.tabPropControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabPropControl.Location = new System.Drawing.Point(0, 0);
            this.tabPropControl.Name = "tabPropControl";
            this.tabPropControl.SelectedIndex = 0;
            this.tabPropControl.Size = new System.Drawing.Size(789, 482);
            this.tabPropControl.TabIndex = 0;
            // 
            // PropertiesForm
            // 
            this.ClientSize = new System.Drawing.Size(789, 482);
            this.Controls.Add(this.tabPropControl);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "PropertiesForm";
            this.Text = "Properties";
            this.ResumeLayout(false);

        }

        private System.Windows.Forms.TabControl tabPropControl;
    }
}