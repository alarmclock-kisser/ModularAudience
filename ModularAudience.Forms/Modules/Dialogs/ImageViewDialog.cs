using System.Drawing;
using System.Drawing.Imaging;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ModularAudience.Audio;
using ModularAudience.Audio.Midi;
using ModularAudience.Audio.Omr;
using ModularAudience.Llama.Dtos;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class ImageViewDialog : Form
{
    private readonly ImageObj imageObj;
    private Bitmap? displayedFrame;
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(5) };

    public MidiDto? GeneratedMidiDto { get; private set; }
    public MidiFileData? GeneratedMidiFile { get; private set; }

    public ImageViewDialog(ImageObj imageObj)
    {
        this.imageObj = imageObj ?? throw new ArgumentNullException(nameof(imageObj));
        this.InitializeComponent();

        if (this.imageObj.FrameCount == 0)
        {
            this.numericUpDown_frame.Enabled = false;
            return;
        }

        this.numericUpDown_frame.Maximum = this.imageObj.FrameCount;
        this.numericUpDown_frame.Value = 1;
        this.ShowFrame(0);
    }

    private void numericUpDown_frame_ValueChanged(object? sender, EventArgs e)
    {
        this.ShowFrame(decimal.ToInt32(this.numericUpDown_frame.Value) - 1);
    }

    private void pictureBox_image_Resize(object? sender, EventArgs e)
    {
        this.pictureBox_image.Invalidate();
    }

    private void ShowFrame(int frameIndex)
    {
        Bitmap nextFrame = CreateBitmap(this.imageObj[frameIndex]);
        Bitmap? previousFrame = this.displayedFrame;
        this.displayedFrame = nextFrame;
        this.pictureBox_image.Image = this.displayedFrame;
        previousFrame?.Dispose();
    }

    private static Bitmap CreateBitmap(SixLabors.ImageSharp.Image<Rgba32> frame)
    {
        byte[] pixels = new byte[frame.Width * frame.Height * 4];
        frame.CopyPixelDataTo(pixels);

        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            (pixels[offset], pixels[offset + 2]) = (pixels[offset + 2], pixels[offset]);
        }

        var bitmap = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb);
        BitmapData bitmapData = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            Marshal.Copy(pixels, 0, bitmapData.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }

        return bitmap;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this.displayedFrame?.Dispose();
        this.displayedFrame = null;
        base.OnFormClosed(e);
    }

    private async void button_llm_Click(object sender, EventArgs e)
    {
        this.button_llm.Enabled = false;
        try
        {
            MidiDto midiDto = await this.RequestMidiDtoAsync();
            this.GeneratedMidiDto = midiDto;
            this.GeneratedMidiFile = midiDto.ToMidiFileData();
            this.Text = $"Image Viewer - {this.GeneratedMidiFile.Tracks.Sum(track => track.Notes.Count)} notes generated";
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
        catch (Exception ex)
        {
            LogCollection.Log(ex);
            this.ShowErrorDialog("MIDI generation failed", ex.Message);
        }
        finally
        {
            this.button_llm.Enabled = true;
        }
    }

    private async Task<MidiDto> RequestMidiDtoAsync()
    {
        string rawUrl = this.textBox_apiUrl.Text.Trim();
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("Enter a valid OpenAI-compatible API URL.");
        }

        string endpoint = rawUrl.TrimEnd('/');
        if (!endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            endpoint += endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? "/chat/completions"
                : "/v1/chat/completions";
        }

        object[] imageParts = this.imageObj.Frames.Select((frame, index) =>
        {
            using var stream = new MemoryStream();
            frame.Save(stream, new PngEncoder());
            string imageDataUrl = $"data:image/png;base64,{Convert.ToBase64String(stream.ToArray())}";
            return (object)new
            {
                type = "image_url",
                image_url = new { url = imageDataUrl, detail = "high" }
            };
        }).ToArray();

        string systemPrompt = "You convert sheet-music page images into MIDI data. Output valid JSON only, with no markdown fences or explanation. " +
            "Use this exact schema: {filePath:string,ticksPerQuarterNote:integer,defaultBpm:number,pitchFrequency:number,tracks:[{index:integer,name:string,lengthTicks:integer,notes:[{noteNumber:integer,channel:integer,velocity:integer,startTick:integer,durationTicks:integer}]}]}. " +
            "noteNumber is MIDI 0-127, channel is 0-15, velocity is 1-127, startTick is non-negative, durationTicks is positive. " +
            "Use 960 ticks per quarter note unless the score clearly requires another resolution. Infer rhythm, rests, key signature, time signature and tempo from the score; use 120 BPM only when tempo is not visible. Return every detected staff as a track.";
        object[] content = [
            new { type = "text", text = "Create a complete MidiDto from all supplied PDF page images. Keep page order and combine the pages into one coherent timeline." },
            .. imageParts
        ];

        var payload = new
        {
            model = "gpt-4o-mini",
            temperature = 0.1,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content }
            },
            response_format = new { type = "json_object" }
        };

        string requestJson = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };

        string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        using HttpResponseMessage response = await HttpClient.SendAsync(request);
        string responseBody = await response.Content.ReadAsStringAsync();
        LogCollection.Log($"Image-to-MIDI LLM response: {responseBody}");
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"The API returned HTTP {(int)response.StatusCode}: {responseBody}");
        }

        using JsonDocument responseDocument = JsonDocument.Parse(responseBody);
        string contentText = responseDocument.RootElement.GetProperty("choices")[0]
            .GetProperty("message").GetProperty("content").GetString()
            ?? throw new InvalidDataException("The API response did not contain assistant content.");
        MidiDto dto = MidiDto.ParseBestEffort(contentText, out bool repaired);
        if (repaired)
        {
            LogCollection.Log("The LLM MIDI JSON required best-effort repair before parsing.");
        }

        return dto;
    }

    private void ShowErrorDialog(string title, string message)
    {
        using var dialog = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(620, 220),
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false
        };
        var textBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill,
            Text = message
        };
        var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 32 };
        dialog.Controls.Add(textBox);
        dialog.Controls.Add(okButton);
        dialog.AcceptButton = okButton;
        dialog.ShowDialog(this);
    }
}