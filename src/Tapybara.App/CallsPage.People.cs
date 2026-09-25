using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Tapybara.App.Localization;
using Tapybara.Core.Calls;
using Wpf.Ui.Controls;

using AutomationProperties = System.Windows.Automation.AutomationProperties;
using Binding = System.Windows.Data.Binding;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Ellipse = System.Windows.Shapes.Ellipse;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MenuItem = System.Windows.Controls.MenuItem;
using Orientation = System.Windows.Controls.Orientation;
using TextBlock = System.Windows.Controls.TextBlock;
using TextElement = System.Windows.Documents.TextElement;
using UiButton = Wpf.Ui.Controls.Button;
using UiTextBox = Wpf.Ui.Controls.TextBox;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Tapybara.App;

/// <summary>Панель участников: люди звонка, их имена, подсказки и разделение заново.</summary>
public partial class CallsPage
{
    /// <summary>Сколько знакомых имён предлагать чипами у голоса, сверх отмеченных участников.</summary>
    private const int SuggestedNames = 6;

    /// <summary>Подсказки книги голосов для неназванных голосов этого звонка: голос → кто похож.</summary>
    private Dictionary<string, VoiceMatch> _suggestions = [];

    /// <summary>
    /// Кого книга голосов узнаёт среди неназванных голосов этого звонка.
    /// </summary>
    /// <remarks>
    /// Имя — это человек, а не голос: два неназванных голоса, похожих на
    /// Кирилла, — скорее всего, два куска Кирилла, и подсказка обоим верна.
    /// Раньше имя получал только более похожий, а уже данные на звонке имена
    /// не предлагались вовсе — и после «Разделить заново» знакомого человека,
    /// разваленного на два голоса, приходилось собирать по памяти.
    /// </remarks>
    private Dictionary<string, VoiceMatch> Suggest()
    {
        var result = new Dictionary<string, VoiceMatch>();
        if (!Settings.RememberVoices || _transcript is null || _session is null)
        {
            return result;
        }

        IEnumerable<string> unnamed = _transcript.Voices.Count == 0
            ? (_session.Participants.Count == 0 ? [CallVoices.WholeOtherSide] : [])
            : _transcript.Voices.Where(v => CallSpeakers.NameOf(_session, v) is null);

        foreach (string voice in unnamed)
        {
            if (_transcript.VoicePrints.TryGetValue(voice, out float[]? print)
                && _services.Voices.Match(print) is { } match)
            {
                result[voice] = match;
            }
        }

        return result;
    }

    /// <summary>Принять все подсказки разом — человек их видел в баннере.</summary>
    private void AcceptSuggestions()
    {
        foreach ((string voice, VoiceMatch match) in _suggestions.ToList())
        {
            if (voice == CallVoices.WholeOtherSide)
            {
                SetOtherSideName(match.Name);
            }
            else
            {
                SetNames([voice], match.Name);
            }
        }
    }

    /// <summary>
    /// «Похоже на: Кирилл · 84%» и кнопка «Верно» под ним.
    /// </summary>
    /// <remarks>
    /// Друг под другом, а не в строку: в строке с кнопкой имя и процент
    /// переносились на две строки и выглядели зажатыми.
    /// </remarks>
    private static StackPanel SuggestionRow(VoiceMatch match, Action accept)
    {
        var row = new StackPanel { Margin = new Thickness(0, Tokens.Space3, 0, 0) };

        TextBlock text = Ui.Body(string.Format(
            L.S.Formatting,
            L.S.VoiceSuggestion,
            match.Name,
            Math.Round(match.Score * 100).ToString(L.S.Formatting)));
        text.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
        row.Children.Add(text);

        var confirm = new UiButton
        {
            Content = L.S.VoiceAcceptSuggestion,
            Appearance = ControlAppearance.Primary,
            Margin = new Thickness(0, Tokens.Space2, 0, 0),
        };
        confirm.Click += (_, _) => accept();
        row.Children.Add(confirm);
        return row;
    }

    /// <summary>Запомнить слепок голоса под именем, если запоминание включено.</summary>
    private void LearnVoice(string voice, string? name)
    {
        if (name is not null
            && Settings.RememberVoices
            && _transcript?.VoicePrints.TryGetValue(voice, out float[]? print) == true)
        {
            _services.Voices.Learn(name, print);
        }
    }

    /// <summary>Участники открытого звонка — вы, названные и неназванные.</summary>
    private IReadOnlyList<CallPerson> People() =>
        _transcript is null || _session is null ? [] : CallPeople.Of(_session, _transcript);

