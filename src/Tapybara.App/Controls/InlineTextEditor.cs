using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace Tapybara.App.Controls;

/// <summary>Нативное редактирование текста; экран решает, как сохранить завершённую правку.</summary>
public sealed class InlineTextEditor : System.Windows.Controls.TextBox
{
    private string? _original;
    private object? _context;
    private string? _draft;
    public bool HasDraftChanges => _original is not null && _draft != _original;
    public event Func<InlineTextEditor, object?, string, bool>? CommitRequested;
    public event System.Windows.Controls.ContextMenuEventHandler? DocumentMenuOpening;

    static InlineTextEditor()
    {
        // Обработчик производного класса идёт раньше TextBox: иначе тот уже
        // успевает открыть встроенное меню и скрыть наш обработчик через Handled.
        EventManager.RegisterClassHandler(typeof(InlineTextEditor), System.Windows.Controls.ContextMenuService.ContextMenuOpeningEvent,
            new System.Windows.Controls.ContextMenuEventHandler((sender, args) =>
            {
                var editor = (InlineTextEditor)sender;
                editor.DocumentMenuOpening?.Invoke(editor, args);
            }), true);
    }

    public InlineTextEditor()
    {
        IsUndoEnabled = false;
        IsInactiveSelectionHighlightEnabled = true;
        AcceptsReturn = true;
        GotKeyboardFocus += (_, _) =>
        {
            if (_original is not null && !ReferenceEquals(_context, DataContext) && !Flush())
            {
                return;
            }
            _original ??= Text;
            _draft = Text;
            _context = DataContext;
        };
        TextChanged += (_, _) =>
        {
            if (_original is not null && ReferenceEquals(_context, DataContext))
            {
                _draft = Text;
            }
        };
        LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (!IsKeyboardFocusWithin && ContextMenu?.IsOpen != true)
            {
                Flush();
            }
        }, DispatcherPriority.Input);
        DataContextChanged += (_, _) => Flush();
        Unloaded += (_, _) => Flush();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _original is not null)
            {
                SetCurrentValue(TextProperty, _original);
                e.Handled = true;
            }
        };
    }

    public bool Flush()
    {
        string? original = _original;
        object? context = _context;
        string? draft = _draft;
        _original = null;
        _context = null;
        _draft = null;
        if (original is not null && draft is not null && draft != original)
        {
            if (CommitRequested?.Invoke(this, context, draft) == false)
            {
                _original = original;
                _context = context;
                _draft = draft;
                return false;
            }
        }
        if (IsKeyboardFocusWithin)
        {
            _original = Text;
            _context = DataContext;
            _draft = Text;
        }
        return true;
    }

    public void DiscardDraft()
    {
        _original = null;
        _context = null;
        _draft = null;
        ReloadText();
    }

    /// <summary>Обновить привязку после отмены или конфликта, не разрушая её.</summary>
    public void ReloadText() => BindingOperations.GetBindingExpression(this, TextProperty)?.UpdateTarget();
}
