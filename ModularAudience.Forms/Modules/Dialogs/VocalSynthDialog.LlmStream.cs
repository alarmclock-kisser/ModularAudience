using ModularAudience.Audio;
using ModularAudience.Forms.Synthesis;
using ModularAudience.Llama;

namespace ModularAudience.Forms.Modules.Dialogs;

public sealed partial class VocalSynthDialog
{
    private VocalLlmStreamSettings? llmSettings;
    private LlamaStreamProxy? llmProxy;
    private VocalStreamSpeaker? llmSpeaker;
    private bool llmInitializing = true;
    private DateTime lastLlmStatusUpdate;
    private int llmTurnDeltas;
    private int llmTurnCharacters;
    private readonly System.Windows.Forms.Timer llmIdleHint = new() { Interval = 15000 };

    private void RestoreLlmState()
    {
        llmSettings = VocalLlmStreamSettings.Load();
        // Require an explicit opt-in after every application start: binding the proxy
        // port and opening the audio device must not happen implicitly.
        llmSettings.Enabled = false;
        llmLiveCheckBox.Checked = false;
        llmIdleHint.Tick += (_, _) =>
        {
            llmIdleHint.Stop();
            if (llmTurnDeltas == 0)
            {
                LogManager.LogWarning("[Vocal LLM] No streamed assistant text has arrived yet. "
                    + "Open llama UI at the proxy URL (not directly at llama-server), send a message, and check "
                    + "the model filter.");
            }
        };
        llmInitializing = false;
    }

    private void OpenLlmConnectionDialog()
    {
        VocalLlmStreamSettings current = llmSettings ?? VocalLlmStreamSettings.Load();
        using var dialog = new VocalLlmStreamDialog(current);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        VocalLlmStreamSettings updated = dialog.Settings;
        updated.Enabled = llmLiveCheckBox.Checked;
        updated.Save();
        llmSettings = updated;
        LogManager.LogInfo($"[Vocal LLM] Connection settings saved to disk: server {updated.UpstreamUrl}, "
            + $"proxy port {updated.ListenPort}, model filter "
            + $"{(updated.UseModelFilter ? $"'{updated.ModelFilter}'" : "off")}.");

        if (llmLiveCheckBox.Checked)
        {
            _ = RestartLlmStreamAsync();
        }
        else
        {
            llmStatusLabel.Text = "Connection settings saved.";
        }
    }

    private async void LlmLiveCheckedChanged(object? sender, EventArgs e)
    {
        if (llmInitializing)
        {
            return;
        }

        try
        {
            if (llmLiveCheckBox.Checked)
            {
                await StartLlmStreamAsync();
            }
            else
            {
                StopLlmStream(saveEnabledState: true);
            }
        }
        catch (Exception error)
        {
            llmLiveCheckBox.Checked = false;
            llmStatusLabel.Text = $"LLM stream failed: {error.Message}";
            LogManager.LogError($"[Vocal LLM] Stream failed: {error}");
        }
    }

    private Task StartLlmStreamAsync()
    {
        StopLlmStream(saveEnabledState: false);
        VocalLlmStreamSettings config = llmSettings ?? VocalLlmStreamSettings.Load();

        var proxy = new LlamaStreamProxy();
        proxy.AssistantTurnStarted += OnAssistantTurnStarted;
        proxy.AssistantDelta += OnAssistantDelta;
        proxy.AssistantTurnCompleted += OnAssistantTurnCompleted;
        proxy.Error += OnLlmError;
        proxy.Diagnostic += OnLlmDiagnostic;

        try
        {
            proxy.Start(config.UpstreamUrl, config.ListenPort,
                config.UseModelFilter ? config.ModelFilter : "", config.ApiKey);
        }
        catch
        {
            proxy.AssistantTurnStarted -= OnAssistantTurnStarted;
            proxy.AssistantDelta -= OnAssistantDelta;
            proxy.AssistantTurnCompleted -= OnAssistantTurnCompleted;
            proxy.Error -= OnLlmError;
            proxy.Diagnostic -= OnLlmDiagnostic;
            proxy.Dispose();
            throw;
        }

        VocalStreamSpeaker speaker = new(
            () => settings.Clone(),
            config.CreateChunkingSettings(),
            config.FlushMilliseconds,
            config.MinBufferMilliseconds,
            config.AdaptiveSpeed,
            config.MinSpeedFactor,
            settings.Speed);
        speaker.Error += OnLlmError;
        speaker.ChunkRendered += OnStreamChunkRendered;
        speaker.TurnAudioCompleted += OnTurnAudioCompleted;
        speaker.Start();

        llmProxy = proxy;
        llmSpeaker = speaker;
        config.Enabled = true;
        config.Save();
        llmStatusLabel.Text = $"Open llama UI at {proxy.ListenUrl} - not at {config.UpstreamUrl}";
        LogManager.LogInfo($"[Vocal LLM] Live speech started. Proxy {proxy.ListenUrl} -> {config.UpstreamUrl}. "
            + $"IMPORTANT: open llama UI in the browser at {proxy.ListenUrl}, NOT at {config.UpstreamUrl}; "
            + "otherwise the proxy never sees any chat stream. "
            + $"Engine: {(settings.UseWindowsSpeech ? "Natural speech (Windows)" : "VocalSynth formant engine")}, "
            + $"speed {settings.Speed:F2}.");
        llmIdleHint.Stop();
        llmIdleHint.Start();
        return Task.CompletedTask;
    }

