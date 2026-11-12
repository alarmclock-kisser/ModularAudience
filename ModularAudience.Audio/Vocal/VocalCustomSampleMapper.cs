using System.Globalization;

namespace ModularAudience.Audio.Vocal;

public sealed record VocalCustomSampleMapping(
    string IpaSubstring,
    float[] Samples,
    int SampleRate,
    int Channels);

internal sealed record VocalCustomSampleToken(
    VocalCustomSampleMapping Mapping,
    int MatchId,
    int SampleStartFrame,
    int SampleFrameCount,
    int PartIndex,
    int PartCount,
    bool HasNucleus,
    int SourceStart,
    int SourceLength) : VocalToken
{
    internal double DurationMilliseconds =>
        SampleFrameCount * 1000.0 / Mapping.SampleRate;
}

internal static class VocalCustomSampleMapper
{
    private sealed record CompiledMapping(
        VocalCustomSampleMapping Mapping, string[] PhoneSymbols, int Order);

    internal static bool IsValidIpaSubstring(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512) return false;
        return VocalIpa.TryParseNotation(value, CancellationToken.None, out List<VocalPronunciationPhone> phones)
            && phones.Count is > 0 and <= 256;
    }

    internal static void ValidateMappings(IReadOnlyList<VocalCustomSampleMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        foreach (VocalCustomSampleMapping mapping in mappings)
        {
            ArgumentNullException.ThrowIfNull(mapping);
            if (string.IsNullOrWhiteSpace(mapping.IpaSubstring)) continue;
            if (!IsValidIpaSubstring(mapping.IpaSubstring))
                throw new ArgumentException(
                    $"Custom sample mapping '{mapping.IpaSubstring}' must be a valid IPA sequence, such as /tʃ/.",
                    nameof(mappings));
            if (mapping.SampleRate <= 0 || mapping.Channels is < 1 or > 2
                || mapping.Samples is null || mapping.Samples.Length == 0
                || mapping.Samples.Length % mapping.Channels != 0)
                throw new ArgumentException("A custom sample mapping has an invalid audio format.", nameof(mappings));
        }
    }

    internal static IReadOnlyList<VocalToken> Apply(
        VocalLanguage language,
        string text,
        VocalEmphasisSyntax syntax,
        IReadOnlyList<VocalToken> tokens,
        IReadOnlyList<VocalCustomSampleMapping> mappings,
        bool splitForSinging,
        CancellationToken cancellationToken)
    {
        if (mappings.Count == 0) return tokens;
        ValidateMappings(mappings);

        List<CompiledMapping> compiled = [];
        for (int order = 0; order < mappings.Count; order++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VocalCustomSampleMapping mapping = mappings[order];
            if (string.IsNullOrWhiteSpace(mapping.IpaSubstring)) continue;
            if (!VocalIpa.TryParseNotation(
                mapping.IpaSubstring, cancellationToken, out List<VocalPronunciationPhone> phones))
                throw new ArgumentException($"Custom sample mapping '{mapping.IpaSubstring}' is not valid IPA.");
            compiled.Add(new CompiledMapping(mapping, phones.Select(phone => phone.Symbol).ToArray(), order));
        }
        if (compiled.Count == 0) return tokens;

        CompiledMapping[] orderedMappings = compiled
            .OrderByDescending(mapping => mapping.PhoneSymbols.Length)
            .ThenBy(mapping => mapping.Order)
            .ToArray();
        List<VocalToken> result = new(tokens.Count);
        int matchId = 0;
        for (int tokenIndex = 0; tokenIndex < tokens.Count;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompiledMapping? match = null;
            if (tokens[tokenIndex] is VocalPhonemeToken)
            {
                foreach (CompiledMapping candidate in orderedMappings)
                {
                    if (Matches(tokens, tokenIndex, candidate.PhoneSymbols))
                    {
                        match = candidate;
                        break;
                    }
                }
            }

            if (match is null)
            {
                result.Add(tokens[tokenIndex++]);
                continue;
            }

            int phoneCount = match.PhoneSymbols.Length;
            (int sourceStart, int sourceLength) = LocateSourceRange(
                language, text, syntax, tokens, tokenIndex, phoneCount, cancellationToken);
            VocalPhonemeToken[] matchedPhones = tokens
                .Skip(tokenIndex)
                .Take(phoneCount)
                .Cast<VocalPhonemeToken>()
                .ToArray();
            int partCount = splitForSinging
                ? Math.Max(1, CountSyllableNuclei(language, matchedPhones))
                : 1;
            int sourceFrameCount = match.Mapping.Samples.Length / match.Mapping.Channels;
            if (sourceFrameCount < partCount)
                throw new InvalidDataException(
                    $"Custom sample mapping '{match.Mapping.IpaSubstring}' is too short for its {partCount} sung syllables.");

            for (int part = 0; part < partCount; part++)
            {
                int firstFrame = (int)((long)part * sourceFrameCount / partCount);
                int endFrame = (int)((long)(part + 1) * sourceFrameCount / partCount);
                result.Add(new VocalCustomSampleToken(
                    match.Mapping,
                    matchId,
                    firstFrame,
                    endFrame - firstFrame,
                    part,
                    partCount,
                    splitForSinging && partCount > 1 || matchedPhones.Any(IsSyllableNucleus),
                    sourceStart,
                    sourceLength));
            }

            matchId++;
            tokenIndex += phoneCount;
        }

        return result;
    }

    internal static IReadOnlyList<VocalToken> AddSamplesToSpeechTokens(
        IReadOnlyList<VocalToken> speechTokens,
        IReadOnlyList<VocalCustomSampleToken> samples,
        CancellationToken cancellationToken)
    {
        if (samples.Count == 0) return speechTokens;
        List<VocalToken> result = [];
        HashSet<int> emittedMatches = [];
        foreach (VocalToken token in speechTokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (token is not VocalTextToken textToken || textToken.SourceStart < 0
                || textToken.SourceLength <= 0)
            {
                result.Add(token);
                continue;
            }

            int tokenStart = textToken.SourceStart;
            int tokenEnd = tokenStart + Math.Min(textToken.SourceLength, textToken.Text.Length);
            int cursor = tokenStart;
            VocalCustomSampleToken[][] overlappingMatches = samples
                .Where(sample => sample.SourceStart >= 0 && sample.SourceLength > 0
                    && sample.SourceStart < tokenEnd
                    && sample.SourceStart + sample.SourceLength > tokenStart)
                .GroupBy(sample => sample.MatchId)
                .OrderBy(group => group.First().SourceStart)
                .Select(group => group.OrderBy(sample => sample.PartIndex).ToArray())
                .ToArray();

            foreach (VocalCustomSampleToken[] matchParts in overlappingMatches)
            {
                VocalCustomSampleToken sample = matchParts[0];
                int sampleStart = Math.Clamp(sample.SourceStart, tokenStart, tokenEnd);
                int sampleEnd = Math.Clamp(sample.SourceStart + sample.SourceLength, tokenStart, tokenEnd);
                if (sampleStart > cursor)
                    result.Add(SliceTextToken(textToken, cursor, sampleStart));
                if (emittedMatches.Add(sample.MatchId))
                    result.AddRange(matchParts);
                cursor = Math.Max(cursor, sampleEnd);
            }

            if (cursor < tokenEnd) result.Add(SliceTextToken(textToken, cursor, tokenEnd));
            if (overlappingMatches.Length == 0) result.Add(textToken);
        }
        return result;
    }

    private static VocalTextToken SliceTextToken(VocalTextToken token, int start, int end)
    {
        int offset = start - token.SourceStart;
        int length = end - start;
        return token with
        {
            Text = token.Text.Substring(offset, length),
            SourceStart = start,
            SourceLength = length,
        };
    }

    private static bool Matches(IReadOnlyList<VocalToken> tokens, int start, IReadOnlyList<string> phones)
    {
        if (start > tokens.Count - phones.Count) return false;
        for (int offset = 0; offset < phones.Count; offset++)
        {
            if (tokens[start + offset] is not VocalPhonemeToken token
                || !string.Equals(token.Phoneme, phones[offset], StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private static int CountSyllableNuclei(
        VocalLanguage language, IReadOnlyList<VocalPhonemeToken> phones)
    {
        int count = 0;
        string? previousSymbol = null;
        foreach (VocalPhonemeToken phone in phones)
        {
            bool isNucleus = IsSyllableNucleus(phone)
                && (phone.Phoneme.StartsWith("beat-", StringComparison.Ordinal)
                    || language == VocalLanguage.AnimalCrossing
                    || phone.Phoneme != previousSymbol);
            if (isNucleus) count++;
            previousSymbol = phone.Phoneme;
        }
        return count;
    }

    private static bool IsSyllableNucleus(VocalPhonemeToken token) =>
        token.Phoneme.StartsWith("beat-", StringComparison.Ordinal)
        || VocalPhonemeInventory.Contains(token.Phoneme)
            && VocalPhonemeInventory.Get(token.Phoneme).IsVowel;

    private static (int Start, int Length) LocateSourceRange(
        VocalLanguage language,
        string text,
        VocalEmphasisSyntax syntax,
        IReadOnlyList<VocalToken> tokens,
        int tokenIndex,
        int matchPhoneCount,
        CancellationToken cancellationToken)
    {
        if (tokens[tokenIndex] is not VocalPhonemeToken first
            || first.SourceStart < 0 || first.SourceLength <= 0
            || first.SourceStart > text.Length - first.SourceLength)
            return (-1, 0);

        int sourceStart = first.SourceStart;
        int sourceLength = first.SourceLength;
        int firstWordPhone = tokenIndex;
        int wordEndPhone = tokenIndex + 1;
        while (firstWordPhone > 0 && IsFromSourceWord(tokens[firstWordPhone - 1], sourceStart, sourceLength))
            firstWordPhone--;
        while (wordEndPhone < tokens.Count && IsFromSourceWord(tokens[wordEndPhone], sourceStart, sourceLength))
            wordEndPhone++;
        if (wordEndPhone - firstWordPhone == matchPhoneCount)
            return (sourceStart, sourceLength);

        string word = text.Substring(sourceStart, sourceLength);
        int[] graphemeStarts = StringInfo.ParseCombiningCharacters(word);
        if (graphemeStarts.Length <= 64)
        {
            double expectedStart = (tokenIndex - firstWordPhone) * graphemeStarts.Length
                / (double)(wordEndPhone - firstWordPhone);
            double expectedEnd = (tokenIndex - firstWordPhone + matchPhoneCount) * graphemeStarts.Length
                / (double)(wordEndPhone - firstWordPhone);
            (int Start, int Length)? best = null;
            double bestCost = double.PositiveInfinity;
            for (int firstGrapheme = 0; firstGrapheme < graphemeStarts.Length; firstGrapheme++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int endGrapheme = firstGrapheme + 1; endGrapheme <= graphemeStarts.Length; endGrapheme++)
                {
                    int charStart = graphemeStarts[firstGrapheme];
                    int charEnd = endGrapheme < graphemeStarts.Length
                        ? graphemeStarts[endGrapheme]
                        : word.Length;
                    IReadOnlyList<string> candidatePhones = VocalTextParser.Parse(
                            language, word[charStart..charEnd], syntax, cancellationToken)
                        .OfType<VocalPhonemeToken>()
                        .Select(phone => phone.Phoneme)
                        .ToArray();
                    if (candidatePhones.Count != matchPhoneCount) continue;
                    bool equal = true;
                    for (int phone = 0; phone < matchPhoneCount; phone++)
                    {
                        if (!string.Equals(
                            candidatePhones[phone],
                            ((VocalPhonemeToken)tokens[tokenIndex + phone]).Phoneme,
                            StringComparison.Ordinal))
                        {
                            equal = false;
                            break;
                        }
                    }
                    if (!equal) continue;

                    double cost = Math.Abs(firstGrapheme - expectedStart)
                        + Math.Abs(endGrapheme - expectedEnd);
                    if (cost >= bestCost) continue;
                    bestCost = cost;
                    best = (sourceStart + charStart, charEnd - charStart);
                }
            }
            if (best is { } located) return located;
        }

        int phoneOffset = tokenIndex - firstWordPhone;
        int firstFallbackGrapheme = (int)((long)phoneOffset * graphemeStarts.Length
            / (wordEndPhone - firstWordPhone));
        int endFallbackGrapheme = (int)Math.Ceiling(
            (phoneOffset + matchPhoneCount) * graphemeStarts.Length
            / (double)(wordEndPhone - firstWordPhone));
        firstFallbackGrapheme = Math.Clamp(firstFallbackGrapheme, 0, graphemeStarts.Length - 1);
        endFallbackGrapheme = Math.Clamp(endFallbackGrapheme, firstFallbackGrapheme + 1, graphemeStarts.Length);
        int fallbackStart = graphemeStarts[firstFallbackGrapheme];
        int fallbackEnd = endFallbackGrapheme < graphemeStarts.Length
            ? graphemeStarts[endFallbackGrapheme]
            : word.Length;
        return (sourceStart + fallbackStart, fallbackEnd - fallbackStart);
    }

    private static bool IsFromSourceWord(VocalToken token, int sourceStart, int sourceLength) =>
        token is VocalPhonemeToken phone
        && phone.SourceStart == sourceStart
        && phone.SourceLength == sourceLength;
}
