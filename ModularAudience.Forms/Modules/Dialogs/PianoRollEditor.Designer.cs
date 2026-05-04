using ModularAudience.Forms.Modules;

namespace ModularAudience.Forms.Modules.Dialogs
{
    partial class PianoRollEditor
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.panel_roll = new BufferedPatternPanel();
            this.button_playback = new Button();
            this.label_info_bpm = new Label();
            this.numericUpDown_bpm = new NumericUpDown();
            this.label_info_dragndrop = new Label();
            this.contextMenuStrip_rows = new ContextMenuStrip(this.components);
            this.panel_roll.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_bpm).BeginInit();
            this.SuspendLayout();
            // 
            // panel_roll
            // 
            this.panel_roll.AllowDrop = true;
            this.panel_roll.BackColor = SystemColors.ControlLight;
            this.panel_roll.Location = new Point(12, 72);
            this.panel_roll.Name = "panel_roll";
            this.panel_roll.Size = new Size(760, 330);
            this.panel_roll.TabIndex = 0;
            this.panel_roll.DragDrop += this.panel_roll_DragDrop;
            this.panel_roll.DragEnter += this.panel_roll_DragEnter;
            this.panel_roll.Paint += this.panel_roll_Paint;
            this.panel_roll.MouseClick += this.panel_roll_MouseClick;
            this.panel_roll.MouseDoubleClick += this.panel_roll_MouseDoubleClick;
            // 
            // button_playback
            // 
            this.button_playback.Location = new Point(12, 12);
            this.button_playback.Name = "button_playback";
            this.button_playback.Size = new Size(75, 23);
            this.button_playback.TabIndex = 1;
            this.button_playback.TabStop = false;
            this.button_playback.Text = "Play";
            this.button_playback.UseVisualStyleBackColor = true;
            this.button_playback.Click += this.button_playback_Click;
            // 
            // label_info_bpm
            // 
            this.label_info_bpm.AutoSize = true;
            this.label_info_bpm.Location = new Point(104, 17);
            this.label_info_bpm.Name = "label_info_bpm";
            this.label_info_bpm.Size = new Size(32, 15);
            this.label_info_bpm.TabIndex = 2;
            this.label_info_bpm.Text = "BPM";
            // 
            // numericUpDown_bpm
            // 
            this.numericUpDown_bpm.DecimalPlaces = 3;
            this.numericUpDown_bpm.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            this.numericUpDown_bpm.Location = new Point(142, 13);
            this.numericUpDown_bpm.Maximum = new decimal(new int[] { 360, 0, 0, 0 });
            this.numericUpDown_bpm.Minimum = new decimal(new int[] { 15, 0, 0, 0 });
            this.numericUpDown_bpm.Name = "numericUpDown_bpm";
            this.numericUpDown_bpm.Size = new Size(90, 23);
            this.numericUpDown_bpm.TabIndex = 3;
            this.numericUpDown_bpm.Value = new decimal(new int[] { 120, 0, 0, 0 });
            this.numericUpDown_bpm.ValueChanged += this.numericUpDown_bpm_ValueChanged;
            // 
            // label_info_dragndrop
            // 
            this.label_info_dragndrop.AutoSize = true;
            this.label_info_dragndrop.AllowDrop = true;
            this.label_info_dragndrop.Location = new Point(248, 17);
            this.label_info_dragndrop.Name = "label_info_dragndrop";
            this.label_info_dragndrop.Size = new Size(222, 15);
            this.label_info_dragndrop.TabIndex = 4;
            this.label_info_dragndrop.Text = "Drop Samples here to add piano tracks";
            this.label_info_dragndrop.DragDrop += this.label_info_dragndrop_DragDrop;
            this.label_info_dragndrop.DragEnter += this.label_info_dragndrop_DragEnter;
            // 
            // contextMenuStrip_rows
            // 
            this.contextMenuStrip_rows.Name = "contextMenuStrip_rows";
            this.contextMenuStrip_rows.Size = new Size(61, 4);
            // 
            // PianoRollEditor
            // 
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(784, 461);
            this.Controls.Add(this.label_info_dragndrop);
            this.Controls.Add(this.numericUpDown_bpm);
            this.Controls.Add(this.label_info_bpm);
            this.Controls.Add(this.button_playback);
            this.Controls.Add(this.panel_roll);
            this.KeyPreview = true;
            this.MinimumSize = new Size(860, 420);
            this.Name = "PianoRollEditor";
            this.Text = "Piano Roll Editor";
            this.panel_roll.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_bpm).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private BufferedPatternPanel panel_roll;
        private Button button_playback;
        private Label label_info_bpm;
        private NumericUpDown numericUpDown_bpm;
        private Label label_info_dragndrop;
        private ContextMenuStrip contextMenuStrip_rows;
    }
}
