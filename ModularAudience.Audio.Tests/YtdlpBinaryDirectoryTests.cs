using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Ytdlp;
using System.Reflection;
using System.Text.Json;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class YtdlpBinaryDirectoryTests
{
    [TestMethod]
    public void CleanupOldVersions_KeepsActiveVersionOtherToolsAndUserFolders()
    {
        string root = Path.Combine(Path.GetTempPath(), "ytdlp-cleanup-" + Guid.NewGuid().ToString("N"));
        string hash = new('a', 64), activeId = new('b', 32), oldId = new('c', 32);
        string active = Path.Combine(root, $"yt-dlp-{hash}-{activeId}");
        string old = Path.Combine(root, $"yt-dlp-{hash}-{oldId}");
        string other = Path.Combine(root, $"ffmpeg-{hash}-{oldId}");
        string user = Path.Combine(root, "yt-dlp-my-backup");
        try
        {
            foreach (string folder in new[] { active, old, other, user }) Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(old, "yt-dlp.exe"), "historical binary");
            var manager = new YtdlpBinaryManager(root);
            var cleanup = typeof(YtdlpBinaryManager).GetMethod("CleanupOldVersions", BindingFlags.Instance | BindingFlags.NonPublic)!;
            cleanup.Invoke(manager, ["yt-dlp", null]);
            Assert.IsTrue(Directory.Exists(old), "Without an activated version, nothing may be deleted.");
            File.WriteAllText(Path.Combine(root, "yt-dlp.installed.json"), JsonSerializer.Serialize(new
            {
                PackageHash = hash, InstallId = activeId, FileHashes = new Dictionary<string, string> { ["yt-dlp.exe"] = hash }
            }));
            cleanup.Invoke(manager, ["yt-dlp", null]);
            Assert.IsFalse(Directory.Exists(old));
            Assert.IsTrue(Directory.Exists(active));
            Assert.IsTrue(Directory.Exists(other));
            Assert.IsTrue(Directory.Exists(user));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void CustomDirectory_ResolvesInstalledToolsAndClearsCachedRuntimeWhenChanged()
    {
        string root = Path.Combine(Path.GetTempPath(), "ytdlp-location-" + Guid.NewGuid().ToString("N"));
        string first = Path.Combine(root, "first");
        string second = Path.Combine(root, "second");
        try
        {
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            string hash = new('a', 64), install = new('b', 32);
            foreach (var tool in new[] { ("yt-dlp", new[] { "yt-dlp.exe" }), ("ffmpeg", new[] { "ffmpeg.exe", "ffprobe.exe" }), ("deno", new[] { "deno.exe" }) })
            {
                string payload = Path.Combine(first, $"{tool.Item1}-{hash}-{install}");
                Directory.CreateDirectory(payload);
                foreach (string file in tool.Item2) File.WriteAllBytes(Path.Combine(payload, file), [77, 90]);
                File.WriteAllText(Path.Combine(first, tool.Item1 + ".installed.json"), JsonSerializer.Serialize(new
                {
                    PackageHash = hash, InstallId = install, FileHashes = tool.Item2.ToDictionary(file => file, _ => hash)
                }));
            }
            var manager = new YtdlpBinaryManager(first);
            Assert.IsTrue(manager.HasInstalledTools);
            string path = (string)typeof(YtdlpBinaryManager).GetProperty("YtdlpPath", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
            Assert.IsTrue(path.StartsWith(first + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            typeof(YtdlpBinaryManager).GetField("verifiedDenoDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(manager, Path.Combine(first, $"deno-{hash}-{install}"));
            manager.SetBinaryDirectory(second);
            Assert.AreEqual(second, manager.BinaryDirectory);
            Assert.IsFalse(manager.HasInstalledTools);
            Assert.IsNull(typeof(YtdlpBinaryManager).GetMethod("FindDeno", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(manager, null));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void BinaryDirectory_RejectsRelativePathsAndSettingsRetainCustomLocation()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new YtdlpBinaryManager("relative-folder"));
        var type = typeof(ModularAudience.Forms.Modules.Dialogs.YoutubeBrowserWindow).Assembly
            .GetType("ModularAudience.Forms.Modules.Dialogs.YoutubeBrowserSettings")!;
        string path = Path.Combine(Path.GetTempPath(), "Persistent YouTube Tools");
        var settings = JsonSerializer.Deserialize(JsonSerializer.Serialize(new { YTDownloaderBinariesPath = path }), type)!;
        type.GetMethod("Normalize", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(settings, null);
        var roundTrip = JsonSerializer.Deserialize(JsonSerializer.Serialize(settings), type)!;
        Assert.AreEqual(path, type.GetProperty("YTDownloaderBinariesPath")!.GetValue(roundTrip));
    }
}
