namespace ModularAudience.Audio;

/// <summary>
/// User-facing audio storage. Defaults must never depend on the process working
/// directory, a bundle extraction folder, or temporary co-op transport state.
/// </summary>
public static class AudioStoragePaths
{
    private static readonly object WorkspaceGate = new();
    private static readonly List<WeakReference<AudioCollection>> DefaultCollections = [];
    private static string? workingDirectoryOverride;
    public const string ExportsFolderName = "_Exports";
    public const string RecordingsFolderName = "_Recordings";

    public static string MusicDirectory => ResolveMusicDirectory(
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic, Environment.SpecialFolderOption.DoNotVerify),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify));

    public static string DefaultWorkingDirectory => Path.Combine(MusicDirectory, "ModularAudience");

    public static string WorkingDirectory => Volatile.Read(ref workingDirectoryOverride) ?? DefaultWorkingDirectory;

    public static string ExportDirectory => Path.Combine(WorkingDirectory, ExportsFolderName);

    public static string RecordingsDirectory => Path.Combine(WorkingDirectory, RecordingsFolderName);

    public static string PrepareWorkingDirectory(string directory)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("The audio working directory must be absolute.", nameof(directory));
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, ExportsFolderName));
        Directory.CreateDirectory(Path.Combine(root, RecordingsFolderName));
        return root;
    }

    public static void SetWorkingDirectory(string? directory)
    {
        string root = PrepareWorkingDirectory(directory ?? DefaultWorkingDirectory);
        lock (WorkspaceGate)
        {
            Volatile.Write(ref workingDirectoryOverride, directory is null ? null : root);
            DefaultCollections.RemoveAll(reference => !reference.TryGetTarget(out _));
            foreach (WeakReference<AudioCollection> reference in DefaultCollections)
                if (reference.TryGetTarget(out AudioCollection? collection)) collection.ApplyDefaultWorkingDirectory(root);
        }
    }

    internal static void FollowDefaultWorkingDirectory(AudioCollection collection)
    {
        lock (WorkspaceGate)
        {
            collection.ApplyDefaultWorkingDirectory(WorkingDirectory);
            DefaultCollections.Add(new WeakReference<AudioCollection>(collection));
        }
    }

    internal static void StopFollowingDefaultWorkingDirectory(AudioCollection collection)
    {
        lock (WorkspaceGate)
            DefaultCollections.RemoveAll(reference => !reference.TryGetTarget(out AudioCollection? target) || ReferenceEquals(target, collection));
    }

    internal static string ResolveMusicDirectory(string? musicDirectory, string? userProfile)
    {
        // DoNotVerify above preserves the Windows known-folder location even
        // when Music has not been created yet, including redirected folders.
        if (!string.IsNullOrWhiteSpace(musicDirectory) && Path.IsPathFullyQualified(musicDirectory))
            return Path.GetFullPath(musicDirectory);
        if (!string.IsNullOrWhiteSpace(userProfile) && Path.IsPathFullyQualified(userProfile))
            return Path.Combine(Path.GetFullPath(userProfile), "Music");
        throw new DirectoryNotFoundException("Windows did not provide a Music or user-profile folder. Select an explicit audio working directory instead.");
    }
}
