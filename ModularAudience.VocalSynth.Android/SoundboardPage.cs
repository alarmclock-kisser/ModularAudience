using ModularAudience.Audio.Vocal;
using ModularAudience.VocalSynth.Android.Services;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Storage;

namespace ModularAudience.VocalSynth.Android;

public sealed class SoundboardPage : ContentPage
{
    private readonly SoundboardStore store;
    private readonly AndroidPcmPlayer player = new();
    private readonly VerticalStackLayout groupsLayout = new() { Spacing = 16 };
    private readonly Label summaryLabel = new() { Text = "0 audio(s) · 0 B" };
    private readonly Switch lockSwitch = new();
    private readonly Switch publicStorageSwitch = new();
    private CancellationTokenSource? heldPlaybackCancellation;

    internal SoundboardPage(SoundboardStore store)
    {
        this.store = store;
        this.Title = "Soundboard";
        this.BackgroundColor = Color.FromArgb("#171717");
        this.lockSwitch.IsToggled = Preferences.Default.Get("soundboard.locked", false);
        this.publicStorageSwitch.IsToggled = this.store.UsesPublicMusicFolder;

        Button importButton = new() { Text = "Import WAV" };
        importButton.Clicked += async (_, _) => await this.ImportWavAsync();
        this.lockSwitch.Toggled += (_, args) =>
        {
            Preferences.Default.Set("soundboard.locked", args.Value);
        };
        this.publicStorageSwitch.Toggled += (_, args) =>
        {
            this.store.UsesPublicMusicFolder = args.Value;
        };

        VerticalStackLayout content = new()
        {
            Padding = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new Label { Text = "Soundboard", FontSize = 24, FontAttributes = FontAttributes.Bold },
                CreateSwitchRow("Lock editing · hold to play until released", this.lockSwitch),
                CreateSwitchRow("Save WAV in Music/ModularAudience", this.publicStorageSwitch),
                importButton,
                this.groupsLayout,
                new BoxView { HeightRequest = 1, Color = Colors.DimGray },
                this.summaryLabel,
            },
        };
        this.Content = new ScrollView { Content = content };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await this.ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        IReadOnlyList<SoundboardItem> items = await this.store.GetItemsAsync();
        this.groupsLayout.Children.Clear();
        foreach (IGrouping<string, SoundboardItem> group in items
            .GroupBy(item => item.GroupName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            this.groupsLayout.Children.Add(this.CreateGroupView(group.Key, group.ToList()));
        }
        long totalBytes = items.Sum(item => item.FileSize);
        this.summaryLabel.Text = $"{items.Count} audio(s) saved · {FormatBytes(totalBytes)}";
    }

    private View CreateGroupView(string groupName, IReadOnlyList<SoundboardItem> items)
    {
        Button header = new()
        {
            Text = $"{groupName}  ({items.Count})",
            FontAttributes = FontAttributes.Bold,
            FontSize = 18,
            BackgroundColor = Colors.Transparent,
            TextColor = Colors.White,
            HorizontalOptions = LayoutOptions.Fill,
        };
        AttachLongPress(
            header,
            async () =>
            {
                if (this.lockSwitch.IsToggled)
                    return;
                string action = await this.DisplayActionSheetAsync(
                    $"Collection: {groupName}", "Cancel", null, "Rename collection", "Delete collection");
                if (action == "Rename collection")
                {
                    await this.RenameGroupAsync(groupName);
                }
                else if (action == "Delete collection"
                    && await this.DisplayAlertAsync(
                        "Delete collection",
                        $"Delete all {items.Count} local sample(s) in “{groupName}”? The source collection will not be changed.",
                        "Delete",
                        "Cancel"))
                {
                    await this.store.DeleteGroupAsync(groupName);
                    await this.ReloadAsync();
                }
            },
            () => this.RenameGroupAsync(groupName));

        FlexLayout buttons = new()
        {
            Direction = FlexDirection.Row,
            Wrap = FlexWrap.Wrap,
            JustifyContent = FlexJustify.Start,
            AlignItems = FlexAlignItems.Start,
        };
        foreach (SoundboardItem item in items)
        {
            Button button = new()
            {
                Text = item.Name,
                Margin = new Thickness(3),
                Padding = new Thickness(12, 10),
                BackgroundColor = Color.FromArgb("#315b72"),
                TextColor = Colors.White,
                CornerRadius = 10,
                MinimumWidthRequest = 90,
            };
            AttachLongPress(
                button,
                async () =>
                {
                    if (this.lockSwitch.IsToggled)
                        await this.PlayWhileHeldAsync(item);
                    else
                        await this.ShowItemActionsAsync(item);
                },
                () => this.PlayAsync(item));
            buttons.Children.Add(button);
        }
        return new VerticalStackLayout { Spacing = 6, Children = { header, buttons } };
    }

