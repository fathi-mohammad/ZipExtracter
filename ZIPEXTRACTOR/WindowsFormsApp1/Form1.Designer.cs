namespace WindowsFormsApp1
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.Button btnSelectFolder;
        private System.Windows.Forms.Button btnExtract;
        private System.Windows.Forms.ListBox lstZips;
        private System.Windows.Forms.ListBox lstFiles;
        private System.Windows.Forms.Label lblFolder;
        private System.Windows.Forms.ProgressBar progressBar1;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.btnSelectFolder = new System.Windows.Forms.Button();
            this.lstZips = new System.Windows.Forms.ListBox();
            this.btnExtract = new System.Windows.Forms.Button();
            this.lstFiles = new System.Windows.Forms.ListBox();
            this.lblFolder = new System.Windows.Forms.Label();
            this.progressBar1 = new System.Windows.Forms.ProgressBar();
            this.OutputFolder = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.OutputLabel = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // btnSelectFolder
            // 
            this.btnSelectFolder.Location = new System.Drawing.Point(12, 12);
            this.btnSelectFolder.Name = "btnSelectFolder";
            this.btnSelectFolder.Size = new System.Drawing.Size(130, 30);
            this.btnSelectFolder.TabIndex = 0;
            this.btnSelectFolder.Text = "Select Folder";
            this.btnSelectFolder.UseVisualStyleBackColor = true;
            this.btnSelectFolder.Click += new System.EventHandler(this.btnSelectFolder_Click);
            // 
            // lstZips
            // 
            this.lstZips.FormattingEnabled = true;
            this.lstZips.Location = new System.Drawing.Point(12, 101);
            this.lstZips.Name = "lstZips";
            this.lstZips.Size = new System.Drawing.Size(620, 82);
            this.lstZips.TabIndex = 1;
            this.lstZips.SelectedIndexChanged += new System.EventHandler(this.lstZips_SelectedIndexChanged);
            // 
            // btnExtract
            // 
            this.btnExtract.Location = new System.Drawing.Point(12, 188);
            this.btnExtract.Name = "btnExtract";
            this.btnExtract.Size = new System.Drawing.Size(180, 30);
            this.btnExtract.TabIndex = 2;
            this.btnExtract.Text = "Extract Selected Zip";
            this.btnExtract.UseVisualStyleBackColor = true;
            this.btnExtract.Click += new System.EventHandler(this.btnExtract_Click);
            // 
            // lstFiles
            // 
            this.lstFiles.FormattingEnabled = true;
            this.lstFiles.Location = new System.Drawing.Point(12, 258);
            this.lstFiles.Name = "lstFiles";
            this.lstFiles.Size = new System.Drawing.Size(620, 251);
            this.lstFiles.TabIndex = 3;
            // 
            // lblFolder
            // 
            this.lblFolder.AutoSize = true;
            this.lblFolder.Location = new System.Drawing.Point(160, 20);
            this.lblFolder.Name = "lblFolder";
            this.lblFolder.Size = new System.Drawing.Size(96, 13);
            this.lblFolder.TabIndex = 4;
            this.lblFolder.Text = "No folder selected.";
            // 
            // progressBar1
            // 
            this.progressBar1.Location = new System.Drawing.Point(12, 224);
            this.progressBar1.Name = "progressBar1";
            this.progressBar1.Size = new System.Drawing.Size(620, 15);
            this.progressBar1.TabIndex = 5;
            // 
            // OutputFolder
            // 
            this.OutputFolder.Location = new System.Drawing.Point(12, 65);
            this.OutputFolder.Name = "OutputFolder";
            this.OutputFolder.Size = new System.Drawing.Size(130, 30);
            this.OutputFolder.TabIndex = 6;
            this.OutputFolder.Text = "Outout Folder";
            this.OutputFolder.UseVisualStyleBackColor = true;
            this.OutputFolder.Click += new System.EventHandler(this.OutputFolder_Click);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(530, 19);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(0, 13);
            this.label1.TabIndex = 7;
            // 
            // OutputLabel
            // 
            this.OutputLabel.AutoSize = true;
            this.OutputLabel.Location = new System.Drawing.Point(160, 65);
            this.OutputLabel.Name = "OutputLabel";
            this.OutputLabel.Size = new System.Drawing.Size(98, 13);
            this.OutputLabel.TabIndex = 8;
            this.OutputLabel.Text = "No Folder Selected";
            // 
            // Form1
            // 
            this.ClientSize = new System.Drawing.Size(650, 515);
            this.Controls.Add(this.OutputLabel);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.OutputFolder);
            this.Controls.Add(this.progressBar1);
            this.Controls.Add(this.lblFolder);
            this.Controls.Add(this.lstFiles);
            this.Controls.Add(this.btnExtract);
            this.Controls.Add(this.lstZips);
            this.Controls.Add(this.btnSelectFolder);
            this.Name = "Form1";
            this.Text = "Zip Folder Viewer (.NET Framework 4.8)";
            this.Load += new System.EventHandler(this.Form1_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private System.Windows.Forms.Button OutputFolder;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label OutputLabel;
    }
}
