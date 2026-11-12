using System.Globalization;
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
        ParseSegment(language, text, syntax, VocalEmphasis.Normal, result, 0, false, cancellationToken, 0);
        return result;
    }

    public static IReadOnlyList<VocalSyllableTextRange> GetSyllableTextRanges(
        VocalLanguage language, string text, VocalEmphasisSyntax syntax, CancellationToken cancellationToken = default)
    {
        var syllables = VocalSinging.CreateSyllables(
            language, Parse(language, text, syntax, cancellationToken), cancellationToken).Syllables;
        var wordGroups = new Dictionary<(int Start, int Length), List<int>>();
        for (int i = 0; i < syllables.Count; i++)
        {
            var syllable = syllables[i];
            if (syllable.SourceStart < 0 || syllable.SourceLength <= 0
                || syllable.SourceStart + syllable.SourceLength > text.Length)
                continue;
            var key = (syllable.SourceStart, syllable.SourceLength);
            if (!wordGroups.TryGetValue(key, out List<int>? indices))
                wordGroups.Add(key, indices = []);
            indices.Add(i);
        }

        List<VocalSyllableTextRange> ranges = [];
        foreach (var (word, indices) in wordGroups.OrderBy(entry => entry.Key.Start))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string wordText = text.Substring(word.Start, word.Length);
            int[] graphemeStarts = StringInfo.ParseCombiningCharacters(wordText);
            int graphemeCount = graphemeStarts.Length;
            int[] boundaries = CreateSyllableBoundaries(
                wordText, graphemeStarts, indices.Count, cancellationToken);
            for (int part = 0; part < indices.Count; part++)
            {
                int firstGrapheme = boundaries[part];
                int endGrapheme = boundaries[part + 1];
                if (endGrapheme <= firstGrapheme) continue;
                int start = word.Start + graphemeStarts[firstGrapheme];
                int end = endGrapheme == graphemeCount
                    ? word.Start + word.Length
                    : word.Start + graphemeStarts[endGrapheme];
                ranges.Add(new VocalSyllableTextRange(start, end - start, indices[part], indices.Count));
            }
        }
        return ranges.OrderBy(range => range.Start).ToArray();
    }

    private static int[] CreateSyllableBoundaries(
        string word, int[] graphemeStarts, int syllableCount, CancellationToken cancellationToken)
    {
        int graphemeCount = graphemeStarts.Length;
        List<(int Start, int End)> vowelNuclei = [];
        for (int index = 0; index < graphemeCount;)
        {
            if ((index & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (!IsVowelGrapheme(word, graphemeStarts, index))
            {
                index++;
                continue;
            }

            int start = index++;
            while (index < graphemeCount && IsVowelGrapheme(word, graphemeStarts, index))
            {
                if ((index & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                index++;
            }
            vowelNuclei.Add((start, index));
        }

        if (vowelNuclei.Count != syllableCount)
            return CreateEvenSyllableBoundaries(graphemeCount, syllableCount, cancellationToken);

        int[] boundaries = new int[syllableCount + 1];
        boundaries[^1] = graphemeCount;
        for (int syllable = 1; syllable < syllableCount; syllable++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (int _, int previousEnd) = vowelNuclei[syllable - 1];
            (int nextStart, int _) = vowelNuclei[syllable];
            int consonantsBetweenNuclei = nextStart - previousEnd;
            int nextOnsetLength = consonantsBetweenNuclei switch
            {
                0 => 0,
                <= 2 => 1,
                _ => 2
            };
            int boundary = nextStart - nextOnsetLength;
            if (boundary <= boundaries[syllable - 1] || boundary >= graphemeCount)
                return CreateEvenSyllableBoundaries(graphemeCount, syllableCount, cancellationToken);
            boundaries[syllable] = boundary;
        }

        return boundaries;
    }

    private static bool IsVowelGrapheme(string word, int[] graphemeStarts, int index)
    {
        int start = graphemeStarts[index];
        int end = index + 1 < graphemeStarts.Length ? graphemeStarts[index + 1] : word.Length;
        string grapheme = word.Substring(start, end - start).Normalize(NormalizationForm.FormD);
        foreach (char character in grapheme)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) is
                UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                continue;
            return "aeiouyæøœı".Contains(char.ToLowerInvariant(character));
        }
        return false;
    }

    private static int[] CreateEvenSyllableBoundaries(
        int graphemeCount, int syllableCount, CancellationToken cancellationToken)
    {
        int[] boundaries = new int[syllableCount + 1];
        for (int part = 0; part <= syllableCount; part++)
        {
            if ((part & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            boundaries[part] = (int)((long)part * graphemeCount / syllableCount);
        }
        return boundaries;
    }

    internal static IReadOnlyList<VocalToken> ParseWindowsSpeech(
        VocalLanguage language, string text, VocalEmphasisSyntax syntax, CancellationToken cancellationToken)
    {
        var result = new List<VocalToken>();
        ParseSegment(language, text, syntax, VocalEmphasis.Normal, result, 0, true, cancellationToken, 0);
        return result;
    }

    private static void ParseSegment(
        VocalLanguage language, string text, VocalEmphasisSyntax syntax, VocalEmphasis baseEmphasis,
        List<VocalToken> output, int depth, bool preserveText, CancellationToken cancellationToken, int sourceOffset)
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
                    output.Add(new VocalPhonemeToken("animal-dot", pending)
                    {
                        SourceStart = sourceOffset + i,
                        SourceLength = text[i] == '\u2026' ? 1 : 3,
                    });
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
                    if (wrapRule.Mode == VocalEmphasisMode.Muted)
                    {
                        i = close + markerLen;
                        continue;
                    }
                    var modeEmphasis = ModeToEmphasis(wrapRule.Mode);
                    // For pause mode, treat whole as pause
                    if (wrapRule.Mode == VocalEmphasisMode.Pause)
                    {
                        output.Add(new VocalPauseToken(wrapRule.Value));
                    }
                    else
                    {
                        ParseSegment(language, inner, syntax, Combine(baseEmphasis, modeEmphasis),
                            output, depth + 1, preserveText, cancellationToken, sourceOffset + i + markerLen);
                    }
                    i = close + markerLen;
                    continue;
                }
            }

            // Try inline markers
            var inlineRule = TryMatchInline(syntax, text, i);
            if (inlineRule is not null)
            {
                if (inlineRule.Mode == VocalEmphasisMode.Muted)
                {
                    i += inlineRule.Marker.Length;
                    while (i < n && text[i] is not '\r' and not '\n')
                    {
                        if ((i & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                        i++;
                    }
                    continue;
                }
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
                if (text[i] is '\'' or '\u2019')
                {
                    i++;
                    continue;
                }
                string symbol = BeatboxSymbol(text, i, out int consumed);
                bool capitalized = IsCapitalized(text.Substring(i, consumed), allowSingleLetter: true);
                output.Add(new VocalPhonemeToken(symbol, pending, Capitalized: capitalized)
                {
                    SourceStart = sourceOffset + i,
                    SourceLength = consumed,
                });
                output.Add(new VocalPauseToken(250 - VocalPhonemeInventory.Get(symbol).DurationMs));
                pending = baseEmphasis;
                i += consumed;
                continue;
            }

            if (IsPunctuation(text[i], language))
            {
                if (preserveText)
                {
                    output.Add(new VocalTextToken(text[i].ToString()));
                    i++;
                    continue;
                }
                int pause = text[i] switch
                {
                    ',' or '，' or '、' or '・' or '،' => 140,
                    ';' or ':' or '；' or '：' or '؛' or '—' => 180,
                    '¿' or '¡' or '(' or ')' or '（' or '）' or '「' or '」' or '『' or '』'
                        or '《' or '》' or '【' or '】' or '«' or '»' or '“' or '”' or '"' => 0,
                    _ => 240,
                };
                if (pause > 0) output.Add(new VocalPauseToken(pause));
                i++;
                continue;
            }

            int wordStart = i;
            var sb = new StringBuilder();
            while (i < n && !char.IsWhiteSpace(text[i]) && !IsPunctuation(text[i], language)
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
                bool isQuestion = i < n && text[i] is '?' or '？' or '؟';
                bool isExclamation = i < n && text[i] is '!' or '！';
                var emphasis = pending;
                if (emphasis == VocalEmphasis.Normal && isQuestion) emphasis = VocalEmphasis.PitchUp;
                if (emphasis == VocalEmphasis.Normal && isExclamation) emphasis = VocalEmphasis.Strong;
                bool capitalized = IsCapitalized(word, allowSingleLetter: false);

                if (preserveText)
                {
                    output.Add(new VocalTextToken(word, emphasis, capitalized, isQuestion)
                    {
                        SourceStart = sourceOffset + wordStart,
                        SourceLength = word.Length,
                    });
                }
                else
                {
                    if (language is not (VocalLanguage.AnimalCrossing or VocalLanguage.Beatboxing)
                        && VocalIpa.TryParseNotation(word, cancellationToken, out var ipaPhones))
                    {
                        foreach (var phone in ipaPhones)
                            output.Add(new VocalPhonemeToken(phone.Symbol, emphasis, phone.Stressed, capitalized, isQuestion)
                            {
                                Tone = phone.Tone,
                                ToneStart = phone.ToneStart,
                                ToneEnd = phone.ToneEnd,
                                SourceRepetitionLength = phone.SourceRepetitionLength,
                                SourceStart = sourceOffset + wordStart,
                                SourceLength = word.Length,
                            });
                        pending = baseEmphasis;
                        continue;
                    }
                    if (VocalMultilingualPhonemizer.Supports(language))
                    {
                        var phones = VocalMultilingualPhonemizer.PhonemizeWord(language, word, cancellationToken);
                        foreach (var phone in phones)
                            output.Add(new VocalPhonemeToken(phone.Symbol, emphasis, phone.Stressed, capitalized, isQuestion)
                            {
                                Tone = phone.Tone,
                                ToneStart = phone.ToneStart,
                                ToneEnd = phone.ToneEnd,
                                SourceRepetitionLength = phone.SourceRepetitionLength,
                                SourceStart = sourceOffset + wordStart,
                                SourceLength = word.Length,
                            });
                        pending = baseEmphasis;
                        continue;
                    }
                    IReadOnlyList<int>? repetitions = null;
                    IReadOnlyList<string> phonemes;
                    if (language is VocalLanguage.English or VocalLanguage.German)
                        (phonemes, repetitions) = VocalPhonemizer.PhonemizeWithRepetitions(language, word, cancellationToken);
                    else
                        phonemes = VocalPhonemizer.Phonemize(language, word, cancellationToken);
                    int stressed = -1;
                    if (language != VocalLanguage.AnimalCrossing && !UnstressedWords.Contains(word))
                    {
                        int stressStart = 0;
                        if (language == VocalLanguage.German && word.StartsWith("ver", StringComparison.OrdinalIgnoreCase)
                            && phonemes.Count > 3 && phonemes[0] == "f" && phonemes[1] == "e" && phonemes[2] == "de-r-glide"
                            && phonemes.Skip(3).Any(p => p != "de-schwa" && VocalPhonemeInventory.Get(p).IsVowel))
                            stressStart = 3;
                        for (int p = stressStart; p < phonemes.Count; p++)
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
                        output.Add(new VocalPhonemeToken(phonemes[p], emphasis, p == stressed, capitalized, isQuestion)
                        {
                            SourceRepetitionLength = repetitions is null ? 0 : repetitions[p],
                            SourceStart = sourceOffset + wordStart,
                            SourceLength = word.Length,
                        });
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
        if (text.AsSpan(index).StartsWith("tch", StringComparison.OrdinalIgnoreCase))
        {
            consumed = 3;
            return "beat-tch";
        }
        char first = char.ToLowerInvariant(text[index]);
        char next = index + 1 < text.Length ? char.ToLowerInvariant(text[index + 1]) : '\0';
        consumed = (first, next) is ('p', 'f') or ('t', 's') or ('t', 'r') or ('b', 'z') or ('k', 'z') ? 2 : 1;
        return (first, consumed) switch
        {
            ('b', 2) => "beat-bz",
            ('b', _) => "beat-b",
            ('p', 2) => "beat-pf",
            ('p', _) => "beat-p",
            ('k', 2) => "beat-kz",
            ('k', _) => "beat-k",
            ('t', 2) when next == 'r' => "beat-tr",
            ('t', 2) => "beat-ts",
            ('t', _) => "beat-t",
            ('s', _) => "beat-s",
            ('f', _) => "beat-f",
            ('h', _) => "beat-h",
            ('z', _) => "beat-z",
            _ => BestEffortBeatboxSymbol(text[index]),
        };
    }

    private static string BestEffortBeatboxSymbol(char symbol) => char.ToLowerInvariant(symbol) switch
    {
        'a' or 'e' or 'i' or 'o' or 'u' or 'y' => "beat-h",
        'ʘ' => "beat-p",
        'ǀ' or 'ʇ' => "beat-t",
        'ǃ' or 'ʗ' => "beat-k",
        'ǂ' => "beat-tch",
        'ǁ' => "beat-ts",
        's' or 'ʃ' or 'ɕ' or 'ʂ' or 'x' => "beat-s",
        'f' or 'ɸ' => "beat-f",
        'z' or 'ʒ' or 'ʑ' or 'v' or 'm' or 'n' or 'l' or 'r' or 'w' => "beat-z",
        _ when char.IsLetter(symbol) => "beat-k",
        _ => "beat-h",
    };

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

    private static bool IsPunctuation(char c, VocalLanguage language) =>
        c is '.' or ',' or ';' or ':' or '?' or '!' or '\u2026'
        || VocalMultilingualPhonemizer.Supports(language) && c is
            '。' or '，' or '、' or '・' or '；' or '：' or '？' or '！' or '،' or '؛' or '؟'
            or '¿' or '¡' or '—' or '(' or ')' or '（' or '）' or '「' or '」' or '『' or '』'
            or '《' or '》' or '【' or '】' or '«' or '»' or '“' or '”' or '"';

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