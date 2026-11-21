using ModularAudience.Audio;
using ModularAudience.Audio.Processing;
using ModularAudience.Audio.Processors_V1;
using ModularAudience.Audio.Processors_V4;
using ModularAudience.Forms.Helpers;

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
            SnapToGrid = this.checkBox_projectSnap.Checked,
            SnapDivision = this.comboBox_projectSnap.SelectedItem is string value
                && value.StartsWith("1/", StringComparison.Ordinal)
                && int.TryParse(value.AsSpan(2), out int division)
                ? division
                : 16,
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

    private async void checkBox_projectSnap_CheckedChanged(object? sender, EventArgs e)
    {
        if (!this.IsHandleCreated)
        {
            this.UpdateOutputControls();
            return;
        }

        if (!this.checkBox_projectSnap.Checked)
        {
            this.snapEstimateCancellation?.Cancel();
            this.UpdateOutputControls();
            return;
        }

        this.UpdateOutputControls();
        int originalIndex = this.comboBox_projectSnap.SelectedIndex;
        this.comboBox_projectSnap.Enabled = false;
        this.snapEstimateCancellation?.Cancel();
        CancellationTokenSource estimateCancellation = new();
        this.snapEstimateCancellation = estimateCancellation;
        try
        {
            int estimated = await this.EstimateDecompileSnapDivisionAsync(
                estimateCancellation.Token);
            if (!this.IsDisposed
                && !this.Disposing
                && ReferenceEquals(this.snapEstimateCancellation, estimateCancellation))
            {
                this.comboBox_projectSnap.SelectedIndex = this.comboBox_projectSnap.Items
                    .IndexOf($"1/{estimated}");
            }
        }
        catch (OperationCanceledException)
        {
            if (!this.IsDisposed
                && !this.Disposing
                && ReferenceEquals(this.snapEstimateCancellation, estimateCancellation))
            {
                this.comboBox_projectSnap.SelectedIndex = originalIndex;
            }
        }
        catch (Exception error)
        {
            LogManager.Log($"Decompile grid estimation failed: {error}");
            WindowMainStaticHelpers.ShowErrorWithCopyButton(
                this,
                "Snap Grid Estimation",
                error);
        }
        finally
        {
            if (ReferenceEquals(this.snapEstimateCancellation, estimateCancellation))
            {
                this.snapEstimateCancellation = null;
                if (!this.IsDisposed && !this.Disposing)
                {
                    this.comboBox_projectSnap.Enabled = this.checkBox_projectOutput.Checked
                        && this.checkBox_projectSnap.Checked;
                    if (this.closeRequested)
                    {
                        this.BeginInvoke(this.Close);
                    }
                }
            }
            estimateCancellation.Dispose();
        }
    }

    private async Task<int> EstimateDecompileSnapDivisionAsync(
        CancellationToken cancellationToken)
    {
        float bpm = this.sourceSnapshot.Bpm > 0
            ? this.sourceSnapshot.Bpm
            : this.sourceSnapshot.ScannedBpm > 0
                ? this.sourceSnapshot.ScannedBpm
                : 0;
        if (bpm <= 0)
        {
            double estimatedBpm = await BeatScanner.ScanBpmAsync(this.sourceSnapshot);
            cancellationToken.ThrowIfCancellationRequested();
            if (!double.IsFinite(estimatedBpm) || estimatedBpm is < 10 or > 400)
            {
                throw new InvalidDataException("BPM could not be estimated for snap grid resolution.");
            }
            bpm = (float)estimatedBpm;
        }

        LoopAtomizerSettings atomizerSettings = this.CreateCurrentV4Settings();
        LoopAtomizerResult scan = await LoopAtomizer_V4.AtomizeAsync(
            this.sourceSnapshot,
            atomizerSettings,
            cancellationToken: cancellationToken);
        try
        {
            return EstimateSnapDivisionFromAtomics(scan.Atomics, 240d / bpm);
        }
        finally
        {
            foreach (AudioObj atomic in scan.Atomics)
            {
                atomic.Dispose();
            }
        }
    }

    private LoopAtomizerSettings CreateCurrentV4Settings() => new()
    {
        Sensitivity = this.comboBox_atomizerSensitivity.SelectedIndex switch
        {
            0 => AtomizeSensitivity.Conservative,
            2 => AtomizeSensitivity.Aggressive,
            _ => AtomizeSensitivity.Balanced
        },
        MinSliceMs = (int)this.numeric_minSlice.Value,
        TailPaddingMs = (int)this.numeric_tailPadding.Value,
        AllowSingleAtomFallback = this.checkBox_singleAtomFallback.Checked,
        EnableDeduplication = false,
        MinimumRmsLevel = (float)this.numeric_minimumRms.Value,
        MinimumAtomicDurationMs = (int)this.numeric_minimumDuration.Value,
        MaxWorkers = (int)this.numeric_workers.Value
    };

    internal static int EstimateSnapDivisionFromAtomics(
        IReadOnlyList<AudioObj> atomics,
        double secondsPerBar)
    {
        double shortest = double.PositiveInfinity;
        long[] starts = atomics
            .Select(atomic => AudioAtomicTiming.TryGet(atomic, out long startFrame, out _)
                ? startFrame
                : -1)
            .Where(frame => frame >= 0)
            .Distinct()
            .Order()
            .ToArray();
        for (int index = 1; index < starts.Length; index++)
        {
            AudioObj reference = atomics.First(atomic =>
                AudioAtomicTiming.TryGet(atomic, out long frame, out _) && frame == starts[index]);
            if (reference.SampleRate > 0)
            {
                double spacing = (starts[index] - starts[index - 1])
                    / (double)reference.SampleRate;
                if (spacing > 0.0001)
                {
                    shortest = Math.Min(shortest, spacing);
                }
            }
        }
        foreach (AudioObj atomic in atomics)
        {
            if (atomic.Duration.TotalSeconds > 0.0001)
            {
                shortest = Math.Min(shortest, atomic.Duration.TotalSeconds);
            }
        }

        int required = double.IsFinite(shortest)
            ? Math.Max(4, (int)Math.Ceiling(secondsPerBar / shortest - 1e-9))
            : 16;
        int[] divisions = [4, 8, 16, 32, 64, 128, 256];
        return divisions.FirstOrDefault(value => value >= required, 256);
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
        this.checkBox_projectSnap.Enabled = project;
        this.comboBox_projectSnap.Enabled = project && this.checkBox_projectSnap.Checked;
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
