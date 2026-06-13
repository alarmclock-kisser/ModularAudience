using ModularAudience.Audio;
using ModularAudience.Generators;
using NAudio.SoundFont;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ModularAudience.Forms.Modules.Dialogs
{
    public partial class BreakbeatGeneratorDialog : Form
    {
        private static readonly TimeSpan LlmDiscoveryTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan LlmRequestTimeout = TimeSpan.FromMinutes(5);

        internal readonly AudioCollection AudioC = new();
        internal AudioCollectionView? CollectionView { get; private set; } = null;
        private CancellationTokenSource? autoPlayCancellationTokenSource;
        private bool sampleSelectionFromUserInput;

        internal AudioObj? SelectedTrack => this.listBox_samples.SelectedItem as AudioObj;

        private bool AutoPlayEnabled => this.checkBox_autoPlay.Checked;
        private int Bars => (int) this.numericUpDown_bars.Value;
        private int Bpm => (int) this.numericUpDown_bpm.Value;
        private float Density => (float) this.numericUpDown_density.Value;
        private int Resolution => (int) this.numericUpDown_resolution.Value;
        private float Swing => (float) this.numericUpDown_swing.Value;
        private float Complexity => (float) this.numericUpDown_complexity.Value;
        private int Seed => (int) this.numericUpDown_seed.Value;

        private bool Interleaved => this.checkBox_interleaved.Checked;
        internal string SelectedPreset => this.comboBox_preset.SelectedItem as string ?? " - None - ";

        internal bool BotActivated { get; private set; } = false;

        private CancellationTokenSource? botCancellationTokenSource;
        private Task? botLoopTask;
        private AudioObj? botCurrentPlaybackAudio;
        private readonly Lock botPlaybackGate = new();



        public BreakbeatGeneratorDialog(IEnumerable<AudioObj> samples)
        {
            this.InitializeComponent();

            foreach (AudioObj obj in samples)
            {
                this.AudioC.Audios.Add(obj.Clone());
            }

            this.comboBox_drumset.DataSource = Enum.GetValues<DrumsetElement>();
            this.ComboBox_AddPresetsReflection(this.comboBox_preset);

            this.listBox_samples.SelectionMode = SelectionMode.One;
            this.listBox_samples.ContextMenuStrip = this.contextMenuStrip_samples;

            this.listBox_samples.DataSource = this.AudioC.Audios;
            this.listBox_samples.DisplayMember = "Name";
            this.listBox_samples.SelectedIndex = this.listBox_samples.Items.Count > 0 ? 0 : -1;
            this.listBox_samples.DrawItem += this.listBox_samples_DrawItem;
            this.listBox_samples.MouseDown += this.listBox_samples_MouseDown;
            this.listBox_samples.KeyDown += this.listBox_samples_KeyDown;

            this.numericUpDown_seed.Value = new Random().Next(0, 999999998);

            this.checkBox_autoPlay.CheckedChanged += async (s, e) =>
            {
                if (!this.AutoPlayEnabled)
                {
                    this.CancelAutoPlayPreview();
                    return;
                }

                if (this.SelectedTrack is not null)
                {
                    await this.PlaySelectedTrackPreviewAsync(this.SelectedTrack);
                }
            };

            this.StartPosition = FormStartPosition.Manual;
            this.Location = WindowsScreenHelper.GetCornerPosition(this, false, false, WindowMain.CurrentScreenId);

            this.FormClosing += async (s, e) =>
            {
                await this.StopBotAsync(stopPlaybackImmediately: true);
                this.CancelAutoPlayPreview();
                this.pictureBox_beatMap.Image?.Dispose();
                this.pictureBox_beatMap.Image = null;
                this.AudioC.Dispose();
            };

            this.ShowBeatMap(CreateEmptyPattern(this.AudioC.Audios.Count, Math.Max(1, this.Bars * this.Resolution)), this.GetBeatMapRowLabels());
        }

        private async void listBox_samples_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.comboBox_drumset.SelectedItem = this.SelectedTrack?.Tag ?? null;

            if (!this.sampleSelectionFromUserInput)
            {
                return;
            }

            this.sampleSelectionFromUserInput = false;

            if (this.AutoPlayEnabled && this.SelectedTrack is not null && this.listBox_samples.SelectedIndex >= 0)
            {
                await this.PlaySelectedTrackPreviewAsync(this.SelectedTrack);
            }
        }

        private void listBox_samples_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                this.sampleSelectionFromUserInput = true;
            }
        }

        private void listBox_samples_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Up or Keys.Down)
            {
                this.sampleSelectionFromUserInput = true;
            }
        }

        private void comboBox_drumset_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Set tag to selected audioobj
            if (this.SelectedTrack is not null)
            {
                this.SelectedTrack.Tag = this.comboBox_drumset.SelectedItem;
            }
        }

        private void button_autoMap_Click(object sender, EventArgs e)
        {
            string[] sampleNames = this.AudioC.Audios.Select(a => a.Name).ToArray();
            DrumsetElement[] drumsetMapping = BreakbeatGenerator.MatchSampleNamesToDrumsetElements(sampleNames);

            // Set tag to every audioobj
            for (int i = 0; i < this.AudioC.Audios.Count; i++)
            {
                this.AudioC.Audios[i].Tag = drumsetMapping[i];
            }

            this.listBox_samples.SelectedIndex = -1;
            this.listBox_samples.SelectedIndex = this.listBox_samples.Items.Count > 0 ? 0 : -1;
        }

        private void button_edit_Click(object sender, EventArgs e)
        {
            if (this.SelectedTrack is null)
            {
                return;
            }

            var tv = new TrackView(this.SelectedTrack, this.AudioC);
            tv.Show();
        }




        // Special listBox drawItem event to draw AudioObjs that have a tag in grey instead
        private void listBox_samples_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= this.listBox_samples.Items.Count)
            {
                return;
            }
            AudioObj item = (AudioObj) this.listBox_samples.Items[e.Index];
            // Determine the color based on whether the item has a tag
            Color textColor = item.Tag is not null ? Color.Gray : e.ForeColor;
            // Draw the background
            e.DrawBackground();
            // Draw the text
            using (Brush textBrush = new SolidBrush(textColor))
            {
                if (e.Font != null)
                {
                    e.Graphics.DrawString(item.Name, e.Font, textBrush, e.Bounds);
                }
            }
            // Draw the focus rectangle if the item is focused
            e.DrawFocusRectangle();
        }

        private async void button_go_Click(object sender, EventArgs e)
        {
            bool ctrlFlag = (ModifierKeys & Keys.Control) == Keys.Control;

            DrumsetElement[] mappedDrumset = new DrumsetElement[this.AudioC.Audios.Count];
            for (int i = 0; i < this.AudioC.Audios.Count; i++)
            {
                if (this.AudioC.Audios[i].Tag is DrumsetElement de)
                {
                    mappedDrumset[i] = de;
                }
                else
                {
                    LogCollection.Log($"AudioObj '{this.AudioC.Audios[i].Name}' does not have a DrumsetElement mapping. Using 'Snare'.");
                    mappedDrumset[i] = DrumsetElement.Snare;
                }
            }

            LogCollection.Log("Generating Break-Beat with seed: " + this.Seed);

            List<bool[]> breakbeat = await BreakbeatGenerator_V2.GenerateBreakPatternAsync(
                drumset: mappedDrumset,
                bars: this.Bars,
                density: this.Density,
                resolution: this.Resolution,
                swing: this.Swing,
                complexity: this.Complexity,
                interleaved: this.Interleaved,
                seed: this.Seed,
                preset: this.SelectedPreset
            );

            await this.RenderAndShowBreakbeatAsync(breakbeat, this.SelectedPreset != " - None - " ? this.SelectedPreset : "Breakbeat");

            if (!ctrlFlag)
            {
                this.numericUpDown_seed.Value = new Random().Next(0, 999999998);
            }
        }

        private void numericUpDown_seed_ValueChanged(object sender, EventArgs e)
        {
            // Rekursion verhindern, indem wir ein Tag-Flag setzen
            if (this.numericUpDown_seed.Tag is bool busy && busy)
            {
                return;
            }

            try
            {
                this.numericUpDown_seed.Tag = true;
                this.numericUpDown_seed.Value = new Random().Next(0, 999999998);
            }
            finally
            {
                this.numericUpDown_seed.Tag = false;
            }
        }

        private void removeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.SelectedTrack is null)
            {
                return;
            }

            this.AudioC.Audios.Remove(this.SelectedTrack);
        }

        private void editToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.SelectedTrack is null)
            {
                return;
            }

            var tv = new TrackView(this.SelectedTrack, this.AudioC);
        }


        internal void ComboBox_AddPresetsReflection(ComboBox comboBox)
        {
            comboBox.Items.Clear();
            // Via reflection, get public static methods of BreakbeatGenerator or partial that start with "Preset_"
            var presetMethodNames = typeof(BreakbeatGenerator).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(m => m.Name.StartsWith("Preset_")).Select(m => m.Name.Replace("Preset_", "")).ToArray();

            comboBox.Items.AddRange(presetMethodNames);
            comboBox.Items.Add(" - None - ");

            comboBox.SelectedIndex = comboBox.Items.Count - 1;
        }

        private async void button_llm_Click(object sender, EventArgs e)
        {
            if (this.AudioC.Audios.Count == 0)
            {
                LogCollection.Log("LLM Break-Beat generation skipped because no samples are loaded.");
                return;
            }

            this.button_llm.Enabled = false;
            bool previousUseWaitCursor = this.UseWaitCursor;
            this.UseWaitCursor = true;
            string? llmContent = null;

            try
            {
                string apiUrl = this.textBox_apiUrl.Text.Trim();
                string userPrompt = string.IsNullOrWhiteSpace(this.textBox_prompt.Text)
                    ? "Generate a detailed breakbeat that uses the provided drumkit musically."
                    : this.textBox_prompt.Text.Trim();

                var drumkit = this.GetDrumkitSampleInfos();
                int totalSteps = this.Bars * this.Resolution;
                string systemPrompt = this.BuildLlmSystemPrompt();
                string llmUserPrompt = this.BuildLlmUserPrompt(drumkit, userPrompt);

                LogCollection.Log("Generating Break-Beat via LLM.");

                llmContent = await this.RequestBreakbeatFromLlmAsync(apiUrl, systemPrompt, llmUserPrompt);
                LogMultiline("LLM raw response", llmContent);

                LlmBreakbeatParseResult parsed;
                try
                {
                    parsed = this.ParseLlmBreakbeat(llmContent, drumkit, this.Bars, this.Resolution);
                }
                catch (Exception ex)
                {
                    string parseError = "LLM-Antwort konnte nicht als Beat-JSON geparst werden." + Environment.NewLine + Environment.NewLine + ex.Message + Environment.NewLine + Environment.NewLine + "Raw response:" + Environment.NewLine + llmContent;
                    LogMultiline("LLM JSON parse error", parseError);
                    this.ShowCopyableMessageBox("LLM JSON Parse Error", parseError);
                    return;
                }

                List<bool[]> breakbeat = parsed.Pattern;

                if (this.Interleaved)
                {
                    breakbeat = ApplyInterleaving(breakbeat, drumkit.Select(x => x.Element).ToArray());
                }

                await this.RenderAndShowBreakbeatAsync(breakbeat, parsed.PatternName, drumkit.Select(x => x.Name).ToArray());
                LogCollection.Log("LLM Break-Beat generation finished.");
            }
            catch (TaskCanceledException ex)
            {
                string timeoutMessage = "LLM request timed out or was canceled." + Environment.NewLine + Environment.NewLine + ex.Message;
                LogMultiline("LLM timeout", timeoutMessage);
                this.ShowCopyableMessageBox("LLM Timeout", timeoutMessage + (string.IsNullOrWhiteSpace(llmContent) ? string.Empty : Environment.NewLine + Environment.NewLine + "Raw response:" + Environment.NewLine + llmContent));
            }
            catch (Exception ex)
            {
                LogCollection.Log(ex);
                string errorMessage = "LLM generation failed." + Environment.NewLine + Environment.NewLine + ex.Message;
                if (!string.IsNullOrWhiteSpace(llmContent))
                {
                    errorMessage += Environment.NewLine + Environment.NewLine + "Raw response:" + Environment.NewLine + llmContent;
                }

                this.ShowCopyableMessageBox("LLM Error", errorMessage);
            }
            finally
            {
                this.UseWaitCursor = previousUseWaitCursor;
                this.button_llm.Enabled = true;
            }
        }

        private async Task PlaySelectedTrackPreviewAsync(AudioObj track)
        {
            this.CancelAutoPlayPreview();

            var cancellationTokenSource = new CancellationTokenSource();
            this.autoPlayCancellationTokenSource = cancellationTokenSource;

            try
            {
                LogCollection.Log($"AutoPlay preview: {track.Name}");
                await track.PlayAsync(cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogCollection.Log($"AutoPlay preview failed for '{track.Name}'.");
                LogCollection.Log(ex);
            }
            finally
            {
                if (ReferenceEquals(this.autoPlayCancellationTokenSource, cancellationTokenSource))
                {
                    this.autoPlayCancellationTokenSource = null;
                }

                cancellationTokenSource.Dispose();
            }
        }

        private void CancelAutoPlayPreview()
        {
            if (this.autoPlayCancellationTokenSource == null)
            {
                return;
            }

            try
            {
                this.autoPlayCancellationTokenSource.Cancel();
            }
            catch
            {
            }
            finally
            {
                this.autoPlayCancellationTokenSource.Dispose();
                this.autoPlayCancellationTokenSource = null;
            }
        }

        private List<DrumkitSampleInfo> GetDrumkitSampleInfos()
        {
            var drumkit = new List<DrumkitSampleInfo>(this.AudioC.Audios.Count);
            HashSet<string> usedNoteKeys = [];

            for (int i = 0; i < this.AudioC.Audios.Count; i++)
            {
                AudioObj audio = this.AudioC.Audios[i];
                DrumsetElement element = audio.Tag is DrumsetElement mappedElement ? mappedElement : DrumsetElement.Snare;
                string name = string.IsNullOrWhiteSpace(audio.Name) ? $"Sample {i}" : audio.Name.Trim();
                string fileStem = string.IsNullOrWhiteSpace(name) ? string.Empty : Path.GetFileNameWithoutExtension(name);
                string id = $"sample_{i:00}";
                string noteKey = CreateUniquePromptNoteKey(element, i, usedNoteKeys);

                HashSet<string> aliases =
                [
                    name,
                    fileStem,
                    element.ToString(),
                    id,
                    noteKey,
                    i.ToString(CultureInfo.InvariantCulture)
                ];

                drumkit.Add(new DrumkitSampleInfo
                {
                    Audio = audio,
                    Id = id,
                    Index = i,
                    Name = name,
                    NoteKey = noteKey,
                    Element = element,
                    DurationMilliseconds = audio.Duration.TotalMilliseconds,
                    Aliases = aliases.Where(x => !string.IsNullOrWhiteSpace(x)).Select(NormalizeToken).Distinct().ToArray()
                });
            }

            return drumkit;
        }

        private string BuildLlmSystemPrompt()
        {
            return $$"""
                You are a breakbeat arranger for a sampler.
                Return JSON only. Do not add markdown, explanations, or code fences.

                The user message contains the musical request, numeric parameters, and the drumkit dictionary.
                Read the numeric parameters carefully and obey them.
                Infer likely drum roles from the provided note keys, sample names, ids, and durations.
                Sample duration can help estimate how dense or long a hit should feel.

                Return this JSON shape:
                {
                  "metadata": {
                    "userPrompt": "string",
                    "seed": 123,
                    "model": "string"
                  },
                  "parameters": {
                    "bpm": 92.5,
                    "bars": 4,
                    "resolution": 16,
                    "density": 0.333,
                    "swing": 15,
                    "complexity": 0.85
                  },
                  "drumkit": {
                    "kick": { "id": "bd_01", "duration_ms": 180, "sample_index": 0, "sample_name": "Kick.wav" }
                  },
                  "beatmap": {
                    "note_keys": ["kick", "snare", "hihat", "crash"],
                    "bars": [
                      {
                        "bar_index": 0,
                        "patterns": {
                          "kick": [true, false, false, false],
                          "snare": [false, false, true, false]
                        }
                      }
                    ]
                  },
                  "patternName": "ShortName"
                }

                Rules:
                - Output valid JSON only.
                - Keep metadata.userPrompt aligned with the user's verbal prompt.
                - Keep parameters aligned with the user-provided numeric parameters.
                - Keep drumkit aligned with the user-provided drumkit dictionary.
                - Use only note keys and sample references from the provided drumkit.
                - In beatmap.note_keys, list the note keys you actually use.
                - For each bar, patterns must be an object keyed by note key.
                - Each pattern array length must equal the provided resolution.
                - Omitted note keys or omitted bars are treated as silence.
                - Be creative but musically coherent.
                - Favor strong rhythm design that matches the verbal prompt, density, swing, complexity, and seed.
                """;
        }

        private string BuildLlmUserPrompt(IReadOnlyList<DrumkitSampleInfo> drumkit, string userPrompt)
        {
            string modelHint = "local-openai-compatible-model";

            var payload = new
            {
                metadata = new
                {
                    userPrompt,
                    seed = this.Seed,
                    model = modelHint
                },
                parameters = new
                {
                    bpm = this.Bpm,
                    bars = this.Bars,
                    resolution = this.Resolution,
                    density = this.Density,
                    swing = this.Swing,
                    complexity = this.Complexity,
                    preset = this.SelectedPreset != " - None - " ? this.SelectedPreset : null
                },
                drumkit = drumkit.ToDictionary(
                    sample => sample.NoteKey,
                    sample => new
                    {
                        id = sample.Id,
                        duration_ms = Math.Round(sample.DurationMilliseconds, 1),
                        sample_index = sample.Index,
                        sample_name = sample.Name,
                        drum_role = sample.Element.ToString()
                    })
            };

            return JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        private async Task<string> RequestBreakbeatFromLlmAsync(string apiUrl, string systemPrompt, string userPrompt)
        {
            using var httpClient = CreateLlmHttpClient();
            httpClient.Timeout = Timeout.InfiniteTimeSpan;

            using var discoveryCancellationTokenSource = new CancellationTokenSource(LlmDiscoveryTimeout);
            (Uri modelsUri, string model) = await DiscoverWorkingModelAsync(httpClient, apiUrl, discoveryCancellationTokenSource.Token);
            Uri chatCompletionsUri = ReplaceEndpoint(modelsUri, "chat/completions");

            LogCollection.Log($"LLM chat endpoint: {chatCompletionsUri}");
            LogCollection.Log($"LLM model: {model}");

            object[] messages =
            [
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            ];

            Dictionary<string, object?> payload = new()
            {
                ["model"] = model,
                ["temperature"] = Math.Clamp(0.20 + (this.Complexity * 0.60), 0.1, 1.0),
                ["seed"] = this.Seed,
                ["messages"] = messages,
                ["response_format"] = new Dictionary<string, string>
                {
                    ["type"] = "json_object"
                }
            };

            using var requestCancellationTokenSource = new CancellationTokenSource(LlmRequestTimeout);

            try
            {
                return await SendChatCompletionRequestAsync(httpClient, chatCompletionsUri, payload, requestCancellationTokenSource.Token);
            }
            catch (HttpRequestException)
            {
                LogCollection.Log("LLM endpoint rejected response_format=json_object. Retrying without response_format.");
                payload.Remove("response_format");
                return await SendChatCompletionRequestAsync(httpClient, chatCompletionsUri, payload, requestCancellationTokenSource.Token);
            }
            catch (OperationCanceledException ex) when (requestCancellationTokenSource.IsCancellationRequested)
            {
                throw new TaskCanceledException($"The LLM request exceeded the configured timeout of {LlmRequestTimeout.TotalSeconds:0} seconds.", ex);
            }
        }

        private static async Task<string> SendChatCompletionRequestAsync(HttpClient httpClient, Uri endpoint, Dictionary<string, object?> payload, CancellationToken cancellationToken)
        {
            string jsonPayload = JsonSerializer.Serialize(payload);
            LogCollection.Log($"LLM POST {endpoint}");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"LLM request failed with HTTP {(int) response.StatusCode}: {responseBody}");
            }

            using JsonDocument responseJson = JsonDocument.Parse(responseBody);
            if (!TryExtractAssistantContent(responseJson.RootElement, out string content) || string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException("LLM response did not contain assistant content.");
            }

            return content;
        }

        private static async Task<(Uri ModelsUri, string Model)> DiscoverWorkingModelAsync(HttpClient httpClient, string apiUrl, CancellationToken cancellationToken)
        {
            List<string> errors = [];
            foreach (Uri modelsUri in BuildOpenAiCandidateUris(apiUrl, "models"))
            {
                try
                {
                    LogCollection.Log($"LLM connect test: GET {modelsUri}");
                    using HttpResponseMessage response = await httpClient.GetAsync(modelsUri, cancellationToken);
                    string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException($"HTTP {(int) response.StatusCode}: {responseBody}");
                    }

                    using JsonDocument responseJson = JsonDocument.Parse(responseBody);
                    if (TryGetAnyProperty(responseJson.RootElement, out JsonElement dataElement, "data") && dataElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement modelElement in dataElement.EnumerateArray())
                        {
                            if (TryGetAnyProperty(modelElement, out JsonElement idElement, "id") && idElement.ValueKind == JsonValueKind.String)
                            {
                                string? modelId = idElement.GetString();
                                if (!string.IsNullOrWhiteSpace(modelId))
                                {
                                    LogCollection.Log($"LLM connect test OK: {modelsUri}");
                                    return (modelsUri, modelId);
                                }
                            }
                        }
                    }

                    throw new InvalidOperationException("No model id returned by /models endpoint.");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw new TaskCanceledException($"The LLM model discovery exceeded the configured timeout of {LlmDiscoveryTimeout.TotalSeconds:0} seconds.");
                }
                catch (Exception ex)
                {
                    string error = $"LLM connect test failed for {modelsUri}: {ex.Message}";
                    errors.Add(error);
                    LogCollection.Log(error);
                }
            }

            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        private LlmBreakbeatParseResult ParseLlmBreakbeat(string llmContent, IReadOnlyList<DrumkitSampleInfo> drumkit, int bars, int resolution)
        {
            int totalSteps = Math.Max(1, bars * resolution);
            string jsonPayload = ExtractJsonPayload(llmContent);
            using JsonDocument document = ParseJsonPayloadSmart(jsonPayload);

            JsonElement root = document.RootElement;
            var pattern = CreateEmptyPattern(drumkit.Count, totalSteps);
            bool parsedAnything = false;

            if (TryGetAnyProperty(root, out JsonElement beatmapElement, "beatmap"))
            {
                parsedAnything |= ApplyBeatmapDefinition(beatmapElement, pattern, drumkit, bars, resolution);
            }

            if (TryGetAnyProperty(root, out JsonElement tracksElement, "tracks", "lanes", "voices", "samples"))
            {
                parsedAnything |= ApplyTrackDefinitions(tracksElement, pattern, drumkit, totalSteps);
            }

            if (TryGetAnyProperty(root, out JsonElement patternElement, "pattern", "grid", "matrix"))
            {
                parsedAnything |= ApplyPatternDefinition(patternElement, pattern, drumkit, totalSteps);
            }

            if (TryGetAnyProperty(root, out JsonElement stepsElement, "steps", "sequence", "events"))
            {
                parsedAnything |= ApplyStepDefinitions(stepsElement, pattern, drumkit, totalSteps);
            }

            if (!parsedAnything && root.ValueKind == JsonValueKind.Array)
            {
                parsedAnything = ApplyPatternDefinition(root, pattern, drumkit, totalSteps);
            }

            if (!parsedAnything)
            {
                throw new InvalidOperationException("LLM response JSON could not be converted into a breakbeat pattern.");
            }

            string patternName = TryReadString(root, "patternName", "name", "title") ?? "LLMBreakbeat";
            return new LlmBreakbeatParseResult
            {
                PatternName = patternName,
                Pattern = pattern
            };
        }

        private async Task RenderAndShowBreakbeatAsync(List<bool[]> breakbeat, string patternName, IReadOnlyList<string>? rowLabels = null)
        {
            this.ShowBeatMap(breakbeat, rowLabels ?? this.GetBeatMapRowLabels());

            var audioObj = await BreakbeatGenerator_V2.RenderBreakbeatAsync(breakbeat, this.AudioC.Audios, this.Bpm, this.Resolution, this.Swing, patternName);
            if (audioObj == null)
            {
                LogCollection.Log("Failed to generate breakbeat audio.");
                return;
            }

            this.CollectionView ??= new AudioCollectionView([]);
            this.CollectionView.AudioC.Audios.Add(audioObj);
            this.CollectionView.Show();
            this.CollectionView.Rename("Break-Beat" + (this.CollectionView.AudioC.Audios.Count() == 1 ? "" : "(s)") + " Generated " + this.Bpm.ToString("F1", CultureInfo.InvariantCulture) + " BPM");
        }

        private IReadOnlyList<string> GetBeatMapRowLabels()
        {
            return this.AudioC.Audios.Select((audio, index) =>
            {
                string name = string.IsNullOrWhiteSpace(audio.Name) ? $"Sample {index}" : audio.Name.Trim();
                if (audio.Tag is DrumsetElement element)
                {
                    return $"{element}: {name}";
                }

                return name;
            }).ToArray();
        }

        private void ShowBeatMap(IReadOnlyList<bool[]> breakbeat, IReadOnlyList<string>? rowLabels = null)
        {
            if (this.pictureBox_beatMap.Width <= 0 || this.pictureBox_beatMap.Height <= 0)
            {
                return;
            }

            Bitmap bitmap = CreateBeatMapBitmap(breakbeat, rowLabels ?? this.GetBeatMapRowLabels(), this.pictureBox_beatMap.Size, this.Bars, this.Resolution);
            Image? previous = this.pictureBox_beatMap.Image;
            this.pictureBox_beatMap.Image = bitmap;
            previous?.Dispose();
        }

        private static Bitmap CreateBeatMapBitmap(IReadOnlyList<bool[]> breakbeat, IReadOnlyList<string> rowLabels, Size size, int bars, int resolution)
        {
            int width = Math.Max(1, size.Width);
            int height = Math.Max(1, size.Height);
            var bitmap = new Bitmap(width, height);

            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.FromArgb(247, 247, 247));

            if (breakbeat.Count == 0 || breakbeat[0].Length == 0)
            {
                return bitmap;
            }

            int rows = breakbeat.Count;
            int columns = breakbeat[0].Length;
            float leftMargin = Math.Min(116f, width * 0.34f);
            float topMargin = 8f;
            float gridWidth = Math.Max(1f, width - leftMargin - 6f);
            float gridHeight = Math.Max(1f, height - topMargin - 6f);
            float cellWidth = gridWidth / columns;
            float cellHeight = gridHeight / rows;

            using Font labelFont = new("Segoe UI", Math.Max(6f, Math.Min(9f, cellHeight * 0.45f)), FontStyle.Regular, GraphicsUnit.Point);
            using StringFormat labelFormat = new()
            {
                Alignment = StringAlignment.Far,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };
            using Brush hitBrush = new SolidBrush(Color.FromArgb(60, 110, 255));
            using Brush emptyBrush = new SolidBrush(Color.FromArgb(232, 232, 232));
            using Brush textBrush = new SolidBrush(Color.FromArgb(70, 70, 70));
            using Pen gridPen = new(Color.FromArgb(210, 210, 210));
            using Pen barPen = new(Color.FromArgb(130, 130, 130), 2f);
            using Pen subdivisionPen = new(Color.FromArgb(185, 185, 185));

            for (int row = 0; row < rows; row++)
            {
                RectangleF labelBounds = new(0, topMargin + (row * cellHeight), Math.Max(1f, leftMargin - 6f), Math.Max(1f, cellHeight));
                string rowLabel = row < rowLabels.Count ? rowLabels[row] : $"Track {row}";
                graphics.DrawString(rowLabel, labelFont, textBrush, labelBounds, labelFormat);

                for (int column = 0; column < columns; column++)
                {
                    RectangleF cell = new(leftMargin + (column * cellWidth), topMargin + (row * cellHeight), Math.Max(1f, cellWidth - 1f), Math.Max(1f, cellHeight - 1f));
                    graphics.FillRectangle(emptyBrush, cell);
                    if (breakbeat[row][column])
                    {
                        graphics.FillRectangle(hitBrush, cell);
                    }

                    graphics.DrawRectangle(gridPen, cell.X, cell.Y, cell.Width, cell.Height);
                }
            }

            int stepsPerBar = Math.Max(1, resolution);
            for (int column = 0; column <= columns; column++)
            {
                float x = leftMargin + (column * cellWidth);
                bool isBar = column > 0 && column < columns && column % stepsPerBar == 0;
                if (isBar)
                {
                    graphics.DrawLine(barPen, x, topMargin, x, topMargin + gridHeight);
                }
                else if (column < columns)
                {
                    graphics.DrawLine(subdivisionPen, x, topMargin, x, topMargin + gridHeight);
                }
            }

            for (int bar = 0; bar < bars; bar++)
            {
                RectangleF barBounds = new(leftMargin + (bar * stepsPerBar * cellWidth), 0, stepsPerBar * cellWidth, Math.Max(8f, topMargin));
                graphics.DrawString((bar + 1).ToString(CultureInfo.InvariantCulture), labelFont, textBrush, barBounds);
            }

            return bitmap;
        }

        private static HttpClient CreateLlmHttpClient()
        {
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = static (message, cert, chain, errors) =>
                errors == SslPolicyErrors.None || message?.RequestUri?.IsLoopback == true;

            return new HttpClient(handler);
        }

        private static IEnumerable<Uri> BuildOpenAiCandidateUris(string rawUrl, string relativeEndpoint)
        {
            Uri primary = BuildOpenAiUri(rawUrl, relativeEndpoint);
            yield return primary;

            if (!primary.IsLoopback || !string.Equals(primary.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            var builder = new UriBuilder(primary)
            {
                Scheme = Uri.UriSchemeHttp,
                Port = primary.IsDefaultPort ? 80 : primary.Port
            };

            Uri fallback = builder.Uri;
            if (!Uri.Compare(primary, fallback, UriComponents.AbsoluteUri, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase).Equals(0))
            {
                yield return fallback;
            }
        }

        private static Uri ReplaceEndpoint(Uri modelsUri, string relativeEndpoint)
        {
            string absolute = modelsUri.AbsoluteUri;
            int modelsIndex = absolute.LastIndexOf("/models", StringComparison.OrdinalIgnoreCase);
            if (modelsIndex >= 0)
            {
                return new Uri(absolute[..modelsIndex] + "/" + relativeEndpoint.TrimStart('/'), UriKind.Absolute);
            }

            return BuildOpenAiUri(absolute, relativeEndpoint);
        }

        private static JsonDocument ParseJsonPayloadSmart(string jsonPayload)
        {
            try
            {
                return JsonDocument.Parse(jsonPayload, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
            }
            catch
            {
                string repaired = RepairJsonPayload(jsonPayload);
                return JsonDocument.Parse(repaired, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
            }
        }

        private static string RepairJsonPayload(string jsonPayload)
        {
            string repaired = jsonPayload
                .Replace('“', '"')
                .Replace('”', '"')
                .Replace('‘', '\'')
                .Replace('’', '\'');

            repaired = Regex.Replace(repaired, @"(?<=[{,]\s*)'(?<name>[^']+)'\s*:", "\"${name}\":");
            repaired = Regex.Replace(repaired, @"(?<=[{,]\s*)(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:", "\"${name}\":");
            repaired = Regex.Replace(repaired, @":\s*'(?<value>[^']*)'", ": \"${value}\"");
            repaired = CompletePossiblyTruncatedJson(repaired);
            repaired = Regex.Replace(repaired, @",\s*(?=[}\]])", string.Empty);
            return repaired.Trim();
        }

        private static string CompletePossiblyTruncatedJson(string jsonPayload)
        {
            if (string.IsNullOrWhiteSpace(jsonPayload))
            {
                return jsonPayload;
            }

            var closers = new Stack<char>();
            var builder = new StringBuilder(jsonPayload.Length + 32);
            bool inString = false;
            bool escaped = false;

            foreach (char character in jsonPayload)
            {
                builder.Append(character);

                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (character == '\\')
                    {
                        escaped = true;
                    }
                    else if (character == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                switch (character)
                {
                    case '"':
                        inString = true;
                        break;
                    case '{':
                        closers.Push('}');
                        break;
                    case '[':
                        closers.Push(']');
                        break;
                    case '}':
                    case ']':
                        if (closers.Count > 0 && closers.Peek() == character)
                        {
                            closers.Pop();
                        }
                        break;
                }
            }

            if (inString)
            {
                if (escaped)
                {
                    builder.Append('\\');
                }

                builder.Append('"');
            }

            string completed = builder.ToString();
            completed = Regex.Replace(completed, @",\s*$", string.Empty);

            while (closers.Count > 0)
            {
                completed = Regex.Replace(completed, @",\s*$", string.Empty);
                completed += closers.Pop();
            }

            return completed;
        }

        private void ShowCopyableMessageBox(string title, string message)
        {
            using Form dialog = new()
            {
                Text = title,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.SizableToolWindow,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(720, 360),
                ShowInTaskbar = false
            };

            var textBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Dock = DockStyle.Fill,
                Text = message,
                Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point)
            };

            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8)
            };

            var buttonOk = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                AutoSize = true
            };

            var buttonCopy = new Button
            {
                Text = "Copy",
                AutoSize = true
            };

            buttonCopy.Click += (_, _) => Clipboard.SetText(message);

            panel.Controls.Add(buttonOk);
            panel.Controls.Add(buttonCopy);
            dialog.Controls.Add(textBox);
            dialog.Controls.Add(panel);
            dialog.AcceptButton = buttonOk;
            dialog.CancelButton = buttonOk;
            dialog.ShowDialog(this);
        }

        private static void LogMultiline(string title, string content)
        {
            string[] lines = content.Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 0)
            {
                LogCollection.Log(title);
                return;
            }

            LogCollection.Log(title + ":");
            foreach (string line in lines)
            {
                LogCollection.Log("  " + line);
            }
        }

        private static List<bool[]> ApplyInterleaving(List<bool[]> breakbeat, IReadOnlyList<DrumsetElement> elements)
        {
            if (breakbeat.Count == 0 || breakbeat[0].Length == 0)
            {
                return breakbeat;
            }

            int steps = breakbeat[0].Length;
            var result = CreateEmptyPattern(breakbeat.Count, steps);

            for (int step = 0; step < steps; step++)
            {
                int chosenTrack = -1;
                int bestPriority = int.MinValue;
                for (int track = 0; track < breakbeat.Count; track++)
                {
                    if (!breakbeat[track][step])
                    {
                        continue;
                    }

                    int priority = GetInterleavePriority(elements[track]);
                    if (priority > bestPriority)
                    {
                        bestPriority = priority;
                        chosenTrack = track;
                    }
                }

                if (chosenTrack >= 0)
                {
                    result[chosenTrack][step] = true;
                }
            }

            return result;
        }

        private static bool ApplyBeatmapDefinition(JsonElement beatmapElement, List<bool[]> pattern, IReadOnlyList<DrumkitSampleInfo> drumkit, int bars, int resolution)
        {
            if (beatmapElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            string[] noteKeys = [];
            if (TryGetAnyProperty(beatmapElement, out JsonElement noteKeysElement, "note_keys", "noteKeys") && noteKeysElement.ValueKind == JsonValueKind.Array)
            {
                noteKeys = noteKeysElement.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString() ?? string.Empty)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToArray();
            }

            if (!TryGetAnyProperty(beatmapElement, out JsonElement barsElement, "bars") || barsElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            bool parsed = false;
            foreach (JsonElement barElement in barsElement.EnumerateArray())
            {
                if (barElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                int barIndex = 0;
                if (TryGetAnyProperty(barElement, out JsonElement barIndexElement, "bar_index", "barIndex", "index"))
                {
                    TryReadInt(barIndexElement, out barIndex);
                }

                if (barIndex < 0 || barIndex >= bars)
                {
                    continue;
                }

                if (!TryGetAnyProperty(barElement, out JsonElement patternsElement, "patterns") || patternsElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (JsonProperty property in patternsElement.EnumerateObject())
                {
                    int trackIndex = ResolveSampleIndex(property.Name, drumkit);
                    if (trackIndex < 0 && noteKeys.Length > 0)
                    {
                        int noteKeyIndex = Array.FindIndex(noteKeys, x => string.Equals(x, property.Name, StringComparison.OrdinalIgnoreCase));
                        if (noteKeyIndex >= 0 && noteKeyIndex < noteKeys.Length)
                        {
                            trackIndex = ResolveSampleIndex(noteKeys[noteKeyIndex], drumkit);
                        }
                    }

                    if (trackIndex < 0)
                    {
                        continue;
                    }

                    bool[]? barPattern = ReadStepArray(property.Value, resolution);
                    if (barPattern == null)
                    {
                        continue;
                    }

                    int barOffset = barIndex * resolution;
                    for (int step = 0; step < Math.Min(resolution, barPattern.Length); step++)
                    {
                        int globalStep = barOffset + step;
                        if (globalStep >= 0 && globalStep < pattern[trackIndex].Length)
                        {
                            pattern[trackIndex][globalStep] = barPattern[step];
                            parsed |= barPattern[step];
                        }
                    }
                }
            }

            return parsed;
        }

        private static int GetInterleavePriority(DrumsetElement element)
        {
            return element switch
            {
                DrumsetElement.Kick => 100,
                DrumsetElement.Snare => 95,
                DrumsetElement.SnareRattle => 90,
                DrumsetElement.Clap => 85,
                DrumsetElement.ThinkBreak => 80,
                DrumsetElement.HiHatOpen => 70,
                DrumsetElement.HiHatClosed => 65,
                DrumsetElement.Ride => 60,
                DrumsetElement.CrashShort => 50,
                DrumsetElement.CrashLong => 45,
                _ => 40
            };
        }

        private static List<bool[]> CreateEmptyPattern(int trackCount, int totalSteps)
        {
            var pattern = new List<bool[]>(trackCount);
            for (int i = 0; i < trackCount; i++)
            {
                pattern.Add(new bool[totalSteps]);
            }

            return pattern;
        }

        private static bool ApplyPatternDefinition(JsonElement patternElement, List<bool[]> pattern, IReadOnlyList<DrumkitSampleInfo> drumkit, int totalSteps)
        {
            if (patternElement.ValueKind == JsonValueKind.Object)
            {
                bool parsed = false;
                foreach (JsonProperty property in patternElement.EnumerateObject())
                {
                    int trackIndex = ResolveSampleIndex(property.Name, drumkit);
                    if (trackIndex < 0)
                    {
                        continue;
                    }

                    bool[]? steps = ReadStepArray(property.Value, totalSteps);
                    if (steps == null)
                    {
                        continue;
                    }

                    pattern[trackIndex] = steps;
                    parsed = true;
                }

                return parsed;
            }

            if (patternElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            JsonElement[] rows = patternElement.EnumerateArray().ToArray();
            if (rows.Length == 0)
            {
                return false;
            }

            if (rows.All(x => x.ValueKind == JsonValueKind.Object))
            {
                return ApplyTrackDefinitions(patternElement, pattern, drumkit, totalSteps) || ApplyStepDefinitions(patternElement, pattern, drumkit, totalSteps);
            }

            if (!rows.All(x => x.ValueKind == JsonValueKind.Array))
            {
                return false;
            }

            if (rows.Length == pattern.Count)
            {
                bool parsed = false;
                for (int track = 0; track < rows.Length; track++)
                {
                    bool[]? steps = ReadStepArray(rows[track], totalSteps);
                    if (steps == null)
                    {
                        continue;
                    }

                    pattern[track] = steps;
                    parsed = true;
                }

                return parsed;
            }

            if (rows.Length == totalSteps)
            {
                bool parsed = false;
                for (int step = 0; step < Math.Min(totalSteps, rows.Length); step++)
                {
                    JsonElement[] stepValues = rows[step].EnumerateArray().ToArray();
                    for (int track = 0; track < Math.Min(pattern.Count, stepValues.Length); track++)
                    {
                        if (TryReadBoolean(stepValues[track], out bool isHit))
                        {
                            pattern[track][step] = isHit;
                            parsed = true;
                        }
                    }
                }

                return parsed;
            }

            return false;
        }

        private static bool ApplyTrackDefinitions(JsonElement tracksElement, List<bool[]> pattern, IReadOnlyList<DrumkitSampleInfo> drumkit, int totalSteps)
        {
            bool parsed = false;

            if (tracksElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in tracksElement.EnumerateObject())
                {
                    int trackIndex = ResolveSampleIndex(property.Name, drumkit);
                    if (trackIndex < 0)
                    {
                        continue;
                    }

                    bool[]? steps = ReadStepArray(property.Value, totalSteps);
                    if (steps == null)
                    {
                        continue;
                    }

                    pattern[trackIndex] = steps;
                    parsed = true;
                }

                return parsed;
            }

            if (tracksElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (JsonElement trackElement in tracksElement.EnumerateArray())
            {
                int trackIndex = ResolveSampleIndex(trackElement, drumkit);
                if (trackIndex < 0)
                {
                    continue;
                }

                JsonElement stepsElement = trackElement;
                if (trackElement.ValueKind == JsonValueKind.Object && TryGetAnyProperty(trackElement, out JsonElement nestedSteps, "steps", "pattern", "grid", "sequence"))
                {
                    stepsElement = nestedSteps;
                }

                bool[]? steps = ReadStepArray(stepsElement, totalSteps);
                if (steps == null)
                {
                    continue;
                }

                pattern[trackIndex] = steps;
                parsed = true;
            }

            return parsed;
        }

        private static bool ApplyStepDefinitions(JsonElement stepsElement, List<bool[]> pattern, IReadOnlyList<DrumkitSampleInfo> drumkit, int totalSteps)
        {
            if (stepsElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            bool parsed = false;
            int implicitStepIndex = 0;

            foreach (JsonElement stepElement in stepsElement.EnumerateArray())
            {
                int stepIndex = implicitStepIndex;
                if (stepElement.ValueKind == JsonValueKind.Object && TryGetAnyProperty(stepElement, out JsonElement explicitStepIndex, "step", "index", "position") && TryReadInt(explicitStepIndex, out int parsedStepIndex))
                {
                    stepIndex = parsedStepIndex;
                }

                if (stepIndex < 0 || stepIndex >= totalSteps)
                {
                    implicitStepIndex++;
                    continue;
                }

                JsonElement hitsElement = stepElement;
                if (stepElement.ValueKind == JsonValueKind.Object && TryGetAnyProperty(stepElement, out JsonElement nestedHits, "hits", "samples", "triggers", "voices", "notes"))
                {
                    hitsElement = nestedHits;
                }

                if (hitsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement hit in hitsElement.EnumerateArray())
                    {
                        int trackIndex = ResolveSampleIndex(hit, drumkit);
                        if (trackIndex >= 0)
                        {
                            pattern[trackIndex][stepIndex] = true;
                            parsed = true;
                        }
                    }
                }
                else
                {
                    int trackIndex = ResolveSampleIndex(hitsElement, drumkit);
                    if (trackIndex >= 0)
                    {
                        pattern[trackIndex][stepIndex] = true;
                        parsed = true;
                    }
                }

                implicitStepIndex++;
            }

            return parsed;
        }

        private static bool[]? ReadStepArray(JsonElement element, int totalSteps)
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            bool[] result = new bool[totalSteps];
            int index = 0;
            foreach (JsonElement value in element.EnumerateArray())
            {
                if (index >= totalSteps)
                {
                    break;
                }

                if (TryReadBoolean(value, out bool isHit))
                {
                    result[index] = isHit;
                }

                index++;
            }

            return result;
        }

        private static int ResolveSampleIndex(JsonElement element, IReadOnlyList<DrumkitSampleInfo> drumkit)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Number when TryReadInt(element, out int numericIndex) && numericIndex >= 0 && numericIndex < drumkit.Count => numericIndex,
                JsonValueKind.String => ResolveSampleIndex(element.GetString(), drumkit),
                JsonValueKind.Object => ResolveSampleIndexFromObject(element, drumkit),
                _ => -1
            };
        }

        private static int ResolveSampleIndexFromObject(JsonElement element, IReadOnlyList<DrumkitSampleInfo> drumkit)
        {
            if (TryGetAnyProperty(element, out JsonElement sampleIndexElement, "sampleIndex", "index", "trackIndex", "voiceIndex") && TryReadInt(sampleIndexElement, out int numericIndex) && numericIndex >= 0 && numericIndex < drumkit.Count)
            {
                return numericIndex;
            }

            if (TryGetAnyProperty(element, out JsonElement sampleNameElement, "sampleName", "sample", "name", "drum", "element", "track") && sampleNameElement.ValueKind == JsonValueKind.String)
            {
                return ResolveSampleIndex(sampleNameElement.GetString(), drumkit);
            }

            return -1;
        }

        private static int ResolveSampleIndex(string? value, IReadOnlyList<DrumkitSampleInfo> drumkit)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return -1;
            }

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numericIndex) && numericIndex >= 0 && numericIndex < drumkit.Count)
            {
                return numericIndex;
            }

            string normalized = NormalizeToken(value);
            foreach (DrumkitSampleInfo sample in drumkit)
            {
                if (sample.Aliases.Contains(normalized))
                {
                    return sample.Index;
                }
            }

            Match numberMatch = Regex.Match(value, @"\d+");
            if (numberMatch.Success && int.TryParse(numberMatch.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out numericIndex) && numericIndex >= 0 && numericIndex < drumkit.Count)
            {
                return numericIndex;
            }

            return -1;
        }

        private static string NormalizeToken(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                }
            }

            return builder.ToString();
        }

        private static string CreateUniquePromptNoteKey(DrumsetElement element, int index, ISet<string> usedNoteKeys)
        {
            string baseKey = CreatePromptNoteKey(element, index);
            string candidate = baseKey;
            int suffix = 2;
            while (!usedNoteKeys.Add(candidate))
            {
                candidate = baseKey + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            return candidate;
        }

        private static string CreatePromptNoteKey(DrumsetElement element, int index)
        {
            return element switch
            {
                DrumsetElement.Kick => "kick",
                DrumsetElement.Snare => "snare",
                DrumsetElement.SnareRattle => "snare_rattle",
                DrumsetElement.HiHatClosed => "hihat",
                DrumsetElement.HiHatOpen => "open_hihat",
                DrumsetElement.Ride => "ride",
                DrumsetElement.TomHigh => "tom_high",
                DrumsetElement.TomMid => "tom_mid",
                DrumsetElement.TomLow => "tom_low",
                DrumsetElement.FloorTom => "floor_tom",
                DrumsetElement.Clap => "clap",
                DrumsetElement.Rim => "rim",
                DrumsetElement.CrashShort => "crash",
                DrumsetElement.CrashLong => "crash_long",
                DrumsetElement.Cowbell => "cowbell",
                DrumsetElement.Shaker => "shaker",
                DrumsetElement.ThinkBreak => "think_break",
                _ => "sample_" + index.ToString(CultureInfo.InvariantCulture)
            };
        }

        private static bool TryReadBoolean(JsonElement element, out bool value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.True:
                    value = true;
                    return true;
                case JsonValueKind.False:
                    value = false;
                    return true;
                case JsonValueKind.Number:
                    if (element.TryGetDouble(out double number))
                    {
                        value = number > 0.0;
                        return true;
                    }
                    break;
                case JsonValueKind.String:
                    string normalized = element.GetString()?.Trim().ToLowerInvariant() ?? string.Empty;
                    if (normalized is "1" or "true" or "yes" or "on" or "x" or "hit")
                    {
                        value = true;
                        return true;
                    }

                    if (normalized is "0" or "false" or "no" or "off" or "." or "-" or "_")
                    {
                        value = false;
                        return true;
                    }
                    break;
            }

            value = false;
            return false;
        }

        private static bool TryReadInt(JsonElement element, out int value)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
            {
                return true;
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                return int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            }

            value = 0;
            return false;
        }

        private static string? TryReadString(JsonElement element, params string[] propertyNames)
        {
            if (!TryGetAnyProperty(element, out JsonElement valueElement, propertyNames) || valueElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return valueElement.GetString();
        }

        private static bool TryGetAnyProperty(JsonElement element, out JsonElement value, params string[] propertyNames)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (propertyNames.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        private static bool TryExtractAssistantContent(JsonElement root, out string content)
        {
            if (TryGetAnyProperty(root, out JsonElement choicesElement, "choices") && choicesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement choice in choicesElement.EnumerateArray())
                {
                    if (TryGetAnyProperty(choice, out JsonElement messageElement, "message") && TryGetAnyProperty(messageElement, out JsonElement contentElement, "content"))
                    {
                        if (TryReadContent(contentElement, out content))
                        {
                            return true;
                        }
                    }

                    if (TryGetAnyProperty(choice, out JsonElement textElement, "text") && textElement.ValueKind == JsonValueKind.String)
                    {
                        content = textElement.GetString() ?? string.Empty;
                        return true;
                    }
                }
            }

            content = string.Empty;
            return false;
        }

        private static bool TryReadContent(JsonElement contentElement, out string content)
        {
            if (contentElement.ValueKind == JsonValueKind.String)
            {
                content = contentElement.GetString() ?? string.Empty;
                return true;
            }

            if (contentElement.ValueKind == JsonValueKind.Array)
            {
                StringBuilder builder = new();
                foreach (JsonElement part in contentElement.EnumerateArray())
                {
                    if (part.ValueKind == JsonValueKind.String)
                    {
                        builder.AppendLine(part.GetString());
                        continue;
                    }

                    if (part.ValueKind == JsonValueKind.Object && TryGetAnyProperty(part, out JsonElement textElement, "text") && textElement.ValueKind == JsonValueKind.String)
                    {
                        builder.AppendLine(textElement.GetString());
                    }
                }

                content = builder.ToString();
                return content.Length > 0;
            }

            content = string.Empty;
            return false;
        }

        private static string ExtractJsonPayload(string content)
        {
            string trimmed = content.Trim();
            if (trimmed.Length == 0)
            {
                throw new InvalidOperationException("LLM returned empty content.");
            }

            Match fenceMatch = Regex.Match(trimmed, "```(?:json)?\\s*(?<json>[\\s\\S]+?)\\s*```", RegexOptions.IgnoreCase);
            if (fenceMatch.Success)
            {
                trimmed = fenceMatch.Groups["json"].Value.Trim();
            }

            int objectStart = trimmed.IndexOf('{');
            int arrayStart = trimmed.IndexOf('[');
            int start = objectStart >= 0 && (arrayStart < 0 || objectStart < arrayStart) ? objectStart : arrayStart;
            if (start < 0)
            {
                throw new InvalidOperationException("LLM content did not contain JSON.");
            }

            if (TryExtractBalancedJson(trimmed[start..], out string json))
            {
                return json;
            }

            return trimmed[start..];
        }

        private static bool TryExtractBalancedJson(string content, out string json)
        {
            int depth = 0;
            bool inString = false;
            bool escaped = false;
            char opening = content[0];
            char closing = opening == '{' ? '}' : ']';

            for (int i = 0; i < content.Length; i++)
            {
                char character = content[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (character == '\\')
                    {
                        escaped = true;
                    }
                    else if (character == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (character == '"')
                {
                    inString = true;
                    continue;
                }

                if (character == opening)
                {
                    depth++;
                }
                else if (character == closing)
                {
                    depth--;
                    if (depth == 0)
                    {
                        json = content[..(i + 1)];
                        return true;
                    }
                }
            }

            json = string.Empty;
            return false;
        }

        private static Uri BuildOpenAiUri(string rawUrl, string relativeEndpoint)
        {
            if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out Uri? inputUri))
            {
                throw new InvalidOperationException("The API URL is not a valid absolute URI.");
            }

            string absolute = inputUri.AbsoluteUri.TrimEnd('/');
            string endpointSuffix = "/v1/" + relativeEndpoint.TrimStart('/');

            if (absolute.EndsWith(endpointSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return new Uri(absolute, UriKind.Absolute);
            }

            if (absolute.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                return new Uri(absolute + "/" + relativeEndpoint.TrimStart('/'), UriKind.Absolute);
            }

            int chatIndex = absolute.IndexOf("/v1/chat/completions", StringComparison.OrdinalIgnoreCase);
            if (chatIndex >= 0)
            {
                absolute = absolute[..chatIndex];
            }

            return new Uri(absolute + endpointSuffix, UriKind.Absolute);
        }

        private sealed class DrumkitSampleInfo
        {
            public required AudioObj Audio { get; init; }
            public required string[] Aliases { get; init; }
            public required double DurationMilliseconds { get; init; }
            public required DrumsetElement Element { get; init; }
            public required string Id { get; init; }
            public required int Index { get; init; }
            public required string Name { get; init; }
            public required string NoteKey { get; init; }
        }

        private sealed class LlmBreakbeatParseResult
        {
            public required List<bool[]> Pattern { get; init; }
            public required string PatternName { get; init; }
        }

        private sealed class BotGenerationSnapshot
        {
            public required int Bars { get; init; }
            public required float Bpm { get; init; }
            public required float Complexity { get; init; }
            public required float Density { get; init; }
            public required bool Interleaved { get; init; }
            public required DrumsetElement[] MappedDrumset { get; init; }
            public required string PatternNameBase { get; init; }
            public required int Resolution { get; init; }
            public required IReadOnlyList<string> RowLabels { get; init; }
            public required int Seed { get; init; }
            public required List<AudioObj> Samples { get; init; }
            public required float Swing { get; init; }
            public required string SelectedPreset { get; init; }
        }

        private sealed class BotPreparedBreakbeat
        {
            public required AudioObj Audio { get; init; }
            public required TimeSpan Duration { get; init; }
            public required List<bool[]> Pattern { get; init; }
            public required string PatternName { get; init; }
            public required IReadOnlyList<string> RowLabels { get; init; }
        }

        private async Task StartBotAsync()
        {
            if (this.AudioC.Audios.Count == 0)
            {
                LogCollection.Log("Breakbeat bot skipped because no samples are loaded.");
                return;
            }

            await this.StopBotAsync(stopPlaybackImmediately: true);

            this.BotActivated = true;
            this.UpdateBotButtonState();
            LogCollection.Log("Breakbeat bot started.");

            var cancellationTokenSource = new CancellationTokenSource();
            this.botCancellationTokenSource = cancellationTokenSource;
            this.botLoopTask = this.RunBotLoopAsync(cancellationTokenSource.Token);
            _ = this.ObserveBotLoopAsync(this.botLoopTask, cancellationTokenSource);
        }

        private async Task StopBotAsync(bool stopPlaybackImmediately)
        {
            this.BotActivated = false;
            this.UpdateBotButtonState(stopping: !stopPlaybackImmediately && this.botLoopTask is not null);

            CancellationTokenSource? cancellationTokenSource = this.botCancellationTokenSource;
            if (cancellationTokenSource != null && !cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    cancellationTokenSource.Cancel();
                }
                catch
                {
                }
            }

            if (stopPlaybackImmediately && this.botCurrentPlaybackAudio is not null)
            {
                try
                {
                    await this.botCurrentPlaybackAudio.StopAsync();
                }
                catch (Exception ex)
                {
                    LogCollection.Log($"Breakbeat bot stop failed: {ex.Message}");
                }
            }

            Task? loopTask = this.botLoopTask;
            if (loopTask is null)
            {
                this.UpdateBotButtonState();
                return;
            }

            try
            {
                await loopTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task ObserveBotLoopAsync(Task loopTask, CancellationTokenSource cancellationTokenSource)
        {
            try
            {
                await loopTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogCollection.Log("Breakbeat bot failed.");
                LogCollection.Log(ex);

                if (!this.IsDisposed)
                {
                    await this.InvokeOnUiAsync(() =>
                    {
                        MessageBox.Show(this, ex.Message, "Breakbeat Bot", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    });
                }
            }
            finally
            {
                if (ReferenceEquals(this.botCancellationTokenSource, cancellationTokenSource))
                {
                    this.botCancellationTokenSource = null;
                }

                if (ReferenceEquals(this.botLoopTask, loopTask))
                {
                    this.botLoopTask = null;
                }

                this.botCurrentPlaybackAudio = null;
                this.BotActivated = false;

                cancellationTokenSource.Dispose();

                if (!this.IsDisposed)
                {
                    await this.InvokeOnUiAsync(() => this.UpdateBotButtonState());
                }

                LogCollection.Log("Breakbeat bot stopped.");
            }
        }

        private async Task RunBotLoopAsync(CancellationToken cancellationToken)
        {
            int generationNumber = 1;
            BotPreparedBreakbeat current = await this.GenerateBotPreparedBreakbeatAsync(generationNumber, cancellationToken);
            Task<BotPreparedBreakbeat>? nextTask = this.GenerateBotPreparedBreakbeatAsync(generationNumber + 1, cancellationToken);
            AudioObj? playbackChain = null;
            Task? playbackTask = null;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int rerollInterval = await this.InvokeOnUiAsync(() => Math.Max(1, (int) this.numericUpDown_reroll.Value));
                    bool shouldAutoExport = await this.InvokeOnUiAsync(() => this.checkBox_autoExport.Checked);
                    bool exportThisGeneration = shouldAutoExport && generationNumber % rerollInterval == 0;

                    await this.PresentBotPreparedBreakbeatAsync(current, exportThisGeneration, generationNumber, cancellationToken);

                    int chainRepeats = playbackChain == null ? Math.Max(1, rerollInterval) : rerollInterval;
                    for (int pass = 0; pass < rerollInterval; pass++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (playbackChain == null)
                        {
                            playbackChain = this.CreateBotPlaybackChain(current, chainRepeats);
                            this.botCurrentPlaybackAudio = playbackChain;
                            playbackTask = playbackChain.PlayAsync(CancellationToken.None, initialVolume: 1.0f);
                        }

                        if (pass == rerollInterval - 1)
                        {
                            BotPreparedBreakbeat appendSource = nextTask is not null
                                ? await nextTask
                                : await this.GenerateBotPreparedBreakbeatAsync(generationNumber + 1, cancellationToken);

                            this.AppendBotPreparedBreakbeat(playbackChain, appendSource, Math.Max(1, rerollInterval));
                        }

                        await this.WaitForBotPlaybackProgressAsync(playbackChain, current.Duration, pass + 1, cancellationToken);
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    generationNumber++;
                    current = nextTask is not null
                        ? await nextTask
                        : await this.GenerateBotPreparedBreakbeatAsync(generationNumber, cancellationToken);

                    nextTask = this.GenerateBotPreparedBreakbeatAsync(generationNumber + 1, cancellationToken);
                }
            }
            finally
            {
                if (playbackTask is not null)
                {
                    try
                    {
                        await playbackTask;
                    }
                    catch
                    {
                    }
                }

                if (nextTask is not null)
                {
                    try
                    {
                        await nextTask;
                    }
                    catch
                    {
                    }
                }
            }
        }

        private async Task<BotPreparedBreakbeat> GenerateBotPreparedBreakbeatAsync(int generationNumber, CancellationToken cancellationToken)
        {
            BotGenerationSnapshot snapshot = await this.CaptureBotGenerationSnapshotAsync(generationNumber);
            cancellationToken.ThrowIfCancellationRequested();

            List<bool[]> breakbeat = await BreakbeatGenerator_V2.GenerateBreakPatternAsync(
                drumset: snapshot.MappedDrumset,
                bars: snapshot.Bars,
                density: snapshot.Density,
                resolution: snapshot.Resolution,
                swing: snapshot.Swing,
                complexity: snapshot.Complexity,
                interleaved: snapshot.Interleaved,
                seed: snapshot.Seed,
                preset: snapshot.SelectedPreset
            );

            cancellationToken.ThrowIfCancellationRequested();

            string patternName = $"{snapshot.PatternNameBase}_{generationNumber:D3}";
            AudioObj audioObj = await BreakbeatGenerator_V2.RenderBreakbeatAsync(breakbeat, snapshot.Samples, snapshot.Bpm, snapshot.Resolution, snapshot.Swing, patternName);
            if (audioObj == null)
            {
                throw new InvalidOperationException("Breakbeat bot could not render the generated audio.");
            }

            return new BotPreparedBreakbeat
            {
                Audio = audioObj,
                Duration = audioObj.Duration,
                Pattern = breakbeat,
                PatternName = patternName,
                RowLabels = snapshot.RowLabels
            };
        }

        private async Task<BotGenerationSnapshot> CaptureBotGenerationSnapshotAsync(int generationNumber)
        {
            return await this.InvokeOnUiAsync(() =>
            {
                DrumsetElement[] mappedDrumset = this.GetMappedDrumset();
                string patternNameBase = this.SelectedPreset != " - None - "
                    ? this.SelectedPreset.Replace(" ", string.Empty)
                    : "BotBreakbeat";

                return new BotGenerationSnapshot
                {
                    Bars = this.Bars,
                    Bpm = this.Bpm,
                    Complexity = this.Complexity,
                    Density = this.Density,
                    Interleaved = this.Interleaved,
                    MappedDrumset = mappedDrumset,
                    PatternNameBase = patternNameBase,
                    Resolution = this.Resolution,
                    RowLabels = this.GetBeatMapRowLabels().ToArray(),
                    Seed = unchecked(this.Seed + (generationNumber * 7919)),
                    Samples = this.AudioC.Audios.Select(audio => audio.Clone()).ToList(),
                    Swing = this.Swing,
                    SelectedPreset = this.SelectedPreset
                };
            });
        }

        private DrumsetElement[] GetMappedDrumset()
        {
            DrumsetElement[] mappedDrumset = new DrumsetElement[this.AudioC.Audios.Count];
            for (int i = 0; i < this.AudioC.Audios.Count; i++)
            {
                mappedDrumset[i] = this.AudioC.Audios[i].Tag is DrumsetElement element ? element : DrumsetElement.Snare;
            }

            return mappedDrumset;
        }

        private async Task PresentBotPreparedBreakbeatAsync(BotPreparedBreakbeat prepared, bool autoExport, int generationNumber, CancellationToken cancellationToken)
        {
            await this.InvokeOnUiAsync(() =>
            {
                this.ShowBeatMap(prepared.Pattern, prepared.RowLabels);

                if (this.CollectionView == null || this.CollectionView.IsDisposed)
                {
                    this.CollectionView = new AudioCollectionView([]);
                }

                this.CollectionView.AudioC.Audios.Add(prepared.Audio.Clone());
                this.CollectionView.Show();
                this.CollectionView.Rename("Break-Beat" + (this.CollectionView.AudioC.Audios.Count == 1 ? string.Empty : "(s)") + " Generated " + this.Bpm.ToString("F1", CultureInfo.InvariantCulture) + " BPM");
            });

            if (!autoExport)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await this.ExportBotPreparedBreakbeatAsync(prepared, generationNumber);
        }

        private async Task ExportBotPreparedBreakbeatAsync(BotPreparedBreakbeat prepared, int generationNumber)
        {
            string format = await this.InvokeOnUiAsync(() => WindowMain.GlobalExportFormat);
            int bits = await this.InvokeOnUiAsync(() => WindowMain.GlobalExportBits);

            string? exportPath = AudioExporter.IsMp3Format(format)
                ? await this.AudioC.Exporter.ExportMp3Async(prepared.Audio.Clone(), bits, Math.Max(1, Environment.ProcessorCount / 2))
                : await this.AudioC.Exporter.ExportWavAsync(prepared.Audio.Clone(), bits);

            if (string.IsNullOrWhiteSpace(exportPath))
            {
                LogCollection.Log($"Breakbeat bot auto export failed for generation #{generationNumber:D3}.");
                return;
            }

            LogCollection.Log($"Breakbeat bot auto exported generation #{generationNumber:D3}: {exportPath}");
        }

        private AudioObj CreateBotPlaybackChain(BotPreparedBreakbeat prepared, int repeats)
        {
            AudioObj chain = prepared.Audio.Clone();
            chain.Volume = 100f;
            chain.Name = prepared.PatternName + "_Chain";

            for (int i = 1; i < Math.Max(1, repeats); i++)
            {
                this.AppendBotPreparedBreakbeat(chain, prepared, 1);
            }

            return chain;
        }

        private void AppendBotPreparedBreakbeat(AudioObj chain, BotPreparedBreakbeat prepared, int repeats)
        {
            if (chain.Data == null || prepared.Audio.Data == null)
            {
                return;
            }

            if (chain.SampleRate != prepared.Audio.SampleRate || chain.Channels != prepared.Audio.Channels)
            {
                throw new InvalidOperationException("Breakbeat bot cannot append segments with different audio formats.");
            }

            int repeatCount = Math.Max(1, repeats);
            lock (this.botPlaybackGate)
            {
                int appendLength = prepared.Audio.Data.Length * repeatCount;
                float[] chainData = chain.Data;
                int originalLength = chainData.Length;
                Array.Resize(ref chainData, originalLength + appendLength);

                for (int i = 0; i < repeatCount; i++)
                {
                    Array.Copy(prepared.Audio.Data, 0, chainData, originalLength + (i * prepared.Audio.Data.Length), prepared.Audio.Data.Length);
                }

                chain.Data = chainData;
                chain.Length = chainData.Length;
                chain.Duration = TimeSpan.FromSeconds((double) chain.Length / (chain.SampleRate * Math.Max(1, chain.Channels)));
                chain.BitDepth = prepared.Audio.BitDepth;
                chain.Bpm = prepared.Audio.Bpm;
            }
        }

        private async Task WaitForBotPlaybackProgressAsync(AudioObj chain, TimeSpan segmentDuration, int completedSegments, CancellationToken cancellationToken)
        {
            double targetSeconds = Math.Max(0.05, segmentDuration.TotalSeconds * completedSegments);

            try
            {
                while (chain.Playing && chain.CurrentTime.TotalSeconds + 0.02 < targetSeconds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(40, CancellationToken.None);
                }
            }
            finally
            {
                if (!chain.Playing && ReferenceEquals(this.botCurrentPlaybackAudio, chain))
                {
                    this.botCurrentPlaybackAudio = null;
                }
            }
        }

        private Task InvokeOnUiAsync(Action action)
        {
            if (this.IsDisposed)
            {
                return Task.CompletedTask;
            }

            if (!this.InvokeRequired)
            {
                action();
                return Task.CompletedTask;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            this.BeginInvoke(new MethodInvoker(() =>
            {
                try
                {
                    action();
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            }));

            return completion.Task;
        }

        private Task<T> InvokeOnUiAsync<T>(Func<T> func)
        {
            if (this.IsDisposed)
            {
                return Task.FromException<T>(new ObjectDisposedException(this.Name));
            }

            if (!this.InvokeRequired)
            {
                return Task.FromResult(func());
            }

            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            this.BeginInvoke(new MethodInvoker(() =>
            {
                try
                {
                    completion.SetResult(func());
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            }));

            return completion.Task;
        }

        private void UpdateBotButtonState(bool stopping = false)
        {
            this.button_bot.Text = stopping ? "Bot: ..." : this.BotActivated ? "Bot: on" : "Bot: off";
            this.button_bot.BackColor = this.BotActivated ? Color.LightGreen : SystemColors.Info;
        }

        private async void button_bot_Click(object sender, EventArgs e)
        {
            this.button_bot.Enabled = false;

            try
            {
                if (!this.BotActivated)
                {
                    await this.StartBotAsync();
                }
                else
                {
                    await this.StopBotAsync(stopPlaybackImmediately: false);
                }
            }
            finally
            {
                this.button_bot.Enabled = true;
            }
        }
    }
}