    private void ShowVoicesPane(bool visible)
    {
        VoicesPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        VoicesColumn.Width = visible ? new GridLength(320) : new GridLength(0);
    }

    /// <summary>
    /// Собрать панель участников: вы и собеседники — людьми, а не голосами.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Карточки собираются кодом, как карточки настроек: их число заранее
    /// неизвестно, а содержимое — чипы имён, цитаты с кнопками — проще
    /// сложить построителем, чем шаблоном с полудюжиной вложенных привязок.
    /// </para>
    /// <para>
    /// Названный человек свёрнут в строку с долей: внимание остаётся на
    /// карточках «Кто это?». Цитаты, его голоса и смена имени — по щелчку.
    /// </para>
    /// </remarks>
    private void ShowVoices()
    {
        VoicesHost.Children.Clear();
        _quoteButtons.Clear();

        if (_transcript is null || _session is null || !_transcript.Lines.Any(l => l.Channel == CallChannel.Theirs))
        {
            ShowVoicesPane(false);
            return;
        }

        ShowVoicesPane(true);

        // Пока над звонком идёт работа — разделение, сверка, — предлагать
        // разделить ещё раз незачем: второе нажатие встало бы в очередь следом.
        bool busy = _services.LiveState(_session.Directory) is not null;
        IReadOnlyList<CallPerson> people = People();

        VoicesHost.Children.Add(PeopleHeader(people.Count, canResplit: !busy && _services.CanSplitVoices()));

        if (!busy && _resplitOpen && _services.CanSplitVoices())
        {
            VoicesHost.Children.Add(ResplitRow());
        }

        if (!busy && VoiceCheck.MoreVoicesLikely(_transcript, out TimeSpan doubtful))
        {
            VoicesHost.Children.Add(MoreVoicesCard(doubtful, Math.Max(_transcript.Voices.Count, 1)));
        }

        foreach (CallPerson person in people)
        {
            VoicesHost.Children.Add(PersonCard(person, people));
        }
    }

    /// <summary>«Участники · 3» и меню «⋯».</summary>
    /// <remarks>
    /// «Разделить заново» нужно редко, а постоянной ссылкой внизу панели
    /// читалось загадкой без контекста. Живёт в меню, как и у звонка.
    /// </remarks>
    private Grid PeopleHeader(int count, bool canResplit)
    {
        var header = new Grid { Margin = new Thickness(0, 0, 0, Tokens.Space3) };
        TextBlock title = Ui.BodyStrong(string.Format(L.S.Formatting, L.S.PeopleHeader, count));
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);

        if (canResplit)
        {
            UiButton more = Ui.MoreButton((L.S.VoicesResplitMenu, SymbolRegular.ArrowClockwise24, () =>
            {
                _resplitOpen = true;
                ShowVoices();
            }));
            more.HorizontalAlignment = HorizontalAlignment.Right;
            more.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(more);
        }

