using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Tapybara.Core.Speech;

/// <summary>
/// Слово в распознанном тексте: где оно, как его сравнивать и как искать целиком.
/// </summary>
/// <remarks>
/// Одно определение на всё приложение: словарь замен в диктовках, правка слов
/// в звонке и поиск имён должны видеть одни и те же слова. Когда у каждого
/// было своё, «Щёлково» находилось в похожих, но не заменялось, а граница
/// слова в звонке и в диктовке отличалась на подчёркивание.
/// </remarks>
internal static partial class Words
{
    private static readonly ConcurrentDictionary<string, Regex> Cache = new(StringComparer.Ordinal);

    /// <summary>Слово: буквы и цифры, с апострофом или дефисом внутри — «don't», «Анн-Мари».</summary>
    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'’\-]*", RegexOptions.CultureInvariant)]
    public static partial Regex Pattern();

    /// <summary>Регистр и «ё» не делают слово другим.</summary>
    public static string Fold(string word) =>
        word.ToLower(CultureInfo.InvariantCulture).Replace('ё', 'е');

    /// <summary>
    /// Шаблон, находящий <paramref name="phrase"/> целиком — без учёта регистра и «ё».
    /// </summary>
    /// <remarks>
    /// Не <c>\bСЛОВО\b</c>, хотя так пишут везде. <c>\b</c> — граница между
    /// буквенным и небуквенным символом, и после ключа, который сам кончается
    /// небуквенным («C#», «C++»), такой границы нет никогда: шаблон не совпал бы
    /// ни с чем, а это ровно те названия, ради которых словарь и заводят.
    /// Шаблоны кэшируются: словарь применяется к каждой диктовке, и
    /// пересобирать их каждый раз было бы расточительно.
    /// </remarks>
    public static Regex Whole(string phrase) => Cache.GetOrAdd(phrase, static key => new Regex(
        $@"(?<!\w){Tolerant(key)}(?!\w)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1)));

    /// <summary>
    /// Шаблон, находящий любую из фраз целиком; длинные — раньше коротких.
    /// </summary>
    /// <remarks>
    /// Одним проходом, а не фразой за фразой: иначе то, что уже заменила одна,
    /// могла бы переписать следующая («Ян» → «Яна», и потом «Яна» → «Яны»).
    /// </remarks>
    public static Regex AnyOf(IEnumerable<string> phrases) => new(
        $@"(?<!\w)(?:{string.Join('|', phrases.OrderByDescending(p => p.Length).Select(Tolerant))})(?!\w)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>Экранированная фраза, в которой «е» и «ё» взаимозаменяемы.</summary>
    private static string Tolerant(string phrase)
    {
        var pattern = new StringBuilder();
        foreach (char c in Regex.Escape(phrase))
        {
            pattern.Append(c is 'е' or 'ё' or 'Е' or 'Ё' ? "[её]" : c.ToString());
        }

        return pattern.ToString();
    }
}
