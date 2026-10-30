using ModularAudience.Ytdlp;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ModularAudience.Forms.Modules.Dialogs;

internal static class YoutubeChromeBridge
{
    internal static string InstallExtensionFiles()
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModularAudience", "chrome-youtube-session");
        Directory.CreateDirectory(directory);
        foreach (string name in new[] { "manifest.json", "background.js", "connect.js" })
        {
            using var resource = typeof(YoutubeChromeBridge).Assembly.GetManifestResourceStream("ModularAudience.BrowserBridge." + name)
                ?? throw new InvalidOperationException("Missing browser extension resource: " + name);
            using var target = File.Create(Path.Combine(directory, name));
            resource.CopyTo(target);
        }
        return directory;
    }

    internal static async Task<YtdlpBrowserSession> ConnectAsync(CancellationToken token)
    {
        // Random loopback port and unguessable, single-use endpoint; cookie values never enter a URL or log.
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        string endpoint = "/ma-youtube/" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant() + "/";
        string origin = $"http://127.0.0.1:{port}";
        using var listener = new HttpListener();
        listener.Prefixes.Add(origin + "/");
        listener.Start();
        OpenChromeSetup(origin + endpoint);
        while (true)
        {
            var context = await listener.GetContextAsync().WaitAsync(token);
            using var response = context.Response;
            if (context.Request.Url?.AbsolutePath != endpoint || context.Request.Headers["Host"] != $"127.0.0.1:{port}")
            { response.StatusCode = 404; continue; }
            response.Headers["Cache-Control"] = "no-store";
            if (context.Request.HttpMethod == "GET")
            {
                response.ContentType = "text/html; charset=utf-8";
                string extensionDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ModularAudience", "chrome-youtube-session");
                byte[] html = Encoding.UTF8.GetBytes("""
                    <!doctype html><meta charset="utf-8"><meta name="modularaudience-session" content="1">
                    <title>ModularAudience YouTube connection</title>
                    <h2>Connect YouTube to ModularAudience</h2>
                    <p id="status">Waiting for the ModularAudience YouTube Session extension.</p>
                    <ol>
                    <li>Switch to the automatically opened <b>chrome://extensions</b> tab.</li>
                    <li>Enable <b>Developer mode / Entwicklermodus</b> (top right).</li>
                    <li>Click <b>Load unpacked / Entpackte Erweiterung laden</b>.</li>
                    <li>In the folder dialog press <b>Ctrl+L, Ctrl+V, Enter</b>, then <b>Select folder / Ordner ausw&auml;hlen</b>.
                    The extension folder has already been copied to your clipboard.</li>
                    <li>Return here and <button onclick="location.reload()">Reload connection / Verbindung neu laden</button>.</li>
                    </ol>
                    <p>Extension folder: <input id="folder" readonly style="width:75%" value="
                    """ + WebUtility.HtmlEncode(extensionDirectory) + """
                    "><button onclick="navigator.clipboard.writeText(document.getElementById('folder').value)">Copy folder / Ordner kopieren</button></p>
                    """);
                await response.OutputStream.WriteAsync(html, token);
                continue;
            }
            if (context.Request.HttpMethod != "POST" || context.Request.Headers["Origin"] != origin)
            { response.StatusCode = 403; continue; }
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = await context.Request.InputStream.ReadAsync(chunk, token)) > 0)
            {
                if (buffer.Length + count > 1024 * 1024) throw new InvalidDataException("Cookie response too large.");
                buffer.Write(chunk, 0, count);
            }
            var session = ParseSession(Encoding.UTF8.GetString(buffer.ToArray()));
            response.StatusCode = 200;
            response.Close();
            return session;
        }
    }

    private static void OpenChromeSetup(string connectionUrl)
    {
        string? chrome = null;
        foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe");
            if (key?.GetValue(null) is string installed && File.Exists(installed.Trim('"')))
            { chrome = installed.Trim('"'); break; }
        }
        chrome ??= new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe")
        }.FirstOrDefault(File.Exists);
        if (chrome is null)
            throw new FileNotFoundException("Google Chrome could not be located. Install Chrome to use the automatic connection.");
        var start = new ProcessStartInfo(chrome) { UseShellExecute = true };
        start.ArgumentList.Add("--new-window");
        start.ArgumentList.Add(connectionUrl);
        start.ArgumentList.Add("chrome://extensions/");
        Process.Start(start);
    }

    internal static YtdlpBrowserSession ParseSession(string json)
    {
        using var document = JsonDocument.Parse(json);
        var text = new StringBuilder("# Netscape HTTP Cookie File\n");
        int accepted = 0;
        foreach (var cookie in document.RootElement.GetProperty("cookies").EnumerateArray())
        {
            string domain = cookie.GetProperty("domain").GetString() ?? "";
            string host = domain.TrimStart('.');
            if (!(host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase))) continue;
            string path = cookie.GetProperty("path").GetString() ?? "/";
            string name = cookie.GetProperty("name").GetString() ?? "";
            string value = cookie.GetProperty("value").GetString() ?? "";
            if (new[] { domain, path, name, value }.Any(field => field.Any(c => c is '\r' or '\n' or '\t' or '\0'))) continue;
            double expiry = cookie.GetProperty("expirationDate").GetDouble();
            if (!double.IsFinite(expiry) || expiry < 0 || expiry > long.MaxValue) continue;
            text.Append(cookie.GetProperty("httpOnly").GetBoolean() ? "#HttpOnly_" : "").Append(domain).Append('\t')
                .Append(domain.StartsWith('.') ? "TRUE" : "FALSE").Append('\t').Append(path).Append('\t')
                .Append(cookie.GetProperty("secure").GetBoolean() ? "TRUE" : "FALSE").Append('\t')
                .Append((long)expiry).Append('\t').Append(name).Append('\t').Append(value).Append('\n');
            accepted++;
        }
        if (accepted == 0) throw new InvalidDataException("No YouTube cookies were received. Open YouTube in this Chrome profile first, then connect again.");
        return new YtdlpBrowserSession { Browser = YtdlpBrowser.Chrome, CookieContents = text.ToString(), ConsentGranted = true };
    }
}
