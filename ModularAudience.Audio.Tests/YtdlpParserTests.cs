using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Ytdlp;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    public sealed class YtdlpParserTests
    {
        [TestMethod]
        public void ParseVideos_FlatPlaylist_PreservesOrderAndCanonicalizesVideoUrls()
        {
            const string json = """
                {"_type":"playlist","entries":[
                  {"_type":"url","id":"dQw4w9WgXcQ","title":"First","url":"dQw4w9WgXcQ"},
                  {"_type":"url","id":"abcdefghijk","channel":"Fallback channel","url":"abcdefghijk"}
                ]}
                """;

            var videos = YtdlpParser.ParseVideos(json);

            Assert.HasCount(2, videos);
            Assert.AreEqual("dQw4w9WgXcQ", videos[0].Id);
            Assert.AreEqual("First", videos[0].Title);
            Assert.AreEqual("https://www.youtube.com/watch?v=dQw4w9WgXcQ", videos[0].Url);
            Assert.AreEqual("abcdefghijk", videos[1].Id);
            Assert.AreEqual("abcdefghijk", videos[1].Title);
            Assert.AreEqual("Fallback channel", videos[1].Uploader);
            Assert.AreEqual("https://www.youtube.com/watch?v=abcdefghijk", videos[1].Url);
        }

        [TestMethod]
        public void ParseVideos_NullAndMalformedEntries_AreSkipped()
        {
            const string json = """
                {"entries":[null,{},42,"unavailable",{"id":"invalid"},
                  {"id":"dQw4w9WgXcQ","title":"Available"},null]}
                """;

            var videos = YtdlpParser.ParseVideos(json);

            Assert.HasCount(1, videos);
            Assert.AreEqual("Available", videos[0].Title);
        }

        [TestMethod]
        public void ParseVideos_MaxResults_CountsValidVideosAndRejectsInvalidLimits()
        {
            const string json = """
                {"entries":[null,{"id":"invalid"},{"id":"dQw4w9WgXcQ"},
                  null,{"id":"abcdefghijk"},{"id":"0123456789_"}]}
                """;

            var videos = YtdlpParser.ParseVideos(json, maxResults: 2);

            Assert.HasCount(2, videos);
            Assert.AreEqual("dQw4w9WgXcQ", videos[0].Id);
            Assert.AreEqual("abcdefghijk", videos[1].Id);
            Assert.HasCount(1, YtdlpParser.ParseVideos(json, maxResults: 1));
            Assert.HasCount(3, YtdlpParser.ParseVideos(json, YtdlpParser.MaximumResults));
            foreach (int limit in new[] { 0, -1, YtdlpParser.MaximumResults + 1 })
            {
                Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                    () => YtdlpParser.ParseVideos(json, limit), $"Invalid limit: {limit}");
            }
        }

        [TestMethod]
        public void ParseVideos_SingleVideo_PreservesMetadataAndPrefersDirectThumbnail()
        {
            const string json = """
                {"id":"dQw4w9WgXcQ","title":"Metadata title","uploader":"Uploader",
                 "channel":"Other channel","duration":"123.5","view_count":"9876543210",
                 "description":"Full description","thumbnail":"https://i.ytimg.com/direct.jpg",
                 "thumbnails":[{"url":"https://i.ytimg.com/fallback.jpg"}]}
                """;

            var videos = YtdlpParser.ParseVideos(json);

            Assert.HasCount(1, videos);
            var video = videos[0];
            Assert.AreEqual("Metadata title", video.Title);
            Assert.AreEqual("Uploader", video.Uploader);
            Assert.AreEqual<double?>(123.5, video.DurationSeconds);
            Assert.AreEqual<long?>(9876543210L, video.ViewCount);
            Assert.AreEqual("Full description", video.Description);
            Assert.AreEqual("https://i.ytimg.com/direct.jpg", video.ThumbnailUrl);
        }

        [TestMethod]
        public void ParseVideos_ThumbnailFallback_SkipsUnsafeAndNullImages()
        {
            const string json = """
                {"id":"dQw4w9WgXcQ","duration":"NaN","view_count":-1,
                 "thumbnail":"http://i.ytimg.com/insecure.jpg","thumbnails":[
                   {"url":"https://i.ytimg.com/small.jpg"},
                   {"url":"https://i.ytimg.com/large.jpg"},null,{},
                   {"url":"http://i.ytimg.com/insecure.jpg"},
                   {"url":"https://user@i.ytimg.com/credentials.jpg"}]}
                """;

            var video = YtdlpParser.ParseVideos(json)[0];

            Assert.AreEqual("https://i.ytimg.com/large.jpg", video.ThumbnailUrl);
            Assert.IsNull(video.DurationSeconds);
            Assert.IsNull(video.ViewCount);
            Assert.IsNull(video.Description);
        }

        [TestMethod]
        public void ValidateYoutubeUrl_AllowedHosts_ProduceCanonicalVideoOrPlaylistUrls()
        {
            const string canonical = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";
            string[] urls =
            [
                "https://youtube.com/watch?v=dQw4w9WgXcQ&feature=share#fragment",
                "https://www.youtube.com/embed/dQw4w9WgXcQ",
                "https://m.youtube.com/shorts/dQw4w9WgXcQ",
                "https://music.youtube.com/live/dQw4w9WgXcQ",
                "https://youtu.be/dQw4w9WgXcQ?t=30"
            ];
            foreach (string url in urls)
            {
                Assert.IsTrue(YtdlpParser.IsYoutubeUrl(url), url);
                Assert.AreEqual(canonical, YtdlpParser.ValidateYoutubeUrl(url).AbsoluteUri, url);
            }
            Assert.AreEqual(canonical + "&list=PL_regression",
                YtdlpParser.ValidateYoutubeUrl("https://youtu.be/dQw4w9WgXcQ?list=PL_regression").AbsoluteUri);
            Assert.AreEqual("https://www.youtube.com/playlist?list=PL_regression",
                YtdlpParser.ValidateYoutubeUrl("https://m.youtube.com/playlist?list=PL_regression&feature=share").AbsoluteUri);
        }

        [TestMethod]
        public void ValidateYoutubeUrl_UntrustedAuthoritiesAndPrivateNetworks_AreRejected()
        {
            string[] urls =
            [
                "https://evil.youtube.com/watch?v=dQw4w9WgXcQ",
                "https://www.youtube.com.evil.example/watch?v=dQw4w9WgXcQ",
                "https://youtube.com@evil.example/watch?v=dQw4w9WgXcQ",
                "https://evil.example@www.youtube.com/watch?v=dQw4w9WgXcQ",
                "http://www.youtube.com/watch?v=dQw4w9WgXcQ",
                "https://www.youtube.com:8443/watch?v=dQw4w9WgXcQ",
                "https://localhost/watch?v=dQw4w9WgXcQ",
                "https://127.0.0.1/watch?v=dQw4w9WgXcQ",
                "https://10.0.0.1/watch?v=dQw4w9WgXcQ",
                "https://172.16.0.1/watch?v=dQw4w9WgXcQ",
                "https://192.168.1.1/watch?v=dQw4w9WgXcQ",
                "https://169.254.169.254/watch?v=dQw4w9WgXcQ",
                "https://[::1]/watch?v=dQw4w9WgXcQ",
                "https://[fd00::1]/watch?v=dQw4w9WgXcQ"
            ];
            foreach (string url in urls)
            {
                Assert.ThrowsExactly<ArgumentException>(() => YtdlpParser.ValidateYoutubeUrl(url), url);
                Assert.IsFalse(YtdlpParser.IsYoutubeUrl(url), url);
            }
        }

        [TestMethod]
        public void ValidateOutputFormat_DownloadOptions_AcceptsAndNormalizesMp3AndWav()
        {
            var defaults = new YtdlpDownloadOptions();
            Assert.AreEqual("wav", defaults.Format);
            Assert.IsFalse(defaults.SaveToDisk);
            Assert.AreEqual(string.Empty, defaults.OutputDirectory);
            foreach (var (input, expected) in new[] { ("mp3", "mp3"), (" WAV ", "wav"), (" MP3 ", "mp3"), ("wav", "wav") })
            {
                var options = new YtdlpDownloadOptions { Format = input };
                Assert.AreEqual(expected, YtdlpParser.ValidateOutputFormat(options.Format), input);
            }
        }

        [TestMethod]
        public void ValidateOutputFormat_UnsupportedFormats_AreRejected()
        {
            foreach (string format in new[] { "", " ", ".mp3", "flac", "mp4", "mp3 --exec command" })
            {
                Assert.ThrowsExactly<ArgumentException>(() => YtdlpParser.ValidateOutputFormat(format), format);
            }
            Assert.ThrowsExactly<ArgumentNullException>(() => YtdlpParser.ValidateOutputFormat(null!));
        }
    }
}
