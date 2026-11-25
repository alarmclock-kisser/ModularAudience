using System.Text.Json;
using ModularAudience.Audio;
using ModularAudience.Llama;

namespace ModularAudience.Forms.Modules.Dialogs;

/// <summary>
/// Configuration dialog for the live LLM -&gt; VocalSynth SSE bridge.
/// </summary>
public sealed class VocalLlmStreamDialog : Form
{
    private readonly TextBox upstream = new() { Width = 320, Text = "http://127.0.0.1:9931" };
    private readonly NumericUpDown port = new()
    {
        Minimum = 1, Maximum = 65535, Value = 8081, Width = 90,
    };
    private readonly CheckBox useModelFilter = new() { Text = "Use model filter", AutoSize = true, Margin = new Padding(3, 8, 8, 0) };
    private readonly ComboBox modelFilter = new()
    {
        Width = 220,
        DropDownStyle = ComboBoxStyle.DropDown,
        Enabled = false,
    };
    private readonly NumericUpDown minWords = new()
    {
        Minimum = 1, Maximum = 100, Value = 3, Width = 70,
    };
    private readonly NumericUpDown maxWords = new()
    {
        Minimum = 1, Maximum = 500, Value = 14, Width = 70,
    };
    private readonly CheckBox breakOnComma = new() { Text = "Comma / semicolon", Checked = true, AutoSize = true };
    private readonly CheckBox breakOnColon = new() { Text = "Colon", Checked = true, AutoSize = true };
    private readonly CheckBox breakOnSentence = new() { Text = "Sentence end (. ! ?)", Checked = true, AutoSize = true };
    private readonly NumericUpDown flushMs = new()
    {
        Minimum = 100, Maximum = 10000, Increment = 50, Value = 700, Width = 90,
    };
    private readonly NumericUpDown minBufferMs = new()
    {
        Minimum = 100, Maximum = 10000, Increment = 50, Value = 900, Width = 90,
    };
    private readonly CheckBox adaptiveSpeed = new() { Text = "Slow down when tokens are too slow", Checked = true, AutoSize = true };
    private readonly NumericUpDown minSpeedFactor = new()
    {
        Minimum = 0.25m, Maximum = 1.0m, Increment = 0.05m, DecimalPlaces = 2, Value = 0.6m, Width = 80,
    };
    private readonly Label proxyUrlLabel = new() { AutoSize = true, ForeColor = System.Drawing.Color.DarkBlue, Margin = new Padding(3, 8, 3, 0) };
    private readonly Label status = new() { AutoSize = true, Text = "Ready.", MaximumSize = new Size(620, 0) };
    private readonly Button testButton = new() { Text = "Test connection", AutoSize = true };
    private readonly Button cancelButton = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
    private readonly Button okButton = new() { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK };

    public VocalLlmStreamSettings Settings { get; private set; }

    public VocalLlmStreamDialog(VocalLlmStreamSettings settings)
    {
        Settings = settings.Clone();
        ApplyToControls(Settings);

        Text = "LLM Live Speech - Connection";
        Name = "VocalLlmStreamDialog";
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var info = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(640, 0),
            Text = "Set the server address in llama UI to the proxy URL shown below. "
                 + "The proxy forwards everything to your llama-server and speaks the assistant's streamed answers.",
        };