    private void AttachLongPress(Button button, Func<Task> onLongPress, Func<Task> onTap)
    {
        CancellationTokenSource? pressCancellation = null;
        bool longPressTriggered = false;
        button.Pressed += (_, _) =>
        {
            longPressTriggered = false;
            pressCancellation?.Cancel();
            pressCancellation?.Dispose();
            pressCancellation = new CancellationTokenSource();
            CancellationToken token = pressCancellation.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500, token);
                    if (token.IsCancellationRequested)
                        return;
                    longPressTriggered = true;
                    await MainThread.InvokeOnMainThreadAsync(onLongPress);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                        this.DisplayAlertAsync("Soundboard action failed", ex.ToString(), "OK"));
                }
            });
        };
        button.Released += (_, _) =>
        {
            pressCancellation?.Cancel();
            pressCancellation?.Dispose();
            pressCancellation = null;
            this.heldPlaybackCancellation?.Cancel();
            this.player.Stop();
        };
        button.Clicked += async (_, _) =>
        {
            if (longPressTriggered)
            {
                longPressTriggered = false;
                return;
            }
            try
            {
                await onTap();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                await this.DisplayAlertAsync("Playback failed", ex.ToString(), "OK");
            }
        };
    }

    private async Task ShowItemActionsAsync(SoundboardItem item)
    {
        string action = await this.DisplayActionSheetAsync(
            item.Name, "Cancel", null, "Edit", "Rename", "Move to group", "Delete");
        switch (action)
        {
            case "Edit":
                await Shell.Current.Navigation.PushAsync(new SoundboardEditorPage(this.store, item));
                break;
            case "Rename":
                string? name = await this.DisplayPromptAsync("Rename sample", "Enter a new sample name:", "Save", "Cancel", item.Name);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    await this.store.RenameItemAsync(item, name);
                    await this.ReloadAsync();
                }
                break;
            case "Move to group":
                await this.MoveItemAsync(item);
                break;
            case "Delete":
                if (await this.DisplayAlertAsync("Delete sample", $"Delete “{item.Name}” from this device?", "Delete", "Cancel"))
                {
                    await this.store.DeleteItemAsync(item);
                    await this.ReloadAsync();
                }
                break;
        }
    }

    private async Task RenameGroupAsync(string groupName)
    {
        string? replacement = await this.DisplayPromptAsync(
            "Rename collection", "Enter a new local collection name:", "Save", "Cancel", groupName);
        if (!string.IsNullOrWhiteSpace(replacement))
        {
            await this.store.RenameGroupAsync(groupName, replacement);
            await this.ReloadAsync();
        }
    }

    private async Task MoveItemAsync(SoundboardItem item)
    {
        IReadOnlyList<SoundboardItem> items = await this.store.GetItemsAsync();
        string[] groups = items.Select(candidate => candidate.GroupName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Append("New group...")
            .ToArray();
        string selected = await this.DisplayActionSheetAsync("Move sample to", "Cancel", null, groups);
        if (selected == "New group...")
        {
            selected = await this.DisplayPromptAsync("New group", "Enter a collection name:", "Move", "Cancel") ?? string.Empty;
        }
        if (!string.IsNullOrWhiteSpace(selected) && selected != "Cancel")
        {
            await this.store.MoveItemAsync(item, selected);
            await this.ReloadAsync();
        }
    }

    private async Task ImportWavAsync()
    {
        try
        {
            FileResult? file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select a WAV file",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.Android] = ["audio/wav", "audio/x-wav"],
                }),
            });
            if (file == null)
                return;
            await using Stream input = await file.OpenReadAsync();
            WaveAudioData wave = WaveAudioReader.Read(input);
            var audio = new VocalAudioData
            {
                Data = wave.Samples,
                SampleRate = wave.SampleRate,
                Channels = wave.Channels,
                BitDepth = 16,
                Length = wave.Samples.LongLength,
                Duration = TimeSpan.FromSeconds(wave.Samples.Length / (double)wave.Channels / wave.SampleRate),
            };
            string name = Path.GetFileNameWithoutExtension(file.FileName);
            string group = await this.DisplayPromptAsync("Collection", "Choose a Soundboard group:", "Save", "Cancel", "Soundboard")
                ?? "Soundboard";
            await this.store.AddAudioAsync(audio, name, group);
            await this.ReloadAsync();
        }
        catch (Exception ex)
        {
            await this.DisplayAlertAsync("Import failed", ex.ToString(), "OK");
        }
    }

    private async Task PlayAsync(SoundboardItem item)
    {
        VocalAudioData audio = await this.store.ReadAudioAsync(item);
        await this.player.PlayAsync(audio, CancellationToken.None);
    }

    private async Task PlayWhileHeldAsync(SoundboardItem item)
    {
        CancellationTokenSource cancellation = new();
        this.heldPlaybackCancellation?.Cancel();
        this.heldPlaybackCancellation?.Dispose();
        this.heldPlaybackCancellation = cancellation;
        try
        {
            VocalAudioData audio = await this.store.ReadAudioAsync(item, cancellation.Token);
            while (!cancellation.IsCancellationRequested)
                await this.player.PlayAsync(audio, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
    }

    private static View CreateSwitchRow(string title, Switch control) => new HorizontalStackLayout
    {
        Spacing = 10,
        Children =
        {
            new Label { Text = title, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.FillAndExpand },
            control,
        },
    };

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    protected override void OnDisappearing()
    {
        this.heldPlaybackCancellation?.Cancel();
        this.player.Dispose();
        base.OnDisappearing();
    }
}
