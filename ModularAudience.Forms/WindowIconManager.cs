namespace ModularAudience.Forms;

internal static class WindowIconManager
{
    private const string ResourceName = "ModularAudience.Forms.ModularAudience_favicon.ico";
    private static readonly Lazy<Icon> ApplicationIconValue = new(LoadApplicationIcon);

    internal static Icon SharedIcon => ApplicationIconValue.Value;

    internal static void Apply(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (!ReferenceEquals(form.Icon, SharedIcon))
        {
            form.Icon = SharedIcon;
        }
    }

    private static Icon LoadApplicationIcon()
    {
        using Stream stream = typeof(WindowIconManager).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The embedded ModularAudience window icon is missing.");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }
}