        var grid = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Padding = new Padding(10),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        void Row(string label, Control control)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 8, 8, 0) });
            grid.Controls.Add(control);
        }

        Row("llama-server address", upstream);
        Row("Proxy listen port", port);
        Row("Model filter", new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = Padding.Empty,
            Controls = { useModelFilter, modelFilter },
        });
        proxyUrlLabel.Text = GetProxyUrl();
        Row("Proxy URL for llama UI", proxyUrlLabel);
        Row("Min words per chunk", minWords);
        Row("Max words per chunk", maxWords);
        Row("Chunk boundaries", new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = Padding.Empty,
            Controls = { breakOnComma, breakOnColon, breakOnSentence },
        });
        Row("Flush partial chunk after (ms)", flushMs);
        Row("Minimum audio buffer (ms)", minBufferMs);
        Row("Adaptive reading speed", adaptiveSpeed);
        Row("Minimum speed factor", minSpeedFactor);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Padding = new Padding(10, 4, 10, 10),
        };
        buttons.Controls.AddRange([okButton, cancelButton, testButton]);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, RowCount = 4, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(info);
        root.Controls.Add(grid);
        root.Controls.Add(status);
        root.Controls.Add(buttons);
        Controls.Add(root);

        AcceptButton = okButton;
        CancelButton = cancelButton;
        port.ValueChanged += (_, _) => UpdateProxyLabel();
        useModelFilter.CheckedChanged += (_, _) => modelFilter.Enabled = useModelFilter.Checked;
        testButton.Click += async (_, _) => await TestConnectionAsync();
        okButton.Click += (_, _) =>
        {
            try
            {
                Settings = ReadFromControls();
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (ArgumentException error)
            {
                status.Text = error.Message;
                DialogResult = DialogResult.None;
            }
        };
    }

    private string GetProxyUrl() => $"http://127.0.0.1:{(int)port.Value}/";

    private void UpdateProxyLabel() => proxyUrlLabel.Text = GetProxyUrl();

    private void ApplyToControls(VocalLlmStreamSettings value)
    {
        upstream.Text = value.UpstreamUrl;
        port.Value = Math.Clamp(value.ListenPort, port.Minimum, port.Maximum);
        useModelFilter.Checked = value.UseModelFilter && !string.IsNullOrWhiteSpace(value.ModelFilter);
        modelFilter.Items.Clear();
        if (!string.IsNullOrWhiteSpace(value.ModelFilter))
        {
            modelFilter.Items.Add(value.ModelFilter);
        }

        modelFilter.Text = value.ModelFilter ?? "";
        modelFilter.Enabled = useModelFilter.Checked;
        minWords.Value = Math.Clamp(value.MinWords, minWords.Minimum, minWords.Maximum);
        maxWords.Value = Math.Clamp(value.MaxWords, maxWords.Minimum, maxWords.Maximum);
        breakOnComma.Checked = value.BreakOnComma;
        breakOnColon.Checked = value.BreakOnColon;
        breakOnSentence.Checked = value.BreakOnSentenceEnd;
        flushMs.Value = Math.Clamp(value.FlushMilliseconds, flushMs.Minimum, flushMs.Maximum);
        minBufferMs.Value = Math.Clamp(value.MinBufferMilliseconds, minBufferMs.Minimum, minBufferMs.Maximum);
        adaptiveSpeed.Checked = value.AdaptiveSpeed;
        minSpeedFactor.Value = Math.Clamp((decimal)value.MinSpeedFactor, minSpeedFactor.Minimum, minSpeedFactor.Maximum);
    }

    private VocalLlmStreamSettings ReadFromControls()
    {
        var result = new VocalLlmStreamSettings
        {
            UpstreamUrl = upstream.Text.Trim(),
            ListenPort = (int)port.Value,
            UseModelFilter = useModelFilter.Checked,
            ModelFilter = useModelFilter.Checked ? modelFilter.Text.Trim() : "",
            MinWords = (int)minWords.Value,
            MaxWords = (int)maxWords.Value,
            BreakOnComma = breakOnComma.Checked,
            BreakOnColon = breakOnColon.Checked,
            BreakOnSentenceEnd = breakOnSentence.Checked,
            FlushMilliseconds = (int)flushMs.Value,
            MinBufferMilliseconds = (int)minBufferMs.Value,
            AdaptiveSpeed = adaptiveSpeed.Checked,
            MinSpeedFactor = (double)minSpeedFactor.Value,
        };
        result.Normalize();
        if (result.ListenPort == new Uri(result.UpstreamUrl).Port)
        {
            throw new ArgumentException("The proxy port must differ from the llama-server port.");
        }

        return result;
    }

    private async Task TestConnectionAsync()
    {
        status.Text = "Testing...";
        testButton.Enabled = false;
        try
        {
            string url = upstream.Text.Trim().TrimEnd('/') + "/v1/models";
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using HttpResponseMessage response = await client.GetAsync(url);
            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                ClearModels();
                status.Text = $"HTTP {(int)response.StatusCode}: {Truncate(body, 120)}";
                LogManager.LogWarning($"[Vocal LLM] Test connection failed: HTTP {(int)response.StatusCode}.");
                return;
            }

            List<string> models = ParseModelIds(body);
            modelFilter.Items.Clear();
            foreach (string model in models)
            {
                modelFilter.Items.Add(model);
            }

            if (models.Count > 0)
            {
                modelFilter.Text = models[0];
                useModelFilter.Checked = true;
                status.Text = $"Connected. Model filter set to '{models[0]}' ({models.Count} model(s) found).";
                LogManager.LogInfo($"[Vocal LLM] Connected to {url}. Models: {string.Join(", ", models)}. Model filter set to '{models[0]}'.");
            }
            else
            {
                ClearModels();
                status.Text = "Connected, but no model is loaded. Model filter left empty.";
                LogManager.LogInfo($"[Vocal LLM] Connected to {url}, but no model was reported.");
            }
        }
        catch (Exception error)
        {
            ClearModels();
            status.Text = $"Connection failed: {error.Message}";
            LogManager.LogWarning($"[Vocal LLM] Test connection failed: {error.Message}");
        }
        finally
        {
            testButton.Enabled = true;
        }
    }

    private void ClearModels()
    {
        modelFilter.Items.Clear();
        modelFilter.Text = "";
        useModelFilter.Checked = false;
        modelFilter.Enabled = false;
    }

    private static List<string> ParseModelIds(string json)
    {
        var result = new List<string>();
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out JsonElement data)
                || data.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (JsonElement item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String)
                {
                    string? value = id.GetString();
                    if (!string.IsNullOrWhiteSpace(value) && !result.Contains(value, StringComparer.Ordinal))
                    {
                        result.Add(value);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Unexpected response shape; leave the filter empty.
        }

        return result;
    }

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length] + "...";
}
