using ModularAudience.VocalSynth.Android.Services;

namespace ModularAudience.VocalSynth.Android;

public sealed class App : Application
{
    private readonly AppShell shell;

    public App()
    {
        SoundboardStore store = new();
        CoOpAndroidSession session = new();
        this.shell = new AppShell(store, session);
    }

    protected override Window CreateWindow(IActivationState? activationState) => new(this.shell);
}
