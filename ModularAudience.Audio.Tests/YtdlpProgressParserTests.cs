using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Ytdlp;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class YtdlpProgressParserTests
{
    [TestMethod]
    public void TryParse_ActualBytes_PreservesPercentSpeedEtaAndElapsed()
    {
        var progress = Parse("""
            {"status":"downloading","downloaded_bytes":1024,"total_bytes":4096,
             "total_bytes_estimate":8192,"speed":512,"eta":6,"elapsed":2.5}
            """);

        Assert.AreEqual("downloading", progress.Status);
        Assert.AreEqual(1024L, progress.DownloadedBytes);
        Assert.AreEqual<long?>(4096, progress.TotalBytes);
        Assert.AreEqual<double?>(25, progress.Percent);
        Assert.IsFalse(progress.IsTotalEstimated);
        Assert.AreEqual<double?>(512, progress.SpeedBytesPerSecond);
        Assert.AreEqual<TimeSpan?>(TimeSpan.FromSeconds(6), progress.Eta);
        Assert.AreEqual<TimeSpan?>(TimeSpan.FromSeconds(2.5), progress.Elapsed);
    }

    [TestMethod]
    public void TryParse_EstimatedTotal_ComputesEtaFromRemainingBytesAndSpeed()
    {
        foreach (string actual in new[] { "null", "0", "\"NA\"" })
        {
            var progress = Parse("{\"status\":\"downloading\",\"downloaded_bytes\":\"1024\"," +
                "\"total_bytes\":" + actual + ",\"total_bytes_estimate\":\"4096\"," +
                "\"speed\":\"512\",\"eta\":\"NA\",\"elapsed\":\"2\"}");

            Assert.AreEqual<long?>(4096, progress.TotalBytes, actual);
            Assert.IsTrue(progress.IsTotalEstimated, actual);
            Assert.AreEqual<double?>(25, progress.Percent, actual);
            Assert.AreEqual<TimeSpan?>(TimeSpan.FromSeconds(6), progress.Eta, actual);
            Assert.AreEqual<TimeSpan?>(TimeSpan.FromSeconds(2), progress.Elapsed, actual);
        }
    }

    [TestMethod]
    public void TryParse_UnknownTotals_LeavesPercentAndEtaUnknown()
    {
        foreach (string totals in new[] { "", ",\"total_bytes\":null,\"total_bytes_estimate\":null",
            ",\"total_bytes\":\"NA\",\"total_bytes_estimate\":\"na\"", ",\"total_bytes\":0,\"total_bytes_estimate\":0" })
        {
            var progress = Parse("{\"status\":\"downloading\",\"downloaded_bytes\":100,\"speed\":50" + totals + "}");

            Assert.AreEqual(100L, progress.DownloadedBytes, totals);
            Assert.IsNull(progress.TotalBytes, totals);
            Assert.IsNull(progress.Percent, totals);
            Assert.IsNull(progress.Eta, totals);
            Assert.IsFalse(progress.IsTotalEstimated, totals);
        }
    }

    [TestMethod]
    public void TryParse_MalformedNonFiniteNegativeAndOverflowFields_FailWithoutPartialProgress()
    {
        string?[] invalidLines = [null, "", "[download] 25%", YtdlpProgressParser.Prefix + "{",
            YtdlpProgressParser.Prefix + "[]", YtdlpProgressParser.Prefix + "{}",
            YtdlpProgressParser.Prefix + "{\"status\":42}",
            YtdlpProgressParser.Prefix + "{\"status\":\" \"}",
            YtdlpProgressParser.Prefix + new string(' ', 1025)];
        foreach (string? line in invalidLines)
        {
            Assert.IsFalse(YtdlpProgressParser.TryParse(line!, out var progress), line);
            Assert.IsNull(progress, line);
        }
        foreach (string field in new[] { "downloaded_bytes", "total_bytes", "total_bytes_estimate", "speed", "eta", "elapsed" })
        foreach (string value in new[] { "true", "{}", "[]", "\"garbage\"", "\"NaN\"", "\"Infinity\"", "\"-Infinity\"", "-1", "1e400" })
            AssertInvalidField(field, value);
        foreach (string field in new[] { "downloaded_bytes", "total_bytes", "total_bytes_estimate" })
        foreach (string value in new[] { "1.5", "9223372036854775808" })
            AssertInvalidField(field, value);
        foreach (string field in new[] { "eta", "elapsed" })
            AssertInvalidField(field, "1e20");
    }

    [TestMethod]
    public void TryParse_FinishedWithoutTotals_ReportsOneHundredPercent()
    {
        foreach (string fields in new[] { "", ",\"downloaded_bytes\":512,\"total_bytes\":null",
            ",\"total_bytes\":\"NA\",\"total_bytes_estimate\":\"NA\"" })
        {
            var progress = Parse("{\"status\":\"finished\"" + fields + "}");

            Assert.AreEqual("finished", progress.Status);
            Assert.AreEqual<double?>(100, progress.Percent, fields);
            Assert.IsNull(progress.TotalBytes, fields);
            Assert.IsNull(progress.Eta, fields);
        }
    }

    private static YtdlpDownloadProgress Parse(string json)
    {
        Assert.IsTrue(YtdlpProgressParser.TryParse(YtdlpProgressParser.Prefix + json, out var progress), json);
        Assert.IsNotNull(progress);
        return progress;
    }

    private static void AssertInvalidField(string field, string value)
    {
        string line = YtdlpProgressParser.Prefix + "{\"status\":\"downloading\",\"" + field + "\":" + value + "}";
        Assert.IsFalse(YtdlpProgressParser.TryParse(line, out var progress), field + ": " + value);
        Assert.IsNull(progress, field + ": " + value);
    }
}
