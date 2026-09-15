using System;
using System.Collections.Generic;

namespace ReVerse.Relay.Desktop;

public interface ILocalizationService
{
    ITranslation Current { get; }
    IReadOnlyList<ITranslation> AvailableLanguages { get; }
    event EventHandler? LanguageChanged;
    bool SetLanguage(string? code);
}
