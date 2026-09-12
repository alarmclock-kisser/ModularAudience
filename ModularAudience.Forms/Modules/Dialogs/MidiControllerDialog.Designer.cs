namespace ModularAudience.Forms.Modules.Dialogs
{
    partial class MidiControllerDialog
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
            this.groupDevice = new GroupBox();
            this.label_input = new Label();
            this.comboBox_input = new ComboBox();
            this.label_output = new Label();
            this.comboBox_output = new ComboBox();
            this.button_connect = new Button();
            this.button_disconnect = new Button();
            this.groupMappings = new GroupBox();
            this.dataGridView_mappings = new DataGridView();
            this.button_add = new Button();
            this.button_remove = new Button();
            this.button_reset = new Button();
            this.groupStatus = new GroupBox();
            this.label_status = new Label();
            this.listView_messages = new ListView();
            this.columnChannel = new ColumnHeader();
            this.columnType = new ColumnHeader();
            this.columnNumber = new ColumnHeader();
            this.columnCommand = new ColumnHeader();
            this.groupDevice.SuspendLayout();
            this.groupMappings.SuspendLayout();
            this.groupStatus.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)this.dataGridView_mappings).BeginInit();
            this.SuspendLayout();
            // 
            // groupDevice
            // 
            this.groupDevice.Controls.Add(this.label_input);
            this.groupDevice.Controls.Add(this.comboBox_input);
            this.groupDevice.Controls.Add(this.label_output);
            this.groupDevice.Controls.Add(this.comboBox_output);
            this.groupDevice.Controls.Add(this.button_connect);
            this.groupDevice.Controls.Add(this.button_disconnect);
            this.groupDevice.Font = new Font("Bahnschrift SemiLight Condensed", 9.75F, FontStyle.Regular, GraphicsUnit.Point, 0);
            this.groupDevice.Location = new Point(12, 9);
            this.groupDevice.Name = "groupDevice";
            this.groupDevice.Size = new Size(496, 95);
            this.groupDevice.TabIndex = 0;
            this.groupDevice.TabStop = false;
            this.groupDevice.Text = "MIDI Device";
            // 
            // label_input
            // 
            this.label_input.AutoSize = true;
            this.label_input.Location = new Point(12, 30);
            this.label_input.Name = "label_input";
            this.label_input.Size = new Size(40, 15);
            this.label_input.TabIndex = 0;
            this.label_input.Text = "Input:";
            // 
            // comboBox_input
            // 
            this.comboBox_input.DropDownStyle = ComboBoxStyle.DropDownList;
            this.comboBox_input.Location = new Point(58, 27);
            this.comboBox_input.Name = "comboBox_input";
            this.comboBox_input.Size = new Size(200, 23);
            this.comboBox_input.TabIndex = 1;
            // 
            // label_output
            // 
            this.label_output.AutoSize = true;
            this.label_output.Location = new Point(12, 62);
            this.label_output.Name = "label_output";
            this.label_output.Size = new Size(46, 15);
            this.label_output.TabIndex = 2;
            this.label_output.Text = "Output:";
            // 
            // comboBox_output
            // 
            this.comboBox_output.DropDownStyle = ComboBoxStyle.DropDownList;
            this.comboBox_output.Location = new Point(58, 59);
            this.comboBox_output.Name = "comboBox_output";
            this.comboBox_output.Size = new Size(200, 23);
            this.comboBox_output.TabIndex = 3;
            // 
            // button_connect
            // 
            this.button_connect.BackColor = SystemColors.Info;
            this.button_connect.Location = new Point(270, 27);
            this.button_connect.Name = "button_connect";
            this.button_connect.Size = new Size(100, 23);
            this.button_connect.TabIndex = 4;
            this.button_connect.Text = "Connect";
            this.button_connect.UseVisualStyleBackColor = false;
            this.button_connect.Click += this.button_connect_Click;
            // 
            // button_disconnect
            // 
            this.button_disconnect.Location = new Point(270, 59);
            this.button_disconnect.Name = "button_disconnect";
            this.button_disconnect.Size = new Size(100, 23);
            this.button_disconnect.TabIndex = 5;
            this.button_disconnect.Text = "Disconnect";
            this.button_disconnect.UseVisualStyleBackColor = true;
            this.button_disconnect.Click += this.button_disconnect_Click;
            // 
            // groupMappings
            // 
            this.groupMappings.Controls.Add(this.dataGridView_mappings);
            this.groupMappings.Controls.Add(this.button_add);
            this.groupMappings.Controls.Add(this.button_remove);
            this.groupMappings.Controls.Add(this.button_reset);
            this.groupMappings.Font = new Font("Bahnschrift SemiLight Condensed", 9.75F, FontStyle.Regular, GraphicsUnit.Point, 0);
            this.groupMappings.Location = new Point(12, 110);
            this.groupMappings.Name = "groupMappings";
            this.groupMappings.Size = new Size(496, 310);
            this.groupMappings.TabIndex = 1;
            this.groupMappings.TabStop = false;
            this.groupMappings.Text = "Command Mappings";
            // 
            // dataGridView_mappings
            // 
            this.dataGridView_mappings.AllowUserToResizeRows = false;
            this.dataGridView_mappings.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            this.dataGridView_mappings.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            this.colChannel = new DataGridViewTextBoxColumn();
            this.colType = new DataGridViewComboBoxColumn();
            this.colNumber = new DataGridViewTextBoxColumn();
            this.colCommand = new DataGridViewComboBoxColumn();
            this.colType.Items.AddRange(new object[] { "CC", "Note On", "Note Off" });
            this.colCommand.Items.AddRange(new object[] {
                "Play", "Stop", "Pause", "PlayPause", "NextTrack", "PreviousTrack",
                "Restart", "Seek", "ToggleLoop", "LoopStart", "LoopEnd", "LoopIn", "LoopOut",
                "VolumeUp", "VolumeDown", "MasterVolume", "TrackVolume",
                "SelectTrack", "ToggleMute", "ToggleSolo", "OpenWindow", "CloseWindow", "None" });
            this.dataGridView_mappings.Columns.AddRange(new DataGridViewColumn[]
            {
                this.colChannel,
                this.colType,
                this.colNumber,
                this.colCommand
            });
            this.colChannel.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            this.colNumber.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            this.dataGridView_mappings.Location = new Point(12, 22);
            this.dataGridView_mappings.MultiSelect = false;
            this.dataGridView_mappings.Name = "dataGridView_mappings";
            this.dataGridView_mappings.RowHeadersVisible = false;
            this.dataGridView_mappings.Size = new Size(472, 240);
            this.dataGridView_mappings.TabIndex = 0;
            this.dataGridView_mappings.CellValidating += this.dataGridView_mappings_CellValidating;
            this.dataGridView_mappings.CellValueChanged += this.dataGridView_mappings_CellValueChanged;
            // 
            // colChannel
            // 
            this.colChannel.HeaderText = "Channel";
            this.colChannel.Name = "colChannel";
            this.colChannel.Width = 70;
            // 
            // colType
            // 
            this.colType.HeaderText = "Type";
            this.colType.Name = "colType";
            this.colType.Width = 100;
            // 
            // colNumber
            // 
            this.colNumber.HeaderText = "Number";
            this.colNumber.Name = "colNumber";
            this.colNumber.Width = 80;
            // 
            // colCommand
            // 
            this.colCommand.HeaderText = "Command";
            this.colCommand.Name = "colCommand";
            this.colCommand.Width = 200;
            // 
            // button_add
            // 
            this.button_add.Location = new Point(12, 270);
            this.button_add.Name = "button_add";
            this.button_add.Size = new Size(80, 23);
            this.button_add.TabIndex = 1;
            this.button_add.Text = "Add";
            this.button_add.UseVisualStyleBackColor = true;
            this.button_add.Click += this.button_add_Click;
            // 
            // button_remove
            // 
            this.button_remove.Location = new Point(100, 270);
            this.button_remove.Name = "button_remove";
            this.button_remove.Size = new Size(80, 23);
            this.button_remove.TabIndex = 2;
            this.button_remove.Text = "Remove";
            this.button_remove.UseVisualStyleBackColor = true;
            this.button_remove.Click += this.button_remove_Click;
            // 
            // button_reset
            // 
            this.button_reset.Location = new Point(188, 270);
            this.button_reset.Name = "button_reset";
            this.button_reset.Size = new Size(120, 23);
            this.button_reset.TabIndex = 3;
            this.button_reset.Text = "Reset Defaults";
            this.button_reset.UseVisualStyleBackColor = true;
            this.button_reset.Click += this.button_reset_Click;
            // 
            // groupStatus
            // 
            this.groupStatus.Controls.Add(this.label_status);
            this.groupStatus.Controls.Add(this.listView_messages);
            this.groupStatus.Font = new Font("Bahnschrift SemiLight Condensed", 9.75F, FontStyle.Regular, GraphicsUnit.Point, 0);
            this.groupStatus.Location = new Point(12, 425);
            this.groupStatus.Name = "groupStatus";
            this.groupStatus.Size = new Size(496, 185);
            this.groupStatus.TabIndex = 2;
            this.groupStatus.TabStop = false;
            this.groupStatus.Text = "Status";
            // 
            // label_status
            // 
            this.label_status.AutoSize = true;
            this.label_status.Location = new Point(12, 22);
            this.label_status.Name = "label_status";
            this.label_status.Size = new Size(100, 15);
            this.label_status.TabIndex = 0;
            this.label_status.Text = "Not connected";
            // 
            // listView_messages
            // 
            this.listView_messages.Columns.AddRange(new ColumnHeader[]
            {
                this.columnChannel,
                this.columnType,
                this.columnNumber,
                this.columnCommand
            });
            this.listView_messages.HideSelection = false;
            this.listView_messages.Location = new Point(12, 42);
            this.listView_messages.MultiSelect = false;
            this.listView_messages.Name = "listView_messages";
            this.listView_messages.Size = new Size(472, 130);
            this.listView_messages.TabIndex = 1;
            this.listView_messages.View = View.Details;
            // 
            // columnChannel
            // 
            this.columnChannel.Text = "Ch";
            this.columnChannel.Width = 40;
            // 
            // columnType
            // 
            this.columnType.Text = "Type";
            this.columnType.Width = 80;
            // 
            // columnNumber
            // 
            this.columnNumber.Text = "Num";
            this.columnNumber.Width = 60;
            // 
            // columnCommand
            // 
            this.columnCommand.Text = "Command";
            this.columnCommand.Width = 280;
            // 
            // MidiControllerDialog
            // 
            this.AcceptButton = this.button_connect;
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(520, 620);
            this.Controls.Add(this.groupDevice);
            this.Controls.Add(this.groupMappings);
            this.Controls.Add(this.groupStatus);
            this.Font = new Font("Bahnschrift SemiLight Condensed", 9.75F, FontStyle.Regular, GraphicsUnit.Point, 0);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.Name = "MidiControllerDialog";
            this.StartPosition = FormStartPosition.CenterParent;
            this.Text = "MIDI Controller";
            this.groupDevice.ResumeLayout(false);
            this.groupDevice.PerformLayout();
            this.groupMappings.ResumeLayout(false);
            this.groupStatus.ResumeLayout(false);
            this.groupStatus.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)this.dataGridView_mappings).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private GroupBox groupDevice;
        private Label label_input;
        private ComboBox comboBox_input;
        private Label label_output;
        private ComboBox comboBox_output;
        private Button button_connect;
        private Button button_disconnect;
        private GroupBox groupMappings;
        private DataGridView dataGridView_mappings;
        private DataGridViewTextBoxColumn colChannel;
        private DataGridViewComboBoxColumn colType;
        private DataGridViewTextBoxColumn colNumber;
        private DataGridViewComboBoxColumn colCommand;
        private Button button_add;
        private Button button_remove;
        private Button button_reset;
        private GroupBox groupStatus;
        private Label label_status;
        private ListView listView_messages;
        private ColumnHeader columnChannel;
        private ColumnHeader columnType;
        private ColumnHeader columnNumber;
        private ColumnHeader columnCommand;
    }
}
