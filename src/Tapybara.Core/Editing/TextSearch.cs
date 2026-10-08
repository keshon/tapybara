namespace Tapybara.Core.Editing;

/// <summary>Буквальный диапазон текста: без расширения до слова или похожих форм.</summary>
public readonly record struct TextMatch(int Start, int Length);

/// <summary>Общий поиск для редактируемых документов; совпадения не перекрываются.</summary>
public static class TextSearch
{
    public static IReadOnlyList<TextMatch> Find(string text, string query)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length == 0) { return []; }
        var matches = new List<TextMatch>();
        int start = 0;
        while (start <= text.Length - query.Length)
        {
            int at = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
            if (at < 0) { break; }
            matches.Add(new(at, query.Length));
            start = at + query.Length;
        }
        return matches;
    }
}
