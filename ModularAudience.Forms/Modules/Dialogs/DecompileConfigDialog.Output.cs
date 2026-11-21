using ModularAudience.Audio.Processing;
using ModularAudience.Audio.Processors_V4;

namespace ModularAudience.Forms.Modules.Dialogs;

public sealed partial class DecompileConfigDialog
{
    private AudioDecompileOptions ReadDecompileOptions()
    {
        AudioDecompileOutputKind output = 0;
        if (this.checkBox_projectOutput.Checked)
        {
            output |= AudioDecompileOutputKind.Project;
        }

        if (this.checkBox_collectionOutput.Checked)
        {
            output |= AudioDecompileOutputKind.Collections;
        }
        AudioDecompileProjectDetail detail = this.comboBox_projectDetail.SelectedIndex switch
        {
            0 => AudioDecompileProjectDetail.Transients,
            1 => AudioDecompileProjectDetail.Beats,
            2 => AudioDecompileProjectDetail.Bars,
            _ => throw new ArgumentOutOfRangeException()
        };
        AudioDecompileOverlapPolicy overlap = this.comboBox_projectOverlap.SelectedIndex switch
        {
            0 => AudioDecompileOverlapPolicy.Allow,
            1 => AudioDecompileOverlapPolicy.AdditionalLanes,
            _ => throw new ArgumentOutOfRangeException()
        };
        AudioDecompileAtomizerKind atomizer = this.comboBox_atomizer.SelectedIndex switch
        {
            0 => AudioDecompileAtomizerKind.TransientV4,
            1 => AudioDecompileAtomizerKind.LegacyV1,
            _ => throw new ArgumentOutOfRangeException()
        };
        bool deduplicate = output.HasFlag(AudioDecompileOutputKind.Collections)
            && this.checkBox_collectionDeduplicate.Checked;
        int maxWorkers = (int)this.numeric_workers.Value;
        var v4 = new LoopAtomizerSettings
        {
            Sensitivity = this.comboBox_atomizerSensitivity.SelectedIndex switch
            {
                0 => AtomizeSensitivity.Conservative,
                1 => AtomizeSensitivity.Balanced,
                2 => AtomizeSensitivity.Aggressive,
                _ => AtomizeSensitivity.Balanced
            },
            MinSliceMs = (int)this.numeric_minSlice.Value,
            TailPaddingMs = (int)this.numeric_tailPadding.Value,
            AllowSingleAtomFallback = this.checkBox_singleAtomFallback.Checked,
            EnableDeduplication = deduplicate,
            MaxVariantsPerCluster = (int)this.numeric_maxVariants.Value,
            MinimumRmsLevel = (float)this.numeric_minimumRms.Value,
            MinimumAtomicDurationMs = (int)this.numeric_minimumDuration.Value,
            ClusterSimilarityThreshold = (float)this.numeric_similarity.Value,
            MaxWorkers = maxWorkers
        };

        return new AudioDecompileOptions
        {
            Atomizer = atomizer,
            V4Settings = v4,
            LegacySimilarityThreshold = this.checkBox_legacyAutoSimilarity.Checked
                ? null
                : (float)this.numeric_legacySimilarity.Value,
            Output = output,
            ProjectDetail = detail,
            ProjectOverlap = overlap,
            ProjectAttackMs = (int)this.numeric_projectAttack.Value,
            DeduplicateCollections = deduplicate,
            MaxWorkers = maxWorkers,
            BeatsPerBar = 4
        };
    }

    private void comboBox_atomizer_SelectedIndexChanged(object? sender, EventArgs e)
    {
        this.UpdateAtomizerPanel();
    }

    private void outputOption_CheckedChanged(object? sender, EventArgs e)
    {
        this.UpdateOutputControls();
    }

    private void UpdateAtomizerPanel()
    {
        this.panel_atomizerV4.Visible = this.comboBox_atomizer.SelectedIndex == 0;
        this.panel_atomizerLegacy.Visible = this.comboBox_atomizer.SelectedIndex == 1;
        if (this.panel_atomizerLegacy.Visible)
        {
            this.panel_atomizerLegacy.BringToFront();
        }
        else
        {
            this.panel_atomizerV4.BringToFront();
        }
    }

    private void UpdateOutputControls()
    {
        bool project = this.checkBox_projectOutput.Checked;
        bool collections = this.checkBox_collectionOutput.Checked;
        this.comboBox_projectDetail.Enabled = project;
        this.comboBox_projectOverlap.Enabled = project;
        this.numeric_projectAttack.Enabled = project;
        this.checkBox_collectionDeduplicate.Enabled = collections;
        this.checkBox_atomizerDeduplicate.Enabled = collections;
        if (!project && !collections)
        {
            this.label_outputNote.Text = "Select at least one output destination before starting Decompile.";
        }
        else if (project && collections)
        {
            this.label_outputNote.Text = "Both outputs will open. The project uses every atomic occurrence; Collections may deduplicate representatives independently.";
        }
        else if (project)
        {
            this.label_outputNote.Text = "Project mode creates stem lanes at exact source positions. Choose whether overlapping atomics share one lane or receive additional overlap lanes; project audio always remains non-deduplicated.";
        }
        else
        {
            this.label_outputNote.Text = "Collection mode opens one independently owned AudioCollectionView per stem. Dedupe controls representative samples and does not affect project reconstruction.";
        }
    }
}
