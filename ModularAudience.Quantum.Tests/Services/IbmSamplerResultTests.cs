using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Quantum.Models;
using ModularAudience.Quantum.Tests.Mocks;

namespace ModularAudience.Quantum.Tests.Services;

[TestClass]
public sealed class IbmSamplerResultTests
{
    [TestMethod]
    [DataRow("{\"counts\":{\"1001\":2,\"0110\":2}}")]
    [DataRow("{\"counts\":{\"0x9\":1,\"0b1001\":1,\"110\":2}}")]
    [DataRow("{\"results\":[{\"data\":{\"c\":{\"num_bits\":4,\"samples\":[\"1001\",\"0110\",\"1001\",\"0110\"]}}}]}")]
    [DataRow("{\"pub_results\":[{\"data\":{\"c\":{\"array\":[[9],[6],[9],[6]]}}}]}")]
    public async Task SupportedCountAndSampleFormats_AreDecoded(string json)
    {
        var counts = await ExecuteResultAsync(json);
        Assert.AreEqual(2, counts["1001"]);
        Assert.AreEqual(2, counts["0110"]);
    }

    [TestMethod]
    public async Task JsonEncodedResultString_IsDecoded()
    {
        const string result = "{\"counts\":{\"1001\":2,\"0110\":2}}";
        var counts = await ExecuteResultAsync("\"" + JsonEncodedText.Encode(result) + "\"");
        Assert.AreEqual(2, counts["1001"]);
    }

    [TestMethod]
    public async Task RuntimeEncoderWrappers_AreDecodedWithoutReflection()
    {
        const string result = """
            {"__type__":"PrimitiveResult","__value__":{"pub_results":[
              {"__type__":"SamplerPubResult","__value__":{"data":
                {"__type__":"DataBin","__value__":{"fields":{"c":
                  {"__type__":"BitArray","__value__":{"num_bits":4,"array":[[9],[6],[9],[6]]}}
                }}}
              }}
            ]}}
            """;
        var counts = await ExecuteResultAsync(result);
        Assert.AreEqual(2, counts["0110"]);
    }

    [TestMethod]
    public async Task MultiByteSamples_UseBigEndianBytesAndLittleEndianBitIndices()
    {
        var counts = await ExecuteResultAsync(RegisterResult("{\"num_bits\":16,\"samples\":[[128,1],[0,6],[128,1],[0,6]]}"), 16);
        Assert.AreEqual(2, counts["1000000000000001"]);
        Assert.AreEqual(2, counts["0000000000000110"]);
    }

