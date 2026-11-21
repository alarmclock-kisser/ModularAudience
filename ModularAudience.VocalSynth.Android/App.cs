namespace ModularAudience.VocalSynth.Android;

public sealed class App : Application
{
    public App()
    {
        SoundboardStore store = new();
        CoOpAndroidSession session = new();
        MainPage = new AppShell(store, session);
    }
}
