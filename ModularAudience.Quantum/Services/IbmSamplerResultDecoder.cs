using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ModularAudience.Quantum.Models;
using ModularAudience.Quantum.Serialization;

namespace ModularAudience.Quantum.Services;

internal static class IbmSamplerResultDecoder
{
    internal static IReadOnlyDictionary<string, int> Decode(JsonElement root, QuantumJobRequest request, CancellationToken token)
    {
        try { return DecodeCore(root, request, token); }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or ArgumentException or
            FormatException or OverflowException or InvalidDataException)
        {
            throw new JsonException("IBM Quantum returned an invalid Sampler result.", ex);
        }
    }

    private static IReadOnlyDictionary<string, int> DecodeCore(JsonElement root, QuantumJobRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Some Runtime versions return a JSON-encoded string instead of an object.
        if (root.ValueKind == JsonValueKind.String)
        {
            using var document = JsonDocument.Parse(root.GetString()!);
            if (document.RootElement.ValueKind == JsonValueKind.String) throw new JsonException("Invalid nested Sampler result string.");
            return Decode(document.RootElement, request, token);
        }
        root = Unwrap(root);
        JsonElement register = root;
        if (!Has(root, "counts"))
        {
            JsonElement pubs = Has(root, "results") ? root.GetProperty("results") : root.GetProperty("pub_results");
            if (pubs.ValueKind != JsonValueKind.Array || pubs.GetArrayLength() != 1)
                throw new JsonException("Exactly one Sampler PUB result is required.");
            JsonElement data = Unwrap(Unwrap(pubs[0]).GetProperty("data"));
            if (Has(data, "fields")) data = data.GetProperty("fields");
            if (!Has(data, request.ClassicalRegister))
                throw new JsonException("The Sampler result does not contain the requested classical register.");
            register = Unwrap(data.GetProperty(request.ClassicalRegister));
        }

        if (Has(register, "num_bits") && register.GetProperty("num_bits").GetInt32() != request.QubitCount)
            throw new JsonException("The Sampler register width does not match the request.");
        Dictionary<string, int> counts;
        if (Has(register, "counts"))
        {
            var raw = register.GetProperty("counts").Deserialize(QuantumJsonContext.Default.DictionaryStringInt32)
                ?? throw new JsonException("The Sampler returned null counts.");
            counts = new(StringComparer.Ordinal);
            foreach (var (bits, count) in raw)
            {
                token.ThrowIfCancellationRequested();
                string key = NormalizeBits(bits, request.QubitCount);
                counts[key] = checked(counts.GetValueOrDefault(key) + count);
                if (count < 0) throw new JsonException("Sampler frequencies cannot be negative.");
            }
        }
        else
        {
            counts = new(StringComparer.Ordinal);
            JsonElement samples = Has(register, "samples") ? register.GetProperty("samples") : register.GetProperty("array");
            if (samples.ValueKind == JsonValueKind.Object)
                ReadNumpySamples(Unwrap(samples), counts, request, token);
            else
            {
                if (samples.ValueKind != JsonValueKind.Array) throw new JsonException("Sampler samples must be an array.");
                foreach (JsonElement sample in samples.EnumerateArray())
                {
                    token.ThrowIfCancellationRequested();
                    string bits = sample.ValueKind == JsonValueKind.String ? NormalizeBits(sample.GetString()!, request.QubitCount)
                        : ReadByteSample(sample, request.QubitCount);
                    counts[bits] = counts.GetValueOrDefault(bits) + 1;
                }
            }
        }
        var result = QuantumCounts.Snapshot(counts, request.QubitCount);
        if (result.Sum(pair => (long)pair.Value) != request.Shots)
            throw new JsonException("The Sampler result shot count does not match the request.");
        return result;
    }

    private static JsonElement Unwrap(JsonElement value) => Has(value, "__type__") && Has(value, "__value__")
        ? value.GetProperty("__value__") : value;

    private static bool Has(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out _);

    private static string NormalizeBits(string bits, int width)
    {
        if (bits.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!uint.TryParse(bits.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint value) || value >= (1u << width))
                throw new JsonException("Invalid hexadecimal Sampler measurement.");
            return Convert.ToString(value, 2).PadLeft(width, '0');
        }
        if (bits.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) bits = bits[2..];
        if (bits.Length == 0 || bits.Length > width || bits.Any(bit => bit is not ('0' or '1')))
            throw new JsonException("Invalid binary Sampler measurement.");
        return bits.PadLeft(width, '0');
    }

    private static string ReadByteSample(JsonElement sample, int width)
    {
        if (sample.ValueKind != JsonValueKind.Array || sample.GetArrayLength() != (width + 7) / 8)
            throw new JsonException("Sampler byte samples have an invalid shape.");
        int value = 0;
        foreach (var item in sample.EnumerateArray()) value = (value << 8) | item.GetByte();
        if (value >= (1 << width)) throw new JsonException("Sampler byte samples exceed the register width.");
        return Convert.ToString(value, 2).PadLeft(width, '0');
    }

    private static void ReadNumpySamples(JsonElement encoded, Dictionary<string, int> counts, QuantumJobRequest request, CancellationToken token)
    {
        if (encoded.ValueKind != JsonValueKind.String) throw new JsonException("Unsupported encoded Sampler array.");
        // RuntimeEncoder uses base64(zlib(numpy.save(uint8 array))). Bound decompression before allocating.
        byte[] compressed = Convert.FromBase64String(encoded.GetString()!);
        using var input = new MemoryStream(compressed);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        int bytesPerShot = (request.QubitCount + 7) / 8;
        int maximumLength = checked(request.Shots * bytesPerShot + 4096);
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        int read;
        while ((read = zlib.Read(buffer)) > 0)
        {
            token.ThrowIfCancellationRequested();
            if (output.Length + read > maximumLength) throw new JsonException("The encoded Sampler array exceeds the expected size.");
            output.Write(buffer, 0, read);
        }
        byte[] npy = output.ToArray();
        if (npy.Length < 10 || npy[0] != 0x93 || !npy.AsSpan(1, 5).SequenceEqual("NUMPY"u8))
            throw new JsonException("Invalid NumPy Sampler array.");
        int prefix = npy[6] == 1 ? 10 : 12;
        if (npy.Length < prefix || npy[6] is < 1 or > 3) throw new JsonException("Unsupported NumPy array version.");
        int headerLength = npy[8] | (npy[9] << 8);
        if (prefix == 12) headerLength |= npy[10] << 16 | npy[11] << 24;
        if (headerLength < 0 || headerLength > npy.Length - prefix) throw new JsonException("Invalid NumPy array header.");
        string header = Encoding.ASCII.GetString(npy, prefix, headerLength);
        string shape = $"({request.Shots.ToString(CultureInfo.InvariantCulture)}, {bytesPerShot.ToString(CultureInfo.InvariantCulture)})";
        if (!header.Contains("'descr': '|u1'", StringComparison.Ordinal) || !header.Contains("'fortran_order': False", StringComparison.Ordinal) ||
            !header.Contains("'shape': " + shape, StringComparison.Ordinal) || npy.Length - prefix - headerLength != request.Shots * bytesPerShot)
            throw new JsonException("Only unswept, row-major uint8 Sampler arrays of the requested shape are supported.");
        for (int offset = prefix + headerLength; offset < npy.Length; offset += bytesPerShot)
        {
            token.ThrowIfCancellationRequested();
            int value = 0;
            for (int b = 0; b < bytesPerShot; b++) value = (value << 8) | npy[offset + b];
            if (value >= (1 << request.QubitCount)) throw new JsonException("Sampler samples exceed the register width.");
            string bits = Convert.ToString(value, 2).PadLeft(request.QubitCount, '0');
            counts[bits] = counts.GetValueOrDefault(bits) + 1;
        }
    }
}
