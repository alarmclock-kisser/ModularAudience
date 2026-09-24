using ModularAudience.MidiController;
using ModularAudience.MidiController.Models;

namespace ModularAudience.Forms.Modules.Dialogs
{
    public partial class MidiControllerDialog : Form
    {
        private readonly MidiControllerService _service;
        private readonly MidiCommandMapper _mapper;

        private static readonly string[] NoteCommands =
        [
            "Play", "Stop", "Pause", "PlayPause", "NextTrack", "PreviousTrack",
            "Restart", "Seek", "ToggleLoop", "LoopStart", "LoopEnd", "LoopIn", "LoopOut",
            "SelectTrack", "ToggleMute", "ToggleSolo", "OpenWindow", "CloseWindow", "None"
        ];

        private static readonly string[] CcCommands =
        [
            "MasterVolume", "VolumeUp", "VolumeDown", "TrackVolume",
            "ToggleLoop", "PlayPause", "Stop", "NextTrack", "PreviousTrack",
            "Restart", "Seek", "LoopIn", "LoopOut", "ToggleMute", "ToggleSolo",
            "OpenWindow", "CloseWindow", "None"
        ];

        private static readonly string[] CcTypes = ["CC", "Note On", "Note Off"];
        private bool IsClosing { get; set; }

        public MidiControllerDialog(MidiControllerService service, MidiCommandMapper mapper)
        {
            this._service = service;
            this._mapper = mapper;
            this.InitializeComponent();
            this.LoadDevicesAsync();
            this.RefreshMappingsGrid();
            this._service.MessageReceived += this.OnServiceMessageReceived;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            this.IsClosing = true;
            this._service.MessageReceived -= this.OnServiceMessageReceived;
            this._service.CloseInput();
            this._service.CloseOutput();
            base.OnFormClosing(e);
        }

        private async void LoadDevicesAsync()
        {
            var devices = await MidiControllerService.GetDevicesAsync();
            if (this.IsDisposed || !this.IsHandleCreated)
                return;

            this.comboBox_input.Items.Clear();
            this.comboBox_output.Items.Clear();

            foreach (var d in devices.Where(d => d.IsInput))
                this.comboBox_input.Items.Add(new DeviceItem(d.Id, d.Name));

            foreach (var d in devices.Where(d => d.IsOutput))
                this.comboBox_output.Items.Add(new DeviceItem(d.Id, d.Name));

            if (this.comboBox_input.Items.Count > 0) this.comboBox_input.SelectedIndex = 0;
            if (this.comboBox_output.Items.Count > 0) this.comboBox_output.SelectedIndex = 0;
        }

        private void RefreshMappingsGrid()
        {
            this.dataGridView_mappings.Rows.Clear();
            foreach (var (ch, cmd, d1, mapped) in this._mapper.GetCustomMappings())
            {
                int row = this.dataGridView_mappings.Rows.Add(ch, "CC", d1, mapped.ToString());
                this.SetRowType(row, "CC");
            }
        }

        private void SetRowType(int row, string type)
        {
            var rowCells = this.dataGridView_mappings.Rows[row].Cells;
            if (type == "Note On")
            {
                rowCells[1].Value = "Note On";
                rowCells[2].Value = 0;
                rowCells[3].Value = "Play";
            }
            else if (type == "Note Off")
            {
                rowCells[1].Value = "Note Off";
                rowCells[2].Value = 0;
                rowCells[3].Value = "Stop";
            }
        }

        private void button_add_Click(object? sender, EventArgs e)
        {
            int row = this.dataGridView_mappings.Rows.Add(0, "CC", 0, "None");
            this.dataGridView_mappings.CurrentCell = this.dataGridView_mappings.Rows[row].Cells[1];
        }

        private void button_remove_Click(object? sender, EventArgs e)
        {
            if (this.dataGridView_mappings.SelectedRows.Count == 0) return;
            int row = this.dataGridView_mappings.SelectedRows[0].Index;
            var cells = this.dataGridView_mappings.Rows[row].Cells;
            int ch = Convert.ToInt32(cells[0].Value);
            string type = cells[1].Value?.ToString() ?? "CC";
            int num = Convert.ToInt32(cells[2].Value);

            if (type == "CC")
                this._mapper.RemoveCustomMapping(ch, 0xB0, num);
            else if (type == "Note On")
                this._mapper.RemoveCustomMapping(ch, 0x90, num);
            else
                this._mapper.RemoveCustomMapping(ch, 0x80, num);

            this.dataGridView_mappings.Rows.RemoveAt(row);
        }