        return header;
    }

    /// <summary>
    /// «Похоже, на звонке был ещё кто-то» — и разделить заново на голос больше.
    /// </summary>
    /// <remarks>
    /// Самая частая причина чужих цитат — не ошибка на отдельной реплике, а
    /// неверное число голосов: отметили одного, а говорили трое. Тогда чужие
    /// реплики не похожи ни на один найденный голос, и проверка это видит.
    /// Лечится не правкой по одной реплике, а разделением заново.
    /// </remarks>
    private Border MoreVoicesCard(TimeSpan doubtful, int found)
    {
        var body = new StackPanel();
        body.Children.Add(Ui.BodyStrong(L.S.VoicesMoreTitle));

        TextBlock hint = Ui.Caption(string.Format(L.S.Formatting, L.S.VoicesMoreHint, L.S.Duration(doubtful)));
        hint.Margin = new Thickness(0, Tokens.Space1, 0, 0);
        body.Children.Add(hint);

        body.Children.Add(SplitOffer(found + 1));

        Border card = Ui.Card(body);
        card.Margin = new Thickness(0, 0, 0, Tokens.Space2);
        card.SetResourceReference(Border.BorderBrushProperty, "AccentFillColorDefaultBrush");
        return card;
    }

    /// <summary>У кого из людей открыт выбор имени — по <see cref="KeyOf"/>.</summary>
    private readonly HashSet<string> _renaming = [];

    /// <summary>Какие карточки названных людей раскрыты — по <see cref="KeyOf"/>.</summary>
    private readonly HashSet<string> _expanded = [];

    /// <summary>
    /// Сколько цитат каждого голоса человек отклонил на этом звонке.
    /// </summary>
    /// <remarks>
    /// Одна чужая цитата — ошибка разделителя на одной реплике. Две из одного
    /// голоса — уже признак, что в голосе два человека.
    /// </remarks>
    private readonly Dictionary<string, int> _rejected = [];

    /// <summary>
    /// Кнопки ▶ у цитат — чтобы менять ▶ на ■ на месте.
    /// </summary>
    /// <remarks>
    /// Раньше ради одного значка пересобиралась вся панель, и набранное в
    /// поле «другое…» имя пропадало, когда цитата доигрывала.
    /// </remarks>
    private readonly Dictionary<CallLine, UiButton> _quoteButtons = [];

    /// <summary>Показать у каждой цитаты ▶ или ■ — какая играет.</summary>
    private void UpdateQuoteButtons()
    {
        foreach ((CallLine quote, UiButton button) in _quoteButtons)
        {
            bool playing = _playingQuote == quote && _player.IsPlaying;
            button.Icon = new SymbolIcon { Symbol = playing ? SymbolRegular.Stop16 : SymbolRegular.Play16 };
        }
    }

    /// <summary>Раскрыт ли выбор числа голосов для переразделения.</summary>
    private bool _resplitOpen;

    /// <summary>Чем карточка человека помнит, раскрыта ли она, — между перерисовками.</summary>
    /// <remarks>Имя, а не голос: присоединили голос — человек тот же, и карточка тоже.</remarks>
    private static string KeyOf(CallPerson person) =>
        person.IsMe ? CallSession.Me : person.Name?.ToUpperInvariant() ?? person.Voices[0];

    private static bool IsWholeSide(CallPerson person) => person.Voices is [CallVoices.WholeOtherSide];

    private static Brush ColorOf(CallPerson person) =>
        person.IsMe ? VoicePalette.Me : VoicePalette.For(Math.Max(person.Color, 0));

    /// <summary>Карточка одного участника.</summary>
    /// <param name="person">Кто: вы, названный человек или неназванный голос.</param>
    /// <param name="people">Все на звонке — для чипов «уже здесь».</param>
    private Border PersonCard(CallPerson person, IReadOnlyList<CallPerson> people)
    {
        string key = KeyOf(person);
        bool whole = IsWholeSide(person);
        bool unnamed = !person.IsMe && person.Name is null;
        bool waiting = unnamed && !whole;
        bool renaming = _renaming.Contains(key);

        // Своё раскрывать есть что, только если к вам отнесли голоса с той
        // стороны; у собеседника — цитаты и имя.
        bool expandable = !unnamed && (!person.IsMe || person.Voices.Count > 0);
        bool open = unnamed || renaming || (expandable && _expanded.Contains(key));

        var body = new StackPanel();

        // Заголовок: цвет, имя, сколько говорил, доля от всего звонка.
        var title = new Grid();
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        title.Children.Add(Ui.Dot(ColorOf(person), Tokens.DotLarge));

        TextBlock nameText = person switch
        {
            { IsMe: true } => Ui.BodyStrong(Settings.EffectiveMyName),
            { Name: { } name } => Ui.BodyStrong(name),
            _ when whole => Ui.BodySecondary(OtherSideLabel(Settings)),
            _ => Ui.BodySecondary(L.S.VoiceWho),
        };
        if (waiting)
        {
            nameText.FontStyle = FontStyles.Italic;
        }

        nameText.TextWrapping = TextWrapping.NoWrap;
        nameText.TextTrimming = TextTrimming.CharacterEllipsis;
        nameText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(nameText, 1);
        title.Children.Add(nameText);

        TextBlock stats = Ui.Caption($"{L.S.Duration(person.Speech)} · {Math.Round(person.Share * 100).ToString(L.S.Formatting)}%");
        stats.Margin = new Thickness(Tokens.Space2, 0, 0, 0);
        stats.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(stats, 2);
        title.Children.Add(stats);

        // Заголовок — кнопка, а не щелчок по сетке: раскрыть карточку должно
        // быть можно и с клавиатуры, и экранным диктором.
        if (expandable && !renaming)
        {
            var chevron = new SymbolIcon
            {
                Symbol = open ? SymbolRegular.ChevronUp16 : SymbolRegular.ChevronDown16,
                FontSize = Tokens.Caption,
                Margin = new Thickness(Tokens.Space2, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            chevron.SetResourceReference(ForegroundProperty, "TextFillColorTertiaryBrush");
            Grid.SetColumn(chevron, 3);
            title.Children.Add(chevron);

            Button toggle = Ui.Link(string.Empty);
            toggle.Content = title;
            toggle.Padding = new Thickness(Tokens.Space1);
            toggle.Margin = new Thickness(-Tokens.Space1);
            toggle.HorizontalAlignment = HorizontalAlignment.Stretch;
            toggle.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
            AutomationProperties.SetName(toggle, nameText.Text);
            toggle.Click += (_, _) =>
            {
                if (!_expanded.Remove(key))
                {
                    _expanded.Add(key);
                }

                ShowVoices();
            };
            body.Children.Add(toggle);
        }
        else
        {
            body.Children.Add(title);
        }
        body.Children.Add(ShareBar(person.Share, ColorOf(person)));

        if (person.IsMe)
        {
            TextBlock mine = Ui.Caption(L.S.VoiceMe);
            mine.Margin = new Thickness(0, Tokens.Space1, 0, 0);
            body.Children.Add(mine);
        }

        if (open)
        {
            AddPersonDetails(body, person, people, key, unnamed, renaming);
        }

        Border card = Ui.Card(body);
        card.Margin = new Thickness(0, 0, 0, Tokens.Space2);

        // Голос ждёт имени — рамка акцентом: это то, ради чего сюда пришли.
        if (waiting)
        {
            card.SetResourceReference(Border.BorderBrushProperty, "AccentFillColorDefaultBrush");
        }

        return card;
    }

    /// <summary>Раскрытая часть карточки: подсказка, цитаты, голоса, выбор имени.</summary>
    private void AddPersonDetails(
        StackPanel body,
        CallPerson person,
        IReadOnlyList<CallPerson> people,
        string key,
        bool unnamed,
        bool renaming)
    {
        string first = person.Voices.Count > 0 ? person.Voices[0] : string.Empty;

        if (unnamed && _rejected.GetValueOrDefault(first) >= 2 && !IsWholeSide(person))
        {
            body.Children.Add(RejectedHint());
        }

        if (unnamed && _suggestions.TryGetValue(first, out VoiceMatch? suggested))
        {
            body.Children.Add(SuggestionRow(suggested, () => NamePerson(person, suggested.Name, people)));
        }

        if (person.Quotes.Count > 0)
        {
            var quotes = new StackPanel { Margin = new Thickness(0, Tokens.Space3, 0, 0) };
            foreach (CallLine quote in person.Quotes)
            {
                quotes.Children.Add(QuoteRow(quote));
            }

            body.Children.Add(quotes);
        }

        // Из каких голосов человек собран — и «отделить» у каждого: промах
        // чипом исправляется одним щелчком, а не повторным разделением.
        if (person.Voices.Count > (person.IsMe ? 0 : 1))
        {
            var pieces = new StackPanel { Margin = new Thickness(0, Tokens.Space2, 0, 0) };
            foreach (string voice in person.Voices)
            {
                pieces.Children.Add(VoicePiece(voice));
            }

            body.Children.Add(pieces);
        }

        if (person.IsMe)
        {
            return;
        }

        if (unnamed || renaming)
        {
            WrapPanel chips = NameChips(person, people, picked =>
            {
                _renaming.Remove(key);
                NamePerson(person, picked, people);
            });
            chips.Margin = new Thickness(0, Tokens.Space3, 0, 0);
            body.Children.Add(chips);
            return;
        }

        // Имя уже есть — выбор свёрнут в «Изменить»: шесть чипов под
        // названным человеком — это шум, а не помощь. «Изменить» — на этом
        // звонке, «Переименовать везде» — правда о человеке во всех.
        Button change = Ui.Link(L.S.VoiceChange);
        change.Click += (_, _) =>
        {
            _renaming.Add(key);
            ShowVoices();
        };

        Button everywhere = Ui.Link(L.S.PersonRenameEverywhere);
        everywhere.Margin = new Thickness(Tokens.Space4, 0, 0, 0);
        everywhere.Click += (_, _) =>
        {
            if (person.Name is { } name && PersonRenamer.Run(
                    Window.GetWindow(this), _services.Settings, _services.Voices, _services.CallsDirectory(), name, to: null, _services.Render))
            {
                RefreshAfterEdit();
            }
        };

        body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, Tokens.Space1, 0, 0),
            Children = { change, everywhere },
        });
    }

    /// <summary>«Голос B · 12 с   Отделить».</summary>
    private StackPanel VoicePiece(string voice)
    {
        TimeSpan speech = TimeSpan.FromSeconds(_transcript!.Lines
            .Where(l => l.Voice == voice)
            .Sum(l => Math.Max(0, (l.End - l.Start).TotalSeconds)));

        TextBlock label = Ui.Caption($"{L.S.TranscriptVoice} {voice} · {L.S.Duration(speech)}");
        label.VerticalAlignment = VerticalAlignment.Center;

        Button detach = Ui.SmallLink(L.S.VoiceDetach);
        detach.FontSize = Tokens.Caption;
        detach.Padding = new Thickness(4, 0, 4, 0);
        detach.Margin = new Thickness(Tokens.Space2, 0, 0, 0);
        detach.VerticalAlignment = VerticalAlignment.Center;
        detach.Click += (_, _) => SetNames([voice], null);

        return new StackPanel { Orientation = Orientation.Horizontal, Children = { label, detach } };
    }

    private static Grid ShareBar(double share, Brush color)
    {
        var bar = new Grid { Height = 4, Margin = new Thickness(0, Tokens.Space2, 0, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(share, 0.001), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - share, 0.001), GridUnitType.Star) });

        var track = new Border { CornerRadius = new CornerRadius(2) };
        track.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorTertiaryBrush");
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = color });
        return bar;
    }

    /// <summary>«Две цитаты отсюда оказались чужими» — и разделить на голос больше.</summary>
    private StackPanel RejectedHint()
    {
        var panel = new StackPanel { Margin = new Thickness(0, Tokens.Space3, 0, 0) };
        panel.Children.Add(Ui.Caption(L.S.VoiceRejectedHint));
        panel.Children.Add(SplitOffer(_transcript!.Voices.Count + 1));
        return panel;
    }

    /// <summary>
    /// «Разделить заново — голосов: N», а если нечем — чего не хватает и «Скачать модель».
    /// </summary>
    /// <remarks>
    /// Сказать, что кто-то ещё был, и не дать ничего сделать, — тупик: так и
    /// было, пока модели разделения не хватало.
    /// </remarks>
    private StackPanel SplitOffer(int wanted)
    {
        var offer = new StackPanel();
        if (_services.CanSplitVoices())
        {
            var split = new UiButton
            {
                Content = string.Format(L.S.Formatting, L.S.VoicesSplitInto, wanted),
                Appearance = ControlAppearance.Primary,
                Margin = new Thickness(0, Tokens.Space3, 0, 0),
            };
            split.Click += (_, _) => Resplit(wanted);
            offer.Children.Add(split);
        }
        else if (ModelNeeds.Missing(Settings, ModelNeeds.SplitVoices) is { Count: > 0 } missing)
        {
            TextBlock need = Ui.Caption(string.Format(L.S.Formatting, L.S.VoicesNeedModels, L.S.KindNames(missing)));
            need.Margin = new Thickness(0, Tokens.Space2, 0, 0);
            offer.Children.Add(need);

            var get = new UiButton
            {
                Content = L.S.ButtonGetModel,
                Appearance = ControlAppearance.Primary,
                Margin = new Thickness(0, Tokens.Space2, 0, 0),
            };
            get.Click += (_, _) => _services.FetchModel(missing[0]);
            offer.Children.Add(get);
        }

        return offer;
    }

    /// <summary>Разделить голоса заново — панель при этом начинает с чистого листа.</summary>
    private void Resplit(int wanted)
    {
        if (_session is not null)
        {
            ResetPanelState();
            _ = _services.Resplit(_session.Directory, wanted);
        }
    }

    /// <summary>
    /// Забыть, что в панели было раскрыто, открыто для имени и отклонено.
    /// </summary>
    /// <remarks>
    /// Ключи — имена и буквы голосов. У другого звонка и после разделения
    /// заново те же ключи значат других людей: «B» нового разделения — не
    /// «B» прежнего, и его отклонённые цитаты к нему не относятся.
    /// </remarks>
    private void ResetPanelState()
    {
        _renaming.Clear();
        _expanded.Clear();
        _rejected.Clear();
        _resplitOpen = false;
        _quoteButtons.Clear();
    }

    /// <summary>
    /// «Не этот человек» под цитатой: куда её отдать.
    /// </summary>
    /// <remarks>
    /// Раньше чужая цитата в карточке была тупиком: её было видно, а
    /// исправить можно было только в транскрипте, найдя там ту же реплику.
    /// Если голоса не разделялись, переселять цитату некуда — остаётся
    /// разделить голоса.
    /// </remarks>
    private void ShowNotThisMenu(FrameworkElement anchor, CallLine quote)
    {
        if (_transcript is null || _session is null)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        if (_transcript.Voices.Count == 0)
        {
            menu.Items.Add(SplitOrGetModelItem(_session.Directory));
        }
        else
        {
            AddSaidBy(menu, quote, reject: true);
        }

        menu.IsOpen = true;
    }

    /// <summary>
    /// Кому отдать реплику: люди на звонке, вы, кто-то другой.
    /// </summary>
    /// <param name="menu">Куда добавить пункты.</param>
    /// <param name="line">Реплика собеседника.</param>
    /// <param name="reject">
    /// Из «Не этот человек» под цитатой: текущего человека в списке нет, и
    /// отказ считается — два отказа из одного голоса подсказывают, что в нём
    /// двое.
    /// </param>
    private void AddSaidBy(ItemsControl menu, CallLine line, bool reject)
    {
        foreach (CallPerson person in People())
        {
            bool current = line.Voice is { } voice && person.Voices.Contains(voice);
            if (reject && current)
            {
                continue;
            }

            string header = person switch
            {
                { IsMe: true } => Settings.EffectiveMyName,
                { Name: { } name } => name,
                _ => $"{L.S.TranscriptVoice} {person.Voices[0]}",
            };

            var item = new MenuItem
            {
                Header = header,
                IsCheckable = !reject,
                IsChecked = current,
                Icon = MenuDot(ColorOf(person)),
            };

            CallPerson target = person;
            item.Click += (_, _) =>
            {
                if (current)
                {
                    return;
                }

                CountRejection();

                // Своих голосов на той стороне может ещё не быть — тогда
                // реплика становится новым голосом, сразу названным «я».
                if (target.Voices.Count > 0)
                {
                    ReassignLine(line, target.Voices[0]);
                }
                else
                {
                    ReassignLine(line, CallVoices.NextFree(_transcript!.Voices), CallSession.Me);
                }
            };
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());

        // Новый голос — следующая свободная буква: реплика станет отдельной
        // карточкой «Кто это?».
        var someone = new MenuItem { Header = L.S.VoiceSomeoneElse };
        someone.Click += (_, _) =>
        {
            CountRejection();
            ReassignLine(line, CallVoices.NextFree(_transcript!.Voices));
        };
        menu.Items.Add(someone);

        void CountRejection()
        {
            if (reject && line.Voice is { } from)
            {
                _rejected[from] = _rejected.GetValueOrDefault(from) + 1;
            }
        }
    }

    /// <summary>Точка человека в пункте меню — без отступа, который нужен ей рядом с текстом.</summary>
    private static Ellipse MenuDot(Brush color)
    {
        Ellipse dot = Ui.Dot(color, Tokens.Dot);
        dot.Margin = new Thickness(0);
        return dot;
    }

    /// <summary>
    /// Пункт «Кто-то другой» там, где голоса не разделялись: разделить —
    /// или, если нечем, скачать чем.
    /// </summary>
    /// <remarks>
    /// Раньше без модели разделения пункт был просто серым. Человек видел,
    /// что кто-то ещё был на звонке, и не мог узнать, почему с этим ничего
    /// нельзя сделать.
    /// </remarks>
    private MenuItem SplitOrGetModelItem(string directory)
    {
        if (_services.CanSplitVoices())
        {
            var split = new MenuItem { Header = L.S.VoiceSomeoneElseSplit };
            split.Click += (_, _) => Resplit(2);
            return split;
        }

        var get = new MenuItem { Header = L.S.VoiceSomeoneElseGetModel };
        IReadOnlyList<Tapybara.Core.Models.ModelKind> missing = ModelNeeds.Missing(Settings, ModelNeeds.SplitVoices);
        get.IsEnabled = missing.Count > 0;
        get.Click += (_, _) => _services.FetchModel(missing[0]);
        return get;
    }

    /// <summary>Цитата с кнопкой прослушивания и «Не этот человек».</summary>
    private Grid QuoteRow(CallLine quote)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, Tokens.Space2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        bool playing = _playingQuote == quote && _player.IsPlaying;
        var play = new UiButton
        {
            Icon = new SymbolIcon { Symbol = playing ? SymbolRegular.Stop16 : SymbolRegular.Play16 },
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            CornerRadius = Tokens.PillRadius,
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = L.S.VoicePlayQuote,
        };
        play.Click += (_, _) => PlayQuote(quote);
        _quoteButtons[quote] = play;
        row.Children.Add(play);

        var text = new StackPanel();
        text.Children.Add(Ui.Body(L.S.Quote(quote.Text)));
        TextBlock stamp = Ui.Caption(CallTranscriptRenderer.Stamp(quote.Start));
        stamp.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        stamp.VerticalAlignment = VerticalAlignment.Center;
        stamp.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");

        Button notThis = Ui.SmallLink(L.S.VoiceNotThis);
        notThis.FontSize = Tokens.Caption;
        notThis.Padding = new Thickness(4, 0, 4, 0);
        notThis.Margin = new Thickness(Tokens.Space2, 0, 0, 0);
        notThis.VerticalAlignment = VerticalAlignment.Center;
        notThis.Click += (_, _) => ShowNotThisMenu(notThis, quote);

        text.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { stamp, notThis },
        });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    /// <summary>
    /// Чипы имён: кто подходит на этот голос.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Первыми — подсказанные книгой голосов и те, кто уже есть на звонке, с
    /// цветной точкой: выбрать такого — значит присоединить голос к нему.
    /// «Это я» — для своего голоса, попавшего в чужую дорожку через динамики.
    /// </para>
    /// <para>
    /// Дальше отмеченные на карточке после звонка и знакомые — свежие первыми.
    /// Искать нужное имя среди шестидесяти незачем: а если его нет,
    /// «другое…» открывает поле.
    /// </para>
    /// </remarks>
    private WrapPanel NameChips(CallPerson person, IReadOnlyList<CallPerson> people, Action<string?> pick)
    {
        var chips = new WrapPanel();
        Style style = Ui.ChipStyle();
        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, string label, Brush? dot)
        {
            if (!shown.Add(name))
            {
                return;
            }

            object content = label;
            if (dot is not null)
            {
                Ellipse mark = Ui.Dot(dot, Tokens.Dot);

                // Цвет текста — от кнопки, а не от общего стиля TextBlock:
                // тот чёрный, и у отмеченного чипа на синем имя не читалось.
                var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
                text.SetBinding(TextBlock.ForegroundProperty, new Binding
                {
                    Path = new PropertyPath(TextElement.ForegroundProperty),
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ContentPresenter), 1),
                });

                content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { mark, text },
                };
            }

            var chip = new ToggleButton
            {
                Content = content,
                IsChecked = string.Equals(name, person.Name, StringComparison.OrdinalIgnoreCase),
                Style = style,
            };
            AutomationProperties.SetName(chip, label);
            // Checked/Unchecked, а не Click: щелчок — лишь один из способов
            // переключить чип. Экранный диктор и UI Automation переключают
            // его через TogglePattern, и Click при этом не приходит вовсе.
            chip.Checked += (_, _) => pick(name);
            chip.Unchecked += (_, _) => pick(null);
            chips.Children.Add(chip);
        }

        if (person.Name is { } own)
        {
            Add(own, own, ColorOf(person));
        }

        foreach (string hinted in person.Voices.Where(_suggestions.ContainsKey).Select(v => _suggestions[v].Name))
        {
            Add(hinted, hinted, people.FirstOrDefault(p => string.Equals(p.Name, hinted, StringComparison.OrdinalIgnoreCase)) is { } there ? ColorOf(there) : null);
        }

        foreach (CallPerson there in people.Where(p => p.Name is not null))
        {
            Add(there.Name!, there.Name!, ColorOf(there));
        }

        if (!IsWholeSide(person))
        {
            Add(CallSession.Me, L.S.VoiceItsMe, VoicePalette.Me);
        }

        int limit = shown.Count + _session!.Participants.Count + SuggestedNames;
        foreach (string known in _session.Participants.Concat(Settings.KnownParticipants))
        {
            if (shown.Count >= limit)
            {
                break;
            }

            Add(known, known, null);
        }

        var other = new ToggleButton
        {
            Content = L.S.VoiceOtherName,
            Style = style,
        };
        other.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");

        other.Checked += (_, _) =>
        {
            int at = chips.Children.IndexOf(other);
            chips.Children.Remove(other);

            var entry = new UiTextBox
            {
                PlaceholderText = L.S.VoiceNamePlaceholder,
                Width = 150,
                Margin = new Thickness(0, 0, 6, 6),
            };

            entry.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    string? typed = KnownParticipants.Normalize(Settings.KnownParticipants, entry.Text);
                    if (typed is not null)
                    {
                        pick(typed);
                    }
                }
                else if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    ShowVoices();
                }
            };

            chips.Children.Insert(at, entry);
            entry.Focus();
        };

        chips.Children.Add(other);
        return chips;
    }

    /// <summary>
    /// «Разделено неверно? Искать голосов: 2 3 4 5».
    /// </summary>
    /// <remarks>
    /// Без подсказки разделитель ошибается в числе голосов чаще, чем в их
    /// границах: склеивает двух похожих или разваливает одного простуженного.
    /// Сказать ему число — самый сильный рычаг, и стоит это минуту
    /// процессора, а не повторное распознавание.
    /// </remarks>
    private StackPanel ResplitRow()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, Tokens.Space3) };

        // Выбор числа и отдельная кнопка — а не ряд цифр, где нажатие на
        // цифру сразу запускало разделение: передумать было нельзя, а
        // промахнуться — легко. «Отмена» сворачивает всё обратно в ссылку.
        TextBlock hint = Ui.Caption(L.S.VoicesResplitCount);
        hint.Margin = new Thickness(0, 0, 0, Tokens.Space1);
        panel.Children.Add(hint);

        int found = Math.Max(_transcript!.Voices.Count, 1);
        var count = new System.Windows.Controls.ComboBox { MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Left };
        for (int n = 2; n <= 8; n++)
        {
            count.Items.Add(n);
        }

        count.SelectedItem = Math.Clamp(found + 1, 2, 8);
        panel.Children.Add(count);

        var go = new UiButton
        {
            Content = L.S.VoicesResplitGo,
            Appearance = ControlAppearance.Primary,
            Margin = new Thickness(0, 0, Tokens.Space2, 0),
        };
        go.Click += (_, _) =>
        {
            if (count.SelectedItem is int wanted)
            {
                Resplit(wanted);
                Reload(keepSelection: true);
            }
        };

        var cancel = new UiButton { Content = L.S.ButtonCancel };
        cancel.Click += (_, _) =>
        {
            _resplitOpen = false;
            ShowVoices();
        };

        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, Tokens.Space3, 0, 0),
            Children = { go, cancel },
        });

        return panel;
    }

    /// <summary>
    /// Назвать человека — все его голоса разом.
    /// </summary>
    /// <remarks>
    /// Имя — это человек. Имя того, кто уже есть на звонке, присоединяет
    /// голос к нему; новое имя у названного — переименование на этом звонке.
    /// Раньше второй голос с тем же именем отнимал имя у первого, а склеить
    /// два куска одного человека можно было только безвозвратно.
    /// </remarks>
    private void NamePerson(CallPerson person, string? name, IReadOnlyList<CallPerson> people)
    {
        if (person.IsMe)
        {
            return;
        }

        // «павел» и «Павел» — один человек; пишем так, как он уже назван.
        if (name is not null && people.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) is { Name: { } same })
        {
            name = same;
        }

        if (IsWholeSide(person))
        {
            SetOtherSideName(name);
            return;
        }

        SetNames(person.Voices, name);
    }

    /// <summary>Дать голосам имя — или снять его, отделив голоса от человека.</summary>
    private void SetNames(IReadOnlyList<string> voices, string? name)
    {
        if (_session is null)
        {
            return;
        }

        string directory = _session.Directory;
        _session = CallMeta.Update(directory, s =>
        {
            var names = new Dictionary<string, string>(s.VoiceNames);
            foreach (string voice in voices)
            {
                if (name is null)
                {
                    names.Remove(voice);
                }
                else
                {
                    names[voice] = name;
                }
            }

            return s with { VoiceNames = names };
        }) ?? _session;

        if (name != CallSession.Me)
        {
            RememberName(name);
            foreach (string voice in voices)
            {
                LearnVoice(voice, name);
            }
        }

        _services.Render(directory);
        RefreshAfterEdit();
    }

    private void SetOtherSideName(string? name)
    {
        if (_session is null)
        {
            return;
        }

        string directory = _session.Directory;
        _session = CallMeta.Update(directory, s => s with { Participants = name is null ? [] : [name] }) ?? _session;

        RememberName(name);
        LearnVoice(CallVoices.WholeOtherSide, name);
        _ = _services.Reconcile(directory);
        RefreshAfterEdit();
    }

    /// <summary>Поднять имя в начало знакомых: на следующем звонке оно будет первым чипом.</summary>
    private void RememberName(string? name)
    {
        if (name is not null)
        {
            _services.Settings.Update(s => s with
            {
                KnownParticipants = [.. KnownParticipants.Touch(s.KnownParticipants, [name])],
            });
        }
    }
}
