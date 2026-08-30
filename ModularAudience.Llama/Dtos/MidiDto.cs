using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModularAudience.Audio.Midi;

namespace ModularAudience.Llama.Dtos;

public sealed class MidiDto
{
    private static readonly JsonSerializerOptions TolerantJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("ticksPerQuarterNote")]
    public int TicksPerQuarterNote { get; set; } = 960;

    [JsonPropertyName("defaultBpm")]
    public double DefaultBpm { get; set; } = 120.0;

    [JsonPropertyName("pitchFrequency")]
    public double PitchFrequency { get; set; } = 440.0;

    [JsonPropertyName("tracks")]
    public List<MidiTrackDto> Tracks { get; set; } = [];

    public static MidiDto ParseBestEffort(string response, out bool repaired)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(response);
        string extracted = ExtractJsonObject(response);

        try
        {
            MidiDto? dto = JsonSerializer.Deserialize<MidiDto>(extracted, TolerantJsonOptions);
            if (dto != null)
            {
                repaired = false;
                return dto;
            }
        }
        catch (JsonException)
        {
        }

        string repairedJson = RepairJson(extracted);
        try
        {
            using JsonDocument repairedDocument = JsonDocument.Parse(repairedJson);
            MidiDto? dto = JsonSerializer.Deserialize<MidiDto>(repairedJson, TolerantJsonOptions);
            if (dto != null)
            {
                repaired = true;
                return dto;
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The LLM response was not valid or repairable MIDI JSON.", ex);
        }

        throw new InvalidDataException("The LLM returned an empty MidiDto.");
    }

    private static string ExtractJsonObject(string response)
    {
        string text = response.Trim();
        int start = text.IndexOf('{');
        if (start < 0)
        {
            throw new InvalidDataException("The LLM response did not contain a JSON object.");
        }

        int end = text.LastIndexOf('}');
        return (end >= start ? text[start..(end + 1)] : text[start..]).Trim();
    }

    private static string RepairJson(string json)
    {
        string normalized = Regex.Replace(json, @"([{,]\s*)([A-Za-z_][A-Za-z0-9_-]*)\s*:", "$1\"$2\":");
        normalized = normalized.Replace("'", "\"");
        normalized = Regex.Replace(normalized, @",\s*([}\]])", "$1");

        List<char> openBrackets = [];
        StringBuilder output = new();
        bool inString = false;
        bool escaped = false;
        char previousSignificant = '\0';

        for (int i = 0; i < normalized.Length; i++)
        {
            char current = normalized[i];
            if (inString)
            {
                output.Append(current);
                if (escaped)
                {
                    escaped = false;
                }
                else if (current == '\\')
                {
                    escaped = true;
                }
                else if (current == '"')
                {
                    inString = false;
                }
                continue;
            }

            if (current == '"')
            {
                if (previousSignificant is not ('{' or '[' or ',' or ':') && previousSignificant != '\0')
                {
                    output.Append(',');
                }

                inString = true;
                output.Append(current);
                previousSignificant = current;
                continue;
            }

            if (current is '{' or '[')
            {
                openBrackets.Add(current);
            }
            else if (current is '}' or ']')
            {
                if (openBrackets.Count > 0)
                {
                    openBrackets.RemoveAt(openBrackets.Count - 1);
                }
                else
                {
                    continue;
                }
            }

            output.Append(current);
            if (!char.IsWhiteSpace(current))
            {
                previousSignificant = current;
            }
        }

        if (inString)
        {
            output.Append('"');
        }

        for (int i = openBrackets.Count - 1; i >= 0; i--)
        {
            output.Append(openBrackets[i] == '{' ? '}' : ']');
        }

        return output.ToString();
    }

    public MidiFileData ToMidiFileData(string? filePath = null)
    {
        if (this.TicksPerQuarterNote <= 0)
        {
            throw new InvalidDataException("ticksPerQuarterNote must be greater than zero.");
        }

        if (!double.IsFinite(this.DefaultBpm) || this.DefaultBpm <= 0)
        {
            throw new InvalidDataException("defaultBpm must be a finite positive number.");
        }

        if (!double.IsFinite(this.PitchFrequency) || this.PitchFrequency <= 0)
        {
            throw new InvalidDataException("pitchFrequency must be a finite positive number.");
        }

        List<MidiTrackData> tracks = [];
        foreach (MidiTrackDto trackDto in this.Tracks ?? [])
        {
            MidiTrackData track = new()
            {
                Index = trackDto.Index,
                Name = trackDto.Name ?? string.Empty
            };

            foreach (MidiNoteDto noteDto in trackDto.Notes ?? [])
            {
                if (noteDto.NoteNumber is < 0 or > 127)
                {
                    throw new InvalidDataException($"Note number {noteDto.NoteNumber} is outside the MIDI range 0-127.");
                }

                if (noteDto.Channel is < 0 or > 15)
                {
                    throw new InvalidDataException($"MIDI channel {noteDto.Channel} is outside the range 0-15.");
                }

                if (noteDto.Velocity is < 1 or > 127)
                {
                    throw new InvalidDataException($"MIDI velocity {noteDto.Velocity} is outside the range 1-127.");
                }

                if (noteDto.StartTick < 0 || noteDto.DurationTicks <= 0)
                {
                    throw new InvalidDataException("MIDI note timing must use a non-negative startTick and a positive durationTicks.");
                }

                track.Notes.Add(new MidiNoteData
                {
                    NoteNumber = noteDto.NoteNumber,
                    Channel = noteDto.Channel,
                    Velocity = noteDto.Velocity,
                    StartTick = noteDto.StartTick,
                    DurationTicks = noteDto.DurationTicks
                });
            }

            track.ExtendLengthTo(Math.Max(trackDto.LengthTicks, track.Notes.Count == 0
                ? 0
                : track.Notes.Max(note => note.StartTick + note.DurationTicks)));
            tracks.Add(track);
        }

        return MidiFileData.CreateGenerated(
            tracks,
            this.TicksPerQuarterNote,
            this.DefaultBpm,
            filePath ?? this.FilePath,
            this.PitchFrequency);
    }
}

public sealed class MidiTrackDto
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("lengthTicks")]
    public long LengthTicks { get; set; }

    [JsonPropertyName("notes")]
    public List<MidiNoteDto> Notes { get; set; } = [];
}

public sealed class MidiNoteDto
{
    [JsonPropertyName("noteNumber")]
    public int NoteNumber { get; set; }

    [JsonPropertyName("channel")]
    public int Channel { get; set; }

    [JsonPropertyName("velocity")]
    public int Velocity { get; set; } = 100;

    [JsonPropertyName("startTick")]
    public long StartTick { get; set; }

    [JsonPropertyName("durationTicks")]
    public long DurationTicks { get; set; } = 1;
}
