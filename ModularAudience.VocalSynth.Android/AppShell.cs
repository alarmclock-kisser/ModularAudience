using ModularAudience.VocalSynth.Android.Services;

namespace ModularAudience.VocalSynth.Android;

internal sealed class AppShell : Shell
{
    internal AppShell(SoundboardStore store, CoOpAndroidSession session)
    {
        this.Title = "ModularAudience";
        this.FlyoutBehavior = FlyoutBehavior.Flyout;
        this.Items.Add(CreateItem("VocalSynth", new MainPage()));
        this.Items.Add(CreateItem("Co-op", new CoOpPage(session, store)));
        this.Items.Add(CreateItem("Soundboard", new SoundboardPage(store)));
    }

    private static FlyoutItem CreateItem(string title, Page page)
    {
        var item = new FlyoutItem
        {
            Title = title,
            Route = title.Replace("-", string.Empty, StringComparison.Ordinal),
        };
        item.Items.Add(new ShellContent
        {
            Title = title,
            Content = page,
            Route = title.Replace("-", string.Empty, StringComparison.Ordinal),
        });
        return item;
    }
}
