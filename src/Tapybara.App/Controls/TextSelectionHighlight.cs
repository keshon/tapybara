using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TextBox = System.Windows.Controls.TextBox;

namespace Tapybara.App.Controls;

/// <summary>Временное выделение поиска; ручное выделение и переработанные строки ему не принадлежат.</summary>
public sealed class TextSelectionHighlight
{
    private TextBox? _editor;
    private object? _context;
    private int _start;
    private int _length;

    public void Show(TextBox editor, int start, int length)
    {
        Clear();
        if (editor.IsKeyboardFocusWithin) { return; }
        editor.Select(start, length);
        _editor = editor; _context = editor.DataContext; _start = start; _length = length;
        editor.SelectionChanged += OnSelectionChanged;
        editor.DataContextChanged += OnContextChanged;
        editor.GotKeyboardFocus += OnFocused;
    }
    public void Clear()
    {
        TextBox? editor = _editor;
        bool owned = editor is not null && ReferenceEquals(editor.DataContext, _context)
            && editor.SelectionStart == _start && editor.SelectionLength == _length;
        Release();
        if (owned) { editor!.Select(editor.SelectionStart, 0); }
    }
    private void Release()
    {
        if (_editor is { } editor)
        {
            editor.SelectionChanged -= OnSelectionChanged;
            editor.DataContextChanged -= OnContextChanged;
            editor.GotKeyboardFocus -= OnFocused;
        }
        _editor = null; _context = null;
    }
    private void OnSelectionChanged(object sender, RoutedEventArgs e) => Release();
    private void OnContextChanged(object sender, DependencyPropertyChangedEventArgs e) => Release();
    private void OnFocused(object sender, KeyboardFocusChangedEventArgs e) => Release();
}
