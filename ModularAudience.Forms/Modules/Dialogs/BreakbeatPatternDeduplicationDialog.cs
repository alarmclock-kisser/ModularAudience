namespace ModularAudience.Forms.Modules.Dialogs;

public partial class BreakbeatPatternDeduplicationDialog : Form
{
    public float SimilarityThreshold => (float)this.numericUpDown_similarityThreshold.Value;

    public BreakbeatPatternDeduplicationDialog(int candidateTrackCount)
    {
        this.InitializeComponent();
        this.label_candidateTracks.Text = $"{candidateTrackCount} compatible sample tracks will be compared.";
    }
}
