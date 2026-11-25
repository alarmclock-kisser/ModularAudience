using ModularAudience.Audio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ModularAudience.Llama
{
    public class LlamaConnector
    {
        public string ApiUrl { get; private set; }
        public string Model { get; set; } = "gemma-4";
        public string ApiKey { get; set; } = "";

        /// <summary>
        /// Optional log sink. The Instrument Coder connects this to its own log list and to LogManager.
        /// </summary>
        public Action<string>? Logger { get; set; }

        private readonly HttpClient _httpClient;

        public LlamaConnector(string apiUrl = "http://localhost:9931", string model = "gemma-4")
        {
            this.ApiUrl = NormalizeUrl(apiUrl);
            this.Model = model;

            // Audio inference plus a long code answer can take several minutes on a local model.
            this._httpClient = LlamaHttp.CreateClient(TimeSpan.FromMinutes(10));
        }

        /// <summary>
        /// Updates the endpoint at runtime so a changed server URL also affects an existing connector.
        /// </summary>
        public void SetApiUrl(string apiUrl)
        {
            string normalized = NormalizeUrl(apiUrl);
            if (normalized == this.ApiUrl) return;

            this.ApiUrl = normalized;
            this.Log($"LLM endpoint set to {this.ApiUrl}.");
        }

        private static string NormalizeUrl(string apiUrl)
        {
            // Strip trailing /v1/, /v1, /v1/chat/completions, etc.
            string url = apiUrl.Trim().TrimEnd('/');
            int v1Index = url.IndexOf("/v1", StringComparison.OrdinalIgnoreCase);
            if (v1Index >= 0)
            {
                url = url[..v1Index];
            }
            return url.TrimEnd('/');
        }

        private void Log(string message)
        {
            Console.WriteLine($"[LlamaConnector] {message}");
            try
            {
                this.Logger?.Invoke(message);
            }
            catch
            {
                // A broken log sink must never break the request.
            }
        }

        /// <summary>
        /// Sends an audio file as an OpenAI-compatible input_audio block. Current llama-server
        /// builds accept raw base64 in input_audio.data and detect the format automatically.
        /// </summary>
        public async Task<string?> SendAudioAsync(AudioObj audio, string prompt = "Analyze the audio elements in this track.")
        {
            if (audio == null || string.IsNullOrWhiteSpace(audio.FilePath) || !File.Exists(audio.FilePath))
            {
                this.Log("Audio analysis skipped: no file was available.");
                return null;
            }

            try
            {
                byte[] audioBytes = await File.ReadAllBytesAsync(audio.FilePath);
                string extension = Path.GetExtension(audio.FilePath).TrimStart('.').ToLowerInvariant();
                if (extension != "wav" && extension != "mp3" && extension != "flac")
                {
                    extension = "wav";
                }

                var payload = new
                {
                    model = this.Model,
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = new object[]
                            {
                                new { type = "text", text = prompt },
                                CreateAudioBlock(audioBytes, extension)
                            }
                        }
                    },
                    temperature = 0.2,
                    max_tokens = 800
                };

                this.Log($"Sending audio analysis request ({audioBytes.Length} bytes, model '{this.Model}').");
                using var response = await PostChatAsync(payload);

                if (!response.IsSuccessStatusCode)
                {
                    string errorBody = await response.Content.ReadAsStringAsync();
                    this.Log($"Audio analysis failed: HTTP {(int)response.StatusCode} {errorBody}");
                    return null;
                }

                string responseJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseJson);
                string? messageContent = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                this.Log("Audio analysis succeeded.");
                return messageContent?.Trim();
            }
            catch (Exception ex)
            {
                this.Log($"Error sending audio: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Sends a multimodal refinement prompt to llama-server using OpenAI typed content blocks:
        /// text + input_audio (raw base64 WAV) for reference and synthesized audio, plus an optional
        /// image_url PNG spectrogram. Returns the model's text response (improved C# DSP code).
        /// </summary>
        /// <param name="referenceAudio">WAV bytes of the reference instrument sample.</param>
        /// <param name="synthesizedAudio">WAV bytes of the current synth render.</param>
        /// <param name="spectrogramPng">Optional PNG spectrogram bytes.</param>
        /// <param name="prompt">Text instruction for the LLM.</param>
        /// <param name="includeSpectrogram">Whether to attach the spectrogram image block.</param>
        public virtual async Task<string?> SendMultimodalSynthPromptAsync(
            byte[] referenceAudio,
            byte[] synthesizedAudio,
            byte[]? spectrogramPng,
            string prompt,
            bool includeSpectrogram = false)
        {
            return await SendMultimodalSynthPromptStreamingAsync(
                referenceAudio, synthesizedAudio, spectrogramPng, prompt, includeSpectrogram, null);
        }

        /// <summary>
        /// Streaming variant: sends the multimodal prompt and delivers partial text chunks
        /// via <paramref name="onChunk"/> as they arrive (SSE). Returns the full accumulated text.
        /// </summary>
        public virtual async Task<string?> SendMultimodalSynthPromptStreamingAsync(
            byte[] referenceAudio,
            byte[] synthesizedAudio,
            byte[]? spectrogramPng,
            string prompt,
            bool includeSpectrogram = false,
            Action<string>? onChunk = null)
        {
            if (referenceAudio == null || referenceAudio.Length == 0)
            {
                this.Log("Multimodal request skipped: reference audio is empty.");
                return null;
            }
            if (synthesizedAudio == null || synthesizedAudio.Length == 0)
            {
                this.Log("Multimodal request skipped: synthesized audio is empty.");
                return null;
            }

            // Keep partially streamed text across request failures and timeouts. The caller
            // can still show it in the code editor instead of leaving the box empty.
            var accumulated = new StringBuilder();

            try
            {
                // llama-server uses OpenAI-style typed content blocks. Audio must be sent as
                // input_audio with raw base64 data; audio_url is not supported by current builds.
                var contentBlocks = BuildContentBlocks(
                    prompt, referenceAudio, synthesizedAudio, spectrogramPng, includeSpectrogram);

                var payload = new
                {
                    model = this.Model,
                    messages = new[] { new { role = "user", content = contentBlocks } },
                    temperature = 0.2,
                    max_tokens = 4000,
                    stream = true
                };

                this.Log($"Sending multimodal synth request (reference {referenceAudio.Length} bytes, " +
                         $"synth {synthesizedAudio.Length} bytes, model '{this.Model}').");

                string errorBody;
                int statusCode;
                using (var response = await PostChatAsync(payload))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        return await ReadSseAsync(response, onChunk, accumulated);
                    }

                    statusCode = (int)response.StatusCode;
                    errorBody = await response.Content.ReadAsStringAsync();
                }

                this.Log($"Multimodal request failed: HTTP {statusCode} {errorBody}");

                // Some servers reject typed audio blocks. Keep code generation usable by
                // falling back to text plus the optional spectrogram image.
                this.Log("Retrying with text-only prompt (and spectrogram if enabled).");
                string fallbackPrompt = prompt +
                    "\n\n(Note: Audio input was rejected by the server. Generate based on the text description only.)";
                var fallbackBlocks = BuildContentBlocks(
                    fallbackPrompt, null, null, spectrogramPng, includeSpectrogram);
                var fallbackPayload = new
                {
                    model = this.Model,
                    messages = new[] { new { role = "user", content = fallbackBlocks } },
                    temperature = 0.2,
                    max_tokens = 4000,
                    stream = true
                };

                using var fallbackResponse = await PostChatAsync(fallbackPayload);
                fallbackResponse.EnsureSuccessStatusCode();
                return await ReadSseAsync(fallbackResponse, onChunk, accumulated);
            }
            catch (Exception ex)
            {
                this.Log($"Error sending multimodal synth prompt: {ex}");
                string partial = accumulated.ToString().Trim();
                if (partial.Length > 0)
                {
                    this.Log($"Keeping {partial.Length} characters of partially streamed code after the error.");
                    return partial;
                }

                return null;
            }
        }

        private static object CreateAudioBlock(byte[] audioBytes, string format = "wav")
        {
            return new
            {
                type = "input_audio",
                input_audio = new
                {
                    // Raw base64 is documented by llama-server; the format is detected automatically.
                    data = Convert.ToBase64String(audioBytes),
                    format = format
                }
            };
        }

        private static List<object> BuildContentBlocks(
            string prompt,
            byte[]? referenceAudio,
            byte[]? synthesizedAudio,
            byte[]? spectrogramPng,
            bool includeSpectrogram)
        {
            var blocks = new List<object> { new { type = "text", text = prompt } };

            if (referenceAudio != null && referenceAudio.Length > 0)
            {
                blocks.Add(CreateAudioBlock(referenceAudio));
            }
            if (synthesizedAudio != null && synthesizedAudio.Length > 0)
            {
                blocks.Add(CreateAudioBlock(synthesizedAudio));
            }
            if (includeSpectrogram && spectrogramPng != null && spectrogramPng.Length > 0)
            {
                blocks.Add(new
                {
                    type = "image_url",
                    image_url = new
                    {
                        url = $"data:image/png;base64,{Convert.ToBase64String(spectrogramPng)}"
                    }
                });
            }

            return blocks;
        }

        protected virtual async Task<HttpResponseMessage> PostChatAsync(object payload)
        {
            string json = JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{this.ApiUrl}/v1/chat/completions")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            LlamaHttp.SetBearer(request, this.ApiKey);

            return await this._httpClient.SendAsync(request);
        }

        private async Task<string?> ReadSseAsync(
            HttpResponseMessage response,
            Action<string>? onChunk,
            StringBuilder accumulated)
        {
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);
            int chunkCount = 0;

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

                string data = line["data:".Length..].Trim();
                if (data == "[DONE]") break;

                try
                {
                    using var doc = JsonDocument.Parse(data);
                    if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        var delta = choices[0];
                        if (delta.TryGetProperty("delta", out var deltaObj) &&
                            deltaObj.TryGetProperty("content", out var contentProp))
                        {
                            string? chunk = contentProp.GetString();
                            if (!string.IsNullOrEmpty(chunk))
                            {
                                accumulated.Append(chunk);
                                chunkCount++;
                                if (chunkCount == 1)
                                {
                                    this.Log($"First LLM stream chunk received ({chunk.Length} characters).");
                                }
                                else if (chunkCount % 20 == 0)
                                {
                                    this.Log($"LLM stream chunk {chunkCount}: {accumulated.Length} characters accumulated.");
                                }
                                onChunk?.Invoke(chunk);
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                    // Ignore malformed or keep-alive chunks.
                }
            }

            string result = accumulated.ToString().Trim();
            this.Log($"LLM stream finished ({result.Length} characters in {chunkCount} chunks).");
            return result.Length > 0 ? result : null;
        }

        // ====================================================================================
        // LLM-DJ CORE STUBS
        // ====================================================================================

        /// <summary>
        /// Listens to a track and returns the prominent elements (vocals, kick, snare, synths, etc.).
        /// </summary>
        public async Task<List<string>> AnalyzeTrackElementsAsync(AudioObj audio)
        {
            string prompt = "List all prominent audio elements, stems, and sound effects in this track as a comma-separated list.";
            _ = await this.SendAudioAsync(audio, prompt);

            // TODO: Split and clean the returned string.
            throw new NotImplementedException("Track element analysis is not implemented yet.");
        }

        /// <summary>
        /// Checks whether track B is harmonically compatible with track A.
        /// </summary>
        public async Task<bool> AssessHarmonicCompatibilityAsync(AudioObj trackA, AudioObj trackB)
        {
            // This may require sending both audio clips in the same context if llama-server
            // supports multi-audio arrays.
            throw new NotImplementedException("Harmonic compatibility assessment is not implemented yet.");
        }

        /// <summary>
        /// Listens to the currently playing ensemble and checks whether rhythms are clashing.
        /// </summary>
        public async Task<bool> EvaluateEnsembleSyncAsync(IReadOnlyList<AudioObj> playingTracks)
        {
            // Idea: mix the tracks on the fly into a small buffer, encode it as base64 and ask:
            // "Are the kicks and transients in this audio perfectly synced or clashing?"
            throw new NotImplementedException("Ensemble sync evaluation is not implemented yet.");
        }

        /// <summary>
        /// Suggests a timestamp (in samples or milliseconds) at which track B should enter the ensemble.
        /// </summary>
        public async Task<long> SuggestMixInPointSamplesAsync(AudioObj targetTrack, AudioObj upcomingTrack)
        {
            throw new NotImplementedException("Mix-in point suggestion is not implemented yet.");
        }

        /// <summary>
        /// Analyzes the audio and generates a grid of active/inactive steps for breakbeat slicing.
        /// Each list entry represents one bar; the bool array represents the slices.
        /// </summary>
        public async Task<List<bool[]>> GenerateBreakPatternAsync(AudioObj audio)
        {
            // Prompt idea: "Analyze the rhythm and return a 16-step grid as JSON arrays where 1 is a hit and 0 is silence."
            throw new NotImplementedException("Break pattern generation is not implemented yet.");
        }
    }
}