        private void button_reset_Click(object? sender, EventArgs e)
        {
            if (MessageBox.Show(this, "Remove all custom mappings and restore defaults?", "Reset",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            foreach (var (ch, cmd, d1, _) in this._mapper.GetCustomMappings())
                this._mapper.RemoveCustomMapping(ch, cmd, d1);

            this.RefreshMappingsGrid();
        }

        private void button_connect_Click(object? sender, EventArgs e)
        {
            if (this.comboBox_input.SelectedItem is not DeviceItem inputItem)
            {
                MessageBox.Show(this, "No MIDI input device selected.", "MIDI Controller",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!Task.Run(() => this._service.OpenInputAsync(new MidiDevice
            {
                Id = inputItem.Id,
                Name = inputItem.Name,
                IsInput = true
            })).Result)
            {
                MessageBox.Show(this, $"Failed to open input '{inputItem.Name}'.", "MIDI Controller",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (this.comboBox_output.SelectedItem is DeviceItem outputItem)
            {
                _ = this._service.OpenOutputAsync(new MidiDevice
                {
                    Id = outputItem.Id,
                    Name = outputItem.Name,
                    IsOutput = true
                });
            }

            this.label_status.Text = $"Input: {inputItem.Name}";
            this.button_connect.Enabled = false;
            this.button_disconnect.Enabled = true;
        }

        private void button_disconnect_Click(object? sender, EventArgs e)
        {
            this._service.CloseInput();
            this._service.CloseOutput();
            this.label_status.Text = "Not connected";
            this.button_connect.Enabled = true;
            this.button_disconnect.Enabled = false;
        }

        private void OnServiceMessageReceived(object? sender, MidiMessageInfo msg)
        {
            if (this.IsDisposed || this.IsClosing || !this.IsHandleCreated) return;
            this.BeginInvoke(() =>
            {
                if (this.IsDisposed || this.IsClosing)
                    return;

                string type = msg.Command == 0x90 ? "Note On" : msg.Command == 0x80 ? "Note Off" : "CC";
                string command = this._mapper.Map(msg).ToString();
                var item = new ListViewItem([
                    msg.Channel.ToString(), type, msg.Data1.ToString(), command
                ]);
                this.listView_messages.Items.Insert(0, item);
                if (this.listView_messages.Items.Count > 50)
                    this.listView_messages.Items.RemoveAt(this.listView_messages.Items.Count - 1);
            });
        }

        private void dataGridView_mappings_CellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.ColumnIndex == 0)
            {
                if (!int.TryParse(e.FormattedValue?.ToString(), out int ch) || ch < 0 || ch > 15)
                {
                    e.Cancel = true;
                    if (this.dataGridView_mappings.CurrentCell is not null)
                        this.dataGridView_mappings.CurrentCell.ErrorText = "0-15";
                }
            }
            else if (e.ColumnIndex == 2)
            {
                if (!int.TryParse(e.FormattedValue?.ToString(), out int num) || num < 0 || num > 127)
                {
                    e.Cancel = true;
                    if (this.dataGridView_mappings.CurrentCell is not null)
                        this.dataGridView_mappings.CurrentCell.ErrorText = "0-127";
                }
            }
        }

        private void dataGridView_mappings_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var cells = this.dataGridView_mappings.Rows[e.RowIndex].Cells;
            int ch = Convert.ToInt32(cells[0].Value);
            string type = cells[1].Value?.ToString() ?? "CC";
            int num = Convert.ToInt32(cells[2].Value);
            string command = cells[3].Value?.ToString() ?? "None";
            var midiCmd = (MidiCommand)Enum.Parse(typeof(MidiCommand), command);

            if (type == "CC")
                this._mapper.AddCustomMapping(ch, 0xB0, num, midiCmd);
            else if (type == "Note On")
                this._mapper.AddCustomMapping(ch, 0x90, num, midiCmd);
            else
                this._mapper.AddCustomMapping(ch, 0x80, num, midiCmd);

            this.UpdateTypeColumn(e.RowIndex, type);
        }

        private void UpdateTypeColumn(int row, string type)
        {
            var cells = this.dataGridView_mappings.Rows[row].Cells;
            if (type == "CC")
            {
                cells[3].Value = CcCommands;
                cells[2].Value = cells[2].Value;
            }
            else if (type == "Note On")
            {
                cells[3].Value = NoteCommands;
                cells[2].Value = 0;
            }
            else
            {
                cells[3].Value = NoteCommands;
                cells[2].Value = 0;
            }
        }

        private sealed record DeviceItem(int Id, string Name)
        {
            public override string ToString() => this.Name;
        }
    }
}
