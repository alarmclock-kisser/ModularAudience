using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms.Helpers;
using ModularAudience.Forms.Updates;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class ApplicationBuildInformationTests
{
    [TestMethod]
    [DataRow(127L)]
    [DataRow(128L)]
    public void ReleaseNumber_TakesPrecedenceWithoutReadingFileTimestamp(long releaseNumber)
    {
        string label = WindowMainFormatHelpers.FormatBuildInformation(releaseNumber,
            () => throw new AssertFailedException("A published release must not read its file timestamp."));

        Assert.AreEqual($"Build: {releaseNumber}", label);
    }

    [TestMethod]
    public void UnnumberedLocalBuild_StillDisplaysBuildTimestamp()
    {
        string label = WindowMainFormatHelpers.FormatBuildInformation(null, () => new DateTime(2026, 11, 8, 14, 3, 5));

        Assert.AreEqual("Build: 08/11/2026 - 14:03:05", label);
    }

    [TestMethod]
    public void DisplayUsesRunningAssemblyMetadata_NotCachedInstalledRelease()
    {
        long? current = ApplicationRelease.RunningReleaseNumber();
        DateTime timestamp = new(2026, 11, 8, 14, 3, 5);
        string label = WindowMainFormatHelpers.FormatBuildInformation(current, () => timestamp);

        Assert.AreEqual(current.HasValue ? $"Build: {current.Value}" : "Build: 08/11/2026 - 14:03:05", label);
    }
}
