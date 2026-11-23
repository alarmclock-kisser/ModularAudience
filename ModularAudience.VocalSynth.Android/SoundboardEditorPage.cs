using ModularAudience.Audio.Vocal;
using ModularAudience.VocalSynth.Android.Services;

namespace ModularAudience.VocalSynth.Android;

internal sealed class SoundboardEditorPage : ContentPage
{
    private readonly SoundboardStore store;
    private readonly SoundboardItem item;
    private readonly AndroidPcmPlayer player = new();
    private readonly Slider trimStart = new() { Minimum = 0, Maximum = 0.99, Value = 0 };
    private readonly Slider trimEnd = new() { Minimum = 0.01, Maximum = 1, Value = 1 };
    private readonly Slider fadeIn = new() { Minimum = 0, Maximum = 1, Value = 0 };
    private readonly Slider fadeOut = new() { Minimum = 0, Maximum = 1, Value = 0 };
    private readonly Slider leadingSilence = new() { Minimum = 0, Maximum = 2000, Value = 0 };
    private readonly Slider trailingSilence = new() { Minimum = 0, Maximum = 2000, Value = 0 };
    private readonly Slider rate = new() { Minimum = 0.5, Maximum = 2, Value = 1 };
    private readonly Picker modePicker = new() { Title = "Edit mode" };
    private readonly Entry nameEntry = new() { Placeholder = "Sample name" };
    private readonly Entry groupEntry = new() { Placeholder = "Collection name" };
    private readonly GraphicsView waveform = new();
    private readonly WaveformDrawable drawable = new();
    private VocalAudioData? source;

