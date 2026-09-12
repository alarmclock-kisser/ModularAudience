using ModularAudience.Audio;
using ModularAudience.Audio.Processing;
using ModularAudience.Audio.Processors_V4;
using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Forms.Modules
{
    public partial class TrackView
    {
        private void menuItem_atomize_Click(object? sender, EventArgs e) => _ = this.AtomizeSelectionOrTrackAsync();

        private async Task AtomizeSelectionOrTrackAsync()
        {
            if (this.OriginalAudio.Data == null || this.OriginalAudio.Data.Length == 0)
            {
                return;
            }

            bool previousWaitCursor = this.UseWaitCursor;
            this.UseWaitCursor = true;
            this.menuItem_atomize.Enabled = false;

            CancellationTokenSource? cts = new();
            ProgressDialog? progressDialog = null;
            Progress<double> progress = new(value => progressDialog?.Report(value));

            try
            {
                bool hasSelection = this.HasValidSelection();
                string baseName = string.IsNullOrWhiteSpace(this.OriginalAudio.Name) ? "Audio" : this.OriginalAudio.Name.Trim();
                if (hasSelection)
                {
                    baseName += "_Selection";
                }

                AudioObj source = hasSelection
                    ? await this.OriginalAudio.CloneFromSelectionAsync() ?? throw new InvalidOperationException("Failed to clone the selection.")
                    : await this.OriginalAudio.CloneAsync() ?? throw new InvalidOperationException("Failed to clone the audio.");

                try
                {
                    progressDialog = new ProgressDialog($"Atomizing '{baseName}' ...", progress, ct: cts.Token, cancellationSource: cts);
                    progressDialog.Show(this);
                    progressDialog.BringToFront();

                    AudioAtomizeResult result = await AudioAtomizerWorkflow
                        .AtomizeAsync(source, LoopAtomizerSettings.Default, progress, cts.Token);
                    List<AudioObj> atomics = result.Atomics.ToList();
                    if (atomics.Count == 0)
                    {
                        MessageBox.Show(this, "No atomic hits could be extracted from the current selection/audio.", "Atomize", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    var atomicsView = new AudioCollectionView(atomics);
                    atomicsView.Rename($"{baseName}_Atomics");

                    if (result.IsLikelyDrumLoop && !string.IsNullOrWhiteSpace(result.SummaryLog))
                    {
                        LogCollection.Log("Atomize classified hits: " + result.SummaryLog);
                    }
                    else
                    {
                        LogCollection.Log($"TrackView atomize extracted {atomics.Count} atomic sample(s) from '{this.OriginalAudio.Name}'.");
                    }
                }
                finally
                {
                    source.Dispose();
                }

                progressDialog.Complete();
            }
            catch (OperationCanceledException)
            {
                LogCollection.Log("Atomize cancelled.");
            }
            catch (Exception ex)
            {
                LogCollection.Log(ex);
                MessageBox.Show(this, "Atomize failed: " + ex.Message, "Atomize", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (progressDialog != null && !progressDialog.IsDisposed)
                {
                    progressDialog.Close();
                }

                cts.Dispose();
                this.menuItem_atomize.Enabled = true;
                this.UseWaitCursor = previousWaitCursor;
            }
        }
    }
}
