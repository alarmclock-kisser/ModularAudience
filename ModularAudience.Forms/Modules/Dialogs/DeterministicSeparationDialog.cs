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
        private bool updatingScrollState;
        private bool automaticProfilesDefaultActive = true;
        private DateTime? operationStart;
        private double? lastFraction;
        private double? lastElapsedSeconds;

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
            this.UpdateSettingsValidation();
        }

        private void SetSourceInformation(string name, int sampleRate, int channels, long sampleCount)
        {
            string displayName = string.IsNullOrWhiteSpace(name) ? "Untitled" : name;
            this.label_source.Text = FormattableString.Invariant(
                $"Source: {displayName} • {sampleRate:N0} Hz • {channels} channel(s) • {sampleCount:N0} interleaved samples");
            this.toolTip_settings.SetToolTip(this.label_source, this.label_source.Text);
        }

        private DeterministicSeparationSettings ReadSettings(
            int pendingProfileIndex = -1,
            CheckState pendingProfileState = CheckState.Indeterminate)
        {
            ImmutableArray<InstrumentProfileId> profiles = this.ReadSelectedProfiles(pendingProfileIndex, pendingProfileState);
            if (profiles.IsEmpty && !this.checkBox_addAutomaticProfiles.Checked)
            {
                throw new ArgumentException("Select an instrument profile or enable extra automatic profiles.");
            }

            InstrumentEnsembleMode ensembleMode = profiles.Length == 0
                ? InstrumentEnsembleMode.Automatic
                : this.checkBox_addAutomaticProfiles.Checked
                    ? InstrumentEnsembleMode.ProfilesAndAutomatic
                    : InstrumentEnsembleMode.ProfilesOnly;
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

            this.domainUpDown_cqtBinsPerOctave.Items.AddRange(new object[] { "12 bins/octave", "24 bins/octave", "36 bins/octave" });
            this.domainUpDown_cqtBinsPerOctave.Text = "12 bins/octave";
        }

        private ImmutableArray<InstrumentProfileId> ReadSelectedProfiles(
            int pendingProfileIndex = -1,
            CheckState pendingProfileState = CheckState.Indeterminate)
        {
            List<InstrumentProfileId> selected = new();
            foreach (int index in this.checkedListBox_profiles.CheckedIndices)
            {
                if (index == pendingProfileIndex) continue;
                if (this.checkedListBox_profiles.Items[index] is InstrumentProfile profile)
                    selected.Add(profile.Id);
            }

            if (pendingProfileIndex >= 0
                && pendingProfileState == CheckState.Checked
                && this.checkedListBox_profiles.Items[pendingProfileIndex] is InstrumentProfile pendingProfile)
            {
                selected.Add(pendingProfile.Id);
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
            if (this.analysis == null)
            {
                this.InvalidateAnalysis("Settings changed. Detect sources again before separating.");
            }
            this.UpdateSettingsValidation();
        }

        private bool TryReadSettings(
            out DeterministicSeparationSettings settings,
            out string validationMessage,
            int pendingProfileIndex = -1,
            CheckState pendingProfileState = CheckState.Indeterminate)
        {
            try
            {
                settings = this.ReadSettings(pendingProfileIndex, pendingProfileState);
                DeterministicSeparationProcessor.ValidateSettingsForSource(this.source, settings);
                validationMessage = string.Empty;
                return true;
            }
            catch (ArgumentOutOfRangeException ex) when (!string.IsNullOrWhiteSpace(ex.ParamName))
            {
                settings = null!;
                validationMessage = $"Invalid value for {ex.ParamName}.";
                return false;
            }
            catch (ArgumentException ex)
            {
                settings = null!;
                validationMessage = ex.Message;
                return false;
            }
            catch (InvalidCastException)
            {
                settings = null!;
                validationMessage = "One or more settings have an invalid value.";
                return false;
            }
        }

        private bool UpdateSettingsValidation(int pendingProfileIndex = -1, CheckState pendingProfileState = CheckState.Indeterminate)
        {
            bool valid = this.TryReadSettings(out DeterministicSeparationSettings currentSettings,
                out string validationMessage, pendingProfileIndex, pendingProfileState);
            bool operationRunning = this.operationCancellation != null;
            bool matchesAnalysis = valid && this.analysis != null
                && this.analysis.Settings.IsEquivalentTo(currentSettings);

            this.button_detect.Enabled = !operationRunning && valid;
            this.button_separate.Enabled = !operationRunning && matchesAnalysis;
            this.button_restoreSettings.Enabled = !operationRunning
                && this.analysis != null && !matchesAnalysis;
            if (!valid)
            {
                if (!operationRunning)
                    this.label_status.Text = $"Invalid settings: {validationMessage}";
            }
            else if (!operationRunning && this.analysis != null && !matchesAnalysis)
            {
                this.label_status.Text = "Settings differ from the current analysis. Restore detection settings or detect sources again.";
            }
            else if (!operationRunning && matchesAnalysis)
            {
                this.label_status.Text = "Analysis settings match. Separation is ready.";
            }

            return valid;
        }

        private void comboBox_presets_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (this.initializing || !this.CanUseUi || this.operationCancellation != null)
            {
                return;
            }

            string preset = this.comboBox_presets.SelectedItem as string;
            if (preset == null) return;
            this.ApplyPreset(preset);
            this.label_hopSizeValue.Text = $"{(int) this.comboBox_windowSize.SelectedItem! / 4} samples (fixed)";
            if (this.analysis == null)
            {
                this.InvalidateAnalysis($"Preset '{preset}' applied. Detect sources again before separating.");
            }
            this.UpdateSettingsValidation();
        }

        private static readonly Dictionary<string, (int, int, int, int, int, int, int, double, double, double, bool, int, bool, bool)> PresetValues = new()
        {
            ["Best Quality"] = (16384, 32, 500, 8192, 512, 65, 65, 1.0, 0.0, 100.0, true, 36, true, false),
            ["High Quality"] = (8192, 24, 300, 4096, 256, 49, 49, 1.5, 0.0005, 95.0, true, 24, true, false),
            ["Balanced"] = (4096, 16, 80, 1024, 128, 17, 17, 2.0, 0.001, 75.0, false, 12, false, false),
            ["Faster"] = (2048, 12, 40, 512, 64, 9, 9, 3.0, 0.002, 60.0, false, 12, false, false),
            ["Draft"] = (1024, 8, 20, 256, 32, 5, 5, 4.0, 0.005, 50.0, false, 12, false, false),
            ["DnB / Breaks"] = (8192, 24, 200, 2048, 256, 33, 17, 2.0, 0.001, 95.0, true, 24, false, false),
            ["Electro / Synth"] = (8192, 24, 250, 2048, 256, 17, 49, 1.5, 0.0005, 85.0, true, 36, true, false),
            ["Pop / Band"] = (4096, 20, 150, 2048, 128, 25, 25, 2.0, 0.001, 80.0, false, 12, true, false),
            ["Complex Ensemble"] = (16384, 32, 400, 4096, 512, 49, 49, 1.0, 0.0, 95.0, true, 36, true, false)
        };

        private void ApplyPreset(string preset)
        {
            if (!PresetValues.TryGetValue(preset, out var v)) return;

            this.comboBox_windowSize.SelectedItem = v.Item1;
            this.numeric_maxComponents.Value = v.Item2;
            this.numeric_iterations.Value = v.Item3;
            this.numeric_analysisFrames.Value = v.Item4;
            this.numeric_blockFrames.Value = v.Item5;
            this.numeric_medianFrames.Value = v.Item6;
            this.numeric_medianBins.Value = v.Item7;
            this.numeric_separationMargin.Value = (decimal) v.Item8;
            this.numeric_maskFloor.Value = (decimal) v.Item9;
            this.numeric_transientPreservation.Value = (decimal) v.Item10;
            this.checkBox_cqtAnalysis.Checked = v.Item11;
            this.domainUpDown_cqtBinsPerOctave.Text = $"{v.Item12} bins/octave";
            this.checkBox_pyin.Checked = v.Item13;
            this.checkBox_ilrma.Checked = v.Item14;
        }

        private void profiles_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            if (this.initializing || !this.CanUseUi || this.operationCancellation != null)
            {
                return;
            }

            bool checkedCountChanged = e.NewValue == CheckState.Checked && e.CurrentValue != CheckState.Checked
                || e.NewValue != CheckState.Checked && e.CurrentValue == CheckState.Checked;
            bool selectingFirstProfile = this.automaticProfilesDefaultActive
                && e.NewValue == CheckState.Checked
                && e.CurrentValue != CheckState.Checked
                && this.checkedListBox_profiles.CheckedItems.Count == 0;
            if (selectingFirstProfile)
            {
                this.automaticProfilesDefaultActive = false;
                this.checkBox_addAutomaticProfiles.Checked = false;
            }

            if (checkedCountChanged)
            {
                if (this.analysis == null)
                {
                    this.InvalidateAnalysis("Instrument profile selection changed. Detect sources again before separating.");
                }
                this.UpdateSettingsValidation(e.Index, e.NewValue);
            }
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

        private void button_restoreSettings_Click(object? sender, EventArgs e)
        {
            if (!this.CanUseUi || this.operationCancellation != null || this.analysis == null)
            {
                return;
            }

            DeterministicSeparationSettings settings = this.analysis.Settings;
            this.initializing = true;
            try
            {
                this.comboBox_windowSize.SelectedItem = settings.WindowSize;
                this.numeric_maxComponents.Value = settings.MaxComponents;
                this.numeric_iterations.Value = settings.Iterations;
                this.numeric_analysisFrames.Value = settings.AnalysisFrames;
                this.numeric_blockFrames.Value = settings.BlockFrames;
                this.numeric_medianFrames.Value = settings.MedianFrames;
                this.numeric_medianBins.Value = settings.MedianBins;
                this.numeric_separationMargin.Value = (decimal) settings.SeparationMargin;
                this.numeric_maskFloor.Value = (decimal) settings.MaskFloor;
                this.numeric_transientPreservation.Value = (decimal) (settings.TransientPreservation * 100.0);
                this.numeric_threads.Value = settings.Threads;
                this.checkBox_cqtAnalysis.Checked = settings.UseCqtAnalysis;
                this.checkBox_cqtSynthesis.Checked = settings.UseCqtSynthesis;
                this.checkBox_pyin.Checked = settings.UsePyin;
                this.checkBox_ilrma.Checked = settings.UseIlrma;
                this.domainUpDown_cqtBinsPerOctave.Text = $"{settings.CqtBinsPerOctave} bins/octave";
                this.numeric_cqtMinimumHz.Value = (decimal) settings.CqtMinimumHz;
                this.numeric_pyinMinimumHz.Value = (decimal) settings.PyinMinimumHz;
                this.numeric_pyinMaximumHz.Value = (decimal) settings.PyinMaximumHz;
                this.numeric_ilrmaIterations.Value = settings.IlrmaIterations;
                this.numeric_ilrmaComponents.Value = settings.IlrmaComponents;
                this.checkBox_addAutomaticProfiles.Checked = settings.EnsembleMode == InstrumentEnsembleMode.ProfilesAndAutomatic;
                if (!settings.InstrumentProfiles.IsDefaultOrEmpty)
                {
                    this.automaticProfilesDefaultActive = false;
                }
                for (int index = 0; index < this.checkedListBox_profiles.Items.Count; index++)
                {
                    bool selected = this.checkedListBox_profiles.Items[index] is InstrumentProfile profile
                        && settings.InstrumentProfiles.Contains(profile.Id);
                    this.checkedListBox_profiles.SetItemChecked(index, selected);
                }
                this.label_hopSizeValue.Text = $"{settings.HopSize} samples (fixed)";
            }
            finally
            {
                this.initializing = false;
            }

            this.UpdateSettingsValidation();
            this.label_status.Text = "Detection settings restored. Analysis is ready for separation.";
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
            this.operationStart = DateTime.Now;
            this.lastFraction = null;
            this.lastElapsedSeconds = null;
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
                this.operationStart = null;
                this.lastFraction = null;
                this.lastElapsedSeconds = null;
                this.EndOperation(cancellation);
            }
        }

        private void SetOperationState(bool running)
        {
            this.comboBox_presets.Enabled = !running;
            this.groupBox_settings.Enabled = !running;
            this.tabControl_advanced.Enabled = !running;
            this.dataGridView_sources.Enabled = !running && this.analysis != null;
            this.UpdateSettingsValidation();
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
                this.UpdateElapsedLabel(fraction);
            });
        }

        private void UpdateElapsedLabel(double fraction)
        {
            if (this.operationStart == null || !this.CanUseUi) return;
            double elapsed = (DateTime.Now - this.operationStart.Value).TotalSeconds;
            if (fraction <= 0)
            {
                this.label_eta.Text = FormattableString.Invariant($"Elapsed: {this.FormatElapsed(elapsed)} · ETA: --:--");
                return;
            }
            double elapsedFraction = fraction - (this.lastFraction ?? 0);
            if (elapsedFraction <= 0)
            {
                this.label_eta.Text = FormattableString.Invariant($"Elapsed: {this.FormatElapsed(elapsed)} · ETA: --:--");
                return;
            }
            double elapsedPerFraction = elapsed / fraction;
            double etaSeconds = Math.Max(elapsed, elapsedPerFraction);
            this.lastFraction = fraction;
            this.lastElapsedSeconds = elapsed;
            this.label_eta.Text = FormattableString.Invariant($"Elapsed: {this.FormatElapsed(elapsed)} · ETA: {this.FormatElapsed(etaSeconds)}");
        }

        private string FormatElapsed(double totalSeconds)
        {
            int minutes = (int) totalSeconds / 60;
            int seconds = (int) totalSeconds % 60;
            return $"{minutes}:{seconds:D2}";
        }

        private async Task AnalyzeAsync(CancellationTokenSource cancellation)
        {
            if (!this.TryReadSettings(out DeterministicSeparationSettings settings, out string validationMessage))
            {
                this.InvalidateAnalysis($"Invalid settings: {validationMessage}");
                return;
            }

            this.InvalidateAnalysis("Detecting sources...");
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

        private void dataGridView_sources_CellClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == this.column_use.Index)
            {
                this.dataGridView_sources.CurrentCell = this.dataGridView_sources.Rows[e.RowIndex].Cells[e.ColumnIndex];
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
            if (!this.TryReadSettings(out DeterministicSeparationSettings settings, out string validationMessage))
            {
                this.InvalidateAnalysis($"Invalid settings: {validationMessage}");
                return;
            }

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

        private void DeterministicSeparationDialog_Shown(object? sender, EventArgs e)
        {
            this.initializing = true;
            try
            {
                this.PerformLayout();
                Rectangle workingArea = Screen.FromControl(this).WorkingArea;
                int nonClientHeight = this.Height - this.ClientSize.Height;
                int maximumClientHeight = Math.Max(100, workingArea.Height - nonClientHeight - 12);
                this.tableLayoutPanel_main.AutoScroll = false;
                int preferredClientHeight = Math.Max(this.ClientSize.Height,
                    this.tableLayoutPanel_main.GetPreferredSize(new Size(this.ClientSize.Width, 0)).Height);
                int initialClientHeight = Math.Min(preferredClientHeight, maximumClientHeight);
                this.ClientSize = new Size(this.ClientSize.Width, initialClientHeight);
                this.PerformLayout();
                this.UpdateMainScrollState();
                int maximumLeft = Math.Max(workingArea.Left, workingArea.Right - this.Width);
                int maximumTop = Math.Max(workingArea.Top, workingArea.Bottom - this.Height);
                this.Location = new Point(
                    Math.Clamp(this.Left, workingArea.Left, maximumLeft),
                    Math.Clamp(this.Top, workingArea.Top, maximumTop));
            }
            finally
            {
                this.initializing = false;
            }
        }

        private void DeterministicSeparationDialog_Resize(object? sender, EventArgs e)
        {
            if (!this.initializing)
            {
                this.UpdateMainScrollState();
            }
        }

        private void UpdateMainScrollState()
        {
            if (this.updatingScrollState || this.IsDisposed || this.Disposing)
            {
                return;
            }

            this.updatingScrollState = true;
            try
            {
                this.tableLayoutPanel_main.AutoScroll = false;
                int preferredHeight = this.tableLayoutPanel_main
                    .GetPreferredSize(new Size(this.tableLayoutPanel_main.ClientSize.Width, 0)).Height;
                this.tableLayoutPanel_main.AutoScroll = preferredHeight > this.tableLayoutPanel_main.ClientSize.Height;
            }
            finally
            {
                this.updatingScrollState = false;
            }
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
