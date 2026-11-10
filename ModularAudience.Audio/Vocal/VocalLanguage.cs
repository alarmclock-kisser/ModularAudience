namespace ModularAudience.Audio.Vocal;

/// <summary>Supported spoken languages for the VocalSynth. Only English and German
/// are fully phonemized in the first implementation; others fall back to a generic
/// Latin vowel/consonant mapper so the UI stays usable and extensible.</summary>
public enum VocalLanguage
{
    English,
    German,
    French,
    Spanish,
    Italian,
    Chinese,
    Japanese,
    Turkish,
    Arabic,
    Russian,
}

public static class VocalLanguageExtensions
{
    public static string DisplayName(this VocalLanguage language) => language switch
    {
        VocalLanguage.English => "English",
        VocalLanguage.German => "German",
        VocalLanguage.French => "French",
        VocalLanguage.Spanish => "Spanish",
        VocalLanguage.Italian => "Italian",
        VocalLanguage.Chinese => "Chinese",
        VocalLanguage.Japanese => "Japanese",
        VocalLanguage.Turkish => "Turkish",
        VocalLanguage.Arabic => "Arabic",
        VocalLanguage.Russian => "Russian",
        _ => language.ToString(),
    };

    public static bool IsFullyImplemented(this VocalLanguage language) =>
        language is VocalLanguage.English or VocalLanguage.German;
}