using System.Text;

namespace ModularAudience.Audio.Vocal;

public static class VocalTextParser
{
    private static readonly HashSet<string> UnstressedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "of", "the", "to", "und", "der", "die", "das", "ein", "eine",
    };

    public static IReadOnlyList<VocalToken> Parse(
        VocalLanguage language, string text, VocalEmphasisSyntax syntax, CancellationToken cancellationToken = default)
    {
        var result = new List<VocalToken>();
        if (string.IsNullOrEmpty(text)) return result;
        ParseSegment(language, text, syntax, VocalEmphasis.Normal, result, 0, false, cancellationToken);
        return result;
    }

    internal static IReadOnlyList<VocalToken> ParseWindowsSpeech(
        VocalLanguage language, string text, VocalEmphasisSyntax syntax, CancellationToken cancellationToken)
    {
        var result = new List<VocalToken>();
        ParseSegment(language, text, syntax, VocalEmphasis.Normal, result, 0, true, cancellationToken);
        return result;
    }

    private static void ParseSegment(
        VocalLanguage language, string text, VocalEmphasisSyntax syntax, VocalEmphasis baseEmphasis,
        List<VocalToken> output, int depth, bool preserveText, CancellationToken cancellationToken)
    {
        if (depth > 32) throw new ArgumentException("Emphasis markers are nested too deeply.", nameof(text));
        int i = 0;
        var pending = baseEmphasis;
        int n = text.Length;
        while (i < n)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (language == VocalLanguage.Beatboxing)
            {
                if (char.IsWhiteSpace(text[i]) || text[i] is '|' or ',' or ';' or ':' or '!' or '?')
                {
                    i++;
                    continue;
                }
                if (text[i] is '.' or '-')
                {
                    output.Add(new VocalPauseToken(250));
                    i++;
                    continue;
                }
            }
            // Whitespace handling: spaces -> proportional pause, newlines -> paragraph pause.
            if (char.IsWhiteSpace(text[i]))
            {
                bool hasNewline = false;
                int spaceCount = 0;
                while (i < n && char.IsWhiteSpace(text[i]))
                {
                    if ((i & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                    if (text[i] == '\n' || text[i] == '\r') hasNewline = true;
                    else spaceCount++;
                    i++;
                }
                if (hasNewline)
                {
                    output.Add(new VocalPauseToken(350));
                }
                else if (preserveText && spaceCount == 1)
                {
                    output.Add(new VocalTextToken(" "));
                }
                else if (spaceCount > 0)
                {
                    output.Add(new VocalPauseToken(Math.Max(60, spaceCount * 70)));
                }
                continue;
            }

            if (language == VocalLanguage.AnimalCrossing && IsEllipsisAt(text, i))
            {
                output.Add(new VocalPauseToken(75));
                for (int dot = 0; dot < 3; dot++)
                {
                    if (dot > 0) output.Add(new VocalPauseToken(85 + dot * 15));
                    output.Add(new VocalPhonemeToken("animal-dot", pending));
                }
                pending = baseEmphasis;
                i += text[i] == '\u2026' ? 1 : 3;
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
                        ParseSegment(language, inner, syntax, Combine(baseEmphasis, modeEmphasis),
                            output, depth + 1, preserveText, cancellationToken);
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

            if (wrapRule is not null)
            {
                i += wrapRule.Marker.Length;
                continue;
            }

            if (language == VocalLanguage.Beatboxing)
            {
                string symbol = BeatboxSymbol(text, i, out int consumed);
                bool capitalized = IsCapitalized(text.Substring(i, consumed), allowSingleLetter: true);
                output.Add(new VocalPhonemeToken(symbol, pending, Capitalized: capitalized));
                output.Add(new VocalPauseToken(250 - VocalPhonemeInventory.Get(symbol).DurationMs));
                pending = baseEmphasis;
                i += consumed;
                continue;
            }

            if (IsPunctuation(text[i]))
            {
                if (preserveText)
                {
                    output.Add(new VocalTextToken(text[i].ToString()));
                    i++;
                    continue;
                }
                int pause = text[i] switch
                {
                    ',' => 140,
                    ';' or ':' => 180,
                    _ => 240,
                };
                output.Add(new VocalPauseToken(pause));
                i++;
                continue;
            }

            var sb = new StringBuilder();
            while (i < n && !char.IsWhiteSpace(text[i]) && !IsPunctuation(text[i])
                && !IsMarkerStart(syntax, text, i)
                && !(language == VocalLanguage.AnimalCrossing && IsEllipsisAt(text, i)))
            {
                if ((i & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                sb.Append(text[i]);
                i++;
            }
            var word = sb.ToString();
            if (!string.IsNullOrWhiteSpace(word))
            {
                bool isQuestion = i < n && text[i] == '?';
                bool isExclamation = i < n && text[i] == '!';
                var emphasis = pending;
                if (emphasis == VocalEmphasis.Normal && isQuestion) emphasis = VocalEmphasis.PitchUp;
                if (emphasis == VocalEmphasis.Normal && isExclamation) emphasis = VocalEmphasis.Strong;
                bool capitalized = IsCapitalized(word, allowSingleLetter: false);

                if (preserveText)
                {
                    output.Add(new VocalTextToken(word, emphasis, capitalized, isQuestion));
                }
                else
                {
                    var phonemes = VocalPhonemizer.Phonemize(language, word, cancellationToken);
                    int stressed = -1;
                    if (language != VocalLanguage.AnimalCrossing && !UnstressedWords.Contains(word))
                    {
                        for (int p = 0; p < phonemes.Count; p++)
                        {
                            if (!VocalPhonemeInventory.Get(phonemes[p]).IsVowel) continue;
                            if (language == VocalLanguage.German && phonemes[p] == "de-schwa") continue;
                            stressed = p;
                            bool lastVowel = word.Equals("hello", StringComparison.OrdinalIgnoreCase);
                            bool secondVowel = word.Equals("intrigued", StringComparison.OrdinalIgnoreCase);
                            if ((!lastVowel && !secondVowel) || lastVowel && p == phonemes.Count - 1
                                || secondVowel && phonemes[p] == "ee")
                                break;
                        }
                    }
                    for (int p = 0; p < phonemes.Count; p++)
                        output.Add(new VocalPhonemeToken(phonemes[p], emphasis, p == stressed, capitalized, isQuestion));
                }
                pending = baseEmphasis;
            }
        }
    }

    private static bool IsCapitalized(string text, bool allowSingleLetter)
    {
        int letters = 0;
        foreach (char c in text)
        {
            if (!char.IsLetter(c)) continue;
            if (!char.IsUpper(c)) return false;
            letters++;
        }
        return letters >= (allowSingleLetter ? 1 : 2);
    }

    private static string BeatboxSymbol(string text, int index, out int consumed)
    {
        char first = char.ToLowerInvariant(text[index]);
        char next = index + 1 < text.Length ? char.ToLowerInvariant(text[index + 1]) : '\0';
        consumed = first == 'p' && next == 'f' || first == 't' && next == 's' ? 2 : 1;
        return (first, consumed) switch
        {
            ('b', _) => "beat-b",
            ('p', 2) => "beat-pf",
            ('p', _) => "beat-p",
            ('k', _) => "beat-k",
            ('t', 2) => "beat-ts",
            ('t', _) => "beat-t",
            ('s', _) => "beat-s",
            ('f', _) => "beat-f",
            ('h', _) => "beat-h",
            _ => throw new ArgumentException(
                $"Unsupported beatbox notation '{text[index]}'. Use B, P, K, T, TS, PF, S, F, H, '.' or '-'.", nameof(text)),
        };
    }

    private static VocalEmphasisRule? TryMatchWrapping(VocalEmphasisSyntax syntax, string text, int index)
    {
        return syntax.Rules.Where(r => r.Wrapping && !string.IsNullOrEmpty(r.Marker))
            .OrderByDescending(r => r.Marker.Length)
            .FirstOrDefault(r => text.AsSpan(index).StartsWith(r.Marker, StringComparison.Ordinal));
    }

    private static VocalEmphasisRule? TryMatchInline(VocalEmphasisSyntax syntax, string text, int index)
    {
        return syntax.Rules.Where(r => !r.Wrapping && !string.IsNullOrEmpty(r.Marker))
            .OrderByDescending(r => r.Marker.Length)
            .FirstOrDefault(r => text.AsSpan(index).StartsWith(r.Marker, StringComparison.Ordinal));
    }

    private static bool IsMarkerStart(VocalEmphasisSyntax syntax, string text, int index)
    {
        return syntax.Rules.Any(r => !string.IsNullOrEmpty(r.Marker)
            && text.AsSpan(index).StartsWith(r.Marker, StringComparison.Ordinal));
    }

    private static bool IsEllipsisAt(string text, int index) =>
        text[index] == '\u2026' || text.AsSpan(index).StartsWith("...", StringComparison.Ordinal);

    private static bool IsPunctuation(char c) => c is '.' or ',' or ';' or ':' or '?' or '!' or '\u2026';

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