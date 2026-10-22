# ModularAudience.Ytdlp

Standalone `net10.0` library with no NuGet, Forms, Audio, shell or logging dependencies. The managed project is platform-neutral; its automatically provisioned native tools require Windows x64. The Forms project integrates it through the nonmodal YouTube Browser. An optional, explicitly authorized browser session is supported for authentication; DRM/access-control bypass is not.

## Browser integration

Create a `YtdlpBinaryManager` and call `EnsureBinariesAsync(progress, cancellationToken)` **once on each browser opening**, then construct `YtdlpClient` with that same manager. This checks yt-dlp, FFmpeg, and the required Deno runtime. There is intentionally no Forms browser or application lifecycle hook in this library. Without that initial call, search/browse/download initialize the tools automatically. Subsequent operations reuse the initialized manager without further GitHub calls; concurrent automatic initialization is serialized and rechecks readiness before requesting releases. Explicit calls to `EnsureBinariesAsync` still check for updates. Cancellation is propagated; installation failures without a verified usable previous version are thrown, except for the optional Deno PATH fallback described below.

Updates live under `Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)/ModularAudience/binaries_ytdlp`. Each tool is installed in an immutable version directory. A staged state file is atomically replaced only after download, hash verification, extraction, and executable checks succeed. Installed executable hashes are rechecked during each update check. Old versions are retained so ongoing jobs are not interrupted; they can be removed manually when no jobs are running. The update gate serializes updates within the process; unique staging/version directories also prevent concurrent application instances from overwriting executables.

Sources:

- `https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest`, asset `yt-dlp.exe`, verified against `SHA2-256SUMS`.
- `https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest`, asset `ffmpeg-master-latest-win64-gpl.zip`, verified against the GitHub SHA256 digest or release checksum manifest. Only `bin/ffmpeg.exe` and `bin/ffprobe.exe` are extracted.
- `https://api.github.com/repos/denoland/deno/releases/latest`, asset `deno-x86_64-pc-windows-msvc.zip`, verified against the GitHub SHA256 digest or its dedicated `.sha256`/`.sha256sum` sidecar. Sidecars support a single raw hash, a standard hash/filename line, and Deno's Windows PowerShell `Algorithm: SHA256` / `Hash:` format. Only `deno.exe` is extracted; it is at the archive root in current releases.

ZIP executable selection accepts the exact filename at the archive root or after a directory separator, rejects traversal/absolute entry paths, and writes only to fixed executable names inside staging. It does not extract arbitrary archive paths.

HTTP redirects must stay on explicitly approved HTTPS GitHub asset hosts. Downloads are streamed to unique staging files with size limits. A failed update emits a warning if installed executable hashes still match; otherwise it fails. GitHub unauthenticated API rate limits and network availability apply. Each dependency update has a 20-minute timeout.

Binary transfer messages identify the actual asset and show downloaded / total decimal MB, throttled to approximately four updates per second. SHA256 verification is a separate phase; a completed transfer alone does not mean installation succeeded.

## Browser metadata and audio quality

Results support ascending/descending header sorting with direction arrows. Duration, view count and upload date sort by their typed values, with unknown values last. `UploadDate` is a nullable `DateOnly`, parsed strictly from yt-dlp's `upload_date` (`yyyyMMdd`). Flat results may omit dates; the browser's **Load full metadata** action calls `GetVideoMetadataAsync` asynchronously for only the selected video. No dates are guessed. Seven-column visibility settings migrate older six-column arrays without changing the original six preferences.

MP3 offers 64/96/128/192/256/320 kbps. WAV offers actual 8/16/24/32-bit PCM via `ExtractAudio+ffmpeg_o:-c:a`: `pcm_u8`, `pcm_s16le`, `pcm_s24le`, `pcm_s32le`. 32-bit WAV is signed integer PCM, not floating point. These settings are independent; legacy MP3 values migrate to the nearest supported bitrate. Increasing depth cannot restore quality absent from the source.

Thumbnail loading prefers YouTube's canonical HTTPS JPEG endpoint instead of flat-metadata WebP/AVIF URLs unsupported by GDI+. A trusted metadata URL is a fallback. The loader checks byte signatures, size and dimensions and reports unsupported encodings explicitly; redirects and untrusted image hosts remain disabled.

## Network routing and connection failures

`YtdlpClient.NetworkOptions` applies to metadata requests; `YtdlpDownloadOptions.Network` captures each download's routing. `YtdlpNetworkOptions.IpVersion` defaults to `Automatic`. `ProxyUrl = null` preserves inherited/default discovery, an empty string explicitly disables proxies, and a nonempty HTTP(S) URL explicitly selects an authorized proxy. Credentials in URLs are rejected. No automatic IP/proxy/security workaround is applied. These settings affect yt-dlp, not the GitHub installer or thumbnail HTTP client.

