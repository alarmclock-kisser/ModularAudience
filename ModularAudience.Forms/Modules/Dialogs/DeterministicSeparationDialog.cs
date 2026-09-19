using ModularAudience.Audio;
using ModularAudience.Audio.Processors_V4;
using ModularAudience.Forms.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ModularAudience.Forms
{
    public partial class DeterministicSeparationDialog : Form
    {
        private const string AnalysisNotice = "Group count and effective rank are estimates, not a true instrument count. Scores are acoustic heuristics, not probabilities.\r\nSampled analysis may miss rare events. Residual always retains ambiguous, unmodeled and unselected material.";
        private readonly AudioObj source;
        private DeterministicSeparationAnalysis? analysis;
        private CancellationTokenSource? operationCancellation;
        private bool initializing = true;
        private bool acceptingProgress;
        private bool closeRequested;

        private bool CanUseUi => !this.IsDisposed && !this.Disposing && !this.closeRequested;

        public DeterministicSeparationDialog(AudioObj source)
        {
            ArgumentNullException.ThrowIfNull(source);
            this.source = source;
            this.InitializeComponent();
            this.InitializeAdvancedControls();
            this.SetSourceInformation(source.Name, source.SampleRate, source.Channels, source.Data?.LongLength ?? 0);
            this.textBox_warnings.Text = AnalysisNotice;
            this.initializing = false;
        }

        private void SetSourceInformation(string name, int sampleRate, int channels, long sampleCount)
        {
            string displayName = string.IsNullOrWhiteSpace(name) ? "Untitled" : name;
            this.label_source.Text = FormattableString.Invariant(
                $"Source: {displayName} • {sampleRate:N0} Hz • {channels} channel(s) • {sampleCount:N0} interleaved samples");
            this.toolTip_settings.SetToolTip(this.label_source, this.label_source.Text);
        }

        private DeterministicSeparationSettings ReadSettings()
        {
            InstrumentEnsembleMode ensembleMode = this.ReadEnsembleMode();
            ImmutableArray<InstrumentProfileId> profiles = this.ReadSelectedProfiles(ensembleMode);
            DeterministicSeparationSettings settings = new()
            {
                WindowSize = (int) this.comboBox_windowSize.SelectedItem!,
                MaxComponents = (int) this.numeric_maxComponents.Value,
                Iterations = (int) this.numeric_iterations.Value,
                AnalysisFrames = (int) this.numeric_analysisFrames.Value,
                BlockFrames = (int) this.numeric_blockFrames.Value,
                MedianFrames = (int) this.numeric_medianFrames.Value,
                MedianBins = (int) this.numeric_medianBins.Value,
                SeparationMargin = (double) this.numeric_separationMargin.Value,
                MaskFloor = (double) this.numeric_maskFloor.Value,
                TransientPreservation = (double) this.numeric_transientPreservation.Value / 100.0,
                Threads = (int) this.numeric_threads.Value,
                UseCqtAnalysis = this.checkBox_cqtAnalysis.Checked,
                UseCqtSynthesis = this.checkBox_cqtSynthesis.Checked,
                UsePyin = this.checkBox_pyin.Checked,
                UseIlrma = this.checkBox_ilrma.Checked,
                CqtBinsPerOctave = this.ReadCqtBinsPerOctave(),
                CqtMinimumHz = (double) this.numeric_cqtMinimumHz.Value,
                PyinMinimumHz = (double) this.numeric_pyinMinimumHz.Value,
                PyinMaximumHz = (double) this.numeric_pyinMaximumHz.Value,
                IlrmaIterations = (int) this.numeric_ilrmaIterations.Value,
                IlrmaComponents = (int) this.numeric_ilrmaComponents.Value,
                EnsembleMode = ensembleMode,
                InstrumentProfiles = profiles
            };
            settings.Validate();
            return settings;
        }

        private int ReadCqtBinsPerOctave()
        {
            string text = this.domainUpDown_cqtBinsPerOctave.Text;
            return text == "24 bins/octave" ? 24 : text == "36 bins/octave" ? 36 : 12;
        }

        private void InitializeAdvancedControls()
        {
            foreach (InstrumentProfile profile in InstrumentProfileCatalog.All)
            {
                this.checkedListBox_profiles.Items.Add(profile);
            }

            this.domainUpDown_ensembleMode.Items.AddRange(new object[] { "Automatic", "ProfilesOnly", "ProfilesAndAutomatic" });
            this.domainUpDown_ensembleMode.Text = "Automatic";
            this.domainUpDown_cqtBinsPerOctave.Items.AddRange(new object[] { "12 bins/octave", "24 bins/octave", "36 bins/octave" });
            this.domainUpDown_cqtBinsPerOctave.Text = "12 bins/octave";
        }

        private InstrumentEnsembleMode ReadEnsembleMode()
        {
            string text = this.domainUpDown_ensembleMode.Text;
            return text == "ProfilesOnly" ? InstrumentEnsembleMode.ProfilesOnly
                : text == "ProfilesAndAutomatic" ? InstrumentEnsembleMode.ProfilesAndAutomatic
                : InstrumentEnsembleMode.Automatic;
        }

        private ImmutableArray<InstrumentProfileId> ReadSelectedProfiles(InstrumentEnsembleMode ensembleMode)
        {
            if (ensembleMode == InstrumentEnsembleMode.Automatic) return [];
            List<InstrumentProfileId> selected = new();
            foreach (int index in this.checkedListBox_profiles.CheckedIndices)
            {
                if (this.checkedListBox_profiles.Items[index] is InstrumentProfile profile)
                    selected.Add(profile.Id);
            }

            return selected.ToImmutableArray();
        }

        private void settings_ValueChanged(object? sender, EventArgs e)
        {
            if (this.initializing || !this.CanUseUi || this.operationCancellation != null)
            {
                return;
            }

            this.label_hopSizeValue.Text = $"{(int) this.comboBox_windowSize.SelectedItem! / 4} samples (fixed)";
            this.InvalidateAnalysis("Settings changed. Detect sources again before separating.");
        }

        private void profiles_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            if (this.initializing || !this.CanUseUi || this.operationCancellation != null)
            {
                return;
            }

            bool checkedCountChanged = e.NewValue == CheckState.Checked && e.CurrentValue != CheckState.Checked
                || e.NewValue != CheckState.Checked && e.CurrentValue == CheckState.Checked;
            if (checkedCountChanged)
                this.InvalidateAnalysis("Instrument profile selection changed. Detect sources again before separating.");
        }

        private void InvalidateAnalysis(string status)
        {
            this.analysis = null;
            this.dataGridView_sources.Rows.Clear();
            this.dataGridView_sources.Enabled = false;
            this.button_separate.Enabled = false;
            this.label_summary.Text = "No current analysis. Detect sources to estimate acoustic groups.";
            this.textBox_warnings.Text = AnalysisNotice;
            this.UpdateOutputEstimate();
            this.progressBar_operation.Value = 0;
            this.label_status.Text = status;
        }

        private async void button_detect_Click(object? sender, EventArgs e)
        {
            await this.RunOperationAsync("Detecting sources...", "Source Detection Failed", this.AnalyzeAsync);
        }

        private async void button_separate_Click(object? sender, EventArgs e)
        {
            await this.RunOperationAsync("Separating and restoring...", "Deterministic Separation Failed", this.SeparateAsync);
        }

        private async Task RunOperationAsync(string status, string errorTitle, Func<CancellationTokenSource, Task> operation)
        {
            if (!this.CanUseUi || this.operationCancellation != null)
            {
                return;
            }

            CancellationTokenSource cancellation = new();
            this.operationCancellation = cancellation;
            this.acceptingProgress = true;
            try
            {
                this.SetOperationState(true);
                this.progressBar_operation.Value = 0;
                this.label_status.Text = status;
                await operation(cancellation);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                this.acceptingProgress = false;
                if (this.CanUseUi) this.label_status.Text = "Operation cancelled.";
            }
            catch (Exception ex)
            {
                this.acceptingProgress = false;
                this.ReportError(errorTitle, ex);
            }
            finally
            {
                this.EndOperation(cancellation);
            }
        }

        private void SetOperationState(bool running)
        {
            this.groupBox_settings.Enabled = !running;
            this.dataGridView_sources.Enabled = !running && this.analysis != null;
            this.button_detect.Enabled = !running;
            this.button_separate.Enabled = !running && this.analysis != null;
            this.button_cancel.Enabled = running;
            this.button_close.Enabled = true;
        }

        private bool IsCurrentOperation(CancellationTokenSource cancellation)
        {
            return this.CanUseUi && ReferenceEquals(this.operationCancellation, cancellation)
                && !cancellation.IsCancellationRequested;
        }

        private IProgress<DeterministicSeparationProgress> CreateProgress(CancellationTokenSource cancellation)
        {
            return new Progress<DeterministicSeparationProgress>(progress =>
            {
                if (!this.acceptingProgress || !this.IsCurrentOperation(cancellation))
                {
                    return;
                }

                double fraction = double.IsFinite(progress.Fraction) ? Math.Clamp(progress.Fraction, 0, 1) : 0;
                this.progressBar_operation.Value = (int) (fraction * this.progressBar_operation.Maximum);
                this.label_status.Text = progress.Stage;
            });
        }

        private async Task AnalyzeAsync(CancellationTokenSource cancellation)
        {
            this.InvalidateAnalysis("Detecting sources...");
            DeterministicSeparationSettings settings = this.ReadSettings();
            DeterministicSeparationAnalysis detected = await DeterministicSeparationProcessor.AnalyzeAsync(
                this.source, settings, this.CreateProgress(cancellation), cancellation.Token);
            this.acceptingProgress = false;
            if (!this.IsCurrentOperation(cancellation))
            {
                return;
            }

            this.analysis = detected;
            this.DisplayAnalysis(detected);
            this.progressBar_operation.Value = this.progressBar_operation.Maximum;
            this.label_status.Text = detected.Sources.Count == 0
                ? "No groups detected. Residual-only separation is available, including for silence."
                : "Analysis ready. Select acoustic groups, or deselect all for Residual only.";
        }

        private void DisplayAnalysis(DeterministicSeparationAnalysis detected)
        {
            this.SetSourceInformation(detected.SourceName, detected.SampleRate, detected.Channels, detected.SampleCount);
            foreach (DeterministicSourceDescriptor descriptor in detected.Sources)
            {
                object? pitch = double.IsFinite(descriptor.FundamentalHz) && descriptor.FundamentalHz > 0
                    ? descriptor.FundamentalHz : null;
                int index = this.dataGridView_sources.Rows.Add(true, descriptor.Name, descriptor.Character,
                    descriptor.Confidence, descriptor.EnergyFraction, pitch, descriptor.Pan);
                this.dataGridView_sources.Rows[index].Tag = descriptor.Id;
            }

            this.label_summary.Text = $"Acoustic groups: {detected.Sources.Count} • Estimated effective signal rank: {detected.EstimatedSignalRank} • Neither is an instrument count";
            this.textBox_warnings.Lines = [AnalysisNotice, .. detected.Warnings];
            this.UpdateOutputEstimate();
        }

        private int[] GetSelectedIds()
        {
            return this.dataGridView_sources.Rows.Cast<DataGridViewRow>()
                .Where(row => row.Tag is int && row.Cells[this.column_use.Index].Value is true)
                .Select(row => (int) row.Tag!).ToArray();
        }

        private void UpdateOutputEstimate()
        {
            if (this.analysis == null)
            {
                this.label_output.Text = "Approx. OUTPUT: analyze first (Residual is always included).\r\nAdditional snapshot and processing workspace are excluded; this is not a total RAM estimate.";
                return;
            }

            int selected = this.GetSelectedIds().Length;
            double selectedMiB = this.analysis.SampleCount * (double) sizeof(float) * (selected + 1) / 1048576.0;
            double allMiB = this.analysis.EstimatedOutputBytes / 1048576.0;
            this.label_output.Text = FormattableString.Invariant(
                $"Approx. OUTPUT: {selectedMiB:F1} MiB ({selected} groups + Residual); all groups + Residual: {allMiB:F1} MiB.\r\nAdditional snapshot and processing workspace are excluded; this is not a total RAM estimate.");
        }

        private void dataGridView_sources_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
        {
            if (this.dataGridView_sources.IsCurrentCellDirty
                && this.dataGridView_sources.CurrentCell?.OwningColumn == this.column_use)
            {
                this.dataGridView_sources.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void dataGridView_sources_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
        {
            if (!this.initializing && this.CanUseUi && e.RowIndex >= 0 && e.ColumnIndex == this.column_use.Index)
            {
                this.UpdateOutputEstimate();
            }
        }

        private async Task SeparateAsync(CancellationTokenSource cancellation)
        {
            DeterministicSeparationSettings settings = this.ReadSettings();
            DeterministicSeparationAnalysis? current = this.analysis;
            if (current == null || !current.Settings.IsEquivalentTo(settings))
            {
                this.InvalidateAnalysis("Settings changed. Detect sources again before separating.");
                return;
            }

            DeterministicSeparationResult? result = null;
            bool published = false;
            try
            {
                int[] selectedIds = this.GetSelectedIds();
                result = await DeterministicSeparationProcessor.SeparateAsync(
                    current, selectedIds, this.CreateProgress(cancellation), cancellation.Token);
                this.acceptingProgress = false;
                if (!this.IsCurrentOperation(cancellation)) return;
                published = this.PublishStems(result.Stems, current.SourceName, cancellation);
                if (published && this.IsCurrentOperation(cancellation)) this.ShowSeparationReport(result);
            }
            finally
            {
                if (result != null && !published) this.DisposeUnownedStems(result.Stems);
            }
        }

        private bool PublishStems(IReadOnlyList<AudioObj> stems, string sourceName, CancellationTokenSource cancellation)
        {
            if (!this.IsCurrentOperation(cancellation)) return false;
            AudioCollectionView? collection = null;
            bool published = false;
            try
            {
                collection = new AudioCollectionView([], $"Deterministic separation - {sourceName}");
                foreach (AudioObj stem in stems)
                {
                    if (!this.IsCurrentOperation(cancellation) || collection.IsDisposed || collection.Disposing) return false;
                    collection.AudioC.Audios.Add(stem);
                }

                if (!this.IsCurrentOperation(cancellation) || collection.IsDisposed || collection.Disposing) return false;
                collection.Show();
                published = this.IsCurrentOperation(cancellation) && !collection.IsDisposed && !collection.Disposing;
                return published;
            }
            finally
            {
                if (!published && collection != null)
                {
                    if (!collection.IsDisposed && !collection.Disposing) collection.Close();
                    WindowMain.CollectionViews.Remove(collection);
                }
            }
        }

        private void ShowSeparationReport(DeterministicSeparationResult result)
        {
            string report = FormattableString.Invariant(
                $"Created {result.Stems.Count} stems including Residual. Maximum mixture reconstruction error: {result.ReconstructionError:G6}.");
            this.progressBar_operation.Value = this.progressBar_operation.Maximum;
            this.label_status.Text = report;
            this.textBox_warnings.AppendText(Environment.NewLine + report);
        }

        private void DisposeUnownedStems(IReadOnlyList<AudioObj> stems)
        {
            foreach (AudioObj stem in stems)
            {
                try
                {
                    stem.Dispose();
                }
                catch (Exception ex)
                {
                    this.ReportError("Stem Cleanup Failed", ex);
                }
            }
        }

        private void ReportError(string title, Exception ex)
        {
            LogCollection.Log($"{title}: {ex}");
            if (!this.CanUseUi) return;
            this.label_status.Text = $"{title}. See the error details.";
            WindowMainStaticHelpers.ShowErrorWithCopyButton(this, title, ex);
        }

        private void button_cancel_Click(object? sender, EventArgs e)
        {
            if (!this.CanUseUi || this.operationCancellation == null) return;
            this.acceptingProgress = false;
            this.operationCancellation.Cancel();
            this.button_cancel.Enabled = false;
            this.label_status.Text = "Cancelling...";
        }

        private void button_close_Click(object? sender, EventArgs e)
        {
            this.Close();
        }

        private void DeterministicSeparationDialog_FormClosing(object? sender, FormClosingEventArgs e)
        {
            this.ReleaseResources();
            if (this.operationCancellation != null)
            {
                e.Cancel = true;
                this.button_cancel.Enabled = false;
                this.button_close.Enabled = false;
                this.label_status.Text = "Cancelling before closing...";
            }
        }

        private void ReleaseResources()
        {
            this.closeRequested = true;
            this.acceptingProgress = false;
            this.analysis = null;
            this.operationCancellation?.Cancel();
        }

        private void EndOperation(CancellationTokenSource cancellation)
        {
            bool cancelled = cancellation.IsCancellationRequested;
            this.acceptingProgress = false;
            if (ReferenceEquals(this.operationCancellation, cancellation)) this.operationCancellation = null;
            cancellation.Dispose();
            if (this.IsDisposed || this.Disposing) return;
            if (this.closeRequested)
            {
                this.Close();
                return;
            }

            if (cancelled) this.label_status.Text = "Operation cancelled.";
            this.SetOperationState(false);
        }
    }
}
