using System.Text;

namespace ModularAudience.Audio.Vocal;

/// <summary>
/// Tunable boundaries for turning a live LLM token stream into speakable chunks.
/// </summary>
public sealed class VocalStreamChunkingSettings
{
    /// <summary>Minimum number of words before a comma/semicolon/colon may end a chunk.</summary>
    public int MinWords { get; set; } = 3;

    /// <summary>Hard upper limit of words in one chunk.</summary>
    public int MaxWords { get; set; } = 14;

    public bool BreakOnComma { get; set; } = true;
    public bool BreakOnColon { get; set; } = true;
    public bool BreakOnSentenceEnd { get; set; } = true;

    public void Normalize()
    {
        MinWords = Math.Clamp(MinWords, 1, 100);
        MaxWords = Math.Clamp(MaxWords, MinWords, 500);
    }

    public VocalStreamChunkingSettings Clone() => new()
    {
        MinWords = MinWords,
        MaxWords = MaxWords,
        BreakOnComma = BreakOnComma,
        BreakOnColon = BreakOnColon,
        BreakOnSentenceEnd = BreakOnSentenceEnd,
    };
}

/// <summary>
/// Stateful text buffer that converts arbitrary token-sized deltas into
/// linguistically coherent chunks. Pure logic - no I/O, no threads - so it can
/// be unit tested without a running llama-server.
/// </summary>
public sealed class VocalStreamChunker
{
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "dr", "mr", "mrs", "ms", "prof", "sr", "jr", "st", "vs", "etc",
        "e.g", "i.e", "z.b", "bzw", "ca", "no", "fig", "inc", "ltd", "co",
        "u.s", "a.m", "p.m", "approx", "min", "max", "bsp", "d.h", "s.a", "vgl",
    };

    private static readonly char[] Closing = ['"', '\'', '”', '’', ')', ']', '}'];

    private readonly StringBuilder buffer = new();
    private readonly VocalStreamChunkingSettings settings;

    public VocalStreamChunker(VocalStreamChunkingSettings? settings = null)
    {
        this.settings = (settings ?? new()).Clone();
        this.settings.Normalize();
    }

    public bool HasBufferedText => buffer.Length > 0;

    public string BufferedText => buffer.ToString();

    /// <summary>Adds a token delta and returns every chunk that is complete now.</summary>
    public IReadOnlyList<string> Append(string? delta)
    {
        if (string.IsNullOrEmpty(delta))
        {
            return [];
        }

        buffer.Append(delta);
        return Extract();
    }

    /// <summary>
    /// Ends the current turn: returns all complete chunks plus the remaining tail.
    /// </summary>
    public IReadOnlyList<string> Flush()
    {
        List<string> result = Extract();
        if (buffer.Length > 0)
        {
            string tail = buffer.ToString().Trim();
            buffer.Clear();
            if (tail.Length > 0)
            {
                result.Add(tail);
            }
        }

        return result;
    }

    public void Reset() => buffer.Clear();

    private List<string> Extract()
    {
        List<string> result = [];
        while (buffer.Length > 0)
        {
            while (buffer.Length > 0 && char.IsWhiteSpace(buffer[0]))
            {
                buffer.Remove(0, 1);
            }

            if (buffer.Length == 0)
            {
                break;
            }

            string text = buffer.ToString();
            int cut = FindCut(text);
            if (cut <= 0)
            {
                break;
            }

            string chunk = text[..cut].Trim();
            buffer.Remove(0, cut);
            if (chunk.Length > 0)
            {
                result.Add(chunk);
            }
        }

        return result;
    }

    private int FindCut(string text)
    {
        int forced = FindMaxWordCut(text);
        int words = 0;
        bool inWord = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            bool whitespace = char.IsWhiteSpace(c);
            if (!whitespace && !inWord)
            {
                words++;
                inWord = true;
            }
            else if (whitespace && inWord)
            {
                inWord = false;
            }

            int candidate = -1;
            if (c == '\n' || c == '\r')
            {
                candidate = i + 1;
            }
            else if (c == '.' || c == '!' || c == '?' || c == '…')
            {
                if (!settings.BreakOnSentenceEnd)
                {
                    // no sentence-end boundaries
                }
                else if (c == '.' && IsProtectedDot(text, i))
                {
                    // abbreviation, decimal number, initial or URL
                }
                else if (c == '.' && i + 2 < text.Length && text[i + 1] == '.' && text[i + 2] == '.')
                {
                    candidate = i + 3; // ellipsis
                }
                else if (c == '!' || c == '?' || c == '…')
                {
                    candidate = i + 1;
                }
                else if (i + 1 >= text.Length || char.IsWhiteSpace(text[i + 1]) || Array.IndexOf(Closing, text[i + 1]) >= 0)
                {
                    candidate = i + 1;
                }
            }
            else if (c == ',' || c == ';')
            {
                if (settings.BreakOnComma && words >= settings.MinWords)
                {
                    candidate = i + 1;
                }
            }
            else if (c == ':')
            {
                if (settings.BreakOnColon && words >= settings.MinWords)
                {
                    candidate = i + 1;
                }
            }

            if (candidate > 0 && (forced < 0 || candidate <= forced))
            {
                return ConsumeClosers(text, candidate);
            }
        }

        return forced;
    }

    private int FindMaxWordCut(string text)
    {
        int count = 0;
        bool inWord = false;
        for (int i = 0; i < text.Length; i++)
        {
            bool whitespace = char.IsWhiteSpace(text[i]);
            if (!whitespace && !inWord)
            {
                count++;
                inWord = true;
            }
            else if (whitespace && inWord)
            {
                if (count >= settings.MaxWords)
                {
                    return i + 1;
                }

                inWord = false;
            }
        }

        return -1;
    }

    private static bool IsProtectedDot(string text, int index)
    {
        if (index + 1 < text.Length && char.IsDigit(text[index + 1]))
        {
            return true; // decimal number, e.g. 1.5
        }

        if (index + 1 < text.Length && text[index + 1] == '.')
        {
            return false; // ellipsis, handled as a sentence end
        }

        int start = index;
        while (start > 0 && (char.IsLetter(text[start - 1]) || text[start - 1] == '.'))
        {
            start--;
        }

        string token = text[start..index];
        if (token.Length == 1 && char.IsLetter(token[0]))
        {
            return true; // initial, e.g. A.
        }

        if (Abbreviations.Contains(token))
        {
            return true;
        }

        if (index + 1 < text.Length && char.IsLetter(text[index + 1]) && index > 0 && char.IsLetter(text[index - 1]))
        {
            return true; // no whitespace after the period
        }

        int lookbackStart = Math.Max(0, index - 60);
        string prefix = text[lookbackStart..index];
        int urlIndex = prefix.LastIndexOf("://", StringComparison.Ordinal);
        int wwwIndex = prefix.LastIndexOf("www.", StringComparison.OrdinalIgnoreCase);
        int marker = Math.Max(urlIndex, wwwIndex);
        if (marker >= 0 && prefix[marker..].IndexOfAny([' ', '\t', '\n', '\r']) < 0)
        {
            return true; // URL
        }

        return false;
    }

    private static int ConsumeClosers(string text, int position)
    {
        while (position < text.Length && Array.IndexOf(Closing, text[position]) >= 0)
        {
            position++;
        }

        return position;
    }
}