On connection-refused errors (including Windows 10061), the exception retains stderr and adds redacted process proxy status, effective route arguments and bounded direct DNS/TCP probes against the reported `googlevideo.com` hosts. IPv4/IPv6 results are distinguished. A TCP success is **not** proof that TLS, signed media requests or full downloads work. Direct probes do not test a configured proxy. The diagnosis cannot identify which firewall, gateway, security product or remote server refused an earlier connection. TLS certificate validation and retry counts are unchanged.

Certificate-chain failures and HTTP 503 responses receive distinct explanations. They are not presented as codec problems or successful downloads, and certificate verification is never disabled. Browser settings apply to future operations; already running jobs retain their captured options.

### Bounded retry and assisted recovery

`YtdlpProcessException` retains the exit code, sanitized diagnostics and a `YtdlpFailureKind`. The final `ERROR:` line takes precedence over earlier warnings. HTTP 502/503/504, timeouts and connection resets permit **one additional process attempt**, after a cancellable delay. Each process still has yt-dlp's existing bounded internal retries; this is not an unlimited outer loop. A new process re-extracts metadata/media URLs and can resume the job-local partial download. Conversion, disk publication and RAM import happen only after successful completion.

Reported `Retry-After` seconds and HTTP dates are respected. Waits over 30 seconds or malformed waits are not automatically retried or offered for immediate assisted recovery. HTTP 429, TLS errors, refused connections, browser-cookie failures and access restrictions are not automatically retried by the outer policy.

The Forms browser waits for all active batch jobs and cleanup before showing a single copyable recovery dialog. Only matching, terminal process failures that have not been handed to the audio collection are eligible. Successful imports and cleanup/import failures are never repeated. There is at most one assisted recovery pass per operation; callbacks from earlier passes cannot overwrite current progress. Search/metadata requests use the same recovery dialog. Explicit recovery may select a fresh-URL retry, IPv4 for connection failures, system certificate trust for certificate-chain failures, or an authorized browser session for authentication failures. Options are scoped to this operation and not persisted.

System trust passes `--compat-options no-certifi` to use the runtime's normal system CA trust instead of the bundled certifi CA list. TLS verification remains enabled; no certificate is silently accepted or installed. Browser sessions do not fix certificate errors, refused TCP connections or upstream HTTP 503 responses.

### Browser-session consent and privacy

`YtdlpDownloadOptions.BrowserSession` and `YtdlpClient.MetadataSession` default to null. `YtdlpBrowserSession` supports Edge, Chrome and Firefox, an explicit local profile name, and `ConsentGranted = true`. The dialog resets consent every time. It explains that yt-dlp reads the selected profile's cookie database as the current Windows user and that it can contain sessions for other websites. No profile scanning, cookie access or browser process control occurs merely by opening the dialog.

Only after confirmation is `--cookies-from-browser browser:profile` passed to yt-dlp. Profile paths, containers and credential-bearing arguments are not accepted. No cookie-file export is requested, but yt-dlp can make temporary database copies while reading. Its child TEMP/TMP/TMPDIR are scoped to an owned job subdirectory for browser-session attempts; this scratch is cleaned after process exit/cancellation and cleanup failures emit a warning. Header values for Cookie/Set-Cookie/Authorization/Proxy-Authorization are redacted before diagnostic retention and UI/log callbacks, including long headers crossing read-buffer/tail boundaries. No cookie values enter the managed browser UI; consent/session options are not saved to browser settings. A confirmed recovery operation can include its one bounded automatic process retry. Existing Windows/browser protections remain authoritative: locked databases, DPAPI or app-bound encryption errors terminate the attempt and are not bypassed. No elevation, security-product changes, CAPTCHA solving, geo-bypass, DRM handling or entitlement bypass is added. Authentication success and download availability remain dependent on the authorized account and upstream service.

## JavaScript runtime

The standalone yt-dlp release contains its packaged EJS YouTube challenge solver. Deno is **automatically downloaded and managed** in a verified version directory under `BinaryDirectory`, alongside yt-dlp and FFmpeg. No manual runtime installation or global configuration/PATH changes are required. A failed Deno update keeps the hash-verified previous installation usable. Only if managed installation fails without a usable verified version does the manager look for an existing `deno.exe` in absolute PATH entries and emit a warning; this external runtime is not managed or GitHub-hash-verified by the library. Without either runtime, initialization fails rather than allowing a warning-only, incomplete setup. A loose `BinaryDirectory/deno.exe` is not treated as a verified installation.

The verified managed installation is preferred. The client passes `--no-js-runtimes` followed by `--js-runtimes deno:<absolute executable path>`, so unrelated runtimes are not selected implicitly. `--no-remote-components` remains enabled: the bundled EJS solver is used without remote component downloads. Future upstream YouTube changes can still require a new yt-dlp release, and extraction is not guaranteed for every video.

