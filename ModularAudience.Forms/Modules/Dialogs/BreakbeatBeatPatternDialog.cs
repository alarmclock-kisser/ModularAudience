using ModularAudience.Audio.Processors_V1;

namespace ModularAudience.Forms.Modules.Dialogs;

internal sealed record BreakbeatBeatResolutionOption(string Label, int StepsPerBar)
{
    public override string ToString() => this.Label;
}

internal sealed partial class BreakbeatBeatPatternDialog : Form
{
    private static readonly BreakbeatBeatResolutionOption[] ResolutionOptions =
    [
        new("1/2", 2),
        new("1/4", 4),
        new("1/8", 8),
        new("1/16", 16),
        new("1/32", 32),
        new("1/64", 64),
        new("1/128", 128),
        new("1/256", 256)
    ];

    private readonly BreakbeatTapSequence sequence;
    private readonly int existingTrackCount;

    internal BreakbeatBeatPatternDialog(BreakbeatTapSequence sequence, int existingTrackCount)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        this.sequence = sequence;
        this.existingTrackCount = Math.Max(0, existingTrackCount);
        this.InitializeComponent();

        double initialBpm = sequence.HitCount >= 2
            && double.IsFinite(sequence.EstimatedBpm)
            && sequence.EstimatedBpm is >= 40 and <= 240
                ? sequence.EstimatedBpm
                : WorkingBpmResolver.GetWorkingBpm();
        this.numericUpDown_bpm.Value = Math.Clamp(
            (decimal)Math.Round(initialBpm, 2),
            this.numericUpDown_bpm.Minimum,
            this.numericUpDown_bpm.Maximum);
        this.numericUpDown_drumElements.Value = Math.Clamp(
            sequence.SuggestedDrumElements,
            (int)this.numericUpDown_drumElements.Minimum,
            (int)this.numericUpDown_drumElements.Maximum);
        this.comboBox_resolution.Items.AddRange(ResolutionOptions.Cast<object>().ToArray());
        this.comboBox_resolution.SelectedItem = ResolutionOptions.FirstOrDefault(
            option => option.StepsPerBar == sequence.SuggestedStepsPerBar) ?? ResolutionOptions[2];
        this.UpdateGroupingSummary();
    }

    internal decimal Bpm => this.numericUpDown_bpm.Value;

    internal int StepsPerBar =>
        this.comboBox_resolution.SelectedItem is BreakbeatBeatResolutionOption option
            ? option.StepsPerBar
            : 8;

    internal bool CreateNewLines => this.checkBox_createLines.Checked;

    internal IReadOnlyList<BreakbeatTapHit> GroupedHits =>
        this.sequence.GroupTaps((int)this.numericUpDown_drumElements.Value);

    private void numericUpDown_drumElements_ValueChanged(object? sender, EventArgs e) =>
        this.UpdateGroupingSummary();

    private void UpdateGroupingSummary()
    {
        IReadOnlyList<BreakbeatTapHit> grouped = this.GroupedHits;
        int groupCount = grouped.Select(hit => hit.GroupIndex).Distinct().Count();
        int missingTracks = Math.Max(0, groupCount - this.existingTrackCount);
        this.label_groupingSummary.Text = $"{this.sequence.HitCount} taps detected and grouped into {groupCount} sound type{(groupCount == 1 ? string.Empty : "s")}.";
        this.checkBox_createLines.Enabled = missingTracks > 0;
        if (!this.checkBox_createLines.Enabled)
        {
            this.checkBox_createLines.Checked = false;
        }

        this.label_trackSummary.Text = missingTracks > 0
            ? $"{missingTracks} drum group{(missingTracks == 1 ? string.Empty : "s")} will be skipped unless new lines are created from recorded hits."
            : "Existing tracks are assigned by order; extra tracks remain unchanged.";
    }
}
