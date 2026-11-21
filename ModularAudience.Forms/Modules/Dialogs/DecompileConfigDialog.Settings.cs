using ModularAudience.Audio;
using ModularAudience.Audio.Processing;
using ModularAudience.Audio.Processors_V4;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModularAudience.Forms.Modules.Dialogs;

public sealed partial class DecompileConfigDialog
{
    private static readonly JsonSerializerOptions CloneJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private DecompileDialogSettings persistedSettings = new();

    private void LoadPersistentSettings()
    {
        try
        {
            this.persistedSettings = DecompileDialogSettings.Load(this.settingsPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            this.persistedSettings = new DecompileDialogSettings();
            LogManager.Log($"Decompile settings load failed: {error}");
        }
    }

    private void ApplyPersistentSettings()
    {
        DecompileDialogSettings settings = this.persistedSettings;
        if (settings.Demucs is not null)
        {
            this.demucsSettings = CloneSettings(settings.Demucs) ?? new DemucsDialogSettings();
        }
        SetSelectedIndex(this.comboBox_separationMethod, settings.SeparationMethod);
        SetSelectedIndex(this.comboBox_atomizer, settings.AtomizerMethod);
        SetSelectedIndex(this.comboBox_atomizerSensitivity, settings.AtomizerSensitivity);
        this.numeric_minSlice.Value = ClampNumeric(settings.MinSliceMs, this.numeric_minSlice);
        this.numeric_tailPadding.Value = ClampNumeric(settings.TailPaddingMs, this.numeric_tailPadding);
        this.checkBox_singleAtomFallback.Checked = settings.AllowSingleAtomFallback;
        this.checkBox_atomizerDeduplicate.Checked = settings.DeduplicateAtomizer;
        this.numeric_maxVariants.Value = ClampNumeric(settings.MaxVariantsPerCluster, this.numeric_maxVariants);
        this.numeric_minimumRms.Value = ClampNumeric(
            (decimal)settings.MinimumRmsLevel,
            this.numeric_minimumRms);
        this.numeric_minimumDuration.Value = ClampNumeric(
            settings.MinimumAtomicDurationMs,
            this.numeric_minimumDuration);
        this.numeric_similarity.Value = ClampNumeric(
            (decimal)settings.SimilarityThreshold,
            this.numeric_similarity);
        this.numeric_legacySimilarity.Value = ClampNumeric(
            (decimal)settings.LegacySimilarityThreshold,
            this.numeric_legacySimilarity);
        this.checkBox_legacyAutoSimilarity.Checked = settings.LegacyAutomaticSimilarity;

        this.checkBox_projectOutput.Checked = settings.ProjectOutput;
        this.checkBox_collectionOutput.Checked = settings.CollectionOutput;
        if (!this.checkBox_projectOutput.Checked && !this.checkBox_collectionOutput.Checked)
        {
            this.checkBox_projectOutput.Checked = true;
        }

        SetSelectedIndex(this.comboBox_projectDetail, settings.ProjectDetail);
        SetSelectedIndex(this.comboBox_projectOverlap, settings.ProjectOverlap);
        this.numeric_projectAttack.Value = ClampNumeric(settings.ProjectAttackMs, this.numeric_projectAttack);
        this.checkBox_projectSnap.Checked = settings.ProjectSnapToGrid;
        SetSelectedIndex(this.comboBox_projectSnap, $"1/{settings.ProjectSnapDivision}");
        this.checkBox_collectionDeduplicate.Checked = settings.DeduplicateCollections;
        this.numeric_workers.Value = ClampNumeric(settings.MaxWorkers, this.numeric_workers);

        this.numeric_analogWindow.Value = ClampNumeric(settings.AnalogWindowSize, this.numeric_analogWindow);
        this.numeric_analogOverlap.Value = ClampNumeric(settings.AnalogOverlapPercent, this.numeric_analogOverlap);
        this.numeric_analogSharpness.Value = ClampNumeric(settings.AnalogSharpness, this.numeric_analogSharpness);
        this.checkBox_analogSubtract.Checked = settings.AnalogSubtractFromOriginal;
        this.checkBox_analogTimbre.Checked = settings.AnalogTimbreAware;
        this.checkBox_analogHarmonic.Checked = settings.AnalogHarmonicAware;
        if (settings.AnalogBands.Count > 0)
        {
            this.dataGridView_analogBands.Rows.Clear();
            foreach (DecompileAnalogBandState band in settings.AnalogBands)
            {
                this.dataGridView_analogBands.Rows.Add(band.Name, band.LowHz, band.HighHz);
            }
        }

        SetSelectedIndex(this.comboBox_deterministicPreset, settings.DeterministicPreset);
        this.ApplyDeterministicSettings(settings.Deterministic);
        this.checkBox_deterministicAutomaticProfiles.Checked = settings.DeterministicAutomaticProfiles;
        this.ApplyDeterministicProfiles(settings.DeterministicProfiles);
        this.UpdateSeparationPanel();
        this.UpdateAtomizerPanel();
        this.UpdateOutputControls();
    }

    private void SavePersistentSettings()
    {
        try
        {
            this.persistedSettings = this.CapturePersistentSettings();
            this.persistedSettings.Save(this.settingsPath);
        }
        catch (Exception error)
        {
            LogManager.Log($"Decompile settings save failed: {error}");
        }
    }

    private DecompileDialogSettings CapturePersistentSettings()
    {
        var settings = new DecompileDialogSettings
        {
            SeparationMethod = this.comboBox_separationMethod.SelectedIndex,
            AtomizerMethod = this.comboBox_atomizer.SelectedIndex,
            AtomizerSensitivity = this.comboBox_atomizerSensitivity.SelectedIndex,
            MinSliceMs = (int)this.numeric_minSlice.Value,
            TailPaddingMs = (int)this.numeric_tailPadding.Value,
            AllowSingleAtomFallback = this.checkBox_singleAtomFallback.Checked,
            DeduplicateAtomizer = this.checkBox_atomizerDeduplicate.Checked,
            MaxVariantsPerCluster = (int)this.numeric_maxVariants.Value,
            MinimumRmsLevel = (float)this.numeric_minimumRms.Value,
            MinimumAtomicDurationMs = (int)this.numeric_minimumDuration.Value,
            SimilarityThreshold = (float)this.numeric_similarity.Value,
            LegacySimilarityThreshold = (float)this.numeric_legacySimilarity.Value,
            LegacyAutomaticSimilarity = this.checkBox_legacyAutoSimilarity.Checked,
            ProjectOutput = this.checkBox_projectOutput.Checked,
            CollectionOutput = this.checkBox_collectionOutput.Checked,
            ProjectDetail = this.comboBox_projectDetail.SelectedIndex,
            ProjectOverlap = this.comboBox_projectOverlap.SelectedIndex,
            ProjectAttackMs = (int)this.numeric_projectAttack.Value,
            ProjectSnapToGrid = this.checkBox_projectSnap.Checked,
            ProjectSnapDivision = this.comboBox_projectSnap.SelectedItem is string value
                && value.StartsWith("1/", StringComparison.Ordinal)
                && int.TryParse(value.AsSpan(2), out int division)
                ? division
                : 16,
            DeduplicateCollections = this.checkBox_collectionDeduplicate.Checked,
            MaxWorkers = (int)this.numeric_workers.Value,
            AnalogWindowSize = (int)this.numeric_analogWindow.Value,
            AnalogOverlapPercent = (int)this.numeric_analogOverlap.Value,
            AnalogSharpness = (int)this.numeric_analogSharpness.Value,
            AnalogSubtractFromOriginal = this.checkBox_analogSubtract.Checked,
            AnalogTimbreAware = this.checkBox_analogTimbre.Checked,
            AnalogHarmonicAware = this.checkBox_analogHarmonic.Checked,
            AnalogBands = this.dataGridView_analogBands.Rows
                .Cast<DataGridViewRow>()
                .Where(row => !row.IsNewRow)
                .Select(row => new DecompileAnalogBandState(
                    Convert.ToString(row.Cells[0].Value, CultureInfo.InvariantCulture) ?? string.Empty,
                    Convert.ToString(row.Cells[1].Value, CultureInfo.InvariantCulture) ?? string.Empty,
                    Convert.ToString(row.Cells[2].Value, CultureInfo.InvariantCulture) ?? string.Empty))
                .ToList(),
            DeterministicPreset = this.comboBox_deterministicPreset.Text,
            Deterministic = this.CaptureDeterministicSettings(),
            DeterministicAutomaticProfiles = this.checkBox_deterministicAutomaticProfiles.Checked,
            DeterministicProfiles = this.CaptureDeterministicProfiles(),
            Demucs = this.CaptureDemucsSettings()
        };
        return settings;
    }

    private DemucsDialogSettings? CaptureDemucsSettings()
    {
        DemucsDialogSettings source = this.demucsSettings;
        if (!this.initializingDemucs && this.comboBox_graphOptimization.SelectedItem is not null)
        {
            source.Options = this.ReadDemucsOptions((int)this.numeric_workers.Value);
            source.SelectedModel = (this.comboBox_demucsModel.SelectedItem as DemucsModelChoice)?.Path
                ?? source.SelectedModel;
            source.KeepModelLoadedSeconds = (int)this.numeric_keepModelLoaded.Value;
            source.SelectedDirectMLDeviceIds = this.checkedListBox_directMLDevices.CheckedItems
                .Cast<DirectMLChoice>()
                .Select(choice => choice.Adapter.Id)
                .ToArray();
        }

        return CloneSettings(source);
    }

    private static DemucsDialogSettings? CloneSettings(DemucsDialogSettings? settings)
    {
        if (settings is null)
        {
            return null;
        }

        return JsonSerializer.Deserialize<DemucsDialogSettings>(
            JsonSerializer.Serialize(settings, CloneJsonOptions),
            CloneJsonOptions);
    }

    private void ApplyDeterministicProfiles(IReadOnlyList<string> profileIds)
    {
        HashSet<string> selected = profileIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < this.checkedListBox_deterministicProfiles.Items.Count; index++)
        {
            if (this.checkedListBox_deterministicProfiles.Items[index] is InstrumentProfile profile)
            {
                this.checkedListBox_deterministicProfiles.SetItemChecked(
                    index,
                    selected.Contains(profile.Id.ToString()));
            }
        }
    }

    private string[] CaptureDeterministicProfiles()
    {
        return this.checkedListBox_deterministicProfiles.CheckedItems
            .Cast<InstrumentProfile>()
            .Select(profile => profile.Id.ToString())
            .ToArray();
    }

    private static void SetSelectedIndex(ComboBox comboBox, int index)
    {
        comboBox.SelectedIndex = comboBox.Items.Count == 0
            ? -1
            : Math.Clamp(index, 0, comboBox.Items.Count - 1);
    }

    private static void SetSelectedIndex(ComboBox comboBox, string item)
    {
        int index = comboBox.Items.Cast<object>()
            .Select(value => value.ToString())
            .ToList()
            .FindIndex(value => string.Equals(value, item, StringComparison.Ordinal));
        SetSelectedIndex(comboBox, Math.Max(0, index));
    }

    private static decimal ClampNumeric(decimal value, NumericUpDown control) =>
        Math.Clamp(value, control.Minimum, control.Maximum);

    private static decimal ClampNumeric(int value, NumericUpDown control) =>
        Math.Clamp((decimal)value, control.Minimum, control.Maximum);
}
