using ModularAudience.Audio;
using ModularAudience.Audio.Processors_V4;
using ModularAudience.Audio.Processors_V1;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Generators;

namespace ModularAudience.Forms
{
    public partial class AudioCollectionView
    {
        private async Task SplitCurrentAudioEvenlyAsync(int partCount)
        {
            AudioObj? source = this.GetSingleContextAudio();
            if (source == null)
            {
                return;
            }

            bool previousUseWaitCursor = this.UseWaitCursor;
            this.UseWaitCursor = true;
            this.menuToolStripItem_splitEqualParts.Enabled = false;

            try
            {
                await this.CancelAutoPlayAsync(stopCollection: true);
                IReadOnlyList<AudioObj> slices = await EqualSliceProcessor_V4.SliceAsync(source, partCount);
                if (slices.Count == 0)
                {
                    MessageBox.Show(this, "The selected audio cannot be split into that many equal parts.", "Split Into Equal Parts", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var slicesView = new AudioCollectionView(slices);
                string baseName = string.IsNullOrWhiteSpace(source.Name) ? "Audio" : source.Name.Trim();
                slicesView.Rename($"{baseName}_Split{partCount:D2}");
                LogCollection.Log($"Split '{source.Name}' into {slices.Count} equal parts.");
            }
            catch (Exception ex)
            {
                LogCollection.Log(ex);
                MessageBox.Show(this, "Split failed: " + ex.Message, "Split Into Equal Parts", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.menuToolStripItem_splitEqualParts.Enabled = true;
                this.UseWaitCursor = previousUseWaitCursor;
            }
        }

        private async void menuToolStripItem_splitEqualParts2_Click(object sender, EventArgs e) => await this.SplitCurrentAudioEvenlyAsync(2);
        private async void menuToolStripItem_splitEqualParts4_Click(object sender, EventArgs e) => await this.SplitCurrentAudioEvenlyAsync(4);
        private async void menuToolStripItem_splitEqualParts8_Click(object sender, EventArgs e) => await this.SplitCurrentAudioEvenlyAsync(8);
        private async void menuToolStripItem_splitEqualParts16_Click(object sender, EventArgs e) => await this.SplitCurrentAudioEvenlyAsync(16);
        private async void menuToolStripItem_splitEqualParts32_Click(object sender, EventArgs e) => await this.SplitCurrentAudioEvenlyAsync(32);

        private async void menuToolStripItem_generateBreakbeatRun_Click(object sender, EventArgs e)
        {
            List<AudioObj> selected = this.GetContextAudios();
            if (selected.Count <= 1)
            {
                return;
            }

            bool previousUseWaitCursor = this.UseWaitCursor;
            this.UseWaitCursor = true;
            this.menuToolStripItem_generateBreakbeat.Enabled = false;

            try
            {
                await this.CancelAutoPlayAsync(stopCollection: true);

                var workflowSettings = new BreakbeatWorkflowSettings(
                    this.breakbeatBpm,
                    this.breakbeatBars,
                    this.breakbeatHitsPerBar,
                    this.breakbeatDensity,
                    this.breakbeatComplexity,
                    this.breakbeatResolution,
                    this.breakbeatSwing);

                BreakbeatWorkflowResult result = await BreakbeatAtomizerWorkflow_V4.GenerateBreakbeatAsync(
                    selected,
                    this.Text,
                    workflowSettings);

                if (result.Rendered == null)
                {
                    MessageBox.Show(this, "Breakbeat generation returned no rendered audio.", "Generate Breakbeat", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var resultView = new AudioCollectionView([result.Rendered]);
                resultView.Rename(result.CollectionName);

                if (!string.IsNullOrWhiteSpace(result.LogMessage))
                {
                    LogCollection.Log(result.LogMessage);
                }
            }
            catch (Exception ex)
            {
                LogCollection.Log(ex);
                MessageBox.Show(this, "Generate Breakbeat failed: " + ex.Message, "Generate Breakbeat", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.menuToolStripItem_generateBreakbeat.Enabled = true;
                this.UseWaitCursor = previousUseWaitCursor;
            }
        }

        private void menuToolStripItem_generateBreakbeatBpm80_Click(object sender, EventArgs e) => this.SetBreakbeatBpm(80f);
        private void menuToolStripItem_generateBreakbeatBpm875_Click(object sender, EventArgs e) => this.SetBreakbeatBpm(87.5f);
        private void menuToolStripItem_generateBreakbeatBpm100_Click(object sender, EventArgs e) => this.SetBreakbeatBpm(100f);
        private void menuToolStripItem_generateBreakbeatBpm120_Click(object sender, EventArgs e) => this.SetBreakbeatBpm(120f);
        private void menuToolStripItem_generateBreakbeatBpm140_Click(object sender, EventArgs e) => this.SetBreakbeatBpm(140f);
        private void menuToolStripItem_generateBreakbeatBars1_Click(object sender, EventArgs e) => this.SetBreakbeatBars(1);
        private void menuToolStripItem_generateBreakbeatBars2_Click(object sender, EventArgs e) => this.SetBreakbeatBars(2);
        private void menuToolStripItem_generateBreakbeatBars4_Click(object sender, EventArgs e) => this.SetBreakbeatBars(4);
        private void menuToolStripItem_generateBreakbeatBars8_Click(object sender, EventArgs e) => this.SetBreakbeatBars(8);
        private void menuToolStripItem_generateBreakbeatHits6_Click(object sender, EventArgs e) => this.SetBreakbeatHits(6);
        private void menuToolStripItem_generateBreakbeatHits8_Click(object sender, EventArgs e) => this.SetBreakbeatHits(8);
        private void menuToolStripItem_generateBreakbeatHits12_Click(object sender, EventArgs e) => this.SetBreakbeatHits(12);
        private void menuToolStripItem_generateBreakbeatHits16_Click(object sender, EventArgs e) => this.SetBreakbeatHits(16);
        private void menuToolStripItem_generateBreakbeatHits24_Click(object sender, EventArgs e) => this.SetBreakbeatHits(24);
        private void menuToolStripItem_generateBreakbeatDensitySparse_Click(object sender, EventArgs e) => this.SetBreakbeatDensity(0.28f);
        private void menuToolStripItem_generateBreakbeatDensityBalanced_Click(object sender, EventArgs e) => this.SetBreakbeatDensity(0.45f);
        private void menuToolStripItem_generateBreakbeatDensityDense_Click(object sender, EventArgs e) => this.SetBreakbeatDensity(0.62f);
        private void menuToolStripItem_generateBreakbeatDensityMax_Click(object sender, EventArgs e) => this.SetBreakbeatDensity(0.82f);
        private void menuToolStripItem_generateBreakbeatComplexityLow_Click(object sender, EventArgs e) => this.SetBreakbeatComplexity(0.75f);
        private void menuToolStripItem_generateBreakbeatComplexityBalanced_Click(object sender, EventArgs e) => this.SetBreakbeatComplexity(1.15f);
        private void menuToolStripItem_generateBreakbeatComplexityBusy_Click(object sender, EventArgs e) => this.SetBreakbeatComplexity(1.45f);
        private void menuToolStripItem_generateBreakbeatComplexityWild_Click(object sender, EventArgs e) => this.SetBreakbeatComplexity(1.90f);
        private void menuToolStripItem_generateBreakbeatResolution16_Click(object sender, EventArgs e) => this.SetBreakbeatResolution(16);
        private void menuToolStripItem_generateBreakbeatResolution32_Click(object sender, EventArgs e) => this.SetBreakbeatResolution(32);
        private void menuToolStripItem_generateBreakbeatSwing0_Click(object sender, EventArgs e) => this.SetBreakbeatSwing(0f);
        private void menuToolStripItem_generateBreakbeatSwing6_Click(object sender, EventArgs e) => this.SetBreakbeatSwing(0.06f);
        private void menuToolStripItem_generateBreakbeatSwing12_Click(object sender, EventArgs e) => this.SetBreakbeatSwing(0.12f);
        private void menuToolStripItem_generateBreakbeatSwing18_Click(object sender, EventArgs e) => this.SetBreakbeatSwing(0.18f);

        private void SetBreakbeatBpm(float bpm)
        {
            this.breakbeatBpm = bpm;
            this.UpdateContextMenuState();
        }

        private void SetBreakbeatBars(int bars)
        {
            this.breakbeatBars = bars;
            this.UpdateContextMenuState();
        }

        private void SetBreakbeatHits(int hitsPerBar)
        {
            this.breakbeatHitsPerBar = hitsPerBar;
            this.UpdateContextMenuState();
        }

        private void SetBreakbeatDensity(float density)
        {
            this.breakbeatDensity = density;
            this.UpdateContextMenuState();
        }

        private void SetBreakbeatComplexity(float complexity)
        {
            this.breakbeatComplexity = complexity;
            this.UpdateContextMenuState();
        }

        private void SetBreakbeatResolution(int resolution)
        {
            this.breakbeatResolution = resolution;
            this.UpdateContextMenuState();
        }

        private void SetBreakbeatSwing(float swing)
        {
            this.breakbeatSwing = swing;
            this.UpdateContextMenuState();
        }

        private async void menuToolStripItem_atomize_Click(object sender, EventArgs e)
        {
            AudioObj? source = this.GetSingleContextAudio();
            if (source == null)
            {
                return;
            }

            bool previousUseWaitCursor = this.UseWaitCursor;
            this.UseWaitCursor = true;
            this.menuToolStripItem_atomize.Enabled = false;

            try
            {
                await this.CancelAutoPlayAsync(stopCollection: true);

                LoopAtomizerSettings settings = this.CreateLoopAtomizerSettings();
                AtomizeWorkflowResult result = await BreakbeatAtomizerWorkflow_V4.AtomizeAsync(source, settings);
                List<AudioObj> atomics = result.Atomics.ToList();
                if (atomics.Count == 0)
                {
                    MessageBox.Show(this, "No atomic hits could be extracted with the current atomize settings. Try Aggressive sensitivity or a smaller Min Slice.", "Atomize", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var atomicsView = new AudioCollectionView(atomics);
                string baseName = string.IsNullOrWhiteSpace(source.Name) ? "Audio" : source.Name.Trim();
                atomicsView.Rename(baseName + "_Atomics");

                if (result.IsLikelyDrumLoop)
                {
                    string summary = result.SummaryLog ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(summary))
                    {
                        LogCollection.Log("Atomize classified hits: " + summary);
                    }
                }
                else
                {
                    LogCollection.Log($"Atomize extracted {atomics.Count} atomic sample(s) from '{source.Name}'.");
                }
            }
            catch (Exception ex)
            {
                LogCollection.Log(ex);
                MessageBox.Show(this, "Atomize failed: " + ex.Message, "Atomize", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.menuToolStripItem_atomize.Enabled = true;
                this.UseWaitCursor = previousUseWaitCursor;
            }
        }

        private LoopAtomizerSettings CreateLoopAtomizerSettings()
        {
            return new LoopAtomizerSettings
            {
                Sensitivity = this.atomizeSensitivity,
                MinSliceMs = this.atomizeMinSliceMs,
                TailPaddingMs = this.atomizeTailPaddingMs,
                AllowSingleAtomFallback = false
            };
        }

        private void menuToolStripItem_atomizeSensitivityConservative_Click(object sender, EventArgs e)
        {
            this.atomizeSensitivity = AtomizeSensitivity.Conservative;
            this.UpdateContextMenuState();
        }

        private void menuToolStripItem_atomizeSensitivityBalanced_Click(object sender, EventArgs e)
        {
            this.atomizeSensitivity = AtomizeSensitivity.Balanced;
            this.UpdateContextMenuState();
        }

        private void menuToolStripItem_atomizeSensitivityAggressive_Click(object sender, EventArgs e)
        {
            this.atomizeSensitivity = AtomizeSensitivity.Aggressive;
            this.UpdateContextMenuState();
        }

        private void menuToolStripItem_atomizeMinSlice40_Click(object sender, EventArgs e)
        {
            this.atomizeMinSliceMs = 40;
            this.UpdateContextMenuState();
        }

        private void menuToolStripItem_atomizeMinSlice80_Click(object sender, EventArgs e)
        {
            this.atomizeMinSliceMs = 80;
            this.UpdateContextMenuState();
        }

        private void menuToolStripItem_atomizeMinSlice140_Click(object sender, EventArgs e)
        {
            this.atomizeMinSliceMs = 140;
            this.UpdateContextMenuState();
        }

        private void menuToolStripItem_atomizeTail10_Click(object sender, EventArgs e)
        {
            this.atomizeTailPaddingMs = 10;
            this.UpdateContextMenuState();
        }

        private void menuToolStripItem_atomizeTail30_Click(object sender, EventArgs e)
        {
            this.atomizeTailPaddingMs = 30;
            this.UpdateContextMenuState();
        }

        private void menuToolStripItem_atomizeTail60_Click(object sender, EventArgs e)
        {
            this.atomizeTailPaddingMs = 60;
            this.UpdateContextMenuState();
        }

        private async void menuToolStripItem_delete_Click(object sender, EventArgs e)
        {
            List<AudioObj> toDelete = this.GetContextAudios();
            if (toDelete.Count == 0)
            {
                return;
            }

            foreach (AudioObj audio in toDelete)
            {
                await this.AudioC.RemoveAsync(audio.Id);
            }

            this.ResetAudioListBinding();
        }

        private void menuToolStripItem_toNewCollection_Click(object sender, EventArgs e)
        {
            List<AudioObj> toMove = this.GetContextAudios();
            if (toMove.Count == 0)
            {
                return;
            }

            var newView = new AudioCollectionView(toMove);
            newView.Show();
            foreach (AudioObj audio in toMove)
            {
                this.AudioC.Audios.Remove(audio);
            }
        }

        private void menuToolStripItem_addIndexToNames_CheckedChanged(object sender, EventArgs e)
        {
            this.AudioC.ToggleAddIndexToNames(this.menuToolStripItem_addIndexToNames.Checked);
            this.listBox_audios.Refresh();
        }

        private async void menuToolStripItem_aggregateMixSelected_Click(object sender, EventArgs e)
        {
            List<AudioObj> toMix = this.GetContextAudios();
            if (toMix.Count <= 1)
            {
                return;
            }

            AudioObj? mixedAudio = await TracksMixer.AggregateMixTracks(toMix);
            if (mixedAudio == null)
            {
                return;
            }

            mixedAudio.Name = "Mix of " + string.Join(" + ", toMix.Select(a => a.Name));
            mixedAudio.Rename(mixedAudio.Name);
            this.AudioC.Audios.Add(mixedAudio);
        }

        private void menuToolStripItem_timeStretchSelected_Click(object sender, EventArgs e)
        {
            List<AudioObj> toStretch = this.GetContextAudios();
            if (toStretch.Count == 0)
            {
                return;
            }

            var dlg = new Modules.Dialogs.TimeStretchDialog(null, toStretch);
            dlg.FormClosed += (_, _) =>
            {
                WindowMain.Instance?.UpdateTrackDependentUI();
                WindowMain.Instance?.RefreshAllCollectionViews();
            };
            dlg.Show(this);
        }

        private void menuToolStripItem_demucsSeparateSelected_Click(object sender, EventArgs e)
        {
            AudioObj? toSeparate = this.GetSingleContextAudio();
            if (toSeparate == null)
            {
                return;
            }

            using var separationForm = new OnnxDemucsDialog(toSeparate);
            separationForm.ShowDialog(this);
            WindowMain.Instance?.UpdateTrackDependentUI();
            WindowMain.Instance?.RefreshAllCollectionViews();
        }

        private async void checkBox_autoPlay_CheckedChanged(object? sender, EventArgs e)
        {
            if (!this.checkBox_autoPlay.Checked)
            {
                await this.CancelAutoPlayAsync(stopCollection: true).ConfigureAwait(false);
            }
        }
    }
}
