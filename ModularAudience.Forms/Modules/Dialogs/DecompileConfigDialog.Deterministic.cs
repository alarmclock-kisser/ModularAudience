using ModularAudience.Audio.Processing;
using ModularAudience.Audio.Processors_V4;
using System.Collections.Immutable;
using System.ComponentModel;

namespace ModularAudience.Forms.Modules.Dialogs;

public sealed partial class DecompileConfigDialog
{
    private DeterministicSettingsEditor deterministicEditor = new();

    private void InitializeDeterministicUi()
    {
        foreach (InstrumentProfile profile in InstrumentProfileCatalog.All)
        {
            this.checkedListBox_deterministicProfiles.Items.Add(profile);
        }

        this.ApplyDeterministicPreset(this.comboBox_deterministicPreset.Text);
    }

    private void comboBox_deterministicPreset_SelectedIndexChanged(object? sender, EventArgs e)
    {
        this.ApplyDeterministicPreset(this.comboBox_deterministicPreset.Text);
    }

    private void ApplyDeterministicPreset(string? presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
        {
            presetName = "Balanced";
        }

        DeterministicSeparationPresetValues values = DeterministicSeparationPresets.Get(presetName);
        this.deterministicEditor = new DeterministicSettingsEditor
        {
            WindowSize = values.WindowSize,
            MaxComponents = values.MaxComponents,
            Iterations = values.Iterations,
            AnalysisFrames = values.AnalysisFrames,
            BlockFrames = values.BlockFrames,
            MedianFrames = values.MedianFrames,
            MedianBins = values.MedianBins,
            SeparationMargin = values.SeparationMargin,
            MaskFloor = values.MaskFloor,
            TransientPreservation = values.TransientPreservation,
            UseCqtAnalysis = values.UseCqtAnalysis,
            UseCqtSynthesis = false,
            UsePyin = values.UsePyin,
            UseIlrma = values.UseIlrma,
            CqtBinsPerOctave = values.CqtBinsPerOctave
        };
        this.propertyGrid_deterministic.SelectedObject = this.deterministicEditor;
    }

    private DeterministicAudioStemSeparator CreateDeterministicSeparator(int maxWorkers)
    {
        ImmutableArray<InstrumentProfileId> profiles = this.checkedListBox_deterministicProfiles
            .CheckedItems
            .Cast<InstrumentProfile>()
            .Select(profile => profile.Id)
            .Distinct()
            .ToImmutableArray();
        if (profiles.IsEmpty && !this.checkBox_deterministicAutomaticProfiles.Checked)
        {
            throw new ArgumentException(
                "Select an instrument profile or enable automatic deterministic profiles.");
        }

        InstrumentEnsembleMode ensembleMode = profiles.IsEmpty
            ? InstrumentEnsembleMode.Automatic
            : this.checkBox_deterministicAutomaticProfiles.Checked
                ? InstrumentEnsembleMode.ProfilesAndAutomatic
                : InstrumentEnsembleMode.ProfilesOnly;
        var settings = new DeterministicSeparationSettings
        {
            WindowSize = this.deterministicEditor.WindowSize,
            MaxComponents = this.deterministicEditor.MaxComponents,
            Iterations = this.deterministicEditor.Iterations,
            AnalysisFrames = this.deterministicEditor.AnalysisFrames,
            BlockFrames = this.deterministicEditor.BlockFrames,
            MedianFrames = this.deterministicEditor.MedianFrames,
            MedianBins = this.deterministicEditor.MedianBins,
            SeparationMargin = this.deterministicEditor.SeparationMargin,
            MaskFloor = this.deterministicEditor.MaskFloor,
            TransientPreservation = this.deterministicEditor.TransientPreservation,
            Threads = maxWorkers,
            UseCqtAnalysis = this.deterministicEditor.UseCqtAnalysis,
            UseCqtSynthesis = this.deterministicEditor.UseCqtSynthesis,
            UsePyin = this.deterministicEditor.UsePyin,
            UseIlrma = this.deterministicEditor.UseIlrma,
            CqtBinsPerOctave = this.deterministicEditor.CqtBinsPerOctave,
            CqtMinimumHz = this.deterministicEditor.CqtMinimumHz,
            PyinMinimumHz = this.deterministicEditor.PyinMinimumHz,
            PyinMaximumHz = this.deterministicEditor.PyinMaximumHz,
            IlrmaIterations = this.deterministicEditor.IlrmaIterations,
            IlrmaComponents = this.deterministicEditor.IlrmaComponents,
            EnsembleMode = ensembleMode,
            InstrumentProfiles = profiles
        };
        settings.Validate();
        DeterministicSeparationProcessor.ValidateSettingsForSource(this.sourceSnapshot, settings);
        return new DeterministicAudioStemSeparator(settings);
    }

