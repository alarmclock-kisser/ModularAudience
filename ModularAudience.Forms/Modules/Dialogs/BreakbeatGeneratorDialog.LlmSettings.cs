using System.Globalization;
using ModularAudience.Audio;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class BreakbeatGeneratorDialog
{
    private BreakbeatLlmSettings llmSettings = new();
    private readonly System.Windows.Forms.Timer llmSettingsTimer = new() { Interval = 500 };
    private readonly Dictionary<string, (ToolStripMenuItem Item, ToolStripTextBox Editor)> llmParameterEditors = [];
    private readonly BreakbeatLlmContext llmContext = new();
    private (int Generation, string User, string Assistant)? pendingLlmTurn;
    private static event Action<BreakbeatGeneratorDialog, BreakbeatLlmSettings>? SharedLlmSettingsChanged;
    private static BreakbeatLlmSettings? sharedLlmSettings;
    private bool syncingLlmSettings;
    private ToolStripMenuItem? keepContextItem, jsonResponseItem;
    private void InitializeLlmSettings()
    {
        try { llmSettings = sharedLlmSettings?.Copy() ?? BreakbeatLlmSettings.Load(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { LogManager.Log("Unable to load breakbeat LLM settings: " + error.Message); llmSettings.Normalize(); }
        textBox_apiUrl.Text = llmSettings.Address;
        textBox_prompt.Text = llmSettings.Prompt;
        llmSettingsTimer.Tick += (_, _) => { llmSettingsTimer.Stop(); PersistLlmSettings(); };
        foreach (var text in new[] { textBox_apiUrl, textBox_prompt })
            text.TextChanged += (_, _) =>
            {
                if (syncingLlmSettings) return;
                llmSettings.Address = textBox_apiUrl.Text.Trim(); llmSettings.Prompt = textBox_prompt.Text;
                PublishLlmSettings();
                llmSettingsTimer.Stop(); llmSettingsTimer.Start();
            };
        var menu = new ContextMenuStrip(components!);
        foreach (var definition in BreakbeatLlmSettings.Definitions)
        {
            var parameter = llmSettings.Parameters[definition.Key];
            var item = new ToolStripMenuItem(definition.Label + (definition.Extension ? " (server extension)" : ""))
            { CheckOnClick = false, Checked = parameter.Enabled, ToolTipText = $"API field: {definition.Key}. Only sent when checked." };
            var editor = new ToolStripTextBox { AutoSize = false, Width = 180, Text = parameter.Value.ToString("R", CultureInfo.InvariantCulture) };
            var hint = new ToolStripMenuItem($"Range: {definition.Minimum} … {definition.Maximum}") { Enabled = false };
            bool Commit()
            {
                try
                {
                    parameter.Value = definition.Parse(editor.Text); editor.BackColor = SystemColors.Window;
                    editor.ToolTipText = definition.Key; editor.Text = parameter.Value.ToString("R", CultureInfo.InvariantCulture);
                    PersistLlmSettings(); return true;
                }
                catch (ArgumentException error) { editor.BackColor = Color.MistyRose; editor.ToolTipText = error.Message; return false; }
            }
            editor.TextChanged += (_, _) =>
            {
                try { definition.Parse(editor.Text); editor.BackColor = SystemColors.Window; }
                catch (ArgumentException) { editor.BackColor = Color.MistyRose; }
            };
            editor.Leave += (_, _) => Commit();
            editor.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; if (Commit()) item.HideDropDown(); } };
            var reset = new ToolStripMenuItem($"Default: {definition.Default}");
            reset.Click += (_, _) => { editor.Text = definition.Default.ToString("R", CultureInfo.InvariantCulture); Commit(); };
            item.DropDownItems.Add(editor); item.DropDownItems.Add(hint); item.DropDownItems.Add(reset);
            item.CheckedChanged += (_, _) => { parameter.Enabled = item.Checked; PersistLlmSettings(); };
            item.Click += (_, _) => { item.Checked = !item.Checked; item.ShowDropDown(); };
            llmParameterEditors.Add(definition.Key, (item, editor)); menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        var context = new ToolStripMenuItem("Keep context") { Name = "keepContext", CheckOnClick = true, Checked = llmSettings.KeepContext,
            ToolTipText = "Keep up to 8 successful turns in RAM for variations. Unchecking clears them immediately." };
        context.CheckedChanged += (_, _) =>
        {
            llmSettings.KeepContext = context.Checked;
            if (!context.Checked) { llmContext.Clear(); pendingLlmTurn = null; }
            PersistLlmSettings();
        };
        menu.Items.Add(context);
        keepContextItem = context;
        menu.Items.Add("Clear conversation context", null, (_, _) => { llmContext.Clear(); pendingLlmTurn = null; });
        var json = new ToolStripMenuItem("Request JSON response") { CheckOnClick = true, Checked = llmSettings.JsonResponse };
        jsonResponseItem = json;
        json.CheckedChanged += (_, _) => { llmSettings.JsonResponse = json.Checked; PersistLlmSettings(); }; menu.Items.Add(json);
        var defaults = new ToolStripMenuItem("Reset generation parameters to defaults");
        defaults.Click += (_, _) =>
        {
            syncingLlmSettings = true;
            try
            {
                foreach (var definition in BreakbeatLlmSettings.Definitions)
                {
                    var parameter = llmSettings.Parameters[definition.Key];
                    parameter.Value = definition.Default; parameter.Enabled = definition.EnabledByDefault;
                    llmParameterEditors[definition.Key].Editor.Text = definition.Default.ToString("R", CultureInfo.InvariantCulture);
                    llmParameterEditors[definition.Key].Item.Checked = definition.EnabledByDefault;
                }
                llmSettings.JsonResponse = true; json.Checked = true;
            }
            finally { syncingLlmSettings = false; }
            PersistLlmSettings();
        };
        menu.Items.Add(defaults); button_llm.ContextMenuStrip = menu;
        this.FormClosing += (_, _) => { llmSettingsTimer.Stop(); PersistLlmSettings(); };
        this.FormClosed += (_, _) => { llmContext.Clear(); pendingLlmTurn = null; };
        SharedLlmSettingsChanged += ReceiveSharedLlmSettings;
        this.Disposed += (_, _) => { SharedLlmSettingsChanged -= ReceiveSharedLlmSettings; llmSettingsTimer.Dispose(); llmContext.Clear(); pendingLlmTurn = null; };
    }
    private void PersistLlmSettings()
    {
        if (syncingLlmSettings) return;
        llmSettingsTimer.Stop();
        llmSettings.Address = textBox_apiUrl.Text.Trim(); llmSettings.Prompt = textBox_prompt.Text;
        PublishLlmSettings();
        try
        {
            llmSettings.Save();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { LogManager.Log("Unable to save breakbeat LLM settings: " + error.Message); }
    }
    private void PublishLlmSettings()
    {
        sharedLlmSettings = llmSettings.Copy();
        SharedLlmSettingsChanged?.Invoke(this, sharedLlmSettings);
    }
    private void ReceiveSharedLlmSettings(BreakbeatGeneratorDialog sender, BreakbeatLlmSettings updated)
    {
        if (ReferenceEquals(sender, this) || this.IsDisposed || this.Disposing) return;
        if (this.InvokeRequired)
        {
            this.BeginInvoke(() => ReceiveSharedLlmSettings(sender, updated));
            return;
        }
        syncingLlmSettings = true;
        llmSettingsTimer.Stop();
        try
        {
            textBox_apiUrl.Text = updated.Address; textBox_prompt.Text = updated.Prompt;
            llmSettings.Address = updated.Address; llmSettings.Prompt = updated.Prompt;
            foreach (var definition in BreakbeatLlmSettings.Definitions)
            {
                var target = llmSettings.Parameters[definition.Key]; var incoming = updated.Parameters[definition.Key];
                target.Value = incoming.Value; target.Enabled = incoming.Enabled;
                llmParameterEditors[definition.Key].Editor.Text = incoming.Value.ToString("R", CultureInfo.InvariantCulture);
                llmParameterEditors[definition.Key].Item.Checked = incoming.Enabled;
            }
            llmSettings.KeepContext = updated.KeepContext; llmSettings.JsonResponse = updated.JsonResponse;
            if (keepContextItem != null) keepContextItem.Checked = updated.KeepContext;
            if (jsonResponseItem != null) jsonResponseItem.Checked = updated.JsonResponse;
            if (!updated.KeepContext) { llmContext.Clear(); pendingLlmTurn = null; }
        }
        finally { llmSettingsTimer.Stop(); syncingLlmSettings = false; }
    }
    private BreakbeatLlmSettings CaptureLlmSettings()
    {
        foreach (var definition in BreakbeatLlmSettings.Definitions)
        {
            var editor = llmParameterEditors[definition.Key];
            if (editor.Item.Checked) llmSettings.Parameters[definition.Key].Value = definition.Parse(editor.Editor.Text);
        }
        PersistLlmSettings();
        return llmSettings.Copy();
    }
}
