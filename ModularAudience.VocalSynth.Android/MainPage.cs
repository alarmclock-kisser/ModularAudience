using System.Text.Json;
using ModularAudience.Audio.Vocal;
using ModularAudience.VocalSynth.Android.Services;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;

namespace ModularAudience.VocalSynth.Android;

public sealed partial class MainPage : ContentPage
{
    private readonly VocalVoiceProfileStore profileStore;
    private readonly VocalEmphasisSyntaxStore emphasisStore;
    private readonly AndroidPcmPlayer player = new();
    private readonly List<VocalCustomSampleMapping> customSamples = [];
    private readonly Dictionary<string, Slider> voiceSliders = [];
    private VocalSynthSettings settings;
    private readonly Picker languagePicker = new() { Title = "Language" };
    private readonly Picker voicePicker = new() { Title = "Voice" };
    private readonly Picker midiTrackPicker = new() { Title = "MIDI track" };
    private readonly Editor textEditor = new()
    {
        AutoSize = EditorAutoSizeOption.TextChanges,
        MinimumHeightRequest = 120,
        Placeholder = "Enter text to synthesize",
    };
    private readonly Editor emphasisEditor = new()
    {
        AutoSize = EditorAutoSizeOption.TextChanges,
        MinimumHeightRequest = 130,
        Placeholder = "Emphasis syntax JSON",
    };
    private readonly Entry profileName = new() { Placeholder = "Profile name" };
    private readonly Entry customSampleIpa = new() { Placeholder = "IPA sequence, for example /tʃ/" };
    private readonly Label customSampleFileLabel = new() { Text = "No custom sample selected." };
    private readonly Label midiFileLabel = new() { Text = "No MIDI loaded." };
    private readonly Label midiTrackInfoLabel = new() { Text = "Load a MIDI file to see track details." };
    private readonly Label syllableLabel = new() { Text = "Syllable preview is off." };
    private readonly Label statusLabel = new() { Text = "Ready.", LineBreakMode = LineBreakMode.WordWrap };
    private readonly ActivityIndicator busyIndicator = new() { IsVisible = false, IsRunning = false };
    private readonly Switch singingSwitch = new();
    private readonly Switch androidSpeechSwitch = new();
    private readonly Switch unsupportedBeepSwitch = new();
    private readonly Switch showSyllablesSwitch = new();
    private readonly Slider speedSlider = new() { Minimum = 0.5, Maximum = 2, Value = 1 };
    private readonly Slider midiOffsetSlider = new() { Minimum = -16, Maximum = 16, Value = 0 };
    private readonly Button renderButton = new() { Text = "Preview" };
    private readonly Button createButton = new() { Text = "Create and share WAV" };
    private readonly Button stopButton = new() { Text = "Stop", IsEnabled = false, IsVisible = false };
    private readonly Button fillSyllablesButton = new() { Text = "Fill missing syllables" };
    private readonly Button saveProfileButton = new() { Text = "Save profile" };
    private readonly Button deleteProfileButton = new() { Text = "Delete profile" };
    private readonly Button saveEmphasisButton = new() { Text = "Save emphasis syntax" };
    private readonly Button addCustomSampleButton = new() { Text = "Add WAV sample mapping" };
    private readonly Button loadMidiButton = new() { Text = "Load MIDI" };
    private readonly Button clearMidiButton = new() { Text = "Clear MIDI" };
    private VocalMidiFileData? midi;
    private VocalAudioData? renderedAudio;
    private CancellationTokenSource? renderCancellation;
    private bool syncing;

    public MainPage()
    {
        Title = "VocalSynth";
        settings = VocalSynthSettings.FromPreset(
            VocalVoicePreset.BuiltIn[2], VocalLanguage.English, "Hello world.");
        string appData = FileSystem.AppDataDirectory;
        profileStore = new VocalVoiceProfileStore(Path.Combine(appData, "Profiles"));
        emphasisStore = new VocalEmphasisSyntaxStore(Path.Combine(appData, "EmphasisSyntax.json"));
        BuildPage();
        LoadInitialSettings();
        WireEvents();
        RefreshVoicePicker("Built-in: " + settings.VoiceName);
    }

    private void BuildPage()
    {
        VerticalStackLayout content = new() { Padding = new Thickness(16), Spacing = 12 };
        content.Children.Add(new Label
        {
            Text = "VocalSynth",
            FontSize = 26,
            FontAttributes = FontAttributes.Bold,
        });
        content.Children.Add(new Label
        {
            Text = "Offline multilingual speech and MIDI singing",
            FontSize = 14,
            TextColor = Colors.Gray,
        });

        content.Children.Add(textEditor);
        Grid actionButtons = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 8,
        };
        actionButtons.Children.Add(renderButton);
        Grid.SetColumn(createButton, 1);
        actionButtons.Children.Add(createButton);
        content.Children.Add(actionButtons);
        content.Children.Add(stopButton);

