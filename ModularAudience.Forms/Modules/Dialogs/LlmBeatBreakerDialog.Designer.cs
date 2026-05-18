namespace ModularAudience.Forms.Modules.Dialogs
{
    partial class LlmBeatBreakerDialog
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

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.textBox_apiUrl = new TextBox();
            this.button_testConnection = new Button();
            this.label_connectionInfo = new Label();
            this.listBox_samples = new ListBox();
            this.button_deleteSample = new Button();
            this.comboBox_drumset = new ComboBox();
            this.SuspendLayout();
            // 
            // textBox_apiUrl
            // 
            this.textBox_apiUrl.Location = new Point(412, 12);
            this.textBox_apiUrl.Name = "textBox_apiUrl";
            this.textBox_apiUrl.PlaceholderText = "LLM API URL here ...";
            this.textBox_apiUrl.Size = new Size(280, 23);
            this.textBox_apiUrl.TabIndex = 0;
            this.textBox_apiUrl.Text = "http://localhost:8080/v1";
            // 
            // button_testConnection
            // 
            this.button_testConnection.Location = new Point(617, 41);
            this.button_testConnection.Name = "button_testConnection";
            this.button_testConnection.Size = new Size(75, 23);
            this.button_testConnection.TabIndex = 1;
            this.button_testConnection.Text = "Connect";
            this.button_testConnection.UseVisualStyleBackColor = true;
            // 
            // label_connectionInfo
            // 
            this.label_connectionInfo.AutoSize = true;
            this.label_connectionInfo.Location = new Point(412, 38);
            this.label_connectionInfo.Name = "label_connectionInfo";
            this.label_connectionInfo.Size = new Size(170, 15);
            this.label_connectionInfo.TabIndex = 2;
            this.label_connectionInfo.Text = "No connection established yet.";
            // 
            // listBox_samples
            // 
            this.listBox_samples.FormattingEnabled = true;
            this.listBox_samples.Location = new Point(12, 41);
            this.listBox_samples.Name = "listBox_samples";
            this.listBox_samples.Size = new Size(180, 184);
            this.listBox_samples.TabIndex = 3;
            // 
            // button_deleteSample
            // 
            this.button_deleteSample.BackColor = Color.LightCoral;
            this.button_deleteSample.Location = new Point(12, 231);
            this.button_deleteSample.Name = "button_deleteSample";
            this.button_deleteSample.Size = new Size(50, 23);
            this.button_deleteSample.TabIndex = 4;
            this.button_deleteSample.Text = "Delete";
            this.button_deleteSample.UseVisualStyleBackColor = false;
            // 
            // comboBox_drumset
            // 
            this.comboBox_drumset.FormattingEnabled = true;
            this.comboBox_drumset.Location = new Point(12, 12);
            this.comboBox_drumset.Name = "comboBox_drumset";
            this.comboBox_drumset.Size = new Size(180, 23);
            this.comboBox_drumset.TabIndex = 5;
            this.comboBox_drumset.Text = "Select drum ...";
            // 
            // LlmBeatBreakerDialog
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(704, 441);
            this.Controls.Add(this.comboBox_drumset);
            this.Controls.Add(this.button_deleteSample);
            this.Controls.Add(this.listBox_samples);
            this.Controls.Add(this.label_connectionInfo);
            this.Controls.Add(this.button_testConnection);
            this.Controls.Add(this.textBox_apiUrl);
            this.MaximumSize = new Size(720, 480);
            this.MinimumSize = new Size(720, 480);
            this.Name = "LlmBeatBreakerDialog";
            this.Text = "LlmBeatBreakerDialog";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private TextBox textBox_apiUrl;
        private Button button_testConnection;
        private Label label_connectionInfo;
        private ListBox listBox_samples;
        private Button button_deleteSample;
        private ComboBox comboBox_drumset;
    }
}