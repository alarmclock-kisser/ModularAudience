using ModularAudience.Audio.Vocal;

namespace ModularAudience.Forms.Modules.Dialogs;

public sealed class VocalVoiceProfileDesignerDialog : Form
{
    private readonly VocalVoiceProfileStore store;
    private VocalVoiceProfile profile;
    private readonly TextBox nameBox = new() { Width = 200 };
    private readonly Dictionary<string, NumericUpDown> numbers = [];
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true, Text = "Design a voice color profile." };

    public VocalVoiceProfile? ResultProfile { get; private set; }

    public VocalVoiceProfileDesignerDialog(VocalVoiceProfile? initial = null, VocalVoiceProfileStore? store = null)
    {
        this.store = store ?? VocalVoiceProfileStore.Default();
        profile = (initial ?? VocalVoiceProfile.FromPreset(VocalVoicePreset.BuiltIn[2])).Clone();

        Text = "Voice Profile Designer"; Name = "VocalVoiceProfileDesignerDialog";
        ClientSize = new Size(420, 460); StartPosition = FormStartPosition.CenterParent;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10) };
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.AutoSize));

        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        top.Controls.Add(new Label { Text = "Profile name", AutoSize = true, Margin = new Padding(4, 8, 2, 0) });
        nameBox.Text = profile.Name;
        top.Controls.Add(nameBox);
        root.Controls.Add(top, 0, 0);

        var paramsPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, Padding = new Padding(4) };
        void Number(string label, string prop, decimal min, decimal max, decimal inc)
        {
            var panel = new FlowLayoutPanel { Width = 190, Height = 50, FlowDirection = FlowDirection.TopDown };
            var caption = new Label { Text = label, AutoSize = true };
            var num = new NumericUpDown { Minimum = min, Maximum = max, Increment = inc, DecimalPlaces = inc < 1 ? 2 : 0, Width = 170 };
            var pi = typeof(VocalVoiceProfile).GetProperty(prop)!;
            num.Value = Math.Clamp(Convert.ToDecimal(pi.GetValue(profile)), num.Minimum, num.Maximum);
            num.ValueChanged += (_, _) => pi.SetValue(profile, Convert.ChangeType(num.Value, pi.PropertyType));
            numbers[prop] = num;
            panel.Controls.Add(caption); panel.Controls.Add(num);
            paramsPanel.Controls.Add(panel);
        }
        Number("Base pitch (Hz)", nameof(profile.BasePitchHz), 60, 400, 1);
        Number("Formant scale", nameof(profile.FormantScale), 0.5m, 1.5m, 0.01m);
        Number("Breathiness", nameof(profile.Breathiness), 0, 1, 0.01m);
        Number("Roughness", nameof(profile.Roughness), 0, 1, 0.01m);
        Number("Vibrato (cents)", nameof(profile.VibratoCents), 0, 50, 1);
        Number("Vibrato rate (Hz)", nameof(profile.VibratoHz), 0, 12, 0.1m);
        Number("Voicing", nameof(profile.Voicing), 0, 1, 0.01m);
        Number("Noise", nameof(profile.Noise), 0, 1, 0.01m);
        root.Controls.Add(paramsPanel, 0, 1);

        var baseRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        baseRow.Controls.Add(new Label { Text = "Base preset", AutoSize = true, Margin = new Padding(4, 8, 2, 0) });
        var baseCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        foreach (var p in VocalVoicePreset.BuiltIn) baseCombo.Items.Add(p.Name);
        baseCombo.SelectedIndex = Math.Max(0, VocalVoicePreset.BuiltIn.ToList().FindIndex(p => p.Id == profile.BasePresetId));
        baseCombo.SelectedIndexChanged += (_, _) =>
        {
            var preset = VocalVoicePreset.BuiltIn[baseCombo.SelectedIndex];
            var fp = VocalVoiceProfile.FromPreset(preset, nameBox.Text.Trim());
            // copy params into current profile
            profile.BasePitchHz = fp.BasePitchHz;
            profile.FormantScale = fp.FormantScale;
            profile.Breathiness = fp.Breathiness;
            profile.Roughness = fp.Roughness;
            profile.VibratoCents = fp.VibratoCents;
            profile.VibratoHz = fp.VibratoHz;
            profile.Voicing = fp.Voicing;
            profile.Noise = fp.Noise;
            profile.BasePresetId = fp.BasePresetId;
            profile.Singing = fp.Singing;
            foreach (var kv in numbers)
            {
                var pi = typeof(VocalVoiceProfile).GetProperty(kv.Key)!;
                kv.Value.Value = Math.Clamp(Convert.ToDecimal(pi.GetValue(profile)), kv.Value.Minimum, kv.Value.Maximum);
            }
        };
        baseRow.Controls.Add(baseCombo);
        root.Controls.Add(baseRow, 0, 2);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(6) };
        var saveBtn = new Button { Text = "Save Profile", AutoSize = true };
        var cancelBtn = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        bottom.Controls.Add(saveBtn); bottom.Controls.Add(cancelBtn); bottom.Controls.Add(status);
        root.Controls.Add(bottom, 0, 3);

        Controls.Add(root);
        AcceptButton = saveBtn; CancelButton = cancelBtn;

        saveBtn.Click += (_, _) =>
        {
            var nm = nameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(nm)) { status.Text = "Enter a profile name."; return; }
            profile.Name = nm;
            store.Save(profile);
            ResultProfile = profile.Clone();
            status.Text = "Saved: " + nm;
            DialogResult = DialogResult.OK;
            Close();
        };
    }
}