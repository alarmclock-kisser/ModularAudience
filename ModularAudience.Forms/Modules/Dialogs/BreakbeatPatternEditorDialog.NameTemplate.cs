using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using ModularAudience.Audio;
using ModularAudience.Generators;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class BreakbeatPatternEditorDialog
{
    private BreakbeatPatternEditorNameSettings nameTemplateSettings = new();
    private bool nameTemplateSettingsErrorShown;

    private void LoadNameTemplateSettings()
    {
        try
        {
            this.nameTemplateSettings = BreakbeatPatternEditorNameSettings.Load();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException)
        {
            this.nameTemplateSettings = new BreakbeatPatternEditorNameSettings();
            this.ReportNameTemplateSettingsFailure("load", error);
        }

        this.nameTemplateSettings.RecentTemplates = this.nameTemplateSettings.RecentTemplates
            .Where(template => BreakbeatPatternNameTemplate.TryParse(template, out _, out _))
            .Distinct(StringComparer.Ordinal)
            .Take(BreakbeatPatternEditorNameSettings.MaximumRecentTemplates)
            .ToList();
        this.RefreshNameTemplateItems(this.nameTemplateSettings.Template);
        this.UpdateNameTemplateValidation();
    }

    private void comboBox_nameTemplate_TextChanged(object? sender, EventArgs e)
    {
        this.nameTemplateSettings.Template = this.comboBox_nameTemplate.Text;
        this.UpdateNameTemplateValidation();
        if (!this.initializing)
        {
            this.SaveNameTemplateSettings();
        }
    }

    private void comboBox_nameTemplate_SelectionChangeCommitted(object? sender, EventArgs e)
    {
        if (this.comboBox_nameTemplate.SelectedItem is not string selectedTemplate
            || !BreakbeatPatternNameTemplate.TryParse(selectedTemplate, out _, out _))
        {
            return;
        }

        this.nameTemplateSettings.RecordSuccessfulTemplate(selectedTemplate);
        this.nameTemplateSettings.Template = selectedTemplate;
        this.RefreshNameTemplateItems(selectedTemplate);
        this.UpdateNameTemplateValidation();
        this.SaveNameTemplateSettings();
    }

    private void button_nameTemplateHelp_Click(object? sender, EventArgs e)
    {
        const string help = """
            Name template syntax

            Use one brace group for the per-note template. It is repeated in track order and then by note start time. Text and separators inside the group are copied literally.

            n, nn, nnn, ...  First 1, 2, 3, ... characters of the visible track/sample name.
            p                  Pitch sign (+ or -; zero is 0).
            pp                 Signed whole-semitone pitch.
            ppp                Full signed pitch. Fractions use a Unicode glyph when available, e.g. +3.25 becomes +3¼; otherwise slash notation is used.
            d                  Exact duration in current grid cells, including fractional cells.
            b, bb, bbb, ...    First 1, 2, 3, ... BPM digits. Trailing .0 is removed; for 100.5, {bbb} is 100 and {bbbb} is 100.5.

            Put BPM variables in a separate brace group outside the per-note group. Underscores, spaces, square brackets, and other punctuation are literal.

            Example:
            {nnn_ppp_d }[{bbb}]
            a_0_2 s_-4_1 s_-3_1 ... [100]

            An empty or invalid template uses the generic name. Invalid text is shown in red. Successfully saved templates appear in the dropdown.
            """;
        MessageBox.Show(this, help, "Name Template Help", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private string GetSavedPatternName(
        IReadOnlyList<BreakbeatPatternNote> notes,
        decimal bpm,
        out string? successfulTemplate)
    {
        successfulTemplate = null;
        string genericName = $"Break-Beat Generated [{(float)bpm:F1}]";
        string templateText = this.comboBox_nameTemplate.Text;
        if (!BreakbeatPatternNameTemplate.TryParse(templateText, out BreakbeatPatternNameTemplate? template, out _)
            || template is null)
        {
            this.UpdateNameTemplateValidation();
            return genericName;
        }

        string[] sampleNames = Enumerable.Range(0, this.samples.Count)
            .Select(index => ResolveTrackHeaderLabel(
                index < this.rowLabels.Count ? this.rowLabels[index] : null,
                this.samples[index].Name,
                index))
            .ToArray();
        string name = template.Format(notes, sampleNames, this.GetTicksPerStep(), bpm);
        if (string.IsNullOrWhiteSpace(name))
        {
            return genericName;
        }

        this.comboBox_nameTemplate.ForeColor = SystemColors.WindowText;
        successfulTemplate = templateText;
        return name;
    }

    private string GetUniqueSavedAudioName(string name)
    {
        HashSet<string> existingNames = this.savedCollection is { IsDisposed: false } collection
            ? collection.AudioC.Audios.Select(audio => audio.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        if (!existingNames.Contains(name))
        {
            return name;
        }

        for (int suffix = 1; ; suffix++)
        {
            string candidate = $"{name} ({suffix})";
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private void RecordSuccessfulNameTemplate(string template)
    {
        this.nameTemplateSettings.Template = this.comboBox_nameTemplate.Text;
        this.nameTemplateSettings.RecordSuccessfulTemplate(template);
        this.RefreshNameTemplateItems(this.comboBox_nameTemplate.Text);
        this.SaveNameTemplateSettings();
    }

    private void RefreshNameTemplateItems(string text)
    {
        this.comboBox_nameTemplate.BeginUpdate();
        try
        {
            this.comboBox_nameTemplate.Items.Clear();
            this.comboBox_nameTemplate.Items.AddRange(this.nameTemplateSettings.RecentTemplates.ToArray());
            this.comboBox_nameTemplate.Text = text;
        }
        finally
        {
            this.comboBox_nameTemplate.EndUpdate();
        }
    }

    private void UpdateNameTemplateValidation()
    {
        string text = this.comboBox_nameTemplate.Text;
        this.comboBox_nameTemplate.ForeColor = string.IsNullOrWhiteSpace(text)
            || BreakbeatPatternNameTemplate.TryParse(text, out _, out _)
                ? SystemColors.WindowText
                : Color.Red;
    }

    private void SaveNameTemplateSettings()
    {
        try
        {
            this.nameTemplateSettings.Save();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            this.ReportNameTemplateSettingsFailure("save", error);
        }
    }

    private void ReportNameTemplateSettingsFailure(string action, Exception error)
    {
        string pastTense = action == "load" ? "loaded" : "saved";
        string message = $"Name-template settings could not be {pastTense}: {error.Message}";
        LogManager.Log($"Breakbeat name-template settings could not be {pastTense}.");
        LogManager.Log(error);
        if (this.nameTemplateSettingsErrorShown || this.IsDisposed)
        {
            return;
        }

        this.nameTemplateSettingsErrorShown = true;
        if (this.IsHandleCreated)
        {
            this.toolTip_pattern.Show(message, this.comboBox_nameTemplate, 8_000);
        }
        else
        {
            this.Shown += (_, _) => this.toolTip_pattern.Show(message, this.comboBox_nameTemplate, 8_000);
        }
    }
}
