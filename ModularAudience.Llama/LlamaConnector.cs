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
        public readonly string ApiUrl;
        public string Model { get; set; } = "gemma-4";
        private readonly HttpClient _httpClient;

        public LlamaConnector(string apiUrl = "http://127.0.0.1:8080", string model = "gemma-4")
        {
            // Normalize the URL: strip trailing /v1/, /v1, /v1/chat/completions, etc.
            string url = apiUrl.Trim().TrimEnd('/');
            int v1Index = url.IndexOf("/v1", StringComparison.OrdinalIgnoreCase);
            if (v1Index >= 0)
            {
                url = url[..v1Index];
            }
            this.ApiUrl = url.TrimEnd('/');
            this.Model = model;

            // Audio-Inferencing kann dauern, großzügiger Timeout
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = static (message, cert, chain, errors) =>
                errors == System.Net.Security.SslPolicyErrors.None || message?.RequestUri?.IsLoopback == true;
            this._httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
        }

        /// <summary>
        /// Sends an audio file as a legacy <c>audio_url</c> data URL block (backward compatible with
        /// existing <see cref="AnalyzeTrackElementsAsync"/> callers).
        /// </summary>
        public async Task<string?> SendAudioAsync(AudioObj audio, string prompt = "Analyze the audio elements in this track.")
        {
            if (audio == null || string.IsNullOrWhiteSpace(audio.FilePath) || !File.Exists(audio.FilePath))
            {
                return null;
            }

            try
            {
                // 1. Audio in RAM laden und Base64 encoden
                byte[] audioBytes = await File.ReadAllBytesAsync(audio.FilePath);
                string base64Audio = Convert.ToBase64String(audioBytes);

                // Endung dynamisch ermitteln (wav, mp3, flac)
                string extension = Path.GetExtension(audio.FilePath).TrimStart('.').ToLowerInvariant();
                string mimeType = extension == "mp3" ? "mpeg" : extension;
                string audioDataUrl = $"data:audio/{mimeType};base64,{base64Audio}";

                // 2. Multimodal Payload nach OAI-Spec aufbauen
                var payload = new
                {
                    model = this.Model, // llama.cpp ignoriert den Namen meist bei lokalem Hosting, erfordert aber das Feld
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = new object[]
                            {
                                new { type = "text", text = prompt },
                                new { type = "audio_url", audio_url = new { url = audioDataUrl } }
                            }
                        }
                    },
                    temperature = 0.2, // Niedrige Temp für präzisere Timing/Element-Erkennung
                    max_tokens = 800
                };

                string jsonPayload = JsonSerializer.Serialize(payload);
                using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // 3. Request an llama-server abfeuern
                var response = await this._httpClient.PostAsync($"{this.ApiUrl}/v1/chat/completions", content);
                response.EnsureSuccessStatusCode();

                string responseJson = await response.Content.ReadAsStringAsync();

                // 4. Antwort parsen (einfacher JsonDocument Parse, um keine dicken DTOs anlegen zu müssen)
                using var doc = JsonDocument.Parse(responseJson);
                var messageContent = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                return messageContent?.Trim();
            }
            catch (Exception ex)
            {
                // Hier z.B. an den internen Logger weiterleiten
                Console.WriteLine($"[LlamaConnector] Error sending audio: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Sends a multimodal refinement prompt to the llama-server using OpenAI structured content blocks:
        /// text + <c>input_audio</c> (base64 WAV, format "wav") for the reference and synthesized audio,
        /// plus an optional <c>image_url</c> PNG spectrogram. Returns the model's text response (improved C# DSP code).
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
            if (referenceAudio == null || referenceAudio.Length == 0)
            {
                return null;
            }
            if (synthesizedAudio == null || synthesizedAudio.Length == 0)
            {
                return null;
            }

            try
            {
                var contentBlocks = new List<object>
                {
                    new { type = "text", text = prompt },
                    new
                    {
                        type = "input_audio",
                        input_audio = new
                        {
                            data = $"data:audio/wav;base64,{Convert.ToBase64String(referenceAudio)}",
                            format = "wav"
                        }
                    },
                    new
                    {
                        type = "input_audio",
                        input_audio = new
                        {
                            data = $"data:audio/wav;base64,{Convert.ToBase64String(synthesizedAudio)}",
                            format = "wav"
                        }
                    }
                };

                if (includeSpectrogram && spectrogramPng != null && spectrogramPng.Length > 0)
                {
                    contentBlocks.Add(new
                    {
                        type = "image_url",
                        image_url = new
                        {
                            url = $"data:image/png;base64,{Convert.ToBase64String(spectrogramPng)}"
                        }
                    });
                }

                var payload = new
                {
                    model = this.Model,
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = contentBlocks
                        }
                    },
                    temperature = 0.2,
                    max_tokens = 4000
                };

                string jsonPayload = JsonSerializer.Serialize(payload);
                using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await this._httpClient.PostAsync($"{this.ApiUrl}/v1/chat/completions", content);
                response.EnsureSuccessStatusCode();

                string responseJson = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(responseJson);
                var messageContent = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                return messageContent?.Trim();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LlamaConnector] Error sending multimodal synth prompt: {ex.Message}");
                return null;
            }
        }

        // ====================================================================================
        // LLM-DJ CORE STUBS
        // ====================================================================================

        /// <summary>
        /// Hört in einen Track rein und gibt eine Liste der gefundenen Elemente (Vocals, Kick, Snare, Synths, Vogelgezwitscher) zurück.
        /// </summary>
        public async Task<List<string>> AnalyzeTrackElementsAsync(AudioObj audio)
        {
            string prompt = "List all prominent audio elements, stems, and sound effects in this track as a comma-separated list.";
            string? response = await this.SendAudioAsync(audio, prompt);

            // TODO: String splitten und bereinigen
            throw new NotImplementedException();
        }

        /// <summary>
        /// Bewertet, ob Track B harmonisch (Key/Vibe) zu Track A passt.
        /// </summary>
        public async Task<bool> AssessHarmonicCompatibilityAsync(AudioObj trackA, AudioObj trackB)
        {
            // Erfordert ggf. das Senden beider Audios im selben Context, falls llama-server multi-audio im Array unterstützt
            throw new NotImplementedException();
        }

        /// <summary>
        /// Lauscht in das aktuell laufende Ensemble hinein und prüft, ob Rhythmen auseinander driften ("Clashing").
        /// Gibt true zurück, wenn ein Timeout/Pause zum Resync nötig ist.
        /// </summary>
        public async Task<bool> EvaluateEnsembleSyncAsync(IReadOnlyList<AudioObj> playingTracks)
        {
            // Idee: Tracks on-the-fly zusammenmischen in einen kleinen Puffer, Base64 encoden, 
            // Modell fragen: "Are the kicks and transients in this audio perfectly synced or clashing?"
            throw new NotImplementedException();
        }

        /// <summary>
        /// Schlägt einen Timestamp (in Samples oder Millisekunden) vor, an dem Track B in das Ensemble gedroppt werden sollte.
        /// </summary>
        public async Task<long> SuggestMixInPointSamplesAsync(AudioObj targetTrack, AudioObj upcomingTrack)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Analysiert das Audio und generiert ein Raster aus aktiven/inaktiven Steps für komplexe Breakbeat-Slicings.
        /// Jeder Eintrag in der Liste repräsentiert einen Takt, das bool-Array die Slices.
        /// </summary>
        public async Task<List<bool[]>> GenerateBreakPatternAsync(AudioObj audio)
        {
            // Prompt-Idee: "Analyze the rhythm and return a 16-step grid as JSON arrays where 1 is a hit and 0 is silence."
            throw new NotImplementedException();
        }
    }
}