    internal SoundboardEditorPage(SoundboardStore store, SoundboardItem item)
    {
        this.store = store;
        this.item = item;
        this.Title = "Edit sample";
        this.BackgroundColor = Color.FromArgb("#171717");
        this.modePicker.ItemsSource = new[]
        {
            "Tempo change (preserve pitch)",
            "Varispeed (tempo and pitch)",
            "Pitch shift (preserve length)",
        };
        this.modePicker.SelectedIndex = 0;
        this.nameEntry.Text = item.Name;
        this.groupEntry.Text = item.GroupName;
        this.waveform.Drawable = this.drawable;
        this.waveform.HeightRequest = 140;
        this.waveform.BackgroundColor = Color.FromArgb("#282828");

        Button previewButton = new() { Text = "Preview" };
        Button saveButton = new() { Text = "Save local edit" };
        previewButton.Clicked += async (_, _) => await this.PreviewAsync();
        saveButton.Clicked += async (_, _) => await this.SaveAsync();

        VerticalStackLayout content = new()
        {
            Padding = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new Label { Text = "Waveform", FontSize = 20, FontAttributes = FontAttributes.Bold },
                this.waveform,
                this.nameEntry,
                this.groupEntry,
                CreateSlider("Trim start", this.trimStart),
                CreateSlider("Trim end", this.trimEnd),
                CreateSlider("Fade in", this.fadeIn),
                CreateSlider("Fade out", this.fadeOut),
                CreateSlider("Leading silence (ms)", this.leadingSilence, "0"),
                CreateSlider("Trailing silence (ms)", this.trailingSilence, "0"),
                this.modePicker,
                CreateSlider("Rate / pitch", this.rate),
                new HorizontalStackLayout { Spacing = 8, Children = { previewButton, saveButton } },
            },
        };
        this.Content = new ScrollView { Content = content };
        this.trimStart.ValueChanged += (_, _) => this.ClampTrim();
        this.trimEnd.ValueChanged += (_, _) => this.ClampTrim();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            this.source = await this.store.ReadAudioAsync(this.item);
            this.drawable.SetSamples(this.source.Data, this.source.Channels);
            this.waveform.Invalidate();
        }
        catch (Exception ex)
        {
            await this.DisplayAlertAsync("Could not load sample", ex.ToString(), "OK");
        }
    }

    private async Task PreviewAsync()
    {
        try
        {
            VocalAudioData edited = this.CreateEditedAudio();
            await this.player.PlayAsync(edited, CancellationToken.None);
        }
        catch (Exception ex)
        {
            await this.DisplayAlertAsync("Preview failed", ex.ToString(), "OK");
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            VocalAudioData edited = this.CreateEditedAudio();
            await this.store.UpdateAsync(
                this.item,
                edited,
                this.nameEntry.Text ?? this.item.Name,
                this.groupEntry.Text ?? this.item.GroupName);
            await this.DisplayAlertAsync("Saved", "The local Soundboard copy was updated. The source collection is unchanged.", "OK");
            await Shell.Current.Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await this.DisplayAlertAsync("Save failed", ex.ToString(), "OK");
        }
    }

    private VocalAudioData CreateEditedAudio()
    {
        if (this.source == null)
        {
            throw new InvalidOperationException("The sample has not finished loading.");
        }

        AudioEditMode mode = this.modePicker.SelectedIndex switch
        {
            1 => AudioEditMode.Varispeed,
            2 => AudioEditMode.PitchShift,
            _ => AudioEditMode.TempoPreserving,
        };
        VocalAudioData edited = BasicAudioEditor.Process(
            this.source,
            this.trimStart.Value,
            this.trimEnd.Value,
            this.fadeIn.Value,
            this.fadeOut.Value,
            mode,
            this.rate.Value);
        return AddSilence(
            edited,
            (int)Math.Round(this.leadingSilence.Value),
            (int)Math.Round(this.trailingSilence.Value));
    }

    private static VocalAudioData AddSilence(
        VocalAudioData source,
        int leadingMilliseconds,
        int trailingMilliseconds)
    {
        if (leadingMilliseconds == 0 && trailingMilliseconds == 0)
        {
            return source;
        }

        int channels = source.Channels;
        int sourceFrames = source.Data.Length / channels;
        int leadingFrames = (int)Math.Round(source.SampleRate * leadingMilliseconds / 1000d);
        int trailingFrames = (int)Math.Round(source.SampleRate * trailingMilliseconds / 1000d);
        int outputFrames = checked(sourceFrames + leadingFrames + trailingFrames);
        float[] samples = new float[checked(outputFrames * channels)];
        Array.Copy(source.Data, 0, samples, leadingFrames * channels, source.Data.Length);
        return new VocalAudioData
        {
            Data = samples,
            SampleRate = source.SampleRate,
            Channels = channels,
            BitDepth = source.BitDepth,
            Length = samples.LongLength,
            Duration = TimeSpan.FromSeconds(outputFrames / (double)source.SampleRate),
        };
    }

    private void ClampTrim()
    {
        if (this.trimStart.Value >= this.trimEnd.Value - 0.01)
        {
            if (this.trimStart.IsFocused)
            {
                this.trimStart.Value = Math.Max(0, this.trimEnd.Value - 0.01);
            }
            else
            {
                this.trimEnd.Value = Math.Min(1, this.trimStart.Value + 0.01);
            }
        }
    }

    private static View CreateSlider(string title, Slider slider, string valueFormat = "0.00")
    {
        Label value = new() { Text = slider.Value.ToString(valueFormat, System.Globalization.CultureInfo.InvariantCulture), WidthRequest = 54 };
        slider.ValueChanged += (_, args) =>
            value.Text = args.NewValue.ToString(valueFormat, System.Globalization.CultureInfo.InvariantCulture);
        return new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                new HorizontalStackLayout
                {
                    Children =
                    {
                        new Label { Text = title, HorizontalOptions = LayoutOptions.FillAndExpand },
                        value,
                    },
                },
                slider,
            },
        };
    }

    protected override void OnDisappearing()
    {
        this.player.Dispose();
        base.OnDisappearing();
    }

    private sealed class WaveformDrawable : IDrawable
    {
        private float[] samples = [];
        private int channels = 1;

        internal void SetSamples(float[] data, int audioChannels)
        {
            this.samples = data;
            this.channels = audioChannels;
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.FillColor = Color.FromArgb("#282828");
            canvas.FillRectangle(dirtyRect);
            if (this.samples.Length == 0)
            {
                return;
            }

            canvas.StrokeColor = Color.FromArgb("#64d8cb");
            canvas.StrokeSize = 1;
            int frames = this.samples.Length / this.channels;
            int columns = Math.Max(1, (int)dirtyRect.Width);
            float middle = dirtyRect.Center.Y;
            float amplitude = dirtyRect.Height * 0.44f;
            for (int column = 0; column < columns; column++)
            {
                int start = column * frames / columns;
                int end = Math.Max(start + 1, (column + 1) * frames / columns);
                float minimum = 0;
                float maximum = 0;
                for (int frame = start; frame < Math.Min(end, frames); frame++)
                {
                    float sample = this.samples[frame * this.channels];
                    minimum = Math.Min(minimum, sample);
                    maximum = Math.Max(maximum, sample);
                }
                canvas.DrawLine(column, middle - maximum * amplitude, column, middle - minimum * amplitude);
            }
        }
    }
}
