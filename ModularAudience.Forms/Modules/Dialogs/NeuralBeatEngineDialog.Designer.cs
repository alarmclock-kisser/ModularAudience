namespace ModularAudience.Forms.Modules.Dialogs
{
    partial class NeuralBeatEngineDialog
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
            this.listBox_samples = new ListBox();
            this.contextMenuStrip_samples = new ContextMenuStrip(this.components);
            this.removeToolStripMenuItem = new ToolStripMenuItem();
            this.groupBox_samples = new GroupBox();
            this.groupBox_timing = new GroupBox();
            this.numericUpDown_interleaved = new NumericUpDown();
            this.label_interleaved = new Label();
            this.numericUpDown_bpm = new NumericUpDown();
            this.label_bpm = new Label();
            this.numericUpDown_stepsPerBeat = new NumericUpDown();
            this.label_stepsPerBeat = new Label();
            this.numericUpDown_beatsPerBar = new NumericUpDown();
            this.label_beatsPerBar = new Label();
            this.numericUpDown_bars = new NumericUpDown();
            this.label_bars = new Label();
            this.groupBox_learning = new GroupBox();
            this.numericUpDown_threadCount = new NumericUpDown();
            this.label_threadCount = new Label();
            this.numericUpDown_maxWeight = new NumericUpDown();
            this.label_maxWeight = new Label();
            this.numericUpDown_minWeight = new NumericUpDown();
            this.label_minWeight = new Label();
            this.numericUpDown_weightDecay = new NumericUpDown();
            this.label_weightDecay = new Label();
            this.numericUpDown_temperature = new NumericUpDown();
            this.label_temperature = new Label();
            this.numericUpDown_learningRate = new NumericUpDown();
            this.label_learningRate = new Label();
            this.groupBox_actions = new GroupBox();
            this.button_export = new Button();
            this.numericUpDown_mutation = new NumericUpDown();
            this.label_mutation = new Label();
            this.button_remix = new Button();
            this.button_stop = new Button();
            this.button_generate = new Button();
            this.checkBox_loopUntilFeedback = new CheckBox();
            this.pictureBox_beatMap = new PictureBox();
            this.label_status = new Label();
            this.groupBox_feedback = new GroupBox();
            this.flowLayoutPanel_feedback = new FlowLayoutPanel();
            this.checkBox_loopFeedback = new CheckBox();
            this.button_feedback0 = new Button();
            this.button_feedback25 = new Button();
            this.button_feedback50 = new Button();
            this.button_feedback75 = new Button();
            this.button_feedback100 = new Button();
            this.contextMenuStrip_samples.SuspendLayout();
            this.groupBox_samples.SuspendLayout();
            this.groupBox_timing.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_interleaved).BeginInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_bpm).BeginInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_stepsPerBeat).BeginInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_beatsPerBar).BeginInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_bars).BeginInit();
            this.groupBox_learning.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_threadCount).BeginInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_maxWeight).BeginInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_minWeight).BeginInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_weightDecay).BeginInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_temperature).BeginInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_learningRate).BeginInit();
            this.groupBox_actions.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_mutation).BeginInit();
            ((System.ComponentModel.ISupportInitialize)this.pictureBox_beatMap).BeginInit();
            this.groupBox_feedback.SuspendLayout();
            this.flowLayoutPanel_feedback.SuspendLayout();
            this.SuspendLayout();
            // 
            // listBox_samples
            // 
            this.listBox_samples.ContextMenuStrip = this.contextMenuStrip_samples;
            this.listBox_samples.DisplayMember = "Name";
            this.listBox_samples.Dock = DockStyle.Fill;
            this.listBox_samples.FormattingEnabled = true;
            this.listBox_samples.IntegralHeight = false;
            this.listBox_samples.Name = "listBox_samples";
            this.listBox_samples.TabIndex = 0;
            // 
            // contextMenuStrip_samples
            // 
            this.contextMenuStrip_samples.Items.AddRange(new ToolStripItem[] { this.removeToolStripMenuItem });
            this.contextMenuStrip_samples.Name = "contextMenuStrip_samples";
            this.contextMenuStrip_samples.Size = new Size(118, 26);
            // 
            // removeToolStripMenuItem
            // 
            this.removeToolStripMenuItem.Name = "removeToolStripMenuItem";
            this.removeToolStripMenuItem.Size = new Size(117, 22);
            this.removeToolStripMenuItem.Text = "Remove";
            this.removeToolStripMenuItem.Click += this.removeToolStripMenuItem_Click;
            // 
            // groupBox_samples
            // 
            this.groupBox_samples.Controls.Add(this.listBox_samples);
            this.groupBox_samples.Location = new Point(12, 12);
            this.groupBox_samples.Name = "groupBox_samples";
            this.groupBox_samples.Size = new Size(220, 220);
            this.groupBox_samples.TabIndex = 1;
            this.groupBox_samples.TabStop = false;
            this.groupBox_samples.Text = "Samples";
            // 
            // groupBox_timing
            // 
            this.groupBox_timing.Controls.Add(this.numericUpDown_interleaved);
            this.groupBox_timing.Controls.Add(this.label_interleaved);
            this.groupBox_timing.Controls.Add(this.numericUpDown_bpm);
            this.groupBox_timing.Controls.Add(this.label_bpm);
            this.groupBox_timing.Controls.Add(this.numericUpDown_stepsPerBeat);
            this.groupBox_timing.Controls.Add(this.label_stepsPerBeat);
            this.groupBox_timing.Controls.Add(this.numericUpDown_beatsPerBar);
            this.groupBox_timing.Controls.Add(this.label_beatsPerBar);
            this.groupBox_timing.Controls.Add(this.numericUpDown_bars);
            this.groupBox_timing.Controls.Add(this.label_bars);
            this.groupBox_timing.Location = new Point(238, 12);
            this.groupBox_timing.Name = "groupBox_timing";
            this.groupBox_timing.Size = new Size(210, 220);
            this.groupBox_timing.TabIndex = 2;
            this.groupBox_timing.TabStop = false;
            this.groupBox_timing.Text = "Timing";
            // 
            // numericUpDown_interleaved
            // 
            this.numericUpDown_interleaved.Location = new Point(116, 165);
            this.numericUpDown_interleaved.Maximum = new decimal(new int[] { 16, 0, 0, 0 });
            this.numericUpDown_interleaved.Name = "numericUpDown_interleaved";
            this.numericUpDown_interleaved.Size = new Size(78, 23);
            this.numericUpDown_interleaved.TabIndex = 9;
            this.numericUpDown_interleaved.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // label_interleaved
            // 
            this.label_interleaved.AutoSize = true;
            this.label_interleaved.Location = new Point(12, 167);
            this.label_interleaved.Name = "label_interleaved";
            this.label_interleaved.Size = new Size(72, 15);
            this.label_interleaved.TabIndex = 8;
            this.label_interleaved.Text = "Extra notes";
            // 
            // numericUpDown_bpm
            // 
            this.numericUpDown_bpm.DecimalPlaces = 1;
            this.numericUpDown_bpm.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            this.numericUpDown_bpm.Location = new Point(116, 136);
            this.numericUpDown_bpm.Maximum = new decimal(new int[] { 300, 0, 0, 0 });
            this.numericUpDown_bpm.Minimum = new decimal(new int[] { 20, 0, 0, 0 });
            this.numericUpDown_bpm.Name = "numericUpDown_bpm";
            this.numericUpDown_bpm.Size = new Size(78, 23);
            this.numericUpDown_bpm.TabIndex = 7;
            this.numericUpDown_bpm.Value = new decimal(new int[] { 120, 0, 0, 0 });
            // 
            // label_bpm
            // 
            this.label_bpm.AutoSize = true;
            this.label_bpm.Location = new Point(12, 138);
            this.label_bpm.Name = "label_bpm";
            this.label_bpm.Size = new Size(31, 15);
            this.label_bpm.TabIndex = 6;
            this.label_bpm.Text = "BPM";
            // 
            // numericUpDown_stepsPerBeat
            // 
            this.numericUpDown_stepsPerBeat.Location = new Point(116, 107);
            this.numericUpDown_stepsPerBeat.Maximum = new decimal(new int[] { 32, 0, 0, 0 });
            this.numericUpDown_stepsPerBeat.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_stepsPerBeat.Name = "numericUpDown_stepsPerBeat";
            this.numericUpDown_stepsPerBeat.Size = new Size(78, 23);
            this.numericUpDown_stepsPerBeat.TabIndex = 5;
            this.numericUpDown_stepsPerBeat.Value = new decimal(new int[] { 4, 0, 0, 0 });
            // 
            // label_stepsPerBeat
            // 
            this.label_stepsPerBeat.AutoSize = true;
            this.label_stepsPerBeat.Location = new Point(12, 109);
            this.label_stepsPerBeat.Name = "label_stepsPerBeat";
            this.label_stepsPerBeat.Size = new Size(81, 15);
            this.label_stepsPerBeat.TabIndex = 4;
            this.label_stepsPerBeat.Text = "Steps per beat";
            // 
            // numericUpDown_beatsPerBar
            // 
            this.numericUpDown_beatsPerBar.Location = new Point(116, 78);
            this.numericUpDown_beatsPerBar.Maximum = new decimal(new int[] { 16, 0, 0, 0 });
            this.numericUpDown_beatsPerBar.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_beatsPerBar.Name = "numericUpDown_beatsPerBar";
            this.numericUpDown_beatsPerBar.Size = new Size(78, 23);
            this.numericUpDown_beatsPerBar.TabIndex = 3;
            this.numericUpDown_beatsPerBar.Value = new decimal(new int[] { 4, 0, 0, 0 });
            // 
            // label_beatsPerBar
            // 
            this.label_beatsPerBar.AutoSize = true;
            this.label_beatsPerBar.Location = new Point(12, 80);
            this.label_beatsPerBar.Name = "label_beatsPerBar";
            this.label_beatsPerBar.Size = new Size(80, 15);
            this.label_beatsPerBar.TabIndex = 2;
            this.label_beatsPerBar.Text = "Beats per bar";
            // 
            // numericUpDown_bars
            // 
            this.numericUpDown_bars.Location = new Point(116, 49);
            this.numericUpDown_bars.Maximum = new decimal(new int[] { 64, 0, 0, 0 });
            this.numericUpDown_bars.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_bars.Name = "numericUpDown_bars";
            this.numericUpDown_bars.Size = new Size(78, 23);
            this.numericUpDown_bars.TabIndex = 1;
            this.numericUpDown_bars.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // label_bars
            // 
            this.label_bars.AutoSize = true;
            this.label_bars.Location = new Point(12, 51);
            this.label_bars.Name = "label_bars";
            this.label_bars.Size = new Size(29, 15);
            this.label_bars.TabIndex = 0;
            this.label_bars.Text = "Bars";
            // 
            // groupBox_learning
            // 
            this.groupBox_learning.Controls.Add(this.numericUpDown_threadCount);
            this.groupBox_learning.Controls.Add(this.label_threadCount);
            this.groupBox_learning.Controls.Add(this.numericUpDown_maxWeight);
            this.groupBox_learning.Controls.Add(this.label_maxWeight);
            this.groupBox_learning.Controls.Add(this.numericUpDown_minWeight);
            this.groupBox_learning.Controls.Add(this.label_minWeight);
            this.groupBox_learning.Controls.Add(this.numericUpDown_weightDecay);
            this.groupBox_learning.Controls.Add(this.label_weightDecay);
            this.groupBox_learning.Controls.Add(this.numericUpDown_temperature);
            this.groupBox_learning.Controls.Add(this.label_temperature);
            this.groupBox_learning.Controls.Add(this.numericUpDown_learningRate);
            this.groupBox_learning.Controls.Add(this.label_learningRate);
            this.groupBox_learning.Location = new Point(454, 12);
            this.groupBox_learning.Name = "groupBox_learning";
            this.groupBox_learning.Size = new Size(230, 220);
            this.groupBox_learning.TabIndex = 3;
            this.groupBox_learning.TabStop = false;
            this.groupBox_learning.Text = "Learning";
            // 
            // numericUpDown_threadCount
            // 
            this.numericUpDown_threadCount.Location = new Point(133, 165);
            this.numericUpDown_threadCount.Maximum = new decimal(new int[] { 128, 0, 0, 0 });
            this.numericUpDown_threadCount.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_threadCount.Name = "numericUpDown_threadCount";
            this.numericUpDown_threadCount.Size = new Size(80, 23);
            this.numericUpDown_threadCount.TabIndex = 11;
            this.numericUpDown_threadCount.Value = new decimal(new int[] { 8, 0, 0, 0 });
            // 
            // label_threadCount
            // 
            this.label_threadCount.AutoSize = true;
            this.label_threadCount.Location = new Point(12, 167);
            this.label_threadCount.Name = "label_threadCount";
            this.label_threadCount.Size = new Size(80, 15);
            this.label_threadCount.TabIndex = 10;
            this.label_threadCount.Text = "Thread count";
            // 
            // numericUpDown_maxWeight
            // 
            this.numericUpDown_maxWeight.DecimalPlaces = 2;
            this.numericUpDown_maxWeight.Increment = new decimal(new int[] { 10, 0, 0, 131072 });
            this.numericUpDown_maxWeight.Location = new Point(133, 136);
            this.numericUpDown_maxWeight.Minimum = new decimal(new int[] { 100, 0, 0, -2147352576 });
            this.numericUpDown_maxWeight.Name = "numericUpDown_maxWeight";
            this.numericUpDown_maxWeight.Size = new Size(80, 23);
            this.numericUpDown_maxWeight.TabIndex = 9;
            this.numericUpDown_maxWeight.Value = new decimal(new int[] { 10, 0, 0, 0 });
            // 
            // label_maxWeight
            // 
            this.label_maxWeight.AutoSize = true;
            this.label_maxWeight.Location = new Point(12, 138);
            this.label_maxWeight.Name = "label_maxWeight";
            this.label_maxWeight.Size = new Size(71, 15);
            this.label_maxWeight.TabIndex = 8;
            this.label_maxWeight.Text = "Max weight";
            // 
            // numericUpDown_minWeight
            // 
            this.numericUpDown_minWeight.DecimalPlaces = 2;
            this.numericUpDown_minWeight.Increment = new decimal(new int[] { 10, 0, 0, 131072 });
            this.numericUpDown_minWeight.Location = new Point(133, 107);
            this.numericUpDown_minWeight.Minimum = new decimal(new int[] { 100, 0, 0, -2147352576 });
            this.numericUpDown_minWeight.Name = "numericUpDown_minWeight";
            this.numericUpDown_minWeight.Size = new Size(80, 23);
            this.numericUpDown_minWeight.TabIndex = 7;
            this.numericUpDown_minWeight.Value = new decimal(new int[] { 10, 0, 0, -2147352576 });
            // 
            // label_minWeight
            // 
            this.label_minWeight.AutoSize = true;
            this.label_minWeight.Location = new Point(12, 109);
            this.label_minWeight.Name = "label_minWeight";
            this.label_minWeight.Size = new Size(69, 15);
            this.label_minWeight.TabIndex = 6;
            this.label_minWeight.Text = "Min weight";
            // 
            // numericUpDown_weightDecay
            // 
            this.numericUpDown_weightDecay.DecimalPlaces = 3;
            this.numericUpDown_weightDecay.Increment = new decimal(new int[] { 1, 0, 0, 196608 });
            this.numericUpDown_weightDecay.Location = new Point(133, 78);
            this.numericUpDown_weightDecay.Maximum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_weightDecay.Name = "numericUpDown_weightDecay";
            this.numericUpDown_weightDecay.Size = new Size(80, 23);
            this.numericUpDown_weightDecay.TabIndex = 5;
            this.numericUpDown_weightDecay.Value = new decimal(new int[] { 10, 0, 0, 196608 });
            // 
            // label_weightDecay
            // 
            this.label_weightDecay.AutoSize = true;
            this.label_weightDecay.Location = new Point(12, 80);
            this.label_weightDecay.Name = "label_weightDecay";
            this.label_weightDecay.Size = new Size(81, 15);
            this.label_weightDecay.TabIndex = 4;
            this.label_weightDecay.Text = "Weight decay";
            // 
            // numericUpDown_temperature
            // 
            this.numericUpDown_temperature.DecimalPlaces = 2;
            this.numericUpDown_temperature.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
            this.numericUpDown_temperature.Location = new Point(133, 49);
            this.numericUpDown_temperature.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
            this.numericUpDown_temperature.Minimum = new decimal(new int[] { 1, 0, 0, 131072 });
            this.numericUpDown_temperature.Name = "numericUpDown_temperature";
            this.numericUpDown_temperature.Size = new Size(80, 23);
            this.numericUpDown_temperature.TabIndex = 3;
            this.numericUpDown_temperature.Value = new decimal(new int[] { 80, 0, 0, 131072 });
            // 
            // label_temperature
            // 
            this.label_temperature.AutoSize = true;
            this.label_temperature.Location = new Point(12, 51);
            this.label_temperature.Name = "label_temperature";
            this.label_temperature.Size = new Size(73, 15);
            this.label_temperature.TabIndex = 2;
            this.label_temperature.Text = "Temperature";
            // 
            // numericUpDown_learningRate
            // 
            this.numericUpDown_learningRate.DecimalPlaces = 2;
            this.numericUpDown_learningRate.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
            this.numericUpDown_learningRate.Location = new Point(133, 20);
            this.numericUpDown_learningRate.Maximum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_learningRate.Name = "numericUpDown_learningRate";
            this.numericUpDown_learningRate.Size = new Size(80, 23);
            this.numericUpDown_learningRate.TabIndex = 1;
            this.numericUpDown_learningRate.Value = new decimal(new int[] { 30, 0, 0, 131072 });
            // 
            // label_learningRate
            // 
            this.label_learningRate.AutoSize = true;
            this.label_learningRate.Location = new Point(12, 22);
            this.label_learningRate.Name = "label_learningRate";
            this.label_learningRate.Size = new Size(77, 15);
            this.label_learningRate.TabIndex = 0;
            this.label_learningRate.Text = "Learning rate";
            // 
            // groupBox_actions
            // 
            this.groupBox_actions.Controls.Add(this.button_export);
            this.groupBox_actions.Controls.Add(this.numericUpDown_mutation);
            this.groupBox_actions.Controls.Add(this.label_mutation);
            this.groupBox_actions.Controls.Add(this.button_remix);
            this.groupBox_actions.Controls.Add(this.button_stop);
            this.groupBox_actions.Controls.Add(this.button_generate);
            this.groupBox_actions.Controls.Add(this.checkBox_loopUntilFeedback);
            this.groupBox_actions.Location = new Point(12, 238);
            this.groupBox_actions.Name = "groupBox_actions";
            this.groupBox_actions.Size = new Size(672, 69);
            this.groupBox_actions.TabIndex = 4;
            this.groupBox_actions.TabStop = false;
            this.groupBox_actions.Text = "Generation";
            // 
            // button_export
            // 
            this.button_export.Enabled = false;
            this.button_export.Location = new Point(373, 26);
            this.button_export.Name = "button_export";
            this.button_export.Size = new Size(100, 27);
            this.button_export.TabIndex = 4;
            this.button_export.Text = "Export to Collection";
            this.button_export.UseVisualStyleBackColor = true;
            this.button_export.Click += this.button_export_Click;
            // 
            // numericUpDown_mutation
            // 
            this.numericUpDown_mutation.DecimalPlaces = 2;
            this.numericUpDown_mutation.Increment = new decimal(new int[] { 10, 0, 0, 131072 });
            this.numericUpDown_mutation.Location = new Point(550, 28);
            this.numericUpDown_mutation.Maximum = new decimal(new int[] { 5, 0, 0, 0 });
            this.numericUpDown_mutation.Name = "numericUpDown_mutation";
            this.numericUpDown_mutation.Size = new Size(70, 23);
            this.numericUpDown_mutation.TabIndex = 5;
            this.numericUpDown_mutation.Value = new decimal(new int[] { 120, 0, 0, 131072 });
            // 
            // label_mutation
            // 
            this.label_mutation.AutoSize = true;
            this.label_mutation.Location = new Point(480, 30);
            this.label_mutation.Name = "label_mutation";
            this.label_mutation.Size = new Size(56, 15);
            this.label_mutation.TabIndex = 4;
            this.label_mutation.Text = "Mutation";
            // 
            // button_remix
            // 
            this.button_remix.Enabled = false;
            this.button_remix.Location = new Point(292, 26);
            this.button_remix.Name = "button_remix";
            this.button_remix.Size = new Size(80, 27);
            this.button_remix.TabIndex = 3;
            this.button_remix.Text = "Remix";
            this.button_remix.UseVisualStyleBackColor = true;
            this.button_remix.Click += this.button_remix_Click;
            // 
            // button_stop
            // 
            this.button_stop.Enabled = false;
            this.button_stop.Location = new Point(211, 26);
            this.button_stop.Name = "button_stop";
            this.button_stop.Size = new Size(80, 27);
            this.button_stop.TabIndex = 2;
            this.button_stop.Text = "Stop";
            this.button_stop.UseVisualStyleBackColor = true;
            this.button_stop.Click += this.button_stop_Click;
            // 
            // button_generate
            // 
            this.button_generate.Location = new Point(130, 26);
            this.button_generate.Name = "button_generate";
            this.button_generate.Size = new Size(80, 27);
            this.button_generate.TabIndex = 1;
            this.button_generate.Text = "Generate";
            this.button_generate.UseVisualStyleBackColor = true;
            this.button_generate.Click += this.button_generate_Click;
            // 
            // checkBox_loopUntilFeedback
            // 
            this.checkBox_loopUntilFeedback.AutoSize = true;
            this.checkBox_loopUntilFeedback.Checked = true;
            this.checkBox_loopUntilFeedback.CheckState = CheckState.Checked;
            this.checkBox_loopUntilFeedback.Location = new Point(14, 30);
            this.checkBox_loopUntilFeedback.Name = "checkBox_loopUntilFeedback";
            this.checkBox_loopUntilFeedback.Size = new Size(107, 19);
            this.checkBox_loopUntilFeedback.TabIndex = 0;
            this.checkBox_loopUntilFeedback.Text = "Loop playback";
            this.checkBox_loopUntilFeedback.UseVisualStyleBackColor = true;
            // 
            // pictureBox_beatMap
            // 
            this.pictureBox_beatMap.BorderStyle = BorderStyle.FixedSingle;
            this.pictureBox_beatMap.Location = new Point(12, 337);
            this.pictureBox_beatMap.Name = "pictureBox_beatMap";
            this.pictureBox_beatMap.Size = new Size(672, 205);
            this.pictureBox_beatMap.SizeMode = PictureBoxSizeMode.StretchImage;
            this.pictureBox_beatMap.TabIndex = 5;
            this.pictureBox_beatMap.TabStop = false;
            // 
            // label_status
            // 
            this.label_status.AutoEllipsis = true;
            this.label_status.BorderStyle = BorderStyle.Fixed3D;
            this.label_status.Location = new Point(12, 313);
            this.label_status.Name = "label_status";
            this.label_status.Size = new Size(672, 21);
            this.label_status.TabIndex = 6;
            this.label_status.Text = "Ready. Select at least two samples and generate a beat.";
            this.label_status.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // groupBox_feedback
            // 
            this.groupBox_feedback.Controls.Add(this.flowLayoutPanel_feedback);
            this.groupBox_feedback.Location = new Point(12, 548);
            this.groupBox_feedback.Name = "groupBox_feedback";
            this.groupBox_feedback.Size = new Size(672, 69);
            this.groupBox_feedback.TabIndex = 7;
            this.groupBox_feedback.TabStop = false;
            this.groupBox_feedback.Text = "Feedback (available after the beat has played)";
            // 
            // flowLayoutPanel_feedback
            // 
            this.flowLayoutPanel_feedback.Controls.Add(this.checkBox_loopFeedback);
            this.flowLayoutPanel_feedback.Controls.Add(this.button_feedback0);
            this.flowLayoutPanel_feedback.Controls.Add(this.button_feedback25);
            this.flowLayoutPanel_feedback.Controls.Add(this.button_feedback50);
            this.flowLayoutPanel_feedback.Controls.Add(this.button_feedback75);
            this.flowLayoutPanel_feedback.Controls.Add(this.button_feedback100);
            this.flowLayoutPanel_feedback.Dock = DockStyle.Fill;
            this.flowLayoutPanel_feedback.Location = new Point(3, 19);
            this.flowLayoutPanel_feedback.Name = "flowLayoutPanel_feedback";
            this.flowLayoutPanel_feedback.Padding = new Padding(8, 4, 0, 0);
            this.flowLayoutPanel_feedback.Size = new Size(666, 47);
            this.flowLayoutPanel_feedback.TabIndex = 0;
            // 
            // checkBox_loopFeedback
            // 
            this.checkBox_loopFeedback.AutoSize = true;
            this.checkBox_loopFeedback.Location = new Point(11, 11);
            this.checkBox_loopFeedback.Name = "checkBox_loopFeedback";
            this.checkBox_loopFeedback.Size = new Size(105, 19);
            this.checkBox_loopFeedback.TabIndex = 0;
            this.checkBox_loopFeedback.Text = "Loop Feedback";
            this.checkBox_loopFeedback.UseVisualStyleBackColor = true;
            // 
            // button_feedback0
            // 
            this.button_feedback0.Enabled = false;
            this.button_feedback0.Location = new Point(11, 7);
            this.button_feedback0.Name = "button_feedback0";
            this.button_feedback0.Size = new Size(100, 28);
            this.button_feedback0.TabIndex = 1;
            this.button_feedback0.Tag = "0";
            this.button_feedback0.Text = "0% - Poor";
            this.button_feedback0.UseVisualStyleBackColor = true;
            this.button_feedback0.Click += this.button_feedback_Click;
            // 
            // button_feedback25
            // 
            this.button_feedback25.Enabled = false;
            this.button_feedback25.Location = new Point(137, 7);
            this.button_feedback25.Name = "button_feedback25";
            this.button_feedback25.Size = new Size(100, 28);
            this.button_feedback25.TabIndex = 2;
            this.button_feedback25.Tag = "0.25";
            this.button_feedback25.Text = "25% - Weak";
            this.button_feedback25.UseVisualStyleBackColor = true;
            this.button_feedback25.Click += this.button_feedback_Click;
            // 
            // button_feedback50
            // 
            this.button_feedback50.Enabled = false;
            this.button_feedback50.Location = new Point(263, 7);
            this.button_feedback50.Name = "button_feedback50";
            this.button_feedback50.Size = new Size(100, 28);
            this.button_feedback50.TabIndex = 3;
            this.button_feedback50.Tag = "0.5";
            this.button_feedback50.Text = "50% - Neutral";
            this.button_feedback50.UseVisualStyleBackColor = true;
            this.button_feedback50.Click += this.button_feedback_Click;
            // 
            // button_feedback75
            // 
            this.button_feedback75.Enabled = false;
            this.button_feedback75.Location = new Point(389, 7);
            this.button_feedback75.Name = "button_feedback75";
            this.button_feedback75.Size = new Size(100, 28);
            this.button_feedback75.TabIndex = 4;
            this.button_feedback75.Tag = "0.75";
            this.button_feedback75.Text = "75% - Good";
            this.button_feedback75.UseVisualStyleBackColor = true;
            this.button_feedback75.Click += this.button_feedback_Click;
            // 
            // button_feedback100
            // 
            this.button_feedback100.Enabled = false;
            this.button_feedback100.Location = new Point(515, 7);
            this.button_feedback100.Name = "button_feedback100";
            this.button_feedback100.Size = new Size(100, 28);
            this.button_feedback100.TabIndex = 5;
            this.button_feedback100.Tag = "1";
            this.button_feedback100.Text = "100% - Great";
            this.button_feedback100.UseVisualStyleBackColor = true;
            this.button_feedback100.Click += this.button_feedback_Click;
            // 
            // NeuralBeatEngineDialog
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(696, 629);
            this.Controls.Add(this.groupBox_feedback);
            this.Controls.Add(this.label_status);
            this.Controls.Add(this.pictureBox_beatMap);
            this.Controls.Add(this.groupBox_actions);
            this.Controls.Add(this.groupBox_learning);
            this.Controls.Add(this.groupBox_timing);
            this.Controls.Add(this.groupBox_samples);
            this.FormBorderStyle = FormBorderStyle.SizableToolWindow;
            this.MinimumSize = new Size(712, 668);
            this.Name = "NeuralBeatEngineDialog";
            this.Text = "Neural Beat Engine";
            this.FormClosing += this.NeuralBeatEngineDialog_FormClosing;
            this.contextMenuStrip_samples.ResumeLayout(false);
            this.groupBox_samples.ResumeLayout(false);
            this.groupBox_timing.ResumeLayout(false);
            this.groupBox_timing.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_interleaved).EndInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_bpm).EndInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_stepsPerBeat).EndInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_beatsPerBar).EndInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_bars).EndInit();
            this.groupBox_learning.ResumeLayout(false);
            this.groupBox_learning.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_threadCount).EndInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_maxWeight).EndInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_minWeight).EndInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_weightDecay).EndInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_temperature).EndInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_learningRate).EndInit();
            this.groupBox_actions.ResumeLayout(false);
            this.groupBox_actions.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_mutation).EndInit();
            ((System.ComponentModel.ISupportInitialize)this.pictureBox_beatMap).EndInit();
            this.groupBox_feedback.ResumeLayout(false);
            this.flowLayoutPanel_feedback.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private ListBox listBox_samples;
        private ContextMenuStrip contextMenuStrip_samples;
        private ToolStripMenuItem removeToolStripMenuItem;
        private GroupBox groupBox_samples;
        private GroupBox groupBox_timing;
        private NumericUpDown numericUpDown_interleaved;
        private Label label_interleaved;
        private NumericUpDown numericUpDown_bpm;
        private Label label_bpm;
        private NumericUpDown numericUpDown_stepsPerBeat;
        private Label label_stepsPerBeat;
        private NumericUpDown numericUpDown_beatsPerBar;
        private Label label_beatsPerBar;
        private NumericUpDown numericUpDown_bars;
        private Label label_bars;
        private GroupBox groupBox_learning;
        private NumericUpDown numericUpDown_threadCount;
        private Label label_threadCount;
        private NumericUpDown numericUpDown_maxWeight;
        private Label label_maxWeight;
        private NumericUpDown numericUpDown_minWeight;
        private Label label_minWeight;
        private NumericUpDown numericUpDown_weightDecay;
        private Label label_weightDecay;
        private NumericUpDown numericUpDown_temperature;
        private Label label_temperature;
        private NumericUpDown numericUpDown_learningRate;
        private Label label_learningRate;
        private GroupBox groupBox_actions;
        private Button button_export;
        private NumericUpDown numericUpDown_mutation;
        private Label label_mutation;
        private Button button_remix;
        private Button button_stop;
        private Button button_generate;
        private CheckBox checkBox_loopUntilFeedback;
        private PictureBox pictureBox_beatMap;
        private Label label_status;
        private GroupBox groupBox_feedback;
        private FlowLayoutPanel flowLayoutPanel_feedback;
        private CheckBox checkBox_loopFeedback;
        private Button button_feedback0;
        private Button button_feedback25;
        private Button button_feedback50;
        private Button button_feedback75;
        private Button button_feedback100;
    }
}