## Public API

Namespace: `ModularAudience.Ytdlp`.

### YtdlpBinaryManager

- `YtdlpBinaryManager()`
- `string BinaryDirectory { get; }`
- `Task EnsureBinariesAsync(IProgress<string>? progress, CancellationToken cancellationToken)`

### YtdlpClient

- `YtdlpClient(YtdlpBinaryManager? manager = null)`
- `event EventHandler<string>? ProgressChanged`
- `Task<IReadOnlyList<YtdlpVideo>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)`
- `Task<IReadOnlyList<YtdlpVideo>> BrowseAsync(string youtubeUrl, int maxResults, CancellationToken cancellationToken)`
- `Task<YtdlpDownloadResult> DownloadAsync(YtdlpVideo video, YtdlpDownloadOptions options, IProgress<string>? progress, CancellationToken cancellationToken, IProgress<YtdlpDownloadProgress>? downloadProgress = null)`

The status event receives process diagnostics and service messages for all client operations. Downloads also report to the supplied `IProgress<string>`. Event handlers run on the producing thread; marshal to the UI if needed. Use nonthrowing handlers. No logging dependency is introduced. Process stderr is read concurrently with stdout; the error tail is limited to 16 Ki characters and captured stdout to 16 Mi characters. Nonzero exit codes include stderr diagnostics in the thrown exception. Cancellation kills the process tree, including conversion subprocesses.

### YtdlpDownloadProgress and YtdlpProgressParser

`public sealed record YtdlpDownloadProgress` has public init properties:

- `string Status` (default empty string)
- `long DownloadedBytes`
- `long? TotalBytes`
- `bool IsTotalEstimated`
- `double? Percent`
- `double? SpeedBytesPerSecond`
- `TimeSpan? Eta`
- `TimeSpan? Elapsed`

`public static class YtdlpProgressParser` exposes:

- `public const string Prefix = "__MA_PROGRESS__:"`
- `public static bool TryParse(string line, out YtdlpDownloadProgress? progress)`

The parser accepts prefix-marked JSON objects up to 1024 characters, reading only `status`, `downloaded_bytes`, `total_bytes`, `total_bytes_estimate`, `speed`, `eta` and `elapsed`. Numeric JSON values and invariant numeric strings are supported; absent values, JSON null and the string `NA` mean unknown. Byte counts must be integral and fit in a nonnegative Int64; speed and seconds must be finite and nonnegative. Invalid JSON, invalid numeric values and unrepresentable durations return false with null progress. Missing downloaded bytes default to zero. A positive exact total takes precedence over a positive estimate; zero totals are unknown. Percentages derive from bytes, clamped to 0–100; unknown totals yield null except that `finished` implies 100. Missing ETA is calculated only with a known total and positive speed; an unrepresentable calculated ETA remains null.

Downloads request actual yt-dlp progress at one-second intervals, without simulated percentages. The typed callback is per invocation, not a shared event. Marker lines from either stdout or stderr are consumed privately, even when malformed or no typed callback was supplied; they never reach text callbacks or `ProgressChanged`. Captured stdout still includes the final `after_move:filepath` result. Existing four-argument calls remain source-compatible.

`finished` describes a completed network transfer, not completed conversion, import or saving. There is no `Phase` property or conversion percentage. Conversion diagnostics such as `[ExtractAudio]` continue through the text callback/event. Multiple network transfers can produce separate progress sequences. Custom progress handlers must be nonthrowing; use `Progress<T>` created on the UI context to marshal typed notifications when needed.

### YtdlpBatchRunner

`public static class YtdlpBatchRunner` exposes:

- `public const int MaximumParallelism = 8`
- `public static Task RunAsync<T>(IReadOnlyList<T> items, int maxParallelism, Func<T, CancellationToken, Task> processItem, CancellationToken cancellationToken)`

Parallelism must be 1–8. The runner creates at most `min(items.Count, maxParallelism)` asynchronous workers, never a task per item. Workers yield initially and preserve the caller's synchronization context; without a context, normal async scheduling applies. Keep the input list unchanged until the returned task completes. Synchronous CPU work within callbacks still blocks the caller's context.

A worker slot covers the complete callback, including download, cooldown, import and cleanup. Dispose temporary download results inside that callback to bound retained temporary files. UI imports must use a caller-owned serial gate, because multiple asynchronous callbacks can interleave on the UI context. The runner neither imports nor adds cooldowns itself.

Handle expected per-item errors within the callback to continue the batch. An unexpected worker exception cancels peers through a linked token and is rethrown with its original stack after all workers and their awaited cleanup finish. Caller cancellation also waits for active callbacks and prevents starting additional items once observed. Callbacks must honor their supplied token and await their own child work; the runner cannot forcibly stop callbacks that ignore cancellation. A worker failure is preserved rather than replaced by peer cancellation; otherwise caller cancellation retains the caller token.

