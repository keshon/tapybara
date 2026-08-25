using System.Text;
using System.Text.RegularExpressions;

namespace TapRecorder.Core.Speech;

/// <summary>Причёсывание распознанного текста перед вставкой.</summary>
public static partial class TextPostProcessor
{
    /// <summary>
    /// Маркеры галлюцинаций Whisper.
    /// </summary>
    /// <remarks>
    /// На тишине, шуме и музыке Whisper дописывает то, чему научился на
    /// ютуб-субтитрах: «Продолжение следует…», «Субтитры сделал DimaTorzok».
    /// В диктовке этого не бывает — вырезаем.
    /// <para>
    /// Список сознательно консервативный: пропустить сомнительное дешевле, чем
    /// съесть настоящую реплику. Поэтому «спасибо», «спасибо за внимание» и
    /// «всем пока» здесь НЕТ — их реально говорят.
    /// </para>
    /// </remarks>
    private static readonly string[] Markers =
    [
        "продолжение следует",
        "спасибо за просмотр",
        "подписывайтесь на канал",
        "подпишитесь на канал",
        "ставьте лайк",
        "субтитры сделал",
        "субтитры создавал",
        "субтитры делал",
        "субтитры подготовил",
        "субтитры и перевод",
        "субтитры добавил",
        "редактор субтитров",
        "dimatorzok",
        "thanks for watching",
        "please subscribe",
        "subscribe to",
    ];

    /// <summary>
    /// Теги звука ТОЛЬКО в скобках: [музыка], (аплодисменты), [laughter].
    /// </summary>
    /// <remarks>
    /// Скобки обязательны. Без них фильтр съедал бы живую речь вроде
    /// «мне нравится музыка» или «это звучит логично».
    /// </remarks>
    [GeneratedRegex(
        @"^[\s♪«»""']*[\(\[\{]\s*[^)\]\}]*(музык|аплодис|смех|music|applause|laughter)[^)\]\}]*\s*[\)\]\}][\s♪«»""'.…!]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SoundTagRegex { get; }

    /// <summary>Похож ли текст на галлюцинацию, которую нельзя вставлять в поле ввода.</summary>
    public static bool IsHallucination(string text)
    {
        string raw = text.Trim();
        string normalized = Normalize(raw);

        if (normalized.Length == 0)
        {
            return true; // одна пунктуация или пустота: «!», «…», «♪♪♪»
        }

        if (SoundTagRegex.IsMatch(raw))
        {
            return true;
        }

        return Markers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
    }

    /// <summary>Применить пользовательский словарь замен.</summary>
    /// <remarks>
    /// Замена идёт по границам слова и без учёта регистра: «юджайл» → «YouGile»
    /// должно сработать и в начале предложения, но не внутри другого слова.
    /// </remarks>
    public static string ApplyReplacements(string text, IReadOnlyDictionary<string, string> replacements)
    {
        if (replacements.Count == 0)
        {
            return text;
        }

        string result = text;
        foreach ((string from, string to) in replacements)
        {
            if (from.Length == 0)
            {
                continue;
            }

            result = Regex.Replace(
                result,
                $@"\b{Regex.Escape(from)}\b",
                to.Replace("$", "$$", StringComparison.Ordinal), // $ в замене — служебный символ
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
        }

        return result;
    }

    /// <summary>Привести к виду «только буквы, цифры и пробелы, нижний регистр».</summary>
    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString().Trim();
    }
}
