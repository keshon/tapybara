using Tapybara.Core.Editing;

namespace Tapybara.Core.Calls;

/// <summary>Поиск и массовая буквальная замена только в тексте текущего документа.</summary>
public static class TranscriptSearch
{
    public static CallTranscript ReplaceAll(CallTranscript document, string query, string replacement)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(replacement);
        if (query.Length == 0) { return document; }
        bool changed = false;
        CallLine[] lines = [.. document.Lines.Select(line =>
        {
            if (TextSearch.Find(line.Text, query).Count == 0) { return line; }
            string text = line.Text.Replace(query, replacement, StringComparison.OrdinalIgnoreCase);
            if (text == line.Text) { return line; }
            changed = true;
            return line with { Text = text, HumanEdits = line.HumanEdits | CallLineEdits.Text };
        })];
        return changed ? document with { Lines = lines, EditedByHand = true } : document;
    }
}