    [TestMethod]
    public async Task RequestedRegister_IsSelectedInsteadOfCombiningIndependentRegisters()
    {
        const string json = "{\"results\":[{\"data\":{\"meas\":{\"samples\":[\"1001\",\"0110\",\"1001\",\"0110\"]},\"other\":{\"samples\":[\"0000\"]}}}]}";
        var counts = await ExecuteResultAsync(json, register: "meas");
        Assert.AreEqual(2, counts["1001"]);
        Assert.AreEqual(2, counts.Count);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public async Task CompressedNumpyBitArrays_AreDecoded(int version)
    {
        byte[] npy = Numpy([9, 6, 9, 6], version);
        var counts = await ExecuteResultAsync(EncodedResult(Compress(npy)));
        Assert.AreEqual(2, counts["1001"]);
        Assert.AreEqual(2, counts["0110"]);
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("{}")]
    [DataRow("[]")]
    [DataRow("{\"counts\":null}")]
    [DataRow("{\"counts\":{\"1001\":3}}")]
    [DataRow("{\"counts\":{\"1001\":-1,\"0110\":5}}")]
    [DataRow("{\"counts\":{\"0x100\":4}}")]
    [DataRow("{\"counts\":{\"0xno\":4}}")]
    [DataRow("{\"counts\":{\"\":4}}")]
    [DataRow("{\"counts\":{\"1221\":4}}")]
    [DataRow("{\"counts\":{\"10001\":4}}")]
    [DataRow("{\"results\":[]}")]
    [DataRow("{\"results\":[{},{}]}")]
    [DataRow("{\"results\":[{\"data\":{}}]}")]
    [DataRow("\"\\\"nested\\\"\"")]
    public async Task MalformedHistogramsAndEnvelopes_AreRejected(string json) =>
        await Assert.ThrowsExactlyAsync<JsonException>(() => ExecuteResultAsync(json));

    [TestMethod]
    [DataRow("{\"num_bits\":5,\"samples\":[\"1001\",\"0110\",\"1001\",\"0110\"]}")]
    [DataRow("{\"samples\":null}")]
    [DataRow("{\"samples\":{}}")]
    [DataRow("{\"samples\":[[16],[6],[9],[6]]}")]
    [DataRow("{\"samples\":[[256],[6],[9],[6]]}")]
    [DataRow("{\"samples\":[[0,9],[6],[9],[6]]}")]
    [DataRow("{\"samples\":[9,6,9,6]}")]
    [DataRow("{\"array\":{\"__type__\":\"ndarray\",\"__value__\":\"not-base64\"}}")]
    public async Task InvalidRegisterWidthsAndArrays_AreRejected(string register) =>
        await Assert.ThrowsExactlyAsync<JsonException>(() => ExecuteResultAsync(RegisterResult(register)));

    [TestMethod]
    public async Task InvalidNumpyData_IsRejectedAndDecompressionIsBounded()
    {
        byte[] invalidVersion = Numpy([9, 6, 9, 6]); invalidVersion[6] = 4;
        byte[] invalidHeader = Numpy([9, 6, 9, 6]); invalidHeader[8] = 255; invalidHeader[9] = 255;
        byte[] negativeHeader = Numpy([9, 6, 9, 6], 2); Array.Fill(negativeHeader, (byte)255, 8, 4);
        byte[][] malformed =
        [
            [], new byte[10], new byte[] { 0x93, (byte)'N', (byte)'U', (byte)'M', (byte)'P', (byte)'Y', 2, 0, 0, 0 },
            invalidVersion, invalidHeader, negativeHeader,
            Numpy([9, 6, 9, 6], descriptor: "|i1"), Numpy([9, 6, 9, 6], fortran: true),
            Numpy([9, 6, 9, 6], shots: 5), Numpy([9, 6]), Numpy([25, 6, 9, 6]), new byte[20_000]
        ];
        foreach (byte[] bytes in malformed)
            await Assert.ThrowsExactlyAsync<JsonException>(() => ExecuteResultAsync(EncodedResult(Compress(bytes))));
        await Assert.ThrowsExactlyAsync<JsonException>(() => ExecuteResultAsync(EncodedResult(Convert.ToBase64String([1, 2, 3]))));
    }

    private static async Task<IReadOnlyDictionary<string, int>> ExecuteResultAsync(string json, int qubits = 4, string register = "c")
    {
        using var fixture = new IbmClientFixture(request => request.Method == HttpMethod.Post
            ? MockHttpMessageHandler.Json("{\"id\":\"job-1\",\"status\":\"Completed\"}") : MockHttpMessageHandler.Json(json));
        QuantumJobRequest input = IbmClientFixture.Request(4, qubits) with { ClassicalRegister = register };
        return (await fixture.Client.ExecuteAsync(input, CancellationToken.None)).Counts;
    }

    private static string RegisterResult(string register) => "{\"results\":[{\"data\":{\"c\":" + register + "}}]}";
    private static string EncodedResult(string encoded) => RegisterResult("{\"num_bits\":4,\"array\":{\"__type__\":\"ndarray\",\"__value__\":\"" + encoded + "\"}}");

    private static byte[] Numpy(byte[] samples, int version = 1, string descriptor = "|u1", bool fortran = false, int shots = 4)
    {
        string header = $"{{'descr': '{descriptor}', 'fortran_order': {(fortran ? "True" : "False")}, 'shape': ({shots}, 1), }}\n";
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write((byte)0x93); writer.Write("NUMPY"u8); writer.Write((byte)version); writer.Write((byte)0);
        if (version == 1) writer.Write((ushort)header.Length); else writer.Write(header.Length);
        writer.Write(Encoding.ASCII.GetBytes(header)); writer.Write(samples);
        return stream.ToArray();
    }

    private static string Compress(byte[] bytes)
    {
        using var stream = new MemoryStream();
        using (var zlib = new ZLibStream(stream, CompressionMode.Compress, leaveOpen: true)) zlib.Write(bytes);
        return Convert.ToBase64String(stream.ToArray());
    }
}
