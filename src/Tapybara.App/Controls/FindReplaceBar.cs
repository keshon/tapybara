using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Tapybara.App.Localization;
using UiButton = Wpf.Ui.Controls.Button;
using UiTextBox = Wpf.Ui.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;

namespace Tapybara.App.Controls;

/// <summary>Общие поля и команды поиска; документ, выбор совпадения и история принадлежат экрану.</summary>
public sealed class FindReplaceBar : StackPanel
{
    private readonly UiTextBox _find = new() { ClearButtonEnabled = false, MinWidth = 80 };
    private readonly UiTextBox _replace = new() { ClearButtonEnabled = false, MinWidth = 80 };
    private readonly UiButton _previous = new() { Content = "↑" };
    private readonly UiButton _next = new() { Content = "↓" };
    private readonly UiButton _close = new() { Content = "×" };
    private readonly System.Windows.Controls.Primitives.ToggleButton _mode = new();
    private readonly UiButton _one = new();
    private readonly UiButton _all = new();
    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _replacementRow = new() { Margin = new Thickness(0, Tokens.Space2, 0, 0) };
    private readonly TextBlock _scope = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, Tokens.Space1, 0, 0) };
    private int _total;
    public string Query => _find.Text;
    public string Replacement => _replace.Text;
    public event Action? QueryChanged;
    public event Action<int>? NavigateRequested;
    public event Action<bool>? ReplaceRequested;
    public event Action? CloseRequested;

    public FindReplaceBar()
    {
        foreach (System.Windows.Controls.Control field in new System.Windows.Controls.Control[] { _find, _replace, _mode, _one, _all }) { ControlSizing.Apply(field); }
        foreach (UiButton button in new[] { _previous, _next, _close }) { ControlSizing.Apply(button, iconOnly: true); }
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (FrameworkElement item in new FrameworkElement[] { _count, _previous, _next, _mode, _close })
        { item.Margin = new Thickness(Tokens.Space1, 0, 0, 0); actions.Children.Add(item); }
        var findRow = new Grid();
        findRow.ColumnDefinitions.Add(new ColumnDefinition());
        findRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        findRow.Children.Add(_find); Grid.SetColumn(actions, 1); findRow.Children.Add(actions);
        Children.Add(findRow);
        _replacementRow.ColumnDefinitions.Add(new ColumnDefinition());
        _replacementRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _replacementRow.Children.Add(_replace);
        var replacements = new StackPanel { Orientation = Orientation.Horizontal, Children = { _one, _all } };
        _one.Margin = _all.Margin = new Thickness(Tokens.Space2, 0, 0, 0);
        Grid.SetColumn(replacements, 1); _replacementRow.Children.Add(replacements);
        Children.Add(_replacementRow); Children.Add(_scope);
        _scope.SetResourceReference(StyleProperty, "TextCaption");
        _find.TextChanged += (_, _) => QueryChanged?.Invoke();
        _replace.TextChanged += (_, _) => Refresh();
        _previous.Click += (_, _) => NavigateRequested?.Invoke(-1);
        _next.Click += (_, _) => NavigateRequested?.Invoke(1);
        _one.Click += (_, _) => ReplaceRequested?.Invoke(false);
        _all.Click += (_, _) => ReplaceRequested?.Invoke(true);
        _close.Click += (_, _) => CloseRequested?.Invoke();
        _mode.Checked += (_, _) => _replacementRow.Visibility = Visibility.Visible;
        _mode.Unchecked += (_, _) => _replacementRow.Visibility = Visibility.Collapsed;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; CloseRequested?.Invoke(); }
            else if (e.Key == Key.Enter && ReferenceEquals(e.OriginalSource, _find))
            { e.Handled = true; NavigateRequested?.Invoke(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1); }
            else if (e.Key == Key.Enter && ReferenceEquals(e.OriginalSource, _replace))
            { e.Handled = true; if (_one.IsEnabled) { ReplaceRequested?.Invoke(false); } }
        };
        ShowReplace(false);
        ApplyLanguage();
        SetMatches(-1, 0);
    }
    public void Open(bool replace, string? selection = null)
    {
        ShowReplace(replace);
        if (!string.IsNullOrEmpty(selection)) { _find.Text = selection; }
        _find.Focus(); _find.SelectAll();
    }
    public void ShowReplace(bool replace)
    { _mode.IsChecked = replace; _replacementRow.Visibility = replace ? Visibility.Visible : Visibility.Collapsed; }
    public void SetMatches(int index, int total)
    {
        _total = total;
        _count.Text = string.Format(L.S.Formatting, L.S.FindCount, index < 0 ? 0 : index + 1, total);
        Refresh();
    }
    private void Refresh()
    {
        _previous.IsEnabled = _next.IsEnabled = _total > 0;
        _one.IsEnabled = _all.IsEnabled = _total > 0 && Query.Length > 0 && Query != Replacement;
        _all.Content = string.Format(L.S.Formatting, L.S.FindReplaceAll, _total);
    }
    public void ApplyLanguage()
    {
        _find.PlaceholderText = L.S.FindQuery;
        _replace.PlaceholderText = L.S.FindReplacement;
        _mode.Content = _one.Content = L.S.FindReplace;
        _scope.Text = L.S.FindScope;
        _previous.ToolTip = L.S.FindPrevious; _next.ToolTip = L.S.FindNext; _close.ToolTip = L.S.ButtonClose;
        AutomationProperties.SetName(_find, L.S.FindQuery); AutomationProperties.SetName(_replace, L.S.FindReplacement);
        AutomationProperties.SetName(_previous, L.S.FindPrevious); AutomationProperties.SetName(_next, L.S.FindNext);
        AutomationProperties.SetName(_close, L.S.ButtonClose); Refresh();
    }
}
