using System;
using System.Collections.Generic;
using System.Linq;

namespace ReVerse.Relay.Desktop;

public sealed class TranslationService : ILocalizationService
{
    private static readonly IReadOnlyList<ITranslation> Languages =
        new ITranslation[]
        {
            new EnglishTranslation(),
            new UkrainianTranslation(),
            new ChineseTranslation(),
            new SpanishTranslation()
        };

    public ITranslation Current { get; private set; } = Languages[0];
    public IReadOnlyList<ITranslation> AvailableLanguages => Languages;
    public event EventHandler? LanguageChanged;

    public bool SetLanguage(string? code)
    {
        var language = Languages.FirstOrDefault(item =>
            string.Equals(item.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (language is null || ReferenceEquals(language, Current))
            return false;

        Current = language;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
