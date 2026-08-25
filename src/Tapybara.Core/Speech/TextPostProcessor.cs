using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Tapybara.Core.Speech;

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
    /// Скомпилированные шаблоны замен, по одному на пару «откуда → куда».
    /// </summary>
    /// <remarks>
    /// Статический <c>Regex.Replace</c> кладёт шаблон в общий кэш, а тот по
    /// умолчанию хранит пятнадцать записей. Реальный словарь замен больше, и
    /// каждая диктовка пересобирала бы их все заново. Здесь шаблон компилируется
    /// один раз за жизнь процесса.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, Regex> ReplacementPatterns = new(StringComparer.Ordinal);

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

    /// <summary>
    /// Похож ли ФРАГМЕНТ на галлюцинацию, которую нельзя вставлять в поле ввода.
    /// </summary>
    /// <remarks>
    /// Проверять этим целую диктовку нельзя — и это не теоретическое
    /// замечание. Проверка ищет маркер в любом месте строки, поэтому на
    /// пятиминутной диктовке, где человек упомянул «подписывайтесь на канал»,
    /// выбрасывался бы весь текст целиком, а звук к тому моменту уже стёрт.
    /// Фильтровать надо посегментно: см. <see cref="RemoveHallucinations"/>.
    /// </remarks>
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

    /// <summary>Выбросить сегменты-галлюцинации, оставив остальные нетронутыми.</summary>
    public static IReadOnlyList<TranscriptSegment> RemoveHallucinations(IReadOnlyList<TranscriptSegment> segments) =>
        [.. segments.Where(s => !IsHallucination(s.Text))];

    /// <summary>
    /// Склеить сегменты в текст, восстановив абзацы по паузам в речи.
    /// </summary>
    /// <param name="segments">Сегменты с таймингами, как их отдал whisper.</param>
    /// <param name="paragraphPause">
    /// Пауза, начиная с которой ставится новый абзац.
    /// <see cref="TimeSpan.Zero"/> — не разбивать вовсе.
    /// </param>
    /// <remarks>
    /// Whisper не размечает абзацы: он отдаёт сегменты с пунктуацией внутри
    /// предложений, и всё. Но у сегментов есть тайминги, а пауза — самый
    /// честный сигнал смены мысли: человек останавливается, переходя к
    /// следующему пункту. Просто склеив сегменты пробелом, мы выбрасываем эту
    /// информацию и превращаем десятиминутную диктовку в одну простыню.
    /// <para>
    /// Границы сегментов часто приходятся на середину предложения, поэтому
    /// внутри абзаца соединяем пробелом, а не переводом строки.
    /// </para>
    /// </remarks>
    public static string JoinSegments(IReadOnlyList<TranscriptSegment> segments, TimeSpan paragraphPause)
    {
        if (segments.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (int i = 0; i < segments.Count; i++)
        {
            if (i > 0)
            {
                TimeSpan gap = segments[i].Start - segments[i - 1].End;
                bool newParagraph = paragraphPause > TimeSpan.Zero && gap >= paragraphPause;
                builder.Append(newParagraph ? "\n\n" : " ");
            }

            builder.Append(segments[i].Text);
        }

        return builder.ToString().Trim();
    }

    /// <summary>Применить пользовательский словарь замен.</summary>
    /// <remarks>
    /// Замена идёт по границам слова и без учёта регистра: «юджайл» → «YouGile»
    /// должно сработать и в начале предложения, но не внутри другого слова.
    /// </remarks>
    public static string ApplyReplacements(string text, IReadOnlyDictionary<string, string> replacements)
    {
        if (replacements.Count == 0 || text.Length == 0)
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

            result = PatternFor(from).Replace(
                result,
                to.Replace("$", "$$", StringComparison.Ordinal)); // $ в замене — служебный символ
        }

        return result;
    }

    /// <summary>
    /// Шаблон для одного ключа замены.
    /// </summary>
    /// <remarks>
    /// Не <c>\bКЛЮЧ\b</c>, хотя так пишут везде. <c>\b</c> — это граница между
    /// буквенным и небуквенным символом, и для ключа, который сам кончается
    /// небуквенным («C#», «C++»), такой границы после него не существует
    /// НИКОГДА: шаблон не совпадает ни с чем. А это ровно те названия, ради
    /// которых словарь замен и заводят.
    /// <para>
    /// Ретроспективная и опережающая проверки дают то же поведение для обычных
    /// слов и правильное — для оканчивающихся пунктуацией.
    /// </para>
    /// </remarks>
    private static Regex PatternFor(string from) => ReplacementPatterns.GetOrAdd(from, static key => new Regex(
        $@"(?<!\w){Regex.Escape(key)}(?!\w)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1)));

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
