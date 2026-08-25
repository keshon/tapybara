using System.Globalization;

namespace TapRecorder.Core.Settings;

/// <summary>Значения по умолчанию, зависящие от языка.</summary>
public static class LanguageDefaults
{
    /// <summary>Языки, на которые переведён интерфейс.</summary>
    public static IReadOnlyList<string> SupportedUiLanguages { get; } = ["en", "ru"];

    /// <summary>
    /// Язык интерфейса по системной локали.
    /// </summary>
    /// <remarks>
    /// Если системный язык есть среди поддерживаемых — берём его. Иначе
    /// английский: он понятнее большинству, чем случайно выбранный русский.
    /// </remarks>
    public static string DetectUiLanguage()
    {
        string system = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return SupportedUiLanguages.Contains(system, StringComparer.OrdinalIgnoreCase)
            ? system.ToLowerInvariant()
            : "en";
    }

    /// <summary>
    /// Промпт по умолчанию для языка распознавания.
    /// </summary>
    /// <remarks>
    /// Промпт здесь работает якорем стиля, а не словарём: модель подражает его
    /// пунктуации и оформлению. Стоковые модели без промпта пишут сплошным
    /// текстом без знаков препинания, и этой одной фразы достаточно, чтобы они
    /// начали расставлять их сами.
    /// <para>
    /// Фраза намеренно общая и описывает саму диктовку. Промпт про конкретную
    /// предметную область, не совпавшую с речью, сбивает декодер и замедляет
    /// распознавание в разы.
    /// </para>
    /// </remarks>
    public static string DefaultPrompt(string recognitionLanguage) =>
        recognitionLanguage.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
            ? "Рабочая диктовка: заметки, сообщения и задачи."
            : "A work dictation: notes, messages and tasks.";
}
