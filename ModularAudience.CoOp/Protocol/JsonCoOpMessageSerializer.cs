using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModularAudience.CoOp.Protocol;

public sealed class JsonCoOpMessageSerializer : ICoOpMessageSerializer
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public byte[] Serialize(CoOpMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return JsonSerializer.SerializeToUtf8Bytes(message, Options);
    }

    public CoOpMessage Deserialize(ReadOnlySpan<byte> data)
    {
        try
        {
            return JsonSerializer.Deserialize<CoOpMessage>(data, Options)
                ?? throw new CoOpProtocolException("The co-op message payload was empty.");
        }
        catch (JsonException ex)
        {
            throw new CoOpProtocolException("The co-op message payload could not be deserialized.", ex);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            MaxDepth = 64
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
