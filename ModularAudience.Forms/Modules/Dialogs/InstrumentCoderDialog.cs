using ModularAudience.Audio;
using ModularAudience.Audio.Synthesis;
using ModularAudience.Audio.Synthesis.Runtime;
using ModularAudience.Forms.Synthesis.Runtime;

using ModularAudience.Llama;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ModularAudience.Forms.Modules.Dialogs;

/// <summary>
/// Non-modal dialog for LLM-driven procedural instrument synthesis.
/// Runs a render -> critique -> compile -> hot-swap refinement loop against a local
/// llama-server, with all UI marshalling via SynchronizationContext / IProgress.
/// </summary>
public sealed partial class InstrumentCoderDialog : Form
{
    private static readonly object ActiveDialogsGate = new();
    private static readonly HashSet<InstrumentCoderDialog> ActiveDialogs = [];

    private readonly LlamaConnector llm;
    private readonly SynthCompiler compiler;
    private readonly CustomInstrumentStore store;
    private readonly CustomInstrumentSynthHost host;
    private readonly SynthRefinementOrchestrator orchestrator;
    private readonly SynchronizationContext uiContext;
    private readonly CancellationTokenSource dialogCts = new();

    // UI controls
    private readonly ComboBox referenceAudioCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly Button referencePlayButton = new() { Text = "Play", AutoSize = true, Enabled = false };
    private readonly TextBox promptText = new() { Multiline = true, Height = 90, Width = 500 };
    private readonly TextBox serverUrlText = new() { Text = "http://localhost:9931", Width = 200 };
    private readonly ComboBox modelCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly NumericUpDown maxIterations = new() { Minimum = 1, Maximum = 50, Value = 10, Width = 80 };
    private readonly CheckBox includeSpectrogram = new() { Text = "Include Spectrogram Image", Checked = true, AutoSize = true };
    private readonly CheckBox autoRetryCheckBox = new() { Text = "Continue (send errors + retry)", Checked = true, AutoSize = true };
    private readonly Button startButton = new() { Text = "Start Auto-Refine (send prompt to LLM)", AutoSize = true };
    private readonly Button setPromptButton = new() { Text = "Set Instrument Prompt...", AutoSize = true };
    private readonly Button stopButton = new() { Text = "Stop / Cancel", AutoSize = true, Enabled = false };
    private readonly Button compileButton = new() { Text = "Compile & Audition Current Code", AutoSize = true };
    private readonly Button loadButton = new() { Text = "Load into Active Synth Slot", AutoSize = true };
    private readonly Button revertButton = new() { Text = "Revert to Selected Iteration", AutoSize = true, Enabled = false };
    private readonly TextBox codeBox = new() { Multiline = true, Width = 700, Height = 300, Font = new Font("Consolas", 9f) };
    private readonly ListBox logList = new() { Width = 700, Height = 200 };
    private readonly ComboBox iterationList = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly Label statusLabel = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "Idle." };
    private readonly Button auditionButton = new() { Text = "Audition Synth", AutoSize = true };

    private AudioObj? referenceAudio;
    private WaveOut? referenceOutput;
    private readonly Dictionary<string, AudioObj> referenceAudioMap = new();
    private TextBox? instrumentNameBox;

    // Settings persistence
    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ModularAudience", "InstrumentCoder", "settings.json");
    private bool disposed;

    public InstrumentCoderDialog(
        LlamaConnector? llm = null,
        SynthCompiler? compiler = null,
        CustomInstrumentStore? store = null,
        CustomInstrumentSynthHost? host = null)
    {
        this.uiContext = SynchronizationContext.Current ?? new SynchronizationContext();
        this.llm = llm ?? new LlamaConnector();
        this.llm.Logger = line => AddLogLine($"[Llama] {line}");
        this.compiler = compiler ?? new SynthCompiler();
        this.store = store ?? CustomInstrumentStore.Shared;
        this.host = host ?? new CustomInstrumentSynthHost(new CustomInstrumentDescriptor("New Instrument", ""));
        this.orchestrator = new SynthRefinementOrchestrator(this.llm, this.compiler, this.store, this.host.Descriptor)
        {
            MaxIterations = (int)maxIterations.Value,
            IncludeSpectrogram = includeSpectrogram.Checked
        };

        WireOrchestratorEvents();

        Text = "Instrument Coder - Runtime Procedural Synthesizer"; Name = "InstrumentCoderDialog";
        ClientSize = new Size(1100, 820); MinimumSize = new Size(900, 700); StartPosition = FormStartPosition.CenterScreen;
        BuildUi();

        maxIterations.ValueChanged += (_, _) => { orchestrator.MaxIterations = (int)maxIterations.Value; };
        includeSpectrogram.CheckedChanged += (_, _) => { orchestrator.IncludeSpectrogram = includeSpectrogram.Checked; };
        autoRetryCheckBox.CheckedChanged += (_, _) =>
        {
            orchestrator.AutoRetryCompileErrors = autoRetryCheckBox.Checked;
            AddLogLine($"Compile error auto-retry {(autoRetryCheckBox.Checked ? "enabled" : "disabled")}.");
        };

        startButton.Click += async (_, _) => await StartRefineAsync();
        setPromptButton.Click += (_, _) => ShowInstrumentPromptDialog();
        stopButton.Click += (_, _) => StopRefine();
        compileButton.Click += async (_, _) => await CompileAndAuditionAsync();
        loadButton.Click += (_, _) => LoadIntoActiveSlot();
        revertButton.Click += (_, _) => RevertToSelectedIteration();
        auditionButton.Click += async (_, _) => await AuditionSynthAsync();
        referencePlayButton.Click += async (_, _) => await ToggleReferencePlaybackAsync();
        referenceAudioCombo.SelectedIndexChanged += (_, _) => { referencePlayButton.Enabled = referenceAudioCombo.SelectedItem is not null; };
        iterationList.SelectedIndexChanged += (_, _) => { revertButton.Enabled = iterationList.SelectedItem is not null; };

        codeBox.Text = this.host.Descriptor.Code;
        codeBox.TextChanged += (_, _) => { this.host.Descriptor.Code = codeBox.Text; this.host.Descriptor.UpdatedAt = DateTime.UtcNow; };

        LoadReferenceAudioList();
        RefreshIterationList();

        this.store.InstrumentsChanged += () =>
        {
            uiContext.Post(_ => { LoadReferenceAudioList(); RefreshIterationList(); }, null);
        };

        FormClosing += (_, _) =>
        {
            SaveSettings();
            StopRefine();
            referenceOutput?.Stop();
            referenceOutput?.Dispose();
            dialogCts.Cancel();
        };

        LoadSettings();
        AddLogLine("Instrument Coder dialog opened.");
        try { ModularAudience.Audio.LogManager.Log("[InstrumentCoder] Dialog opened."); } catch (Exception ex) { Console.WriteLine($"LogManager.Log failed: {ex}"); }

        Disposed += (_, _) =>
        {
            lock (ActiveDialogsGate) { ActiveDialogs.Remove(this); }
            orchestrator.Dispose();
            dialogCts.Dispose();
        };

        lock (ActiveDialogsGate) { ActiveDialogs.Add(this); }
    }

    private void BuildUi()
    {
        // Use default WinForms colors (light theme).
        BackColor = SystemColors.Control;
        ForeColor = SystemColors.ControlText;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 8, ColumnCount = 2, Padding = new Padding(10) };
        root.ColumnStyles.Add(new(SizeType.Percent, 50));
        root.ColumnStyles.Add(new(SizeType.Percent, 50));
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Percent, 40));
        root.RowStyles.Add(new(SizeType.Percent, 30));
        root.RowStyles.Add(new(SizeType.AutoSize));

        void Field(Control label, Control control, int column = 0, int row = 0)
        {
            root.Controls.Add(label, column, row);
            root.Controls.Add(control, column + 1, row);
        }

        // Row 0: Reference audio
        var refGroup = new GroupBox { Text = "Reference Audio", AutoSize = true };
        var refLayout = new FlowLayoutPanel { AutoSize = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight };
        refLayout.Controls.Add(new Label { Text = "Select reference sample:", AutoSize = true, Margin = new Padding(5, 5, 5, 0) });
        refLayout.Controls.Add(referenceAudioCombo);
        refLayout.Controls.Add(referencePlayButton);
        refGroup.Controls.Add(refLayout);
        root.Controls.Add(refGroup, 0, 0);
        root.SetColumnSpan(refGroup, 2);

        // Row 1: Target instrument / prompt
        var promptGroup = new GroupBox { Text = "Target Instrument / Prompt", AutoSize = true };
        var promptLayout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 2, Padding = new Padding(5) };
        promptLayout.ColumnStyles.Add(new(SizeType.AutoSize));
        promptLayout.ColumnStyles.Add(new(SizeType.Percent, 100));
        promptLayout.Controls.Add(new Label { Text = "Instrument name:", AutoSize = true }, 0, 0);
        var instrumentName = new TextBox { Text = this.host.Descriptor.Name, Width = 250 };
        instrumentName.TextChanged += (_, _) => { this.host.Descriptor.Name = instrumentName.Text; };
        this.instrumentNameBox = instrumentName;
        promptLayout.Controls.Add(instrumentName, 1, 0);
        promptLayout.Controls.Add(new Label { Text = "Refinement prompt:", AutoSize = true }, 0, 1);
        promptLayout.Controls.Add(promptText, 1, 1);
        promptText.Text = "Improve this synth to better match the reference instrument's timbre and envelope.";
        root.Controls.Add(promptGroup, 0, 1);
        root.SetColumnSpan(promptGroup, 2);

        // Row 2: LLM connection
        var llmGroup = new GroupBox { Text = "LLM Connection (llama-server)", AutoSize = true };
        var llmLayout = new FlowLayoutPanel { AutoSize = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight };
        llmLayout.Controls.Add(new Label { Text = "Server URL:", AutoSize = true, Margin = new Padding(5, 5, 5, 0) });
        llmLayout.Controls.Add(serverUrlText);
        llmLayout.Controls.Add(new Label { Text = "Model:", AutoSize = true, Margin = new Padding(15, 5, 5, 0) });
        llmLayout.Controls.Add(modelCombo);
        llmLayout.Controls.Add(maxIterations);
        llmLayout.Controls.Add(new Label { Text = "max iterations", AutoSize = true, Margin = new Padding(5, 8, 0, 0) });
        llmLayout.Controls.Add(includeSpectrogram);
        llmLayout.Controls.Add(autoRetryCheckBox);
        var connectButton = new Button { Text = "Test Connection", AutoSize = true };
        connectButton.Click += async (_, _) => await TestConnectionAsync();
        llmLayout.Controls.Add(connectButton);
        llmGroup.Controls.Add(llmLayout);
        root.Controls.Add(llmGroup, 0, 2);
        root.SetColumnSpan(llmGroup, 2);

        // Row 3: Action buttons
        var actionBar = new FlowLayoutPanel { AutoSize = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight };
        actionBar.Controls.Add(setPromptButton);
        actionBar.Controls.Add(startButton);
        actionBar.Controls.Add(stopButton);
        actionBar.Controls.Add(compileButton);
        actionBar.Controls.Add(loadButton);
        actionBar.Controls.Add(revertButton);
        actionBar.Controls.Add(auditionButton);
        root.Controls.Add(actionBar, 0, 3);
        root.SetColumnSpan(actionBar, 2);

        // Row 4: Editable code box
        var codeGroup = new GroupBox { Text = "C# DSP Code (editable)", Dock = DockStyle.Fill };
        var codeLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        codeLayout.ColumnStyles.Add(new(SizeType.Percent, 100));
        codeLayout.RowStyles.Add(new(SizeType.Percent, 100));
        codeLayout.RowStyles.Add(new(SizeType.AutoSize));
        codeBox.AcceptsTab = true;
        codeBox.ScrollBars = ScrollBars.Both;
        codeLayout.Controls.Add(codeBox, 0, 0);
        var codeHint = new Label { Text = "Edit the C# DSP code directly. Compile & Audition tests it without LLM. Start Auto-Refine lets the LLM iterate.", AutoSize = true, ForeColor = SystemColors.GrayText };
        codeLayout.Controls.Add(codeHint, 0, 1);
        codeGroup.Controls.Add(codeLayout);
        root.Controls.Add(codeGroup, 0, 4);
        root.SetColumnSpan(codeGroup, 2);

        // Row 5: Iteration log
        var logGroup = new GroupBox { Text = "Iteration Log", Dock = DockStyle.Fill };
        var logLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        logLayout.ColumnStyles.Add(new(SizeType.Percent, 70));
        logLayout.ColumnStyles.Add(new(SizeType.Percent, 30));
        logLayout.RowStyles.Add(new(SizeType.Percent, 100));
        logLayout.RowStyles.Add(new(SizeType.AutoSize));
        logList.Font = new Font("Consolas", 9f);
        logLayout.Controls.Add(logList, 0, 0);
        iterationList.DropDownStyle = ComboBoxStyle.DropDownList;
        logLayout.Controls.Add(iterationList, 1, 0);
        root.Controls.Add(logGroup, 0, 5);
        root.SetColumnSpan(logGroup, 2);

        // Row 6: Status
        root.Controls.Add(statusLabel, 0, 6);
        root.SetColumnSpan(statusLabel, 2);

        Controls.Add(root);
    }

    private static bool IsActiveRefinementState(SynthRefinementState state) =>
        state is SynthRefinementState.Rendering or SynthRefinementState.AnalyzingWithLlm
            or SynthRefinementState.Compiling or SynthRefinementState.HotSwapping;

    private void SetRefineRunning(bool running)
    {
        startButton.Enabled = !running;
        stopButton.Enabled = running;
    }

    private void WireOrchestratorEvents()
    {
        orchestrator.ProgressChanged += progress =>
        {
            uiContext.Post(_ =>
            {
                LogMultiline($"{progress.State}: {progress.Message}");
                statusLabel.Text = $"{progress.State} | {progress.Message}";
                SetRefineRunning(IsActiveRefinementState(progress.State));
            }, null);
        };

        orchestrator.IterationCompleted += iteration =>
        {
            uiContext.Post(_ =>
            {
                RefreshIterationList();
                AddLogLine($"Iteration {iteration.Index}: {iteration.State}");
                if (!string.IsNullOrEmpty(iteration.LlmCritique))
                    AddLogLine($"  LLM critique ({iteration.LlmLatency.TotalSeconds:0.##}s): {iteration.LlmCritique[..Math.Min(200, iteration.LlmCritique.Length)]}...");
                if (!string.IsNullOrEmpty(iteration.CompileError))
                    AddLogLine($"  Compile error ({iteration.CompileTime.TotalSeconds:0.##}s): {iteration.CompileError}");
                if (iteration.State == SynthRefinementState.HotSwapping)
                    AddLogLine($"  Hot-swapped. Peak={iteration.PeakAmplitude:0.000}");
                statusLabel.Text = $"Iteration {iteration.Index} complete.";
            }, null);
        };

        orchestrator.HotSwapped += synth =>
        {
            uiContext.Post(_ =>
            {
                codeBox.Text = this.host.Descriptor.Code;
                statusLabel.Text = "Synth hot-swapped into active slot.";
            }, null);
        };

        orchestrator.CodeStreaming += partialCode =>
        {
            uiContext.Post(_ =>
            {
                if (string.IsNullOrEmpty(partialCode) || codeBox.Text == partialCode) return;
                codeBox.Text = partialCode;
                codeBox.SelectionStart = codeBox.TextLength;
                codeBox.ScrollToCaret();
            }, null);
        };
    }

    private void AddLogLine(string line)
    {
        if (disposed || IsDisposed) return;
        string stamped = $"[{DateTime.Now:HH:mm:ss}] {line}";

        void AppendToUi()
        {
            if (disposed || IsDisposed) return;
            logList.Items.Add(stamped);
            logList.TopIndex = logList.Items.Count - 1;
        }

        if (uiContext != SynchronizationContext.Current)
        {
            uiContext.Post(_ => AppendToUi(), null);
        }
        else
        {
            AppendToUi();
        }

        Console.WriteLine($"[InstrumentCoderDialog] {line}");
        try
        {
            ModularAudience.Audio.LogManager.Log($"[InstrumentCoder] {line}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[InstrumentCoderDialog] LogManager.Log failed: {ex}");
        }
    }

    private void LogMultiline(string text)
    {
        foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (!string.IsNullOrWhiteSpace(line)) AddLogLine(line.TrimEnd());
        }
    }

    private void ShowCopyableError(string title, string message)
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
            Font = new Font("Consolas", 9f)
        };

        var button = new Button
        {
            Text = "OK",
            Dock = DockStyle.Bottom,
            Height = 30
        };
        button.Click += (_, _) => dialog.Close();

        dialog.Controls.Add(textBox);
        dialog.Controls.Add(button);
        dialog.ShowDialog(this);
    }

    private void RefreshIterationList()
    {
        iterationList.Items.Clear();
        for (int i = 0; i < orchestrator.History.Count; i++)
        {
            var it = orchestrator.History[i];
            iterationList.Items.Add($"Iteration {it.Index} | {it.State}");
        }
        if (iterationList.Items.Count > 0) iterationList.SelectedIndex = iterationList.Items.Count - 1;
    }

    private void LoadReferenceAudioList()
    {
        referenceAudioCombo.Items.Clear();
        referenceAudioMap.Clear();
        referenceAudioCombo.Items.Add("<none>");

        try
        {
            foreach (var view in WindowMain.CollectionViews)
            {
                if (view == null || view.IsDisposed) continue;
                foreach (var audio in view.AudioC.Audios)
                {
                    if (audio == null) continue;
                    string key = $"{view.Text} / {audio.Name}";
                    if (!referenceAudioMap.ContainsKey(key))
                    {
                        referenceAudioMap[key] = audio;
                        referenceAudioCombo.Items.Add(key);
                    }
                }
            }
        }
        catch { /* CollectionViews may not be available */ }

        if (referenceAudioCombo.Items.Count > 1)
        {
            referenceAudioCombo.SelectedIndex = 1;
        }
        else
        {
            referenceAudioCombo.SelectedIndex = 0;
        }
    }

    private async Task TestConnectionAsync()
    {
        try
        {
            string url = serverUrlText.Text.Trim();
            if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                MessageBox.Show(this, "Enter a valid server URL.", "Connection Test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // First, test connectivity by querying /v1/models.
            using var httpClient = CreateLlmHttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(15);
            string normalizedUrl = url.TrimEnd('/');
            int v1Index = normalizedUrl.IndexOf("/v1", StringComparison.OrdinalIgnoreCase);
            if (v1Index >= 0) normalizedUrl = normalizedUrl[..v1Index].TrimEnd('/');
            string modelsUrl = normalizedUrl + "/v1/models";

            using var modelsResponse = await httpClient.GetAsync(modelsUrl);
            if (!modelsResponse.IsSuccessStatusCode)
            {
                string body = await modelsResponse.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Models endpoint returned {(int)modelsResponse.StatusCode}: {body}");
            }

            string modelsJson = await modelsResponse.Content.ReadAsStringAsync();
            AddLogLine($"LLM models: {modelsJson[..Math.Min(200, modelsJson.Length)]}");

            // Populate model ComboBox from server response.
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(modelsJson);
                if (doc.RootElement.TryGetProperty("data", out var dataArr) && dataArr.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    modelCombo.Items.Clear();
                    foreach (var model in dataArr.EnumerateArray())
                    {
                        // Try "id" first, then "name", then "model".
                        string? modelId = null;
                        if (model.TryGetProperty("id", out var idProp) && idProp.ValueKind == System.Text.Json.JsonValueKind.String)
                            modelId = idProp.GetString();
                        else if (model.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == System.Text.Json.JsonValueKind.String)
                            modelId = nameProp.GetString();
                        else if (model.TryGetProperty("model", out var modelProp) && modelProp.ValueKind == System.Text.Json.JsonValueKind.String)
                            modelId = modelProp.GetString();

                        if (!string.IsNullOrWhiteSpace(modelId) && !modelCombo.Items.Contains(modelId))
                        {
                            modelCombo.Items.Add(modelId);
                        }
                    }
                    if (modelCombo.Items.Count > 0 && modelCombo.SelectedIndex < 0)
                    {
                        modelCombo.SelectedIndex = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                AddLogLine($"LLM model list could not be parsed: {ex.Message}");
            }

            // Test the chat completions endpoint with a text-only request (no audio).
            // Audio content types may not be supported by all server versions.
            var testConnector = new LlamaConnector(url)
            {
                Model = modelCombo.SelectedItem as string ?? this.llm.Model
            };
            testConnector.Logger = line => AddLogLine($"[Llama] {line}");
            string? result = await TestTextCompletionAsync(testConnector);

            if (result != null)
            {
                AddLogLine($"LLM connection test: OK - {result}");
                statusLabel.Text = "Connected.";
            }
            else
            {
                AddLogLine("LLM connection test: no response from chat completions");
                statusLabel.Text = "No response.";
            }
        }
        catch (Exception ex)
        {
            string fullError = ex.ToString();
            Console.WriteLine($"[InstrumentCoderDialog] Connection test failed: {fullError}");
            AddLogLine($"LLM connection test failed: {ex.Message}");
            statusLabel.Text = "Connection failed.";
            try
            {
                ModularAudience.Audio.LogManager.Log($"[InstrumentCoder] Connection test failed: {fullError}");
            }
            catch (Exception logEx)
            {
                Console.WriteLine($"[InstrumentCoderDialog] LogManager.Log failed: {logEx}");
            }
            ShowCopyableError("LLM Connection Error", fullError);
        }
    }

    private async Task<string?> TestTextCompletionAsync(LlamaConnector connector)
    {
        // Send a direct text-only request so unsupported audio content cannot break the test.
        try
        {
            using var httpClient = CreateLlmHttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            string normalizedUrl = connector.ApiUrl.TrimEnd('/');
            string chatUrl = normalizedUrl + "/v1/chat/completions";

            var payload = new
            {
                model = connector.Model,
                messages = new[] { new { role = "user", content = "Respond with just 'OK'." } },
                temperature = 0.2,
                max_tokens = 50,
                stream = false
            };

            string json = System.Text.Json.JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var response = await httpClient.PostAsync(chatUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[InstrumentCoderDialog] Chat test failed: {(int)response.StatusCode} {errorBody}");
                AddLogLine($"Chat test HTTP {(int)response.StatusCode}: {errorBody[..Math.Min(200, errorBody.Length)]}");
                return null;
            }

            string responseJson = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(responseJson);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()?.Trim();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[InstrumentCoderDialog] Text completion test failed: {ex}");
            AddLogLine($"Text completion test failed: {ex.Message}");
            return null;
        }
    }

    private static HttpClient CreateLlmHttpClient()
    {
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = static (message, cert, chain, errors) =>
            errors == System.Net.Security.SslPolicyErrors.None || message?.RequestUri?.IsLoopback == true;
        return new HttpClient(handler);
    }

    private async Task<byte[]> CreateTestToneWavAsync()
    {
        using var ms = new MemoryStream();
        using var writer = new WaveFileWriter(ms, new WaveFormat(44100, 2));
        for (int i = 0; i < 44100; i++)
        {
            float s = (float)(0.2 * Math.Sin(2 * Math.PI * 440 * i / 44100.0));
            writer.WriteSample(s); writer.WriteSample(s);
        }
        writer.Flush();
        return ms.ToArray();
    }

    private async Task StartRefineAsync()
    {
        AddLogLine("Starting auto-refine loop.");
        statusLabel.Text = "Refining...";

        orchestrator.MaxIterations = (int)maxIterations.Value;
        orchestrator.IncludeSpectrogram = includeSpectrogram.Checked;
        orchestrator.AutoRetryCompileErrors = autoRetryCheckBox.Checked;
        orchestrator.UserPrompt = promptText.Text;
        this.llm.SetApiUrl(serverUrlText.Text);
        if (modelCombo.SelectedItem is string selectedModel && !string.IsNullOrWhiteSpace(selectedModel))
        {
            this.llm.Model = selectedModel;
        }
        AddLogLine($"Using LLM endpoint {this.llm.ApiUrl} with model '{this.llm.Model}'.");
        AddLogLine($"Compile error auto-retry {(autoRetryCheckBox.Checked ? "enabled" : "disabled")}.");
        SetRefineRunning(true);
        orchestrator.Start();
    }

    private void StopRefine()
    {
        orchestrator.Cancel();
        SetRefineRunning(false);
        AddLogLine("Refine cancelled by user.");
        statusLabel.Text = "Idle.";
    }

    private async Task CompileAndAuditionAsync()
    {
        string code = codeBox.Text;
        if (string.IsNullOrWhiteSpace(code))
        {
            MessageBox.Show(this, "Enter C# code to compile.", "Instrument Coder", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        AddLogLine("Compiling current code...");
        var result = this.compiler.Compile(code, dialogCts.Token);
        if (!result.IsSuccess)
        {
            AddLogLine($"Compile failed: {string.Join(" | ", result.Errors)}");
            statusLabel.Text = "Compile failed.";
            return;
        }

        AddLogLine("Compile OK.");
        try
        {
            var synth = new CustomInstrumentSynth(this.host.Descriptor, result.Assembly);
            var safeSynth = new SynthSandboxDecorator(synth);
            this.host.Swap(safeSynth);
            codeBox.Text = code;
            await AuditionSynthAsync();
            statusLabel.Text = "Compiled and loaded into active slot.";
        }
        catch (Exception ex)
        {
            LogMultiline($"Load/audition failed: {ex}");
            statusLabel.Text = "Load failed.";
        }
    }

    private void LoadIntoActiveSlot()
    {
        if (this.host.ActiveSynth == null)
        {
            MessageBox.Show(this, "No synth is currently active. Compile & Audition first.", "Instrument Coder", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        this.store.Save(this.host.Descriptor);
        AddLogLine($"Saved '{this.host.Descriptor.Name}' to Custom Instruments store.");
        statusLabel.Text = "Saved to custom instruments.";

        MessageBox.Show(this,
            $"Instrument '{this.host.Descriptor.Name}' saved to %appdata%\\ModularAudience\\CustomInstruments\\",
            "Instrument Coder", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void RevertToSelectedIteration()
    {
        if (iterationList.SelectedItem is not string selected || orchestrator.History.Count == 0)
        {
            return;
        }

        int index = int.Parse(selected.Split(' ')[1]);
        var it = orchestrator.History.FirstOrDefault(i => i.Index == index);
        if (it == null || string.IsNullOrEmpty(it.Code)) return;

        codeBox.Text = it.Code;
        this.host.Descriptor.Code = it.Code;
        AddLogLine($"Reverted to iteration {index} code.");
        statusLabel.Text = $"Reverted to iteration {index}.";
    }

    private async Task AuditionSynthAsync()
    {
        var settings = new InstrumentSynthSettings
        {
            Family = SynthFamily.Custom,
            CustomInstrumentName = this.host.Descriptor.Name,
            SampleRate = this.host.Descriptor.SampleRate,
            Channels = this.host.Descriptor.Channels,
            MidiNote = this.host.Descriptor.MidiNote,
            Bpm = this.host.Descriptor.Bpm,
            GainPercent = this.host.Descriptor.GainPercent
        };
        var dialog = new InstrumentSampleSynthDialog(SynthFamily.Custom, initialSettings: settings);
        dialog.Show();
        dialog.Activate();
    }

    private async Task ToggleReferencePlaybackAsync()
    {
        if (referenceOutput != null)
        {
            referenceOutput.Stop();
            referenceOutput.Dispose();
            referenceOutput = null;
            referencePlayButton.Text = "Play";
            return;
        }

        if (referenceAudioCombo.SelectedItem is string sel && sel != "<none>" && referenceAudioMap.TryGetValue(sel, out var audio))
        {
            referenceAudio = audio;
            if (referenceAudio != null)
            {
                referenceOutput = new WaveOut { BufferMilliseconds = 60 };
                referencePlayButton.Text = "Stop";
                try
                {
                    await referenceAudio.PlayAsync(dialogCts.Token);
                }
                catch (OperationCanceledException) { }
                referenceOutput?.Stop();
                referenceOutput?.Dispose();
                referenceOutput = null;
                referencePlayButton.Text = "Play";
            }
        }
    }

    private void ShowInstrumentPromptDialog()
    {
        using var dialog = new Form
        {
            Text = "Set Instrument Prompt",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.SizableToolWindow,
            ClientSize = new Size(500, 320),
            ShowInTaskbar = false
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(10) };
        layout.ColumnStyles.Add(new(SizeType.AutoSize));
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.Percent, 100));

        // Family ComboBox
        var familyCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        foreach (var f in Enum.GetValues<SynthFamily>())
        {
            familyCombo.Items.Add(InstrumentSynthCatalog.FamilyName(f));
        }
        familyCombo.SelectedIndex = 0;

        // Voice ComboBox
        var voiceCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        void PopulateVoices()
        {
            voiceCombo.Items.Clear();
            var family = (SynthFamily)familyCombo.SelectedIndex;
            foreach (var v in InstrumentSynthCatalog.Voices(family))
            {
                voiceCombo.Items.Add(v.Name);
            }
            if (voiceCombo.Items.Count > 0) voiceCombo.SelectedIndex = 0;
        }
        PopulateVoices();
        familyCombo.SelectedIndexChanged += (_, _) => PopulateVoices();

        // Prompt text
        var promptBox = new TextBox { Multiline = true, Height = 100, Dock = DockStyle.Fill };
        promptBox.Text = promptText.Text;

        // Optional: allow empty selection
        var noneCheck = new CheckBox { Text = "From scratch (no base instrument)", AutoSize = true, Checked = string.IsNullOrWhiteSpace(promptText.Text) };
        noneCheck.CheckedChanged += (_, _) =>
        {
            familyCombo.Enabled = !noneCheck.Checked;
            voiceCombo.Enabled = !noneCheck.Checked;
        };

        layout.Controls.Add(new Label { Text = "Instrument family:", AutoSize = true }, 0, 0);
        layout.Controls.Add(familyCombo, 1, 0);
        layout.Controls.Add(new Label { Text = "Voice / subtype:", AutoSize = true }, 0, 1);
        layout.Controls.Add(voiceCombo, 1, 1);
        layout.Controls.Add(noneCheck, 0, 2);
        layout.SetColumnSpan(noneCheck, 2);
        layout.Controls.Add(new Label { Text = "Prompt:", AutoSize = true }, 0, 3);
        layout.Controls.Add(promptBox, 1, 3);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var okButton = new Button { Text = "OK", AutoSize = true };
        var cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        okButton.Click += (_, _) =>
        {
            if (noneCheck.Checked)
            {
                promptText.Text = promptBox.Text;
                if (instrumentNameBox != null) instrumentNameBox.Text = "Custom Instrument";
            }
            else
            {
                string familyName = InstrumentSynthCatalog.FamilyName((SynthFamily)familyCombo.SelectedIndex);
                string voiceName = voiceCombo.SelectedItem?.ToString() ?? "";
                string prefix = $"Create a synth inspired by {familyName} / {voiceName}. ";
                promptText.Text = prefix + promptBox.Text;
                if (instrumentNameBox != null) instrumentNameBox.Text = voiceName;
            }
            dialog.DialogResult = DialogResult.OK;
            dialog.Close();
        };
        buttonPanel.Controls.Add(okButton);
        buttonPanel.Controls.Add(cancelButton);

        dialog.Controls.Add(layout);
        dialog.Controls.Add(buttonPanel);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            AddLogLine($"Instrument prompt set: {promptText.Text[..Math.Min(80, promptText.Text.Length)]}...");
        }
    }

    private sealed class CoderSettings
    {
        public string ServerUrl { get; set; } = "http://localhost:9931";
        public string Model { get; set; } = "";
        public int MaxIterations { get; set; } = 10;
        public bool IncludeSpectrogram { get; set; } = true;
        public bool AutoRetryCompileErrors { get; set; } = true;
        public string Prompt { get; set; } = "Improve this synth to better match the reference instrument's timbre and envelope.";
        public string Code { get; set; } = "";
        public string InstrumentName { get; set; } = "New Instrument";
        public string? SelectedReferenceAudio { get; set; }
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            string json = File.ReadAllText(SettingsPath);
            var settings = System.Text.Json.JsonSerializer.Deserialize<CoderSettings>(json);
            if (settings == null) return;

            serverUrlText.Text = settings.ServerUrl;
            if (!string.IsNullOrEmpty(settings.Model) && modelCombo.Items.Contains(settings.Model))
            {
                modelCombo.SelectedItem = settings.Model;
            }
            else if (modelCombo.Items.Count > 0)
            {
                modelCombo.SelectedIndex = 0;
            }
            maxIterations.Value = Math.Clamp(settings.MaxIterations, (int)maxIterations.Minimum, (int)maxIterations.Maximum);
            includeSpectrogram.Checked = settings.IncludeSpectrogram;
            autoRetryCheckBox.Checked = settings.AutoRetryCompileErrors;
            orchestrator.AutoRetryCompileErrors = autoRetryCheckBox.Checked;
            promptText.Text = settings.Prompt;
            codeBox.Text = settings.Code;
            this.host.Descriptor.Name = settings.InstrumentName;
            this.host.Descriptor.Code = settings.Code;
            if (!string.IsNullOrEmpty(settings.SelectedReferenceAudio))
            {
                int idx = referenceAudioCombo.Items.IndexOf(settings.SelectedReferenceAudio);
                if (idx >= 0) referenceAudioCombo.SelectedIndex = idx;
            }

            this.llm.SetApiUrl(serverUrlText.Text);
            if (modelCombo.SelectedItem is string savedModel && !string.IsNullOrWhiteSpace(savedModel))
            {
                this.llm.Model = savedModel;
            }
        }
        catch (Exception ex)
        {
            LogMultiline($"Instrument Coder settings could not be loaded: {ex}");
        }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var settings = new CoderSettings
            {
                ServerUrl = serverUrlText.Text,
                Model = modelCombo.SelectedItem as string ?? "",
                MaxIterations = (int)maxIterations.Value,
                IncludeSpectrogram = includeSpectrogram.Checked,
                AutoRetryCompileErrors = autoRetryCheckBox.Checked,
                Prompt = promptText.Text,
                Code = codeBox.Text,
                InstrumentName = this.host.Descriptor.Name,
                SelectedReferenceAudio = referenceAudioCombo.SelectedItem as string
            };
            string json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            LogMultiline($"Instrument Coder settings could not be saved: {ex}");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            StopRefine();
            referenceOutput?.Stop();
            referenceOutput?.Dispose();
            referenceAudio?.Dispose();
            dialogCts.Dispose();
        }
        base.Dispose(disposing);
    }
}
