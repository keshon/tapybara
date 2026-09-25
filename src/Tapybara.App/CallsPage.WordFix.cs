using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Tapybara.App.Localization;
using Tapybara.Core.Calls;
using Wpf.Ui.Controls;

using CheckBox = System.Windows.Controls.CheckBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
using UiButton = Wpf.Ui.Controls.Button;
using UiTextBox = Wpf.Ui.Controls.TextBox;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Tapybara.App;

/// <summary>Правка текста: слово по двойному щелчку и реплика целиком.</summary>
public partial class CallsPage
{
    /// <summary>Двойной щелчок по слову реплики — исправить его.</summary>
    /// <summary>Двойной щелчок по слову, чья карточка ждёт, пока отпустят кнопку.</summary>
    private (TextBox Box, int Index)? _pendingFix;

    /// <summary>Открытая карточка правки слова — одна на страницу.</summary>
    private Popup? _wordFix;

    private void OnLineDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox { IsReadOnly: true } box)
        {
            e.Handled = true;
            _pendingFix = (box, box.GetCharacterIndexFromPoint(e.GetPosition(box), snapToText: true));
        }
    }

    /// <summary>
    /// Открыть карточку, когда кнопку отпустили.
    /// </summary>
    /// <remarks>
    /// Не на втором нажатии: поле в этот момент держит мышь для выделения, и
    /// отпускание кнопки закрывало только что открытую карточку.
    /// </remarks>
    private void OnLineMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_pendingFix is ({ } box, int index) && sender == box && box.DataContext is TranscriptLineRow row)
        {
            _pendingFix = null;
            Dispatcher.BeginInvoke(() => FixWordAt(box, row, index), DispatcherPriority.Input);
        }
    }

    /// <summary>
    /// Исправить слово: здесь, во всём звонке вместе с похожими написаниями и, по желанию, в словаре.
    /// </summary>
    /// <remarks>
    /// Раньше отсюда можно было только добавить замену в словарь, и к этому
    /// звонку она не применялась — окно советовало распознать его заново.
    /// Теперь правится сам текст: миллисекунды, без Whisper.
    /// </remarks>
    private void FixWordAt(TextBox box, TranscriptLineRow row, int index, int length = 0)
    {
        if (_transcript is null || _session is null || TranscriptEdit.PhraseAt(row.Line.Text, index, length) is not { } word)
        {
            return;
        }

        string original = row.Line.Text.Substring(word.Start, word.Length);
        string directory = _session.Directory;
        box.Select(word.Start, word.Length);

        var field = new UiTextBox { Text = original, MinWidth = 220, ClearButtonEnabled = false };

        var was = new TextBlock
        {
            Text = original,
            TextDecorations = TextDecorations.Strikethrough,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, Tokens.Space2, 0),
        };
        was.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");

        TextBlock arrow = Ui.Caption("→");
        arrow.VerticalAlignment = VerticalAlignment.Center;
        arrow.Margin = new Thickness(0, 0, Tokens.Space2, 0);

        var body = new StackPanel { Width = 360 };
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { was, arrow, field } });

        // Похожие написания — отмеченными: чаще всего их и нужно поменять
        // вместе. Лишнее снимается одним щелчком.
        IReadOnlyList<TranscriptEdit.WordForm> forms = TranscriptEdit.SimilarForms(_transcript, original);
        var checks = new List<(CheckBox Box, TranscriptEdit.WordForm Form)>();
        if (forms.Count > 1)
        {
            TextBlock similar = Ui.Caption(L.S.FixSimilar);
            similar.Margin = new Thickness(0, Tokens.Space3, 0, Tokens.Space1);
            body.Children.Add(similar);

            var list = new WrapPanel();
            foreach (TranscriptEdit.WordForm form in forms)
            {
                var check = new CheckBox
                {
                    Content = $"{form.Form} ×{form.Count}",
                    IsChecked = true,
                    Margin = new Thickness(0, 0, Tokens.Space3, 0),
                };
                checks.Add((check, form));
                list.Children.Add(check);
            }

            body.Children.Add(list);
        }

        var remember = new CheckBox
        {
            Content = L.S.FixRemember,
            IsChecked = true,
            Margin = new Thickness(0, Tokens.Space2, 0, 0),
        };
        body.Children.Add(remember);

        var replaceAll = new UiButton { Appearance = ControlAppearance.Primary, Margin = new Thickness(0, 0, Tokens.Space2, 0) };
        var onlyHere = new UiButton { Content = L.S.FixOnlyHere };
        body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, Tokens.Space3, 0, 0),
            Children = { replaceAll, onlyHere },
        });

        List<string> Chosen() => checks.Count == 0
            ? [original]
            : [.. checks.Where(c => c.Box.IsChecked == true).Select(c => c.Form.Form)];

        void Update()
        {
            int count = checks.Count == 0 ? 1 : checks.Where(c => c.Box.IsChecked == true).Sum(c => c.Form.Count);
            string to = field.Text.Trim();
            replaceAll.Content = string.Format(L.S.Formatting, L.S.FixReplaceAll, count);
            replaceAll.IsEnabled = count > 0 && to.Length > 0 && to != original;
            onlyHere.IsEnabled = to.Length > 0 && to != original;
        }

        foreach ((CheckBox check, _) in checks)
        {
            check.Checked += (_, _) => Update();
            check.Unchecked += (_, _) => Update();
        }

        field.TextChanged += (_, _) => Update();
        Update();

        var card = new Border
        {
            Child = body,
            Padding = new Thickness(Tokens.Space4),
            CornerRadius = Tokens.CardRadius,
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.18 },
            Margin = new Thickness(Tokens.Space2),
        };
        card.SetResourceReference(Border.BackgroundProperty, "ApplicationBackgroundBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");

        Rect at = box.GetRectFromCharacterIndex(word.Start);
        var popup = new Popup
        {
            Child = card,
            PlacementTarget = box,
            Placement = PlacementMode.Bottom,
            PlacementRectangle = at,
            StaysOpen = false,
            AllowsTransparency = true,
        };

        void Apply(Func<CallTranscript, CallTranscript> change, IReadOnlyList<string>? rememberForms)
        {
            popup.IsOpen = false;
            string to = field.Text.Trim();
            if (CallTranscriptStore.Update(directory, change) is null)
            {
                return;
            }

            if (rememberForms is { Count: > 0 })
            {
                _services.Settings.Update(s =>
                {
                    var map = new Dictionary<string, string>(s.Replacements);
                    foreach (string form in rememberForms.Where(f => !f.Equals(to, StringComparison.OrdinalIgnoreCase)))
                    {
                        map[form] = to;
                    }

                    return s with { Replacements = map };
                });
            }

            _services.Render(directory);
            RefreshAfterEdit();
        }

        void ReplaceEverywhere()
        {
            List<string> chosen = Chosen();
            string to = field.Text.Trim();
            Apply(t => TranscriptEdit.Replace(t, chosen, to).Transcript, remember.IsChecked == true ? chosen : null);
        }

        replaceAll.Click += (_, _) => ReplaceEverywhere();

        // «Только здесь» — правка одного места, в словарь не идёт: это
        // исправление, которое разносить не нужно.
        onlyHere.Click += (_, _) =>
        {
            string to = field.Text.Trim();
            Apply(t => TranscriptEdit.ReplaceAt(t, row.Line, word.Start, word.Length, to), null);
        };

        // PreviewKeyDown: Enter поле WPF-UI обрабатывает само, и до KeyDown он не доходил.
        field.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && replaceAll.IsEnabled)
            {
                e.Handled = true;
                ReplaceEverywhere();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                popup.IsOpen = false;
            }
        };

        // Всплывающее окно — отдельное окно Windows, и фокус клавиатуры сам к
        // нему не переходит. Так и было замечено: поле выглядело выделенным, а
        // буквы уходили в реплику под ним, которая только для чтения. Отдаём
        // фокус окну карточки явно, и лишь потом — полю.
        popup.Opened += (_, _) =>
        {
            if (PresentationSource.FromVisual(card) is System.Windows.Interop.HwndSource source)
            {
                NativeFocus.Set(source.Handle);
            }

            field.Focus();
            Keyboard.Focus(field);
            field.SelectAll();
        };
        popup.Closed += (_, _) =>
        {
            PageRoot.Children.Remove(popup);
            if (_wordFix == popup)
            {
                _wordFix = null;
            }
        };

        // В дереве страницы — чтобы карточка знала своё окно и закрывалась
        // вместе со звонком (CloseWordFix).
        CloseWordFix();
        PageRoot.Children.Add(popup);
        _wordFix = popup;
        popup.IsOpen = true;
    }

    /// <summary>Закрыть карточку правки слова — при смене звонка она относится уже не к нему.</summary>
    private void CloseWordFix()
    {
        if (_wordFix is { } open)
        {
            open.IsOpen = false;
        }
    }

    /// <summary>
    /// Исправить реплику целиком — прямо на месте: Enter сохраняет, Esc отменяет.
    /// </summary>
    private void EditLineInPlace(TextBox box, TranscriptLineRow row)
    {
        if (_session is null)
        {
            return;
        }

        string directory = _session.Directory;

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                Finish(save: true);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Finish(save: false);
            }
        }

        void OnLost(object sender, KeyboardFocusChangedEventArgs e) => Finish(save: true);

        void Finish(bool save)
        {
            // Строки списка переиспользуются при прокрутке: обработчики,
            // оставшиеся на поле, сработали бы уже на чужой реплике.
            box.PreviewKeyDown -= OnKey;
            box.LostKeyboardFocus -= OnLost;
            box.IsReadOnly = true;
            string text = box.Text.Trim();

            // Текст возвращается из привязки, а не записывается руками:
            // записанный руками рвёт привязку, и поле, отданное при прокрутке
            // другой реплике, показывало бы старый текст.
            BindingOperations.GetBindingExpression(box, TextBox.TextProperty)?.UpdateTarget();

            if (save
                && text.Length > 0
                && text != row.Line.Text
                && CallTranscriptStore.Update(directory, t => TranscriptEdit.EditLine(t, row.Line, text)) is not null)
            {
                _services.Render(directory);
                RefreshAfterEdit();
            }
        }

        box.IsReadOnly = false;
        box.Focus();
        box.CaretIndex = box.Text.Length;
        box.PreviewKeyDown += OnKey;
        box.LostKeyboardFocus += OnLost;
    }
}
