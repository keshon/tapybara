namespace Tapybara.Core.Calls;

/// <summary>
/// Список тех, с кем уже разговаривали, — для быстрых чипов в окне звонка.
/// </summary>
/// <remarks>
/// Порядок — по свежести: кого отметили последним, тот первый. На пятом
/// звонке подряд с одними и теми же людьми это означает, что нужные имена
/// всегда в начале списка и искать их не приходится.
/// <para>
/// Чистые функции над списком, а не объект с состоянием: список живёт в
/// настройках, у настроек уже есть владелец, и второе место, где то же самое
/// хранится «ещё и здесь», рано или поздно разошлось бы с первым.
/// </para>
/// </remarks>
public static class KnownParticipants
{
    /// <summary>Сколько имён помним. Хвост — те, кого давно не встречали.</summary>
    /// <remarks>
    /// Предохранитель от бесконечного роста настроек. Шестьдесят — это годы
    /// разговоров у любого нормального человека.
    /// </remarks>
    public const int MaxRemembered = 60;

    /// <summary>
    /// Отметить имена использованными: поднять в начало, новые — добавить.
    /// </summary>
    /// <remarks>
    /// Сравнение без учёта регистра. Иначе исправленный «Кирилл» и старый
    /// «кирилл» из прошлой меты жили бы в списке двумя отдельными чипами,
    /// и пользователь выбирал бы между ними, не понимая разницы.
    /// <para>
    /// Именно <c>Ordinal</c>, а не сравнение по культуре. Регистр кириллицы
    /// оно складывает верно, зато результат не зависит от языка системы:
    /// список имён переезжает между машинами вместе с настройками, и «те же
    /// самые» имена обязаны совпадать одинаково везде.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Touch(IReadOnlyList<string> known, IReadOnlyList<string> used)
    {
        if (used.Count == 0)
        {
            return known;
        }

        List<string> rest =
        [
            .. known.Where(name => !used.Contains(name, StringComparer.OrdinalIgnoreCase)),
        ];

        int keep = Math.Max(0, MaxRemembered - used.Count);
        if (rest.Count > keep)
        {
            rest = rest[..keep];
        }

        return [.. used, .. rest];
    }

    /// <summary>
    /// Привести введённое имя к каноническому виду, или <c>null</c>, если пусто.
    /// </summary>
    /// <remarks>
    /// Если такое имя уже известно, возвращаем ЕГО написание: «кирилл»,
    /// набранное второпях, не должно заводить второго Кирилла.
    /// <para>
    /// И никакого «умного» дополнения по подстроке. Оно выглядит удобным ровно
    /// до первого случая, когда «Аня» молча превращается в «Таню», после чего
    /// имя в транскрипте оказывается чужим, а заметил бы это только тот, кто
    /// перечитывает старые звонки.
    /// </para>
    /// </remarks>
    public static string? Normalize(IReadOnlyList<string> known, string? raw)
    {
        string name = raw?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return null;
        }

        return known.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase))
               ?? name;
    }

    /// <summary>Добавить имя к выбранным, если его там ещё нет.</summary>
    public static IReadOnlyList<string> Add(IReadOnlyList<string> selected, string name) =>
        selected.Contains(name, StringComparer.OrdinalIgnoreCase)
            ? selected
            : [.. selected, name];

    /// <summary>Убрать имя из списка.</summary>
    public static IReadOnlyList<string> Remove(IReadOnlyList<string> names, string name) =>
        [.. names.Where(n => !string.Equals(n, name, StringComparison.OrdinalIgnoreCase))];
}
