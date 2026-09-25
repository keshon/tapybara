using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tapybara.App.Localization;
using Tapybara.Core.Calls;
using Tapybara.Core.Diagnostics;
using Tapybara.Core.Windows;
using Wpf.Ui.Controls;

using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Ellipse = System.Windows.Shapes.Ellipse;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using TextBlock = System.Windows.Controls.TextBlock;
using UiTextBox = Wpf.Ui.Controls.TextBox;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Tapybara.App;

/// <summary>Копирование транскрипта — как есть и без имён.</summary>
public partial class CallsPage
{
    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (_session is not null)
        {
            Copy(TranscriptText(_session.TranscriptPath));
        }
    }

    /// <summary>
    /// Скопировать транскрипт, подписав людей псевдонимами.
    /// </summary>
    /// <remarks>
    /// Показать разговор кому-то ещё и не показать, кто в нём был. Файлы
    /// звонка не меняются — меняется только копия (<see cref="PersonNames.Anonymize"/>).
    /// Псевдонимы запоминаются: у Кирилла, однажды ставшего «Шерифом»,
    /// следующая копия предложит того же «Шерифа».
    /// </remarks>
    private void CopyAnonymized()
    {
        if (_session is null || _transcript is null)
        {
            return;
        }

        string me = Settings.EffectiveMyName;
        IReadOnlyList<CallPerson> people = People();
        List<(string Name, Brush? Color)> named =
        [
            (me, VoicePalette.Me),
            .. people.Where(p => p is { IsMe: false, Name: not null }).Select(p => (p.Name!, (Brush?)ColorOf(p))),
        ];

        // И те, кого на звонке не было, но о ком говорили: «обсуждали с
        // Кириллом» выдаёт Кирилла не хуже подписи над репликой.
        foreach (string known in Settings.KnownParticipants)
        {
            if (!named.Any(n => string.Equals(n.Name, known, StringComparison.OrdinalIgnoreCase))
                && PersonNames.Find([_transcript], known).Count > 0)
            {
                named.Add((known, null));
            }
        }

        var window = new ConfirmWindow(
            L.S.AnonymizeTitle,
            L.S.AnonymizeBody,
            primaryButton: L.S.AnonymizeCopy,
            cancelButton: L.S.ButtonCancel,
            icon: SymbolRegular.PersonProhibited24);

        var rows = new Grid();
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var fields = new List<(string Name, UiTextBox Field)>();
        foreach ((string name, Brush? color) in named)
        {
            int row = rows.RowDefinitions.Count;
            rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Упомянутые без голоса — без точки: цвета у них на звонке нет.
            Ellipse dot = Ui.Dot(color ?? System.Windows.Media.Brushes.Transparent, Tokens.Dot);
            var label = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { dot, Ui.Body(name) },
            };
            TextBlock arrow = Ui.Caption("→");
            arrow.VerticalAlignment = VerticalAlignment.Center;
            arrow.Margin = new Thickness(Tokens.Space3, 0, Tokens.Space3, 0);
            var field = new UiTextBox
            {
                Text = Settings.PersonAliases.GetValueOrDefault(name, name),
                ClearButtonEnabled = false,
                Margin = new Thickness(0, 0, 0, Tokens.Space2),
            };

            Grid.SetRow(label, row);
            Grid.SetRow(arrow, row);
            Grid.SetColumn(arrow, 1);
            Grid.SetRow(field, row);
            Grid.SetColumn(field, 2);
            rows.Children.Add(label);
            rows.Children.Add(arrow);
            rows.Children.Add(field);
            fields.Add((name, field));
        }

        window.AddContent(rows);

        Button numbered = Ui.Link(L.S.AnonymizeNumbered);
        numbered.HorizontalAlignment = HorizontalAlignment.Left;
        numbered.Click += (_, _) =>
        {
            // Себя не трогаем: чаще всего копию отдают от своего имени.
            for (int i = 1; i < fields.Count; i++)
            {
                fields[i].Field.Text = string.Format(L.S.Formatting, L.S.AnonymizeParticipant, i);
            }
        };
        window.AddContent(numbered);

        var mentions = new CheckBox { Content = L.S.AnonymizeMentions, IsChecked = true };
        window.AddContent(mentions);

        if (ConfirmWindow.Ask(Window.GetWindow(this), window) != ConfirmChoice.Primary)
        {
            return;
        }

        Dictionary<string, string> aliases = fields
            .Select(f => (f.Name, Alias: f.Field.Text.Trim()))
            .Where(f => f.Alias.Length > 0)
            .ToDictionary(f => f.Name, f => f.Alias);

        _services.Settings.Update(s =>
        {
            var remembered = new Dictionary<string, string>(s.PersonAliases);
            foreach ((string name, string alias) in aliases)
            {
                if (alias == name)
                {
                    remembered.Remove(name);
                }
                else
                {
                    remembered[name] = alias;
                }
            }

            return s with { PersonAliases = remembered };
        });

        (CallSession session, CallTranscript transcript) = PersonNames.Anonymize(_session, _transcript, aliases, mentions.IsChecked == true);
        Copy(CallTranscriptRenderer.Render(
            session,
            transcript,
            aliases.GetValueOrDefault(me, me),
            OtherSideLabel(Settings),
            L.S.TranscriptLabels));
    }

    private static void Copy(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        try
        {
            // В историю буфера — можно: это не диктовка, а текст, который
            // человек сам решил скопировать.
            ClipboardWriter.SetText(text, excludeFromHistory: false);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            AppLog.Warn("Не удалось скопировать транскрипт.", ex);
        }
    }
}