    private async Task RestartLlmStreamAsync()
    {
        try
        {
            await StartLlmStreamAsync();
        }
        catch (Exception error)
        {
            llmLiveCheckBox.Checked = false;
            llmStatusLabel.Text = $"LLM stream failed: {error.Message}";
            LogManager.LogError($"[Vocal LLM] Could not restart stream: {error}");
        }
    }

    private void StopLlmStream(bool saveEnabledState)
    {
        llmIdleHint.Stop();
        bool wasRunning = llmProxy != null || llmSpeaker != null;
        if (llmProxy != null)
        {
            llmProxy.AssistantTurnStarted -= OnAssistantTurnStarted;
            llmProxy.AssistantDelta -= OnAssistantDelta;
            llmProxy.AssistantTurnCompleted -= OnAssistantTurnCompleted;
            llmProxy.Error -= OnLlmError;
            llmProxy.Diagnostic -= OnLlmDiagnostic;
            llmProxy.Stop();
            llmProxy.Dispose();
            llmProxy = null;
        }

        if (llmSpeaker != null)
        {
            llmSpeaker.Error -= OnLlmError;
            llmSpeaker.ChunkRendered -= OnStreamChunkRendered;
            llmSpeaker.TurnAudioCompleted -= OnTurnAudioCompleted;
            llmSpeaker.Stop();
            llmSpeaker.Dispose();
            llmSpeaker = null;
        }

        if (saveEnabledState && llmSettings != null)
        {
            llmSettings.Enabled = false;
            llmSettings.Save();
        }

        if (wasRunning)
        {
            LogManager.LogInfo("[Vocal LLM] Live speech stopped.");
        }
        if (!IsDisposed && llmStatusLabel is { IsDisposed: false })
        {
            llmStatusLabel.Text = "Off";
        }
    }

    private void OnAssistantTurnStarted()
    {
        llmIdleHint.Stop();
        llmTurnDeltas = 0;
        llmTurnCharacters = 0;
        LogManager.LogInfo("[Vocal LLM] Assistant answer started; collecting streamed text.");
    }

    private void OnAssistantDelta(string delta)
    {
        llmTurnDeltas++;
        llmTurnCharacters += delta.Length;
        llmSpeaker?.PushDelta(delta);
        if (DateTime.UtcNow - lastLlmStatusUpdate < TimeSpan.FromMilliseconds(250))
        {
            return;
        }

        lastLlmStatusUpdate = DateTime.UtcNow;
        Ui(() =>
        {
            if (llmSpeaker != null)
            {
                llmStatusLabel.Text = $"Speaking... buffer {llmSpeaker.BufferedMilliseconds:F0} ms, speed x{llmSpeaker.EffectiveSpeed:F2}";
            }
        });
    }

    private void OnAssistantTurnCompleted()
    {
        llmSpeaker?.EndTurn();
        LogManager.LogInfo($"[Vocal LLM] Assistant answer finished: {llmTurnCharacters} characters in "
            + $"{llmTurnDeltas} SSE deltas; remaining text flushed to the speaker. "
            + "Waiting for the audio buffer to drain.");
    }

    private void OnTurnAudioCompleted()
    {
        LogManager.LogInfo("[Vocal LLM] All streamed text has been spoken; listening for the next answer.");
        Ui(() => { if (llmSpeaker != null) { llmStatusLabel.Text = "Waiting for LLM..."; } });
    }

    private void OnStreamChunkRendered(string text, double durationSeconds)
    {
        LogManager.LogInfo($"[Vocal LLM] Speaking chunk ({text.Length} chars, {durationSeconds:F2}s): "
            + $"\"{Truncate(text, 70)}\" - buffer {llmSpeaker?.BufferedMilliseconds:F0} ms, "
            + $"speed x{llmSpeaker?.EffectiveSpeed:F2}.");
    }

    private void OnLlmError(string message)
    {
        LogManager.LogError($"[Vocal LLM] {message}");
        Ui(() => { llmStatusLabel.Text = $"LLM stream: {message}"; });
    }

    private void OnLlmDiagnostic(string message)
    {
        LogManager.LogInfo($"[Vocal LLM] {message}");
        Ui(() => { llmStatusLabel.Text = message; });
    }

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length] + "...";

    private void Ui(Action action)
    {
        if (IsDisposed || llmStatusLabel.IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                // Handle was destroyed during shutdown.
            }
        }
        else
        {
            action();
        }
    }
}
