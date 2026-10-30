using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Ytdlp;
using System.Text.Json;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class YoutubeRelatedTests
{
    [TestMethod]
    public void Parse_WatchRecommendations_PreservesOrderDeduplicatesAndExcludesAdsAndCurrentVideo()
    {
        using var json = JsonDocument.Parse("""
            {"contents":{"twoColumnWatchNextResults":{
              "results":{"videoRenderer":{"videoId":"current1234"}},
              "secondaryResults":{"secondaryResults":{"results":[
                {"compactVideoRenderer":{"videoId":"abcdefghijk","title":{"runs":[{"text":"Next up"}]},
                  "shortBylineText":{"simpleText":"Channel"},"lengthText":{"simpleText":"3:28"},"viewCountText":{"simpleText":"1,234 views"}}},
                {"compactVideoRenderer":{"videoId":"abcdefghijk"}},
                {"adSlotRenderer":{"compactVideoRenderer":{"videoId":"advert12345"}}},
                {"lockupViewModel":{"contentId":"dQw4w9WgXcQ","contentType":"LOCKUP_CONTENT_TYPE_VIDEO",
                  "metadata":{"lockupMetadataViewModel":{"title":{"content":"Second"}}}}},
                {"continuationItemRenderer":{"continuationEndpoint":{"continuationCommand":{"token":"NEXT"}}}}
              ]}}}}}
            """);
        var videos = YoutubeRelatedParser.Parse(json.RootElement, 30, out var continuation);
        CollectionAssert.AreEqual(new[] { "abcdefghijk", "dQw4w9WgXcQ" }, videos.Select(v => v.Id).ToArray());
        Assert.AreEqual("Next up", videos[0].Title);
        Assert.AreEqual("Channel", videos[0].Uploader);
        Assert.AreEqual<double?>(208, videos[0].DurationSeconds);
        Assert.AreEqual<long?>(1234, videos[0].ViewCount);
        Assert.AreEqual("NEXT", continuation);
    }

    [TestMethod]
    public void Parse_ReloadContinuationAndButtonToken_KeepPaginationAvailable()
    {
        using var json = JsonDocument.Parse("""
            {"onResponseReceivedEndpoints":[{"reloadContinuationItemsCommand":{"continuationItems":[
              {"compactVideoRenderer":{"videoId":"abcdefghijk"}},
              {"continuationItemRenderer":{"button":{"buttonRenderer":{"command":{"continuationCommand":{"token":"MORE"}}}}}}
            ]}}]}
            """);
        Assert.HasCount(1, YoutubeRelatedParser.Parse(json.RootElement, 128, out var continuation));
        Assert.AreEqual("MORE", continuation);
    }

    [TestMethod]
    public void Parse_Continuation_RespectsLimit()
    {
        using var json = JsonDocument.Parse("""
            {"onResponseReceivedEndpoints":[{"appendContinuationItemsAction":{"continuationItems":[
              {"compactVideoRenderer":{"videoId":"abcdefghijk"}},
              {"compactVideoRenderer":{"videoId":"dQw4w9WgXcQ"}}
            ]}}]}
            """);
        Assert.HasCount(1, YoutubeRelatedParser.Parse(json.RootElement, 1, out _));
    }
}
