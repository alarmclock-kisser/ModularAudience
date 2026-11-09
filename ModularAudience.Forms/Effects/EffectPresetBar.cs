namespace ModularAudience.Forms.Effects;

/// <summary>The identical preset controls used by Auto Tune, every new effect, and the stretch/pitch effect modes.</summary>
internal sealed class EffectPresetBar : FlowLayoutPanel
{
    internal ComboBox Presets { get; }
    internal TextBox PresetName { get; } = new() { Name = "presetName", Width = 165, PlaceholderText = "Preset name (optional)" };
    internal Button LoadButton { get; } = new() { Name = "loadPreset", Text = "Load", AutoSize = true };
    internal Button SaveButton { get; } = new() { Name = "savePreset", Text = "Save", AutoSize = true };
    internal Button RenameButton { get; } = new() { Name = "renamePreset", Text = "Rename", AutoSize = true };
    internal Button DeleteButton { get; } = new() { Name = "deletePreset", Text = "Delete", AutoSize = true };
    internal Button DefaultsButton { get; } = new() { Name = "resetDefaults", Text = "Defaults", AutoSize = true };

    internal EffectPresetBar(string selectorName = "effectPresets")
    {
        this.Name = "effectPresetBar"; this.Dock = DockStyle.Fill; this.AutoSize = true; this.WrapContents = true;
        this.Presets = new ComboBox { Name = selectorName, Width = 260, DropDownWidth = 540, MaxDropDownItems = 20, DropDownStyle = ComboBoxStyle.DropDownList };
        this.Controls.Add(new Label { Text = "Presets", AutoSize = true, Margin = new Padding(3, 7, 3, 0) });
        this.Controls.AddRange([this.Presets, this.PresetName, this.LoadButton, this.SaveButton, this.RenameButton, this.DeleteButton, this.DefaultsButton]);
    }

    internal void UpdateState(bool busy, bool hasSelection, bool isUser)
    {
        this.LoadButton.Enabled = !busy && hasSelection;
        this.SaveButton.Enabled = !busy;
        this.RenameButton.Enabled = this.DeleteButton.Enabled = !busy && isUser;
    }
}