        content.Children.Add(CreateCollapsibleSection(
            "Language and voice",
            new VerticalStackLayout { Spacing = 8, Children = { languagePicker, voicePicker } },
            isExpanded: true));

        content.Children.Add(CreateCollapsibleSection(
            "Voice settings",
            new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    CreateSliderRow("Speed", speedSlider, "0.00x"),
                    CreateSliderRow("Base pitch (Hz)", AddVoiceSlider("BasePitchHz", 60, 400, 195), "0"),
                    CreateSliderRow("Formant scale", AddVoiceSlider("FormantScale", 0.5, 2, 1), "0.00"),
                    CreateSliderRow("Breathiness", AddVoiceSlider("Breathiness", 0, 1, 0.2), "0.00"),
                    CreateSliderRow("Roughness", AddVoiceSlider("Roughness", 0, 1, 0.08), "0.00"),
                    CreateSliderRow("Vibrato (cents)", AddVoiceSlider("VibratoCents", 0, 40, 10), "0.0"),
                    CreateSliderRow("Vibrato rate (Hz)", AddVoiceSlider("VibratoHz", 0, 15, 5), "0.0"),
                    CreateSliderRow("Voicing", AddVoiceSlider("Voicing", 0, 1, 0.85), "0.00"),
                    CreateSliderRow("Consonant noise", AddVoiceSlider("Noise", 0, 1, 0.5), "0.00"),
                    CreateSwitchRow("Singing voice", singingSwitch),
                    CreateSwitchRow("Android natural speech", androidSpeechSwitch),
                    CreateSwitchRow("Beep for unsupported sounds", unsupportedBeepSwitch),
                },
            }));

        content.Children.Add(CreateCollapsibleSection(
            "MIDI singing",
            new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    new HorizontalStackLayout
                    {
                        Spacing = 8,
                        Children = { loadMidiButton, clearMidiButton },
                    },
                    midiFileLabel,
                    midiTrackPicker,
                    midiTrackInfoLabel,
                    CreateSliderRow("Semitone offset", midiOffsetSlider, "+0.00"),
                    new HorizontalStackLayout
                    {
                        Spacing = 8,
                        Children =
                        {
                            new Label { Text = "Show syllables", VerticalOptions = LayoutOptions.Center },
                            showSyllablesSwitch,
                            fillSyllablesButton,
                        },
                    },
                    syllableLabel,
                },
            }));

        content.Children.Add(CreateCollapsibleSection(
            "Custom samples",
            new VerticalStackLayout
            {
                Spacing = 8,
                Children = { customSampleIpa, addCustomSampleButton, customSampleFileLabel },
            }));

        content.Children.Add(CreateCollapsibleSection(
            "Voice profiles",
            new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    profileName,
                    new HorizontalStackLayout
                    {
                        Spacing = 8,
                        Children = { saveProfileButton, deleteProfileButton },
                    },
                },
            }));

        content.Children.Add(CreateCollapsibleSection(
            "Emphasis syntax JSON",
            new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    new Label
                    {
                        Text = "Edit marker rules used by the phonemizer and renderer.",
                        FontSize = 13,
                        TextColor = Colors.Gray,
                    },
                    emphasisEditor,
                    saveEmphasisButton,
                },
            }));

        content.Children.Add(busyIndicator);
        content.Children.Add(statusLabel);
        Content = new ScrollView { Content = content };
    }

    private static View CreateCollapsibleSection(string title, View sectionContent, bool isExpanded = false)
    {
        Button header = new()
        {
            Text = $"{(isExpanded ? "-" : "+")}  {title}",
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Fill,
            HorizontalTextAlignment = TextAlignment.Start,
            BackgroundColor = Colors.Transparent,
        };
        VerticalStackLayout body = new()
        {
            IsVisible = isExpanded,
            Margin = new Thickness(8, 0, 0, 0),
        };
        body.Children.Add(sectionContent);
        header.Clicked += (_, _) =>
        {
            body.IsVisible = !body.IsVisible;
            header.Text = $"{(body.IsVisible ? "-" : "+")}  {title}";
        };
        return new VerticalStackLayout { Spacing = 4, Children = { header, body } };
    }

    private Slider AddVoiceSlider(string key, double minimum, double maximum, double value)
    {
        Slider slider = new() { Minimum = minimum, Maximum = maximum, Value = value };
        voiceSliders.Add(key, slider);
        return slider;
    }

    private static Label SectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 18,
        FontAttributes = FontAttributes.Bold,
        Margin = new Thickness(0, 10, 0, 0),
    };

    private static View CreateSwitchRow(string label, Switch control) => new HorizontalStackLayout
    {
        Spacing = 10,
        Children =
        {
            new Label { Text = label, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.FillAndExpand },
            control,
        },
    };

    private static View CreateSliderRow(string title, Slider slider, string format)
    {
        Label value = new()
        {
            WidthRequest = 62,
            HorizontalTextAlignment = TextAlignment.End,
            VerticalOptions = LayoutOptions.Center,
            Text = slider.Value.ToString(format, System.Globalization.CultureInfo.InvariantCulture),
        };
        slider.ValueChanged += (_, args) =>
            value.Text = args.NewValue.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
        return new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                new HorizontalStackLayout
                {
                    Children =
                    {
                        new Label { Text = title, HorizontalOptions = LayoutOptions.FillAndExpand, VerticalOptions = LayoutOptions.Center },
                        value,
                    },
                },
                slider,
            },
        };
    }

    private void LoadInitialSettings()
    {
        syncing = true;
        try
        {
            languagePicker.ItemsSource = Enum.GetValues<VocalLanguage>()
                .Select(language => language.DisplayName()).ToList();
            languagePicker.SelectedIndex = (int)settings.Language;
            textEditor.Text = Preferences.Default.Get("vocal.text", settings.Text);
            speedSlider.Value = Math.Clamp(Preferences.Default.Get("vocal.speed", 1.0), 0.5, 2);
            settings.Text = textEditor.Text;
            settings.Speed = speedSlider.Value;
            VocalEmphasisSyntax syntax = emphasisStore.Load();
            emphasisEditor.Text = JsonSerializer.Serialize(syntax, new JsonSerializerOptions { WriteIndented = true });
        }
        finally
        {
            syncing = false;
        }
        RefreshVoicePicker("Built-in: " + settings.VoiceName);
    }

    private void WireEvents()
    {
        languagePicker.SelectedIndexChanged += (_, _) =>
        {
            if (syncing || languagePicker.SelectedIndex < 0) return;
            settings.Language = (VocalLanguage)languagePicker.SelectedIndex;
            PersistSession();
            _ = UpdateSyllablesAsync();
        };
        voicePicker.SelectedIndexChanged += (_, _) => ApplyVoiceSelection();
        speedSlider.ValueChanged += (_, _) =>
        {
            settings.Speed = speedSlider.Value;
            PersistSession();
        };
        singingSwitch.Toggled += (_, args) =>
        {
            settings.Singing = args.Value;
            androidSpeechSwitch.IsEnabled = !args.Value;
            midiTrackPicker.IsEnabled = args.Value && midi is not null;
        };
        unsupportedBeepSwitch.Toggled += (_, args) => settings.BeepForUnsupportedSounds = args.Value;
        androidSpeechSwitch.Toggled += (_, _) =>
        {
            if (androidSpeechSwitch.IsToggled && singingSwitch.IsToggled)
                androidSpeechSwitch.IsToggled = false;
        };
        midiOffsetSlider.ValueChanged += (_, args) =>
            settings.SingingMidiSemitoneOffset = args.NewValue;
        midiTrackPicker.SelectedIndexChanged += (_, _) => ApplySelectedMidiTrack();
        showSyllablesSwitch.Toggled += async (_, _) => await UpdateSyllablesAsync();
        renderButton.Clicked += async (_, _) => await PreviewAsync();
        createButton.Clicked += async (_, _) => await CreateAndShareAsync();
        stopButton.Clicked += (_, _) => StopPlayback();
        loadMidiButton.Clicked += async (_, _) => await LoadMidiAsync();
        clearMidiButton.Clicked += (_, _) => ClearMidi();
        fillSyllablesButton.Clicked += (_, _) => FillMissingSyllables();
        saveProfileButton.Clicked += (_, _) => SaveProfile();
        deleteProfileButton.Clicked += (_, _) => DeleteSelectedProfile();
        saveEmphasisButton.Clicked += (_, _) => SaveEmphasisSyntax();
        addCustomSampleButton.Clicked += async (_, _) => await AddCustomSampleAsync();
        textEditor.TextChanged += (_, _) =>
        {
            settings.Text = textEditor.Text;
            PersistSession();
        };
    }

    private void PersistSession()
    {
        Preferences.Default.Set("vocal.text", textEditor.Text ?? string.Empty);
        Preferences.Default.Set("vocal.speed", speedSlider.Value);
    }

    protected override void OnDisappearing()
    {
        StopPlayback();
        base.OnDisappearing();
    }
}
