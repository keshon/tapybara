namespace Tapybara.App;

/// <summary>Язык распознавания в списке выбора.</summary>
/// <param name="Code">Код для whisper: <c>ru</c>, <c>en</c>, <c>auto</c>.</param>
/// <param name="Name">Как называется по-английски — так его знают везде.</param>
public sealed record WhisperLanguage(string Code, string Name)
{
    public override string ToString() => Code == "auto" ? Name : $"{Name} ({Code})";
}

/// <summary>
/// Языки, которые понимает whisper.
/// </summary>
/// <remarks>
/// <para>
/// Раньше язык вводился в обычное текстовое поле. Написать туда «russian»,
/// «рус» или «ru-RU» вместо «ru» — естественная ошибка, и приложение на неё
/// никак не реагировало: распознавание просто становилось хуже или ломалось
/// совсем, а выглядело это как плохая модель.
/// </para>
/// <para>
/// Список неполный — у whisper их девяносто девять, — но покрывает почти все
/// реальные случаи. Поле оставлено редактируемым: редкий код можно вписать
/// руками, и он проверяется на месте.
/// </para>
/// </remarks>
public static class WhisperLanguages
{
    /// <summary>Определять по звуку.</summary>
    public const string AutoCode = "auto";

    /// <summary>Всё, что предлагается в списке.</summary>
    public static IReadOnlyList<WhisperLanguage> All { get; } =
    [
        new(AutoCode, "Detect automatically"),
        new("en", "English"),
        new("ru", "Russian"),
        new("uk", "Ukrainian"),
        new("de", "German"),
        new("fr", "French"),
        new("es", "Spanish"),
        new("it", "Italian"),
        new("pt", "Portuguese"),
        new("nl", "Dutch"),
        new("pl", "Polish"),
        new("cs", "Czech"),
        new("sk", "Slovak"),
        new("sv", "Swedish"),
        new("no", "Norwegian"),
        new("da", "Danish"),
        new("fi", "Finnish"),
        new("et", "Estonian"),
        new("lv", "Latvian"),
        new("lt", "Lithuanian"),
        new("hu", "Hungarian"),
        new("ro", "Romanian"),
        new("bg", "Bulgarian"),
        new("el", "Greek"),
        new("tr", "Turkish"),
        new("he", "Hebrew"),
        new("ar", "Arabic"),
        new("fa", "Persian"),
        new("hi", "Hindi"),
        new("bn", "Bengali"),
        new("ur", "Urdu"),
        new("th", "Thai"),
        new("vi", "Vietnamese"),
        new("id", "Indonesian"),
        new("ms", "Malay"),
        new("zh", "Chinese"),
        new("ja", "Japanese"),
        new("ko", "Korean"),
        new("kk", "Kazakh"),
        new("hy", "Armenian"),
        new("ka", "Georgian"),
        new("az", "Azerbaijani"),
        new("uz", "Uzbek"),
        new("be", "Belarusian"),
        new("sr", "Serbian"),
        new("hr", "Croatian"),
        new("sl", "Slovenian"),
        new("ca", "Catalan"),
    ];

    /// <summary>Найти язык по коду.</summary>
    public static WhisperLanguage? Find(string? code) =>
        code is null ? null : All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Привести введённое к коду, который понимает whisper.
    /// </summary>
    /// <returns><c>null</c>, если понять не удалось.</returns>
    /// <remarks>
    /// Принимаем и код («ru»), и запись с регионом («ru-RU» — берём первую
    /// часть), и английское название («Russian»).
    /// </remarks>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        string text = input.Trim();

        // «English (en)» — то, как список показывает сам себя.
        int bracket = text.LastIndexOf('(');
        if (bracket > 0 && text.EndsWith(')'))
        {
            text = text[(bracket + 1)..^1].Trim();
        }

        string code = text.Split('-', '_')[0].Trim().ToLowerInvariant();
        if (Find(code) is { } byCode)
        {
            return byCode.Code;
        }

        WhisperLanguage? byName = All.FirstOrDefault(
            l => string.Equals(l.Name, text, StringComparison.OrdinalIgnoreCase));
        if (byName is not null)
        {
            return byName.Code;
        }

        // Код, которого нет в списке, но похожий на код языка, пропускаем:
        // whisper знает больше языков, чем мы перечислили.
        return code.Length == 2 && code.All(char.IsAsciiLetterLower) ? code : null;
    }
}
