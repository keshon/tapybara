using System.IO;
using System.Windows;
using System.Windows.Controls;
using Tapybara.App.Localization;
using Tapybara.Core.Calls;
using Tapybara.Core.Settings;
using Wpf.Ui.Controls;

using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Tapybara.App;

/// <summary>
/// Переименовать человека везде: в звонках, в тексте, в книге голосов и знакомых.
/// </summary>
/// <remarks>
/// <para>
/// Раньше правка имени в словаре меняла только список знакомых и книгу
/// голосов, а в звонках человек оставался под старым именем. Имя — это
/// человек (<see cref="CallPeople"/>), и переименование меняет правду
/// везде, где она записана.
/// </para>
/// <para>
/// Упоминания в тексте — отдельной строкой на каждую форму и с тем, во что
/// она превратится: «кириллу → Шерифу ×5». Падеж по одному слову угадывается
/// не всегда, и человек должен видеть итог, прежде чем соглашаться.
/// </para>
/// </remarks>
internal static class PersonRenamer
{
    /// <summary>Спросить и переименовать.</summary>
    /// <param name="owner">Окно, над которым показать вопрос.</param>
    /// <param name="settings">Настройки: знакомые и псевдонимы.</param>
    /// <param name="voices">Книга голосов: голос переезжает вместе с именем.</param>
    /// <param name="callsRoot">Папка звонков.</param>
    /// <param name="from">Прежнее имя.</param>
    /// <param name="to">Новое, если уже введено; иначе его спросят.</param>
    /// <param name="render">Перерисовать transcript.md звонка.</param>
    /// <returns>Переименовали ли.</returns>
    public static bool Run(
        Window? owner,
        SettingsHost settings,
        VoiceBook voices,
        string callsRoot,
        string from,
        string? to,
        Action<string> render)
    {
        string[] directories = Directory.Exists(callsRoot) ? Directory.GetDirectories(callsRoot) : [];
        List<string> withPerson = [.. directories.Where(d => CallMeta.Load(d) is { } s && PersonNames.IsIn(s, from))];

        // Упоминания ищутся во всех звонках, а не только в тех, где человек
        // был: о нём говорят и без него.
        var transcripts = directories
            .Select(d => (Directory: d, Transcript: CallTranscriptStore.Load(d)))
            .Where(c => c.Transcript is not null)
            .ToList();
        IReadOnlyList<NameMention> mentions = PersonNames.Find(transcripts.Select(c => c.Transcript!), from);
        List<string> withMentions = [.. transcripts
            .Where(c => PersonNames.Find([c.Transcript!], from).Count > 0)
            .Select(c => c.Directory)];

        string newName;
        bool inCalls = withPerson.Count > 0;
        List<NameMention> chosen = [.. mentions];

        // Менять, кроме словаря, нечего — и спрашивать не о чем.
        if (to is not null && withPerson.Count == 0 && mentions.Count == 0)
        {
            newName = to.Trim();
        }
        else if (Ask(owner, from, to, withPerson.Count, mentions) is { } answer)
        {
            (newName, inCalls, chosen) = answer;
        }
        else
        {
            return false;
        }

        if (newName.Length == 0 || newName == from)
        {
            return false;
        }

        settings.Update(s => s with
        {
            KnownParticipants = [.. s.KnownParticipants
                .Select(n => string.Equals(n, from, StringComparison.OrdinalIgnoreCase) ? newName : n)
                .Distinct(StringComparer.OrdinalIgnoreCase)],
            PersonAliases = s.PersonAliases.ToDictionary(
                p => string.Equals(p.Key, from, StringComparison.OrdinalIgnoreCase) ? newName : p.Key,
                p => p.Value),
        });
        voices.Rename(from, newName);

        var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (inCalls)
        {
            foreach (string directory in withPerson)
            {
                CallMeta.Update(directory, s => PersonNames.Rename(s, from, newName));
                touched.Add(directory);
            }
        }

        if (chosen.Count > 0)
        {
            foreach (string directory in withMentions)
            {
                CallTranscriptStore.Update(directory, t => PersonNames.Replace(t, chosen, newName).Transcript);
                touched.Add(directory);
            }
        }

        foreach (string directory in touched)
        {
            render(directory);
        }

        return true;
    }

    /// <summary>Окно вопроса: новое имя, звонки, упоминания.</summary>
    private static (string Name, bool InCalls, List<NameMention> Mentions)? Ask(
        Window? owner,
        string from,
        string? to,
        int calls,
        IReadOnlyList<NameMention> mentions)
    {
        var window = new ConfirmWindow(
            string.Format(L.S.Formatting, L.S.RenameTitle, from),
            L.S.RenameBody,
            primaryButton: L.S.RenameGo,
            cancelButton: L.S.ButtonCancel,
            icon: SymbolRegular.Rename24);

        var field = new TextBox { Text = to ?? from, ClearButtonEnabled = false };
        window.AddContent(field);

        var inCalls = new CheckBox
        {
            Content = string.Format(L.S.Formatting, L.S.RenameInCalls, calls),
            IsChecked = true,
            Visibility = calls > 0 ? Visibility.Visible : Visibility.Collapsed,
        };
        window.AddContent(inCalls);

        var checks = new List<(CheckBox Box, NameMention Mention)>();
        if (mentions.Count > 0)
        {
            var list = new StackPanel();
            list.Children.Add(Ui.Caption(L.S.RenameMentions));
            foreach (NameMention mention in mentions)
            {
                var check = new CheckBox { IsChecked = true, Margin = new Thickness(0, Tokens.Space1, 0, 0) };
                checks.Add((check, mention));
                list.Children.Add(check);
            }

            window.AddContent(list);
        }

        void Update()
        {
            string name = field.Text.Trim();
            foreach ((CheckBox box, NameMention mention) in checks)
            {
                box.Content = $"{mention.Form} → {PersonNames.Inflect(name, mention.Case)} ×{mention.Count}";
            }

            window.PrimaryEnabled = name.Length > 0 && name != from;
        }

        field.TextChanged += (_, _) => Update();
        Update();
        window.Loaded += (_, _) =>
        {
            field.Focus();
            field.SelectAll();
        };

        if (ConfirmWindow.Ask(owner, window) != ConfirmChoice.Primary)
        {
            return null;
        }

        return (
            field.Text.Trim(),
            inCalls.IsChecked == true,
            [.. checks.Where(c => c.Box.IsChecked == true).Select(c => c.Mention)]);
    }
}