    private DecompileDeterministicState CaptureDeterministicSettings()
    {
        return new DecompileDeterministicState
        {
            WindowSize = this.deterministicEditor.WindowSize,
            MaxComponents = this.deterministicEditor.MaxComponents,
            Iterations = this.deterministicEditor.Iterations,
            AnalysisFrames = this.deterministicEditor.AnalysisFrames,
            BlockFrames = this.deterministicEditor.BlockFrames,
            MedianFrames = this.deterministicEditor.MedianFrames,
            MedianBins = this.deterministicEditor.MedianBins,
            SeparationMargin = this.deterministicEditor.SeparationMargin,
            MaskFloor = this.deterministicEditor.MaskFloor,
            TransientPreservation = this.deterministicEditor.TransientPreservation,
            UseCqtAnalysis = this.deterministicEditor.UseCqtAnalysis,
            UseCqtSynthesis = this.deterministicEditor.UseCqtSynthesis,
            UsePyin = this.deterministicEditor.UsePyin,
            UseIlrma = this.deterministicEditor.UseIlrma,
            CqtBinsPerOctave = this.deterministicEditor.CqtBinsPerOctave,
            CqtMinimumHz = this.deterministicEditor.CqtMinimumHz,
            PyinMinimumHz = this.deterministicEditor.PyinMinimumHz,
            PyinMaximumHz = this.deterministicEditor.PyinMaximumHz,
            IlrmaIterations = this.deterministicEditor.IlrmaIterations,
            IlrmaComponents = this.deterministicEditor.IlrmaComponents
        };
    }

    private void ApplyDeterministicSettings(DecompileDeterministicState settings)
    {
        this.deterministicEditor = new DeterministicSettingsEditor
        {
            WindowSize = settings.WindowSize,
            MaxComponents = settings.MaxComponents,
            Iterations = settings.Iterations,
            AnalysisFrames = settings.AnalysisFrames,
            BlockFrames = settings.BlockFrames,
            MedianFrames = settings.MedianFrames,
            MedianBins = settings.MedianBins,
            SeparationMargin = settings.SeparationMargin,
            MaskFloor = settings.MaskFloor,
            TransientPreservation = settings.TransientPreservation,
            UseCqtAnalysis = settings.UseCqtAnalysis,
            UseCqtSynthesis = settings.UseCqtSynthesis,
            UsePyin = settings.UsePyin,
            UseIlrma = settings.UseIlrma,
            CqtBinsPerOctave = settings.CqtBinsPerOctave,
            CqtMinimumHz = settings.CqtMinimumHz,
            PyinMinimumHz = settings.PyinMinimumHz,
            PyinMaximumHz = settings.PyinMaximumHz,
            IlrmaIterations = settings.IlrmaIterations,
            IlrmaComponents = settings.IlrmaComponents
        };
        this.propertyGrid_deterministic.SelectedObject = this.deterministicEditor;
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    private sealed class DeterministicSettingsEditor
    {
        [Category("Core DSP")]
        [Description("STFT window size. Must be a power of two from 256 to 16384.")]
        public int WindowSize { get; set; } = 4096;

        [Category("Core DSP")]
        public int MaxComponents { get; set; } = 16;

        [Category("Core DSP")]
        public int Iterations { get; set; } = 80;

        [Category("Core DSP")]
        public int AnalysisFrames { get; set; } = 1024;

        [Category("Core DSP")]
        public int BlockFrames { get; set; } = 128;

        [Category("Filtering")]
        public int MedianFrames { get; set; } = 17;

        [Category("Filtering")]
        public int MedianBins { get; set; } = 17;

        [Category("Filtering")]
        public double SeparationMargin { get; set; } = 2.0;

        [Category("Filtering")]
        public double MaskFloor { get; set; } = 0.001;

        [Category("Filtering")]
        public double TransientPreservation { get; set; } = 0.75;

        [Category("Advanced Analysis")]
        public bool UseCqtAnalysis { get; set; }

        [Category("Advanced Synthesis")]
        public bool UseCqtSynthesis { get; set; }

        [Category("Advanced Analysis")]
        public bool UsePyin { get; set; }

        [Category("Advanced Analysis")]
        public bool UseIlrma { get; set; }

        [Category("Advanced Analysis")]
        public int CqtBinsPerOctave { get; set; } = 12;

        [Category("Advanced Analysis")]
        public double CqtMinimumHz { get; set; } = 27.5;

        [Category("Advanced Analysis")]
        public double PyinMinimumHz { get; set; } = 27.5;

        [Category("Advanced Analysis")]
        public double PyinMaximumHz { get; set; } = 1500;

        [Category("Advanced Analysis")]
        public int IlrmaIterations { get; set; } = 80;

        [Category("Advanced Analysis")]
        public int IlrmaComponents { get; set; } = 2;
    }
}
