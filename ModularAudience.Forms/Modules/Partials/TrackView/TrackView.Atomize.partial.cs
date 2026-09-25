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

            LoopAtomizerSettings settings = LoopAtomizerSettings.Default;
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
                    progressDialog = new ProgressDialog($"Atomizing '{baseName}' ...", progress,
                        windowCloseDelay: 0.0d, ct: cts.Token, cancellationSource: cts);
                    progressDialog.Show(this);
                    progressDialog.BringToFront();

                    AudioAtomizeResult result = await AudioAtomizerWorkflow
                        .AtomizeAsync(source, settings, progress, cts.Token);
                    List<AudioObj> atomics = result.Atomics.ToList();
                    if (atomics.Count == 0)
                    {
                        progressDialog.Complete();
                        MessageBox.Show(this, "No audible hits were detected in the current selection/audio.", "Atomize", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    List<(string Type, List<AudioObj> Atomics)> atomicGroups = GroupAtomicsByType(
                        atomics, result.IsLikelyDrumLoop);
                    foreach ((string type, List<AudioObj> groupedAtomics) in atomicGroups)
                    {
                        var atomicsView = new AudioCollectionView(groupedAtomics);
                        atomicsView.Rename($"{baseName}-atomics-{type}");
                    }

                    if (result.IsLikelyDrumLoop)
                    {
                        List<AudioObj> drumsetBest = await CreateDrumsetBestAsync(atomicGroups);
                        if (drumsetBest.Count > 0)
                        {
                            try
                            {
                                var drumsetView = new AudioCollectionView(drumsetBest);
                                drumsetView.Rename($"{baseName}-atomics-drumset-best");
                            }
                            catch
                            {
                                foreach (AudioObj audio in drumsetBest)
                                {
                                    audio.Dispose();
                                }

                                throw;
                            }
                        }
                    }

                    if (result.IsLikelyDrumLoop && !string.IsNullOrWhiteSpace(result.SummaryLog))
                    {
                        LogCollection.Log($"Atomize created {atomicGroups.Count} typed collection(s): " +
                            string.Join(", ", atomicGroups.Select(group => $"{group.Type}={group.Atomics.Count}")) +
                            ". Classified hits: " + result.SummaryLog);
                    }
                    else
                    {
                        LogCollection.Log($"TrackView atomize extracted {atomics.Count} atomic sample(s) into " +
                            $"{atomicGroups.Count} collection(s) from '{this.OriginalAudio.Name}'.");
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

        private static List<(string Type, List<AudioObj> Atomics)> GroupAtomicsByType(
            IEnumerable<AudioObj> atomics, bool isLikelyDrumLoop)
        {
            Dictionary<string, List<AudioObj>> groups = new(StringComparer.Ordinal);
            foreach (AudioObj atomic in atomics)
            {
                string type = "unclassified";
                string? confidenceText = atomic.CustomTags["AtomizeConfidence"];
                if (isLikelyDrumLoop && !string.IsNullOrWhiteSpace(atomic.SampleTag) &&
                    double.TryParse(confidenceText, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double confidence) && confidence >= 0.65)
                {
                    type = AtomicTypeSlug(atomic.SampleTag);
                }

                if (!groups.TryGetValue(type, out List<AudioObj>? group))
                {
                    group = [];
                    groups.Add(type, group);
                }

                group.Add(atomic);
            }

            return groups.OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => (group.Key, group.Value
                    .OrderByDescending(atomic => atomic.Duration)
                    .ThenBy(atomic => atomic.Name, StringComparer.Ordinal)
                    .ToList()))
                .ToList();
        }

        private static string AtomicTypeSlug(string type) => type switch
        {
            "HiHatClosed" => "hi-hat-closed",
            "HiHatOpen" => "hi-hat-open",
            "SnareRattle" => "snare-rattle",
            "CrashShort" => "crash-short",
            "CrashLong" => "crash-long",
            "FloorTom" => "floor-tom",
            "TomLow" => "tom-low",
            "TomMid" => "tom-mid",
            "TomHigh" => "tom-high",
            _ => type.Trim().ToLowerInvariant()
        };

        private static async Task<List<AudioObj>> CreateDrumsetBestAsync(
            IEnumerable<(string Type, List<AudioObj> Atomics)> groups)
        {
            List<AudioObj> drumsetBest = [];
            try
            {
                foreach ((string type, List<AudioObj> atomics) in groups.Where(group => group.Type != "unclassified"))
                {
                    IReadOnlyList<AudioObj> representatives = LoopAtomizer_V4.SelectDistinctRepresentatives(atomics);
                    for (int index = 0; index < representatives.Count; index++)
                    {
                        AudioObj representative = representatives[index];
                        AudioObj clone = await representative.CloneAsync();
                        clone.Rename($"{type}#{index + 1:D2}");
                        clone.SampleTag = representative.SampleTag;
                        clone.Tag = representative.Tag;
                        foreach ((string key, string value) in representative.CustomTags.Values)
                        {
                            clone.CustomTags[key] = value;
                        }

                        drumsetBest.Add(clone);
                    }
                }

                return drumsetBest;
            }
            catch
            {
                foreach (AudioObj audio in drumsetBest)
                {
                    audio.Dispose();
                }

                throw;
            }
        }

    }
}
