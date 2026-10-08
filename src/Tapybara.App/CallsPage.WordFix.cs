using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Tapybara.App.Localization;
using Tapybara.Core.Calls;

using TextBox = System.Windows.Controls.TextBox;

namespace Tapybara.App;

/// <summary>Дополнительная явная правка слова и похожих написаний.</summary>
public partial class CallsPage
{
    /// <summary>Открытая карточка правки — одна на страницу.</summary>
    private Popup? _wordFix;

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
        if (_activeEditor?.Flush() == false)
        {
            return;
        }
        if (_transcript is null || _session is null || TranscriptEdit.PhraseAt(row.Line.Text, index, length) is not { } word)
        {
            return;
        }

        string original = row.Line.Text.Substring(word.Start, word.Length);
        string directory = _session.Directory;
        box.Select(word.Start, word.Length);

        IReadOnlyList<TranscriptEdit.WordForm> forms = TranscriptEdit.SimilarForms(_transcript, original);
        var editor = new Controls.TextCorrectionEditor(original, [.. forms.Select(f => (f.Form, f.Count))]);

        var card = new Controls.PopoverSurface
        {
            Child = editor,
            Padding = new Thickness(Tokens.Space4),
            CornerRadius = Tokens.CardRadius,
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.18 },
            Margin = new Thickness(Tokens.Space2),
        };
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
            string to = editor.Replacement;
            if (!ExecuteEdit(directory, change, refresh: false))
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

            _activeEditor = null;
            RefreshAfterEdit();
        }

        editor.ReplaceAllRequested += () =>
        {
            IReadOnlyList<string> chosen = editor.SelectedForms;
            string to = editor.Replacement;
            Apply(t => TranscriptEdit.Replace(t, chosen, to).Transcript, editor.RememberInDictionary ? chosen : null);
        };
        editor.ReplaceHereRequested += () =>
        {
            string to = editor.Replacement;
            Apply(t => TranscriptEdit.ReplaceAt(t, row.Line, word.Start, word.Length, to), null);
        };
        editor.DismissRequested += () => popup.IsOpen = false;

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

            editor.FocusText();
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
        Controls.AnchoredPopup.Open(popup, box, rectangle: at);
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
    /// Вернуть фокус в нативное поле без замены обычного выделения слов.
    /// </summary>
    private static void EditLineInPlace(TextBox box, TranscriptLineRow row)
    {
        box.Focus();
    }
}