### YtdlpVideo

Sealed record with public init properties:

- `string Id`, `Title`, `Url`, `Uploader` (default empty strings)
- `double? DurationSeconds`
- `string? ThumbnailUrl`
- `long? ViewCount`
- `DateOnly? UploadDate`
- `string? Description`

Flat playlist/search metadata may omit duration, uploader, thumbnails, views, or description. Missing optional fields remain null; an absent uploader is empty. Returned video URLs are canonical YouTube watch URLs. Invalid/null entries are skipped; malformed JSON is rejected. A download requires a video URL matching `Id`, not a playlist-only URL.

### YtdlpDownloadOptions

Public init properties:

- `string Format = "wav"`: accepts only `mp3` or `wav`, case-insensitively.
- `bool SaveToDisk = false`
- `string OutputDirectory = ""`: required for saved downloads.
- `int Mp3Bitrate = 192`: one of 64/96/128/192/256/320 kbps; relevant to MP3 encoding.
- `int WavBitDepth = 16`: one of 8/16/24/32 bits; 32-bit signed integer PCM.
- `YtdlpNetworkOptions Network`: per-download routing; optional system trust requires explicit selection in the browser.
- `YtdlpBrowserSession? BrowserSession = null`: explicit browser/profile consent for a recovery operation only.
- `TimeSpan? MaxDuration = TimeSpan.FromHours(4)`: positive, at most 24 hours, or null to disable.
- `long? MaxFileSizeBytes = 2147483648`: positive, at most 16 GiB, or null to disable.

Live streams are excluded. Duration matching is enforced by yt-dlp metadata filtering. The file size option is passed to yt-dlp and checked again on the converted file. It is not a hard disk quota: source sizes can be unknown and WAV conversion can temporarily exceed this limit before the completed result is rejected. Use cancellation and adequate free disk space. No hidden quality reduction is performed.

### YtdlpDownloadResult : IDisposable, IAsyncDisposable

- `string FilePath { get; }`: exact completed file path.
- `bool IsTemporary { get; }`
- `void Dispose()` and `ValueTask DisposeAsync()`

Each download converts in a new job directory below the system temporary directory. Temporary results own that directory: keep the result alive while using or tagging the file, then dispose it. Dispose is idempotent after successful cleanup; if a file is still locked, cleanup throws and may be retried after closing it. Async disposal performs the same local directory cleanup synchronously.

Saved results are published through a unique staging file in the destination directory, then renamed without overwrite. Names include a sanitized title, video ID, and random suffix. `FilePath` is already the **final saved path when DownloadAsync returns**; callers can delay before invoking external taggers without reconstructing the path. Disposing a saved result never deletes the saved file. Failed/canceled work cleans its job and partial-save files on a best-effort basis. No finalizer is used; callers must dispose temporary results.

### YtdlpParser

Public helpers suitable for targeted unit tests, without native binaries or network:

- `const int MaximumResults = 200`
- `string ValidateOutputFormat(string format)`
- `void ValidateMaxResults(int maxResults)`
- `bool IsYoutubeUrl(string? url)`
- `Uri ValidateYoutubeUrl(string url)`
- `IReadOnlyList<YtdlpVideo> ParseVideos(string json, int maxResults = 50)`
- `YtdlpVideo? ParseVideo(System.Text.Json.JsonElement entry)`

Result limits are 1–200. Search queries are nonempty, at most 500 characters, and cannot contain control characters. Queries are always prefixed with `ytsearchN:` and passed after `--`, never interpreted as raw switches. Browsing accepts HTTPS video/playlist endpoints on `youtube.com`, `www.youtube.com`, `m.youtube.com`, `music.youtube.com`, and `youtu.be`. Userinfo, non-HTTPS schemes, alternate ports, unrelated endpoints, and foreign hosts are rejected. Accepted URLs are normalized and unrelated query parameters are discarded. Downloaded paths must be direct, nonempty, non-symlink files of the requested format inside the job directory.

## Licensing and scope

All additional native tools are free open-source software. yt-dlp's standalone executable bundles third-party components; BtbN's selected FFmpeg build is GPL, not LGPL-only. Deno is open source. Review the exact upstream release licenses and redistribution/source-notice obligations before bundling or distributing native tools. Download only content you are authorized to access and use. Browser authentication requires explicit consent; there is no login/DRM/access-control bypass or automatic acceptance of restricted content.

Targeted regression tests cover retry bounds/cancellation/server waits, error classification, header redaction, browser-consent/profile validation, recovery-dialog decisions and exclusion of successful/import-cleanup jobs. Tests do not read real browser cookies or require live YouTube access. Runtime compatibility with changing YouTube endpoints and external network restrictions cannot be guaranteed by offline tests.
