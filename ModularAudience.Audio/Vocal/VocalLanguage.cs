namespace ModularAudience.Audio.Vocal;

/// <summary>Supported pronunciation modes. Other spoken languages currently
/// fall back to the English grapheme mapper.</summary>
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
    AnimalCrossing,
    Beatboxing,
}

public static class VocalLanguageExtensions
{
    public static string ShortCode(this VocalLanguage language) => language switch
    {
        VocalLanguage.English => "EN",
        VocalLanguage.German => "DE",
        VocalLanguage.French => "FR",
        VocalLanguage.Spanish => "ES",
        VocalLanguage.Italian => "IT",
        VocalLanguage.Chinese => "CN",
        VocalLanguage.Japanese => "JP",
        VocalLanguage.Turkish => "TK",
        VocalLanguage.Arabic => "AR",
        VocalLanguage.Russian => "RU",
        VocalLanguage.AnimalCrossing => "AC",
        VocalLanguage.Beatboxing => "BB",
        _ => language.ToString().ToUpperInvariant(),
    };

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
        VocalLanguage.AnimalCrossing => "Animal Crossing",
        VocalLanguage.Beatboxing => "Beatboxing",
        _ => language.ToString(),
    };

    public static bool IsFullyImplemented(this VocalLanguage language) =>
        language is VocalLanguage.English or VocalLanguage.German or VocalLanguage.AnimalCrossing or VocalLanguage.Beatboxing;
}