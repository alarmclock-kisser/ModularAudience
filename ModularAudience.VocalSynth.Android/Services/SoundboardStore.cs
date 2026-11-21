using System.Buffers.Binary;
using System.Text.Json;
using Android.Content;
using Android.OS;
using Android.Provider;
using ModularAudience.Audio.Vocal;
using ModularAudience.CoOp.Protocol;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace ModularAudience.VocalSynth.Android.Services;

internal sealed record SoundboardItem(
    Guid Id,
    string Name,
    string GroupName,
    string FileLocation,
    long FileSize);

internal sealed class SoundboardStore
{
    private const string PublicStoragePreference = "soundboard.publicStorage";
    private readonly string root = Path.Combine(FileSystem.AppDataDirectory, "Soundboard");
    private readonly string metadataPath = Path.Combine(FileSystem.AppDataDirectory, "Soundboard.json");
    private readonly SemaphoreSlim gate = new(1, 1);
    private List<SoundboardItem> items = [];
    private bool loaded;

    internal bool UsesPublicMusicFolder
    {
        get => Preferences.Default.Get(PublicStoragePreference, true);
        set => Preferences.Default.Set(PublicStoragePreference, value);
    }

    internal async Task<IReadOnlyList<SoundboardItem>> GetItemsAsync(CancellationToken cancellationToken = default)
    {
        await this.EnsureLoadedAsync(cancellationToken);
        return this.items.ToArray();
    }

    internal async Task<SoundboardItem> AddSnapshotAsync(
        CoOpAudioSnapshot snapshot,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        float[] samples = ReadSamples(snapshot.SampleData);
        if (snapshot.SampleRate <= 0 || snapshot.Channels is < 1 or > 2
            || samples.Length == 0 || samples.Length % snapshot.Channels != 0)
        {
            throw new InvalidDataException("The received audio has an unsupported sample format.");
        }

        samples = AddShortSampleSafetyPadding(samples, snapshot.SampleRate, snapshot.Channels);
        var audio = new VocalAudioData
        {
            Data = samples,
            SampleRate = snapshot.SampleRate,
            Channels = snapshot.Channels,
            BitDepth = 16,
            Length = samples.LongLength,
            Duration = TimeSpan.FromSeconds(samples.Length / (double)snapshot.Channels / snapshot.SampleRate),
        };
        return await this.AddAudioAsync(
            audio,
            snapshot.Name,
            groupName,
            cancellationToken);
    }

