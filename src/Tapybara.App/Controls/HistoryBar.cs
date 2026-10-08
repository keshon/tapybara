using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Tapybara.App.Localization;
using Wpf.Ui.Controls;

namespace Tapybara.App.Controls;

/// <summary>Общая панель истории; не знает, какой документ правит экран.</summary>
public sealed class HistoryBar : StackPanel
{
    private readonly Wpf.Ui.Controls.Button _undo = new() { Icon = new SymbolIcon { FontSize = 16, Symbol = SymbolRegular.ArrowUndo24 } };
    private readonly Wpf.Ui.Controls.Button _redo = new() { Icon = new SymbolIcon { FontSize = 16, Symbol = SymbolRegular.ArrowRedo24 } };
    public event Action? UndoRequested;
    public event Action? RedoRequested;

    public HistoryBar()
    {
        ControlSizing.Apply(_undo, iconOnly: true);
        ControlSizing.Apply(_redo, iconOnly: true);
        Orientation = System.Windows.Controls.Orientation.Horizontal;
        _undo.Margin = new Thickness(0, 0, Tokens.Space2, 0);
        _undo.Click += (_, _) => UndoRequested?.Invoke();
        _redo.Click += (_, _) => RedoRequested?.Invoke();
        Children.Add(_undo);
        Children.Add(_redo);
        SetAvailability(false, false);
        ApplyLanguage();
    }

    public void SetAvailability(bool undo, bool redo)
    {
        _undo.IsEnabled = undo;
        _redo.IsEnabled = redo;
    }

    public void ApplyLanguage()
    {
        _undo.ToolTip = L.S.EditUndo + " (Ctrl+Z)";
        _redo.ToolTip = L.S.EditRedo + " (Ctrl+Shift+Z)";
        AutomationProperties.SetName(_undo, L.S.EditUndo);
        AutomationProperties.SetName(_redo, L.S.EditRedo);
    }
}
