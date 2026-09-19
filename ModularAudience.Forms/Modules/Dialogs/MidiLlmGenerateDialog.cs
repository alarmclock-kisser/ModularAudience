using ModularAudience.Audio;
using ModularAudience.Audio.Midi;
using ModularAudience.Llama.Dtos;using ModularAudience.Forms.Helpers;using System;
using System.Collections.Generic;
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
    public partial class MidiLlmGenerateDialog : Form
    {
        private static readonly TimeSpan LlmDiscoveryTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan LlmRequestTimeout = TimeSpan.FromMinutes(5);

        private Uri? chatCompletionsUri;
        private string? connectedModel;

        public MidiFileData? GeneratedMidiFileData { get; private set; }

        public MidiLlmGenerateDialog()
        {
            this.InitializeComponent();
        }

        private void button_connect_Click(object? sender, EventArgs e)
        {
            _ = this.ConnectAsync();
        }

        private async Task ConnectAsync()
        {
            this.button_connect.Enabled = false;
            this.label_status.Text = "Testing LLM connection...";
            bool previousUseWaitCursor = this.UseWaitCursor;
            this.UseWaitCursor = true;

            try
            {
                string apiUrl = this.textBox_apiUrl.Text.Trim();
                using HttpClient httpClient = CreateLlmHttpClient();
                httpClient.Timeout = Timeout.InfiniteTimeSpan;
                using CancellationTokenSource discoveryCts = new(LlmDiscoveryTimeout);
                (Uri modelsUri, string model) = await DiscoverWorkingModelAsync(httpClient, apiUrl, discoveryCts.Token);
                this.chatCompletionsUri = ReplaceEndpoint(modelsUri, "chat/completions");
                this.connectedModel = model;
                this.label_model.Text = $"Model: {model}";
                this.label_status.Text = "Connected.";
            }
            catch (TaskCanceledException)
            {
                this.label_status.Text = "Connection timed out.";
            }
            catch (Exception ex)
            {
                LogCollection.Log($"LLM connect test failed: {ex}");
                this.label_status.Text = "Connection failed.";
                this.ShowCopyableMessageBox("LLM Connection Error", ex.Message);
            }
            finally
            {
                this.UseWaitCursor = previousUseWaitCursor;
                this.button_connect.Enabled = true;
            }
        }

        private void button_generate_Click(object? sender, EventArgs e)
        {
            _ = this.GenerateAsync();
        }

        private async Task GenerateAsync()
        {
            this.button_generate.Enabled = false;
            bool previousUseWaitCursor = this.UseWaitCursor;
            this.UseWaitCursor = true;
            string? llmContent = null;

            try
            {
                if (this.chatCompletionsUri == null || this.connectedModel == null)
                {
                    string apiUrl = this.textBox_apiUrl.Text.Trim();
                    using HttpClient httpClient = CreateLlmHttpClient();
                    httpClient.Timeout = Timeout.InfiniteTimeSpan;
                    using CancellationTokenSource discoveryCts = new(LlmDiscoveryTimeout);
                    (Uri modelsUri, string model) = await DiscoverWorkingModelAsync(httpClient, apiUrl, discoveryCts.Token);
                    this.chatCompletionsUri = ReplaceEndpoint(modelsUri, "chat/completions");
                    this.connectedModel = model;
                    this.label_model.Text = $"Model: {model}";
                }

                double bpm = (double) this.numericUpDown_bpm.Value;
                int bars = (int) this.numericUpDown_bars.Value;
                int ticksPerQuarterNote = (int) this.numericUpDown_ppq.Value;
                string userPrompt = string.IsNullOrWhiteSpace(this.textBox_prompt.Text)
                    ? "Generate a simple, musically coherent MIDI pattern."
                    : this.textBox_prompt.Text.Trim();

                string systemPrompt = this.BuildSystemPrompt(bpm, bars, ticksPerQuarterNote);
                string llmUserPrompt = this.BuildUserPrompt(userPrompt, bpm, bars, ticksPerQuarterNote);

                LogCollection.Log("Generating MIDI via LLM.");
                llmContent = await this.RequestMidiFromLlmAsync(systemPrompt, llmUserPrompt);
                LogMultiline("LLM raw response", llmContent);

                MidiDto midiDto;
                try
                {
                    midiDto = MidiDto.ParseBestEffort(llmContent, out bool repaired);
                    if (repaired)
                    {
                        LogCollection.Log("LLM MIDI JSON was repaired before parsing.");
                    }
                }
                catch (Exception ex)
                {
                    string parseError = "The LLM response could not be parsed as MIDI JSON." + Environment.NewLine + Environment.NewLine + ex.Message + Environment.NewLine + Environment.NewLine + "Raw response:" + Environment.NewLine + llmContent;
                    LogMultiline("LLM JSON parse error", parseError);
                    this.ShowCopyableMessageBox("LLM JSON Parse Error", parseError);
                    return;
                }

                this.GeneratedMidiFileData = midiDto.ToMidiFileData();
                int noteCount = this.GeneratedMidiFileData.Tracks.Sum(track => track.Notes.Count);
                this.button_openMidi.Enabled = noteCount > 0;
                this.button_exportMidi.Enabled = noteCount > 0;
                this.label_status.Text = noteCount > 0
                    ? $"Generated {noteCount} note(s) across {this.GeneratedMidiFileData.Tracks.Count} track(s)."
                    : "Generation finished but no notes were produced.";
                LogCollection.Log("LLM MIDI generation finished.");
            }
            catch (TaskCanceledException ex)
            {
                string timeoutMessage = "LLM request timed out or was canceled." + Environment.NewLine + Environment.NewLine + ex.Message;
                LogMultiline("LLM timeout", timeoutMessage);
                this.label_status.Text = "Generation timed out.";
                this.ShowCopyableMessageBox("LLM Timeout", timeoutMessage + (string.IsNullOrWhiteSpace(llmContent) ? string.Empty : Environment.NewLine + Environment.NewLine + "Raw response:" + Environment.NewLine + llmContent));
            }
            catch (Exception ex)
            {
                LogCollection.Log($"LLM MIDI generation failed: {ex}");
                this.label_status.Text = "Generation failed.";
                string errorMessage = "LLM MIDI generation failed." + Environment.NewLine + Environment.NewLine + ex.Message;
                if (!string.IsNullOrWhiteSpace(llmContent))
                {
                    errorMessage += Environment.NewLine + Environment.NewLine + "Raw response:" + Environment.NewLine + llmContent;
                }

                this.ShowCopyableMessageBox("LLM MIDI Error", errorMessage);
            }
            finally
            {
                this.UseWaitCursor = previousUseWaitCursor;
                this.button_generate.Enabled = true;
            }
        }

        private void button_openMidi_Click(object? sender, EventArgs e)
        {
            if (this.GeneratedMidiFileData == null)
            {
                return;
            }

            var window = new MidiWindow(null, this.GeneratedMidiFileData);
            window.Show(this);
        }

        private async void button_exportMidi_Click(object? sender, EventArgs e)
        {
            if (this.GeneratedMidiFileData == null)
            {
                return;
            }

            using SaveFileDialog sfd = new()
            {
                Title = "Export MIDI File",
                Filter = "MIDI Files (*.mid)|*.mid",
                DefaultExt = "mid",
                AddExtension = true,
                FileName = "llm_generated.mid"
            };

            if (sfd.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                this.button_exportMidi.Enabled = false;
                this.label_status.Text = "Exporting MIDI...";
                string? exportedPath = await this.GeneratedMidiFileData.ExportAsync(sfd.FileName);
                this.label_status.Text = exportedPath == null
                    ? "MIDI export failed."
                    : $"MIDI exported to {exportedPath}";
                if (exportedPath == null)
                {
                    MessageBox.Show(this, "MIDI export failed.", "MIDI export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                LogCollection.Log($"MIDI export failed: {ex}");
                ModularAudience.Forms.Helpers.WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "MIDI export failed", ex);
            }
            finally
            {
                this.button_exportMidi.Enabled = true;
            }
        }

        private string BuildSystemPrompt(double bpm, int bars, int ticksPerQuarterNote)
        {
            return "You are a music generation model. Reply with ONLY a single JSON object describing a MIDI file, no prose, no markdown fences.\n" +
                "Use exactly this schema: " +
                "{\"filePath\": string, \"ticksPerQuarterNote\": integer, \"defaultBpm\": number, \"pitchFrequency\": number, " +
                "\"tracks\": [{\"index\": integer, \"name\": string, \"lengthTicks\": integer, " +
                "\"notes\": [{\"noteNumber\": integer 0-127, \"channel\": integer 0-15, \"velocity\": integer 1-127, " +
                "\"startTick\": integer >= 0, \"durationTicks\": integer > 0}]}]. " +
                "startTick and durationTicks are in ticks, with 1 quarter note = ticksPerQuarterNote ticks. " +
                "Keep the pattern within the requested number of bars and musically coherent.";
        }

        private string BuildUserPrompt(string userPrompt, double bpm, int bars, int ticksPerQuarterNote)
        {
            return "Generate a MIDI pattern as JSON.\n" +
                $"Request: {userPrompt}\n" +
                $"Parameters: defaultBpm={bpm.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
                $"ticksPerQuarterNote={ticksPerQuarterNote}, bars={bars}, pitchFrequency=440. " +
                $"Total length in ticks: {bars * ticksPerQuarterNote}.";
        }

        private async Task<string> RequestMidiFromLlmAsync(string systemPrompt, string userPrompt)
        {
            if (this.chatCompletionsUri == null || this.connectedModel == null)
            {
                throw new InvalidOperationException("No LLM endpoint is connected.");
            }

            using HttpClient httpClient = CreateLlmHttpClient();
            httpClient.Timeout = Timeout.InfiniteTimeSpan;

            LogCollection.Log($"LLM chat endpoint: {this.chatCompletionsUri}");
            LogCollection.Log($"LLM model: {this.connectedModel}");

            object[] messages =
            [
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            ];

            Dictionary<string, object?> payload = new()
            {
                ["model"] = this.connectedModel,
                ["temperature"] = 0.4,
                ["messages"] = messages,
                ["response_format"] = new Dictionary<string, string>
                {
                    ["type"] = "json_object"
                }
            };

            using CancellationTokenSource requestCts = new(LlmRequestTimeout);

            try
            {
                return await SendChatCompletionRequestAsync(httpClient, this.chatCompletionsUri, payload, requestCts.Token);
            }
            catch (HttpRequestException)
            {
                LogCollection.Log("LLM endpoint rejected response_format=json_object. Retrying without response_format.");
                payload.Remove("response_format");
                return await SendChatCompletionRequestAsync(httpClient, this.chatCompletionsUri, payload, requestCts.Token);
            }
            catch (OperationCanceledException ex) when (requestCts.IsCancellationRequested)
            {
                throw new TaskCanceledException($"The LLM request exceeded the configured timeout of {LlmRequestTimeout.TotalSeconds:0} seconds.", ex);
            }
        }

        private static async Task<string> SendChatCompletionRequestAsync(HttpClient httpClient, Uri endpoint, Dictionary<string, object?> payload, CancellationToken cancellationToken)
        {
            string jsonPayload = JsonSerializer.Serialize(payload);
            LogCollection.Log($"LLM POST {endpoint}");
            using HttpRequestMessage request = new(HttpMethod.Post, endpoint)
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
    }
}
