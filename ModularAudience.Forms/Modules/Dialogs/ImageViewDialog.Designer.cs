namespace ModularAudience.Forms.Modules.Dialogs;

partial class ImageViewDialog
{
    private System.ComponentModel.IContainer? components = null;
    private PictureBox pictureBox_image;
    private Panel panel_frame;
    private Label label_frame;
    private NumericUpDown numericUpDown_frame;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        this.pictureBox_image = new PictureBox();
        this.panel_frame = new Panel();
        this.textBox_apiUrl = new TextBox();
        this.button_llm = new Button();
        this.numericUpDown_frame = new NumericUpDown();
        this.label_frame = new Label();
        ((System.ComponentModel.ISupportInitialize) this.pictureBox_image).BeginInit();
        this.panel_frame.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize) this.numericUpDown_frame).BeginInit();
        this.SuspendLayout();
        // 
        // pictureBox_image
        // 
        this.pictureBox_image.BackColor = Color.Black;
        this.pictureBox_image.Dock = DockStyle.Fill;
        this.pictureBox_image.Location = new Point(0, 0);
        this.pictureBox_image.Name = "pictureBox_image";
        this.pictureBox_image.Size = new Size(800, 408);
        this.pictureBox_image.SizeMode = PictureBoxSizeMode.Zoom;
        this.pictureBox_image.TabIndex = 0;
        this.pictureBox_image.TabStop = false;
        this.pictureBox_image.Resize += this.pictureBox_image_Resize;
        // 
        // panel_frame
        // 
        this.panel_frame.Controls.Add(this.textBox_apiUrl);
        this.panel_frame.Controls.Add(this.button_llm);
        this.panel_frame.Controls.Add(this.numericUpDown_frame);
        this.panel_frame.Controls.Add(this.label_frame);
        this.panel_frame.Dock = DockStyle.Bottom;
        this.panel_frame.Location = new Point(0, 408);
        this.panel_frame.Name = "panel_frame";
        this.panel_frame.Padding = new Padding(8);
        this.panel_frame.Size = new Size(800, 42);
        this.panel_frame.TabIndex = 1;
        // 
        // textBox_apiUrl
        // 
        this.textBox_apiUrl.Location = new Point(495, 11);
        this.textBox_apiUrl.Name = "textBox_apiUrl";
        this.textBox_apiUrl.PlaceholderText = "OpenAI API Url...";
        this.textBox_apiUrl.Size = new Size(243, 23);
        this.textBox_apiUrl.TabIndex = 28;
        this.textBox_apiUrl.Text = "http://127.0.0.1:8080";
        // 
        // button_llm
        // 
        this.button_llm.Location = new Point(744, 11);
        this.button_llm.Name = "button_llm";
        this.button_llm.Size = new Size(45, 23);
        this.button_llm.TabIndex = 27;
        this.button_llm.Text = "MIDI";
        this.button_llm.UseVisualStyleBackColor = true;
        this.button_llm.Click += this.button_llm_Click;
        // 
        // numericUpDown_frame
        // 
        this.numericUpDown_frame.Location = new Point(58, 9);
        this.numericUpDown_frame.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        this.numericUpDown_frame.Name = "numericUpDown_frame";
        this.numericUpDown_frame.Size = new Size(120, 23);
        this.numericUpDown_frame.TabIndex = 1;
        this.numericUpDown_frame.Value = new decimal(new int[] { 1, 0, 0, 0 });
        this.numericUpDown_frame.ValueChanged += this.numericUpDown_frame_ValueChanged;
        // 
        // label_frame
        // 
        this.label_frame.AutoSize = true;
        this.label_frame.Location = new Point(8, 13);
        this.label_frame.Name = "label_frame";
        this.label_frame.Size = new Size(43, 15);
        this.label_frame.TabIndex = 0;
        this.label_frame.Text = "Frame:";
        // 
        // ImageViewDialog
        // 
        this.AutoScaleDimensions = new SizeF(7F, 15F);
        this.AutoScaleMode = AutoScaleMode.Font;
        this.ClientSize = new Size(800, 450);
        this.Controls.Add(this.pictureBox_image);
        this.Controls.Add(this.panel_frame);
        this.MinimumSize = new Size(320, 240);
        this.Name = "ImageViewDialog";
        this.StartPosition = FormStartPosition.CenterParent;
        this.Text = "Image Viewer";
        ((System.ComponentModel.ISupportInitialize) this.pictureBox_image).EndInit();
        this.panel_frame.ResumeLayout(false);
        this.panel_frame.PerformLayout();
        ((System.ComponentModel.ISupportInitialize) this.numericUpDown_frame).EndInit();
        this.ResumeLayout(false);
    }

    private TextBox textBox_apiUrl;
    private Button button_llm;
}