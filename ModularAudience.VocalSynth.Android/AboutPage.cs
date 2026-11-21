using System.Diagnostics;
using ModularAudience.VocalSynth.Android.Services;
using Microsoft.Maui.ApplicationModel;

namespace ModularAudience.VocalSynth.Android;

public sealed class AboutPage : ContentPage
{
    private readonly AndroidBuildHistoryService historyService = new();
    private readonly Label buildLabel = new() { Text = "Build: -", FontSize = 16 };
    private readonly Label statusLabel = new() { Text = "Loading build history...", LineBreakMode = LineBreakMode.WordWrap };
    private readonly VerticalStackLayout historyList = new() { Spacing = 8 };
    private bool loaded;

    internal AboutPage()
    {
        this.Title = "About";
        this.BackgroundColor = Color.FromArgb("#171717");
        this.Content = BuildContent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!this.loaded)
        {
            this.loaded = true;
            _ = this.LoadHistoryAsync();
        }
    }

    private ScrollView BuildContent()
    {
        VerticalStackLayout content = new()
        {
            Padding = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new Label { Text = "About", FontSize = 26, FontAttributes = FontAttributes.Bold },
                new Label
                {
                    Text = "ModularAudience VocalSynth",
                    FontSize = 16,
                    FontAttributes = FontAttributes.Bold,
                },
                new Label
                {
                    Text = "Offline multilingual speech and MIDI singing for Android",
                    FontSize = 14,
                    TextColor = Colors.Gray,
                },
                new Label
                {
                    Text = $"Version: {AppInfo.Current.VersionString}",
                    FontSize = 14,
                },
                this.buildLabel,
                new BoxView { HeightRequest = 1, Color = Colors.DimGray },
                new Label
                {
                    Text = "Recent APK builds (GitHub)",
                    FontSize = 16,
                    FontAttributes = FontAttributes.Bold,
                },
                this.statusLabel,
                this.historyList,
            },
        };
        return new ScrollView { Content = content };
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            IReadOnlyList<AndroidBuildEntry> entries = await this.historyService.GetRecentApkBuildsAsync();
            this.buildLabel.Text = $"Build: {GetInstalledBuildNumber()}";
            this.statusLabel.Text = entries.Count == 0
                ? "No APK builds found."
                : $"{entries.Count} most recent APK-triggering builds";
            this.historyList.Children.Clear();
            foreach (AndroidBuildEntry entry in entries)
            {
                this.historyList.Children.Add(CreateEntryRow(entry));
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Android build history load failed: {ex}");
            this.statusLabel.Text = $"Could not load build history: {ex.Message}";
        }
    }

    private static int GetInstalledBuildNumber()
    {
        return int.TryParse(AppInfo.Current.BuildString, out int buildNumber)
            ? buildNumber
            : 0;
    }

    private static View CreateEntryRow(AndroidBuildEntry entry)
    {
        string message = string.IsNullOrWhiteSpace(entry.CommitMessage)
            ? "(no commit message)"
            : entry.CommitMessage.Split('\n')[0].Trim();
        string date = entry.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        string? conclusion = entry.Conclusion;
        string status = conclusion == "success" ? "ok" : conclusion ?? "unknown";

        VerticalStackLayout row = new() { Spacing = 2 };
        row.Children.Add(new Label
        {
            Text = $"Build {entry.BuildNumber} · {status} · {date}",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
        });
        row.Children.Add(new Label
        {
            Text = message,
            FontSize = 13,
            TextColor = Colors.LightGray,
            LineBreakMode = LineBreakMode.WordWrap,
        });
        return row;
    }
}
