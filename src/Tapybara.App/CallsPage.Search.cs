using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tapybara.App.Controls;
using Tapybara.App.Localization;
using Tapybara.Core.Calls;
using TextSearch = Tapybara.Core.Editing.TextSearch;

namespace Tapybara.App;

/// <summary>Область поиска — только текст открытого звонка; замена использует обычную историю документа.</summary>
public partial class CallsPage
{
    private sealed record SearchHit(Guid Id, int Start, int Length, string Original);
    private IReadOnlyList<SearchHit> _searchHits = [];
    private int _searchHitIndex = -1;
    private readonly TextSelectionHighlight _searchHighlight = new();

    private void InitializeDocumentSearch()
    {
        DocumentSearch.QueryChanged += () => RefreshDocumentSearch(reset: true);
        DocumentSearch.NavigateRequested += NavigateSearch;
        DocumentSearch.ReplaceRequested += ReplaceSearch;
        DocumentSearch.CloseRequested += () => { ClearSearchHighlight(); DocumentSearch.Visibility = Visibility.Collapsed; LineList.Focus(); };
    }
    private void OnFindClick(object sender, RoutedEventArgs e) => OpenDocumentSearch(replace: true);
    private void OpenDocumentSearch(bool replace)
    {
        if (_transcript is null || _activeEditor?.Flush() == false) { return; }
        string? selection = _activeEditor?.SelectedText;
        _noteHistoryActive = false;
        UpdateHistory();
        DocumentSearch.Visibility = Visibility.Visible;
        DocumentSearch.Open(replace, selection);
        RefreshDocumentSearch(reset: true);
    }
    private void RefreshDocumentSearch(bool reset = false, bool reveal = true)
    {
        FindButton.IsEnabled = _transcript is not null;
        if (DocumentSearch.Visibility != Visibility.Visible) { return; }
        SearchHit? previous = !reset && _searchHitIndex >= 0 && _searchHitIndex < _searchHits.Count ? _searchHits[_searchHitIndex] : null;
        _searchHits = _transcript is null ? [] : [.. _transcript.Lines.SelectMany(line =>
            TextSearch.Find(line.Text, DocumentSearch.Query).Select(match => new SearchHit(line.Id, match.Start, match.Length, line.Text)))];
        _searchHitIndex = previous is null ? -1 : _searchHits.ToList().FindIndex(hit => hit.Id == previous.Id && hit.Start == previous.Start);
        if (_searchHitIndex < 0 && _searchHits.Count > 0) { _searchHitIndex = 0; }
        DocumentSearch.SetMatches(_searchHitIndex, _searchHits.Count);
        SelectSearchHit(reveal);
    }
    private void NavigateSearch(int direction)
    {
        if (_activeEditor?.Flush() == false) { return; }
        RefreshDocumentSearch(reveal: false);
        if (_searchHits.Count == 0) { return; }
        _searchHitIndex = (_searchHitIndex + direction + _searchHits.Count) % _searchHits.Count;
        DocumentSearch.SetMatches(_searchHitIndex, _searchHits.Count);
        SelectSearchHit();
    }
    private void ClearSearchHighlight()
    {
        _searchHighlight.Clear();
        foreach (TranscriptLineRow line in _lines) { line.IsSearchHit = false; }
    }
    private void SelectSearchHit(bool reveal = true)
    {
        ClearSearchHighlight();
        if (_searchHitIndex < 0 || _searchHitIndex >= _searchHits.Count) { return; }
        SearchHit hit = _searchHits[_searchHitIndex];
        TranscriptLineRow? row = _lines.FirstOrDefault(r => r.Line.Id == hit.Id);
        if (row is null) { return; }
        LineList.SelectedItem = row;
        row.IsSearchHit = true;
        if (reveal) { ListViewport.Position(LineList, row); }
        if (LineList.ItemContainerGenerator.ContainerFromItem(row) is DependencyObject container)
        {
            InlineTextEditor? editor = FindEditor(container);
            if (editor is not null && editor.Text == hit.Original) { _searchHighlight.Show(editor, hit.Start, hit.Length); }
        }
    }
    private static InlineTextEditor? FindEditor(DependencyObject parent)
    {
        if (parent is InlineTextEditor editor) { return editor; }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { if (FindEditor(VisualTreeHelper.GetChild(parent, i)) is { } found) { return found; } }
        return null;
    }
    private void ReplaceSearch(bool all)
    {
        if (_activeEditor?.Flush() == false || _session is null || _transcript is null) { return; }
        RefreshDocumentSearch();
        if (_searchHitIndex < 0 || _searchHits.Count == 0 || DocumentSearch.Query.Length == 0) { return; }
        SearchHit hit = _searchHits[_searchHitIndex];
        string query = DocumentSearch.Query;
        string replacement = DocumentSearch.Replacement;
        if (ExecuteEdit(_session.Directory, document => all ? TranscriptSearch.ReplaceAll(document, query, replacement)
            : document.Lines.FirstOrDefault(line => line.Id == hit.Id && line.Text == hit.Original) is { } line
                ? TranscriptEdit.ReplaceAt(document, line, hit.Start, hit.Length, replacement) : document))
        { RefreshDocumentSearch(); }
    }
}
