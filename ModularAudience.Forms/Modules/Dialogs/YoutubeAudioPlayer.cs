using ModularAudience.Ytdlp;
using NAudio.Wave;
using System.Diagnostics;

namespace ModularAudience.Forms.Modules.Dialogs;

internal sealed class YoutubeAudioPlayer : IDisposable
{
    private sealed class Session(
        YoutubeAudioStreamClient http,
        YoutubeAudioStreamResponse response,
        Process process,
        WaveOut output,
        BufferedWaveProvider buffer,
        CancellationTokenSource cancellation)
    {
        public readonly YoutubeAudioStreamClient Http = http;
        public readonly YoutubeAudioStreamResponse Response = response;
        public readonly Process Process = process;
        public readonly WaveOut Output = output;
        public readonly BufferedWaveProvider Buffer = buffer;
        public readonly CancellationTokenSource Cancellation = cancellation;
        public readonly CancellationToken Token = cancellation.Token;
        public readonly TaskCompletionSource FirstPcm = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Completion = Task.CompletedTask;
    }

    private readonly Func<string> ffmpegPath;
    private readonly object gate = new();
    private Session? current;

    public YoutubeAudioPlayer(Func<string> ffmpegPath) =>
        this.ffmpegPath = ffmpegPath ?? throw new ArgumentNullException(nameof(ffmpegPath));

    public async Task<Task> StartAsync(Uri streamUrl, YtdlpNetworkOptions networkOptions, CancellationToken token)
    {
        await this.StopAsync().ConfigureAwait(false);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        YoutubeAudioStreamClient? http = null;
        YoutubeAudioStreamResponse? response = null;
        Process? process = null;
        WaveOut? output = null;
        Session? session = null;
        try
        {
            http = new YoutubeAudioStreamClient(networkOptions);
            response = await http.OpenAsync(streamUrl, cancellation.Token).ConfigureAwait(false);
            process = CreateFfmpegProcess(this.ffmpegPath());
            if (!process.Start()) throw new InvalidOperationException("Unable to start FFmpeg for YouTube audio playback.");
            var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
            var buffer = new BufferedWaveProvider(waveFormat)
            {
                DiscardOnBufferOverflow = false,
                ReadFully = true
            };
            output = new WaveOut { BufferMilliseconds = 100 };
            output.Init(buffer);
            output.Play();
            session = new Session(http, response, process, output, buffer, cancellation);
            lock (this.gate) this.current = session;
            session.Completion = RunSessionAsync(session);

            await Task.WhenAny(session.FirstPcm.Task, session.Completion)
                .WaitAsync(TimeSpan.FromSeconds(20), cancellation.Token).ConfigureAwait(false);
            if (session.Completion.IsCompleted) await session.Completion.ConfigureAwait(false);
            return session.Completion;
        }
        catch
        {
            if (session is not null)
                await StopSessionAsync(session).ConfigureAwait(false);
            else
            {
                cancellation.Cancel();
                if (process is not null) Kill(process);
                output?.Dispose();
                process?.Dispose();
                if (response is not null) await response.DisposeAsync().ConfigureAwait(false);
                http?.Dispose();
                cancellation.Dispose();
            }
            throw;
        }
    }

    public async Task StopAsync()
    {
        Session? session;
        lock (this.gate)
        {
            session = this.current;
            this.current = null;
        }
        if (session is not null) await StopSessionAsync(session).ConfigureAwait(false);
    }

    private static Process CreateFfmpegProcess(string executable)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[]
        {
            "-hide_banner", "-loglevel", "error", "-nostdin", "-i", "pipe:0",
            "-vn", "-sn", "-dn", "-map", "0:a:0", "-f", "f32le", "-acodec", "pcm_f32le",
            "-ar", "44100", "-ac", "2", "pipe:1"
        })
            start.ArgumentList.Add(argument);
        return new Process { StartInfo = start };
    }

    private async Task RunSessionAsync(Session session)
    {
        try
        {
            Task input = CopyInputAsync(session);
            Task output = CopyPcmAsync(session);
            Task<string> errors = session.Process.StandardError.ReadToEndAsync();
            Task exit = session.Process.WaitForExitAsync(session.Token);
            foreach (Task task in new[] { input, output, errors, exit })
                _ = task.ContinueWith(_ => Kill(session.Process), CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.OnlyOnCanceled,
                    TaskScheduler.Default);
            await Task.WhenAll(input, output, errors, exit).ConfigureAwait(false);
            if (session.Process.ExitCode != 0)
            {
                string details = (await errors.ConfigureAwait(false)).Trim();
                if (details.Length > 1200) details = details[..1200];
                throw new InvalidOperationException(
                    details.Length == 0 ? $"FFmpeg exited with code {session.Process.ExitCode}."
                        : $"FFmpeg could not decode the YouTube audio stream: {details}");
            }
        }
        catch (Exception) when (session.Token.IsCancellationRequested)
        {
            throw new OperationCanceledException(session.Token);
        }
        finally
        {
            session.Output.Stop();
            session.Output.Dispose();
            await session.Response.DisposeAsync().ConfigureAwait(false);
            session.Http.Dispose();
            session.Process.Dispose();
            session.Cancellation.Dispose();
            lock (this.gate)
                if (ReferenceEquals(this.current, session)) this.current = null;
        }
    }

    private static async Task CopyInputAsync(Session session)
    {
        await session.Response.Stream.CopyToAsync(session.Process.StandardInput.BaseStream, 81920, session.Token)
            .ConfigureAwait(false);
        await session.Process.StandardInput.BaseStream.FlushAsync(session.Token).ConfigureAwait(false);
        session.Process.StandardInput.Close();
    }

    private static async Task CopyPcmAsync(Session session)
    {
        var bytes = new byte[32768];
        int remainder = 0;
        while (true)
        {
            int read = await session.Process.StandardOutput.BaseStream.ReadAsync(bytes.AsMemory(remainder), session.Token)
                .ConfigureAwait(false);
            if (read == 0) break;
            int available = remainder + read;
            int aligned = available - available % (sizeof(float) * 2);
            if (aligned > 0)
            {
                while (session.Buffer.BufferedBytes > session.Buffer.BufferLength - aligned)
                    await Task.Delay(10, session.Token).ConfigureAwait(false);
                session.Buffer.AddSamples(bytes, 0, aligned);
                session.FirstPcm.TrySetResult();
            }
            remainder = available - aligned;
            if (remainder > 0) Buffer.BlockCopy(bytes, aligned, bytes, 0, remainder);
        }
        if (remainder != 0) throw new InvalidDataException("FFmpeg produced incomplete stereo audio samples.");
    }

    private static async Task StopSessionAsync(Session session)
    {
        try { session.Cancellation.Cancel(); }
        catch (ObjectDisposedException) { }
        Kill(session.Process);
        try { await session.Completion.ConfigureAwait(false); }
        catch (OperationCanceledException) when (session.Token.IsCancellationRequested) { }
        catch (IOException) when (session.Token.IsCancellationRequested) { }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        catch (NotSupportedException) { }
    }

    public void Dispose() => this.StopAsync().GetAwaiter().GetResult();
}