    internal async Task<SoundboardItem> AddAudioAsync(
        VocalAudioData audio,
        string name,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audio);
        await this.EnsureLoadedAsync(cancellationToken);
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            Guid id = Guid.NewGuid();
            (string location, long size) = await this.WriteAudioAsync(id, name, audio, cancellationToken);
            var item = new SoundboardItem(
                id,
                NormalizeName(name),
                NormalizeGroup(groupName),
                location,
                size);
            this.items.Add(item);
            await this.SaveMetadataAsync(cancellationToken);
            return item;
        }
        finally
        {
            this.gate.Release();
        }
    }

    internal async Task<VocalAudioData> ReadAudioAsync(
        SoundboardItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        await using Stream? stream = this.OpenRead(item.FileLocation);
        if (stream == null)
            throw new FileNotFoundException("The saved soundboard WAV could not be opened.");
        WaveAudioData wave = await Task.Run(
            () => WaveAudioReader.Read(stream),
            cancellationToken);
        return new VocalAudioData
        {
            Data = wave.Samples,
            SampleRate = wave.SampleRate,
            Channels = wave.Channels,
            BitDepth = 16,
            Length = wave.Samples.LongLength,
            Duration = TimeSpan.FromSeconds(wave.Samples.Length / (double)wave.Channels / wave.SampleRate),
        };
    }

    internal async Task UpdateAsync(
        SoundboardItem item,
        VocalAudioData audio,
        string name,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(audio);
        await this.EnsureLoadedAsync(cancellationToken);
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            int index = this.items.FindIndex(candidate => candidate.Id == item.Id);
            if (index < 0)
                throw new InvalidOperationException("The soundboard item no longer exists.");
            (string location, long size) = await this.WriteAudioAsync(item.Id, name, audio, cancellationToken);
            SoundboardItem replacement = item with
            {
                Name = NormalizeName(name),
                GroupName = NormalizeGroup(groupName),
                FileLocation = location,
                FileSize = size,
            };
            this.items[index] = replacement;
            await this.SaveMetadataAsync(cancellationToken);
            if (!string.Equals(item.FileLocation, location, StringComparison.Ordinal))
                this.DeleteAudio(item.FileLocation);
        }
        finally
        {
            this.gate.Release();
        }
    }

    internal async Task RenameGroupAsync(
        string existingName,
        string replacementName,
        CancellationToken cancellationToken = default)
    {
        string normalized = NormalizeGroup(replacementName);
        await this.EnsureLoadedAsync(cancellationToken);
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            this.items = this.items.Select(item =>
                string.Equals(item.GroupName, existingName, StringComparison.Ordinal)
                    ? item with { GroupName = normalized }
                    : item).ToList();
            await this.SaveMetadataAsync(cancellationToken);
        }
        finally
        {
            this.gate.Release();
        }
    }

    internal async Task DeleteGroupAsync(string groupName, CancellationToken cancellationToken = default)
    {
        await this.EnsureLoadedAsync(cancellationToken);
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            List<SoundboardItem> removed = this.items
                .Where(item => string.Equals(item.GroupName, groupName, StringComparison.Ordinal))
                .ToList();
            this.items.RemoveAll(item => string.Equals(item.GroupName, groupName, StringComparison.Ordinal));
            await this.SaveMetadataAsync(cancellationToken);
            foreach (SoundboardItem item in removed)
                this.DeleteAudio(item.FileLocation);
        }
        finally
        {
            this.gate.Release();
        }
    }

    internal async Task RenameItemAsync(
        SoundboardItem item,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        await this.EnsureLoadedAsync(cancellationToken);
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            int index = this.items.FindIndex(candidate => candidate.Id == item.Id);
            if (index < 0)
                throw new InvalidOperationException("The soundboard item no longer exists.");
            this.items[index] = this.items[index] with { Name = NormalizeName(newName) };
            await this.SaveMetadataAsync(cancellationToken);
        }
        finally
        {
            this.gate.Release();
        }
    }

    internal async Task MoveItemAsync(
        SoundboardItem item,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        await this.EnsureLoadedAsync(cancellationToken);
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            int index = this.items.FindIndex(candidate => candidate.Id == item.Id);
            if (index < 0)
                throw new InvalidOperationException("The soundboard item no longer exists.");
            this.items[index] = this.items[index] with { GroupName = NormalizeGroup(groupName) };
            await this.SaveMetadataAsync(cancellationToken);
        }
        finally
        {
            this.gate.Release();
        }
    }

    internal async Task DeleteItemAsync(SoundboardItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        await this.EnsureLoadedAsync(cancellationToken);
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            this.items.RemoveAll(candidate => candidate.Id == item.Id);
            await this.SaveMetadataAsync(cancellationToken);
            this.DeleteAudio(item.FileLocation);
        }
        finally
        {
            this.gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (this.loaded)
            return;
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            if (this.loaded)
                return;
            Directory.CreateDirectory(this.root);
            if (File.Exists(this.metadataPath))
            {
                await using FileStream stream = File.OpenRead(this.metadataPath);
                this.items = await JsonSerializer.DeserializeAsync<List<SoundboardItem>>(stream, cancellationToken: cancellationToken)
                    ?? [];
            }
            this.loaded = true;
        }
        finally
        {
            this.gate.Release();
        }
    }

    private async Task<(string Location, long Size)> WriteAudioAsync(
        Guid id,
        string name,
        VocalAudioData audio,
        CancellationToken cancellationToken)
    {
        string temporaryPath = Path.Combine(FileSystem.CacheDirectory, $"{id:N}.wav");
        try
        {
            await WaveFileWriter.WritePcm16Async(temporaryPath, audio, 0, cancellationToken);
            if (this.UsesPublicMusicFolder)
            {
                string? publicLocation = await WritePublicMusicFileAsync(
                    temporaryPath, id, name, cancellationToken);
                if (publicLocation != null)
                {
                    long length = new FileInfo(temporaryPath).Length;
                    return (publicLocation, length);
                }
            }

            string privatePath = Path.Combine(this.root, $"{id:N}.wav");
            File.Copy(temporaryPath, privatePath, true);
            return (privatePath, new FileInfo(privatePath).Length);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private async Task<string?> WritePublicMusicFileAsync(
        string temporaryPath,
        Guid id,
        string name,
        CancellationToken cancellationToken)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
        {
            ContentResolver resolver = global::Android.App.Application.Context.ContentResolver!;
            var values = new ContentValues();
            values.Put(MediaStore.MediaColumns.DisplayName, $"{id:N}-{SafeFileName(name)}.wav");
            values.Put(MediaStore.MediaColumns.MimeType, "audio/wav");
            values.Put(
                MediaStore.Audio.Media.InterfaceConsts.RelativePath,
                global::Android.OS.Environment.DirectoryMusic + "/ModularAudience");
            values.Put(MediaStore.MediaColumns.IsPending, 1);
            global::Android.Net.Uri? uri = resolver.Insert(MediaStore.Audio.Media.ExternalContentUri, values);
            if (uri == null)
                throw new IOException("Android could not create the WAV in Music/ModularAudience.");
            try
            {
                await using Stream output = resolver.OpenOutputStream(uri)
                    ?? throw new IOException("Android could not open the new Music WAV for writing.");
                await using FileStream input = File.OpenRead(temporaryPath);
                await input.CopyToAsync(output, cancellationToken);
                values.Clear();
                values.Put(MediaStore.MediaColumns.IsPending, 0);
                resolver.Update(uri, values, null, null);
                return uri.ToString();
            }
            catch
            {
                resolver.Delete(uri, null, null);
                throw;
            }
        }

        PermissionStatus permission = await Permissions.RequestAsync<Permissions.StorageWrite>();
        if (permission != PermissionStatus.Granted)
            throw new UnauthorizedAccessException("Storage permission is required to save WAV files in Music.");
        string musicDirectory = Path.Combine(
            global::Android.OS.Environment.ExternalStorageDirectory!.AbsolutePath,
            global::Android.OS.Environment.DirectoryMusic!,
            "ModularAudience");
        Directory.CreateDirectory(musicDirectory);
        string destination = Path.Combine(musicDirectory, $"{id:N}-{SafeFileName(name)}.wav");
        File.Copy(temporaryPath, destination, true);
        return destination;
    }

    private Stream? OpenRead(string location)
    {
        if (location.StartsWith("content:", StringComparison.OrdinalIgnoreCase))
        {
            global::Android.Net.Uri uri = global::Android.Net.Uri.Parse(location);
            return global::Android.App.Application.Context.ContentResolver?.OpenInputStream(uri);
        }
        return File.Exists(location) ? File.OpenRead(location) : null;
    }

    private void DeleteAudio(string location)
    {
        if (location.StartsWith("content:", StringComparison.OrdinalIgnoreCase))
        {
            global::Android.Net.Uri uri = global::Android.Net.Uri.Parse(location);
            global::Android.App.Application.Context.ContentResolver?.Delete(uri, null, null);
        }
        else if (File.Exists(location))
        {
            File.Delete(location);
        }
    }

    private async Task SaveMetadataAsync(CancellationToken cancellationToken)
    {
        string temporaryPath = this.metadataPath + ".tmp";
        await using (FileStream stream = File.Create(temporaryPath))
            await JsonSerializer.SerializeAsync(stream, this.items, cancellationToken: cancellationToken);
        File.Move(temporaryPath, this.metadataPath, true);
    }

    private static float[] ReadSamples(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return [];
        if (bytes.Length % sizeof(float) != 0)
            throw new CoOpProtocolException("The audio sample payload is not aligned to 32-bit floats.");

        var samples = new float[bytes.Length / sizeof(float)];
        for (int index = 0; index < samples.Length; index++)
        {
            int offset = index * sizeof(float);
            int bits = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, sizeof(float)));
            samples[index] = BitConverter.Int32BitsToSingle(bits);
        }
        return samples;
    }

    private static float[] AddShortSampleSafetyPadding(float[] samples, int sampleRate, int channels)
    {
        double durationMilliseconds = samples.Length * 1000d / channels / sampleRate;
        if (durationMilliseconds >= 150)
            return samples;

        int paddingFrames = (int)Math.Ceiling(sampleRate * 0.05d);
        int paddingSamples = checked(paddingFrames * channels);
        long paddedLength = samples.LongLength + 2L * paddingSamples;
        if (paddedLength > Array.MaxLength)
            throw new InvalidDataException("The short sample requires too much memory for playback padding.");

        var padded = new float[(int)paddedLength];
        Array.Copy(samples, 0, padded, paddingSamples, samples.Length);
        return padded;
    }

    private static string NormalizeName(string? name)
        => string.IsNullOrWhiteSpace(name) ? "Sound" : name.Trim();

    private static string NormalizeGroup(string? groupName)
        => string.IsNullOrWhiteSpace(groupName) ? "Soundboard" : groupName.Trim();

    private static string SafeFileName(string name)
    {
        string safe = string.Concat(NormalizeName(name).Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        return safe.Length > 60 ? safe[..60] : safe;
    }
}
