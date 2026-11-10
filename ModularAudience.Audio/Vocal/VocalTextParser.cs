using System.Text;

namespace ModularAudience.Audio.Vocal;

public static class VocalTextParser
{
    public static IReadOnlyList<VocalToken> Parse(VocalLanguage language, string text, VocalEmphasisSyntax syntax)
    {
        var result = new List<VocalToken>();
        if (string.IsNullOrEmpty(text)) return result;
        ParseSegment(language, text, syntax, VocalEmphasis.Normal, result, 0);
        return result;
    }

    private static void ParseSegment(VocalLanguage language, string text, VocalEmphasisSyntax syntax, VocalEmphasis baseEmphasis, List<VocalToken> output, int depth)
    {
        int i = 0;
        var pending = baseEmphasis;
        int n = text.Length;
        while (i < n)
        {
            // Whitespace handling: spaces -> proportional pause, newlines -> paragraph pause.
            if (char.IsWhiteSpace(text[i]))
            {
                bool hasNewline = false;
                int spaceCount = 0;
                while (i < n && char.IsWhiteSpace(text[i]))
                {
                    if (text[i] == '\n' || text[i] == '\r') hasNewline = true;
                    else spaceCount++;
                    i++;
                }
                // debug removed
                if (hasNewline)
                {
                    output.Add(new VocalPauseToken(350));
                }
                else if (spaceCount > 0)
                {
                    output.Add(new VocalPauseToken(Math.Max(60, spaceCount * 70)));
                }
                continue;
            }

            // Try wrapping markers
            var wrapRule = TryMatchWrapping(syntax, text, i);
            if (wrapRule is not null)
            {
                int markerLen = wrapRule.Marker.Length;
                int close = text.IndexOf(wrapRule.Marker, i + markerLen, StringComparison.Ordinal);
                if (close >= 0)
                {
                    var inner = text.Substring(i + markerLen, close - i - markerLen);
                    var modeEmphasis = ModeToEmphasis(wrapRule.Mode);
                    // For pause mode, treat whole as pause
                    if (wrapRule.Mode == VocalEmphasisMode.Pause)
                    {
                        output.Add(new VocalPauseToken(wrapRule.Value));
                    }
                    else
                    {
                        ParseSegment(language, inner, syntax, Combine(baseEmphasis, modeEmphasis), output, depth + 1);
                    }
                    i = close + markerLen;
                    continue;
                }
            }

            // Try inline markers
            var inlineRule = TryMatchInline(syntax, text, i);
            if (inlineRule is not null)
            {
                if (inlineRule.Mode == VocalEmphasisMode.Pause)
                {
                    output.Add(new VocalPauseToken(inlineRule.Value));
                }
                else
                {
                    pending = Combine(pending, ModeToEmphasis(inlineRule.Mode));
                }
                i += inlineRule.Marker.Length;
                continue;
            }

            // Accumulate a word (until whitespace or marker)
            var sb = new StringBuilder();
            while (i < n && !char.IsWhiteSpace(text[i]) && !IsMarkerStart(syntax, text, i))
            {
                sb.Append(text[i]);
                i++;
            }
            var word = sb.ToString();
            if (!string.IsNullOrWhiteSpace(word))
            {
                bool isQuestion = word.Contains('?');
                bool isExclamation = word.Contains('!');
                var emphasis = pending;
                if (isQuestion) emphasis = Combine(emphasis, VocalEmphasis.PitchUp);
                if (isExclamation) emphasis = Combine(emphasis, VocalEmphasis.Strong);

                // Phonemize word (phonemizer skips punctuation)
                var phonemes = VocalPhonemizer.Phonemize(language, word);
                foreach (var ph in phonemes)
                {
                    output.Add(new VocalPhonemeToken(ph, emphasis));
                }
                pending = baseEmphasis; // reset pending after word
            }

        }
    }

    private static VocalEmphasisRule? TryMatchWrapping(VocalEmphasisSyntax syntax, string text, int index)
    {
        return syntax.Rules.Where(r => r.Wrapping)
            .OrderByDescending(r => r.Marker.Length)
            .FirstOrDefault(r => text.Substring(index).StartsWith(r.Marker, StringComparison.Ordinal));
    }

    private static VocalEmphasisRule? TryMatchInline(VocalEmphasisSyntax syntax, string text, int index)
    {
        return syntax.Rules.Where(r => !r.Wrapping)
            .OrderByDescending(r => r.Marker.Length)
            .FirstOrDefault(r => text.Substring(index).StartsWith(r.Marker, StringComparison.Ordinal));
    }

    private static bool IsMarkerStart(VocalEmphasisSyntax syntax, string text, int index)
    {
        return syntax.Rules.Any(r => text.Substring(index).StartsWith(r.Marker, StringComparison.Ordinal));
    }

    private static VocalEmphasis ModeToEmphasis(VocalEmphasisMode mode) => mode switch
    {
        VocalEmphasisMode.Strong => VocalEmphasis.Strong,
        VocalEmphasisMode.Whisper => VocalEmphasis.Whisper,
        VocalEmphasisMode.Lengthen => VocalEmphasis.Lengthened,
        VocalEmphasisMode.PitchUp => VocalEmphasis.PitchUp,
        VocalEmphasisMode.PitchDown => VocalEmphasis.PitchDown,
        _ => VocalEmphasis.Normal,
    };

    private static VocalEmphasis Combine(VocalEmphasis a, VocalEmphasis b)
    {
        // b overrides if not normal
        return b == VocalEmphasis.Normal ? a : b;
    }
}