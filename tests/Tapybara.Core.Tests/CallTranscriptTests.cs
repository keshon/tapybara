using Tapybara.Core.Calls;
using Tapybara.Core.Settings;
using Tapybara.Core.Speech;
using Xunit;

namespace Tapybara.Core.Tests;

/// <summary>Реплики звонка, голоса и имена, которые им дают.</summary>
public sealed class CallTranscriptTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tapybara-transcript-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static CallLine Mine(double from, double to, string text) => new(CallChannel.Mine, S(from), S(to), text);

    private static CallLine Theirs(double from, double to, string text, string? voice = null) =>
        new(CallChannel.Theirs, S(from), S(to), text, voice);

    private static SpeakerSpan Span(double from, double to, int speaker) => new(S(from), S(to), speaker);

    private static CallSession Session(
        IReadOnlyList<string>? participants = null,
        IReadOnlyList<string>? voices = null,
        Dictionary<string, string>? names = null) => new()
        {
            Directory = @"C:\calls\2026-09-23 14-02-11 (Zoom)",
            StartedAt = new DateTimeOffset(2026, 9, 23, 14, 2, 11, TimeSpan.FromHours(3)),
            Duration = TimeSpan.FromMinutes(47),
            Participants = participants ?? [],
            Voices = voices ?? [],
            VoiceNames = names ?? [],
        };

    private static string Label(CallLine line, CallSession session, int voiceCount) =>
        CallSpeakers.Label(line, session, voiceCount, "Иннокентий", "Собеседник", "Голос");

    // --- буквы и разметка ---------------------------------------------------

    [Theory]
    [InlineData(0, "A")]
    [InlineData(1, "B")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(27, "AB")]
    public void Letter_CountsLikeSpreadsheetColumns(int index, string expected) =>
        Assert.Equal(expected, CallVoices.Letter(index));

    /// <summary>
    /// Буквы — по порядку появления, а не по номерам кластеров.
    /// </summary>
    /// <remarks>
    /// Разделитель нумерует кластеры с дырами и в своём порядке. Первым
    /// заговоривший голос обязан быть «A», какой бы номер ему ни дали.
    /// </remarks>
    [Fact]
    public void Assign_LettersVoicesByFirstAppearance()
    {
        CallLine[] lines = [Theirs(0, 2, "раз"), Theirs(3, 5, "два"), Theirs(6, 8, "три")];
        SpeakerSpan[] spans = [Span(0, 2.5, 7), Span(2.5, 5.5, 2), Span(5.5, 9, 7)];

        IReadOnlyList<CallLine> assigned = CallVoices.Assign(lines, spans);

        Assert.Equal(["A", "B", "A"], assigned.Select(l => l.Voice));
    }

    [Fact]
    public void Assign_LeavesOwnLinesAlone()
    {
        CallLine[] lines = [Mine(0, 2, "я"), Theirs(0, 2, "они")];

        IReadOnlyList<CallLine> assigned = CallVoices.Assign(lines, [Span(0, 3, 0)]);

        Assert.Null(assigned[0].Voice);
        Assert.Equal("A", assigned[1].Voice);
    }

    /// <summary>Реплика вне найденных участков остаётся без голоса, а не получает ближайший.</summary>
    [Fact]
    public void Assign_DoesNotGuessForLinesOutsideAnyVoice()
    {
        IReadOnlyList<CallLine> assigned = CallVoices.Assign([Theirs(10, 12, "эхо")], [Span(0, 3, 0)]);

        Assert.Null(assigned[0].Voice);
    }

    [Fact]
    public void Voices_ListsInOrderOfAppearanceWithoutRepeats()
    {
        var transcript = new CallTranscript
        {
            Lines = [Theirs(5, 6, "x", "B"), Theirs(1, 2, "y", "A"), Theirs(7, 8, "z", "A"), Mine(0, 1, "я")],
        };

        Assert.Equal(["A", "B"], transcript.Voices);
    }

    // --- люди ---------------------------------------------------------------

    private static CallTranscript FourVoices() => new()
    {
        Lines =
        [
            Theirs(0, 3, "Установщик собирается, но подпись не проходит", "A"),
            Theirs(3, 4, "да", "A"),
            Theirs(10, 12, "Я продлила сертификат вчера вечером, он в хранилище", "B"),
            Theirs(20, 21, "Слышно меня? Я в машине, пишите в чат", "C"),
            Theirs(22, 24, "Эхо моего же голоса из динамиков ноутбука", "D"),
            Mine(30, 40, "своя речь тоже часть звонка"),
        ],
    };

    [Fact]
    public void People_MeFirst_SharesOfTheWholeCallAddUp()
    {
        IReadOnlyList<CallPerson> people = CallPeople.Of(Session(voices: ["A", "B", "C", "D"]), FourVoices());

        Assert.True(people[0].IsMe);
        Assert.Equal(S(10), people[0].Speech);
        Assert.Equal(1.0, people.Sum(p => p.Share), precision: 6);

        // «да» в цитаты не попадает: по нему никого не узнать.
        Assert.Single(people[1].Quotes);
    }

    /// <summary>
    /// Два голоса с одним именем — один человек, а не имя, отнятое у первого.
    /// </summary>
    [Fact]
    public void People_VoicesWithOneNameAreOnePerson()
    {
        CallSession session = Session(
            voices: ["A", "B", "C", "D"],
            names: new() { ["A"] = "Павел", ["C"] = "павел" });

        IReadOnlyList<CallPerson> people = CallPeople.Of(session, FourVoices());

        CallPerson pavel = people.Single(p => p.Name == "Павел");
        Assert.Equal(["A", "C"], pavel.Voices);
        Assert.Equal(S(5), pavel.Speech);
        Assert.Equal(0, pavel.Color);
        Assert.Equal(["B", "D"], people.Where(p => p.Name is null && !p.IsMe).Select(p => p.Voices[0]));
        Assert.Equal(0, CallPeople.Colors(people)["C"]);
    }

    /// <summary>Свой голос, попавший в чужую дорожку, считается своим.</summary>
    [Fact]
    public void People_VoiceNamedMeCountsAsMine()
    {
        CallSession session = Session(voices: ["A", "B", "C", "D"], names: new() { ["D"] = CallSession.Me });

        IReadOnlyList<CallPerson> people = CallPeople.Of(session, FourVoices());

        Assert.Equal(["D"], people[0].Voices);
        Assert.Equal(S(12), people[0].Speech);
        Assert.Equal(4, people.Count);
        Assert.Equal("Иннокентий", Label(Theirs(22, 24, "эхо", "D"), session, 4));
    }

    [Fact]
    public void People_UnsplitOtherSideIsOnePerson()
    {
        var transcript = new CallTranscript { Lines = [Theirs(0, 4, "без разделения"), Mine(4, 8, "я")] };

        IReadOnlyList<CallPerson> people = CallPeople.Of(Session(participants: ["Кирилл"]), transcript);

        Assert.Equal(2, people.Count);
        Assert.Equal("Кирилл", people[1].Name);
        Assert.Equal([CallVoices.WholeOtherSide], people[1].Voices);
        Assert.Equal(0.5, people[1].Share, precision: 6);
    }

    [Fact]
    public void Reassign_MovesOnlyThatLine()
    {
        CallLine target = Theirs(1, 2, "b", "A");
        var transcript = new CallTranscript { Lines = [Theirs(0, 1, "a", "A"), target] };

        CallTranscript changed = CallVoices.Reassign(transcript, target, "B");

        Assert.Equal(["A", "B"], changed.Lines.Select(l => l.Voice));
    }

    // --- имена --------------------------------------------------------------

    /// <summary>
    /// Отмеченные участники НЕ раздаются голосам по порядку появления.
    /// </summary>
    /// <remarks>
    /// Регрессия, ради которой переделывались звонки. Первое имя из списка
    /// доставалось тому, кто заговорил первым, и при двух собеседниках
    /// транскрипт в половине случаев уверенно путал людей. Пока человек не
    /// назвал голоса, они подписаны буквами.
    /// </remarks>
    [Fact]
    public void Label_DoesNotHandParticipantsOutByOrderOfAppearance()
    {
        CallSession session = Session(participants: ["Кирилл", "Марина"], voices: ["A", "B"]);

        Assert.Equal("Голос A", Label(Theirs(0, 1, "x", "A"), session, voiceCount: 2));
        Assert.Equal("Голос B", Label(Theirs(1, 2, "y", "B"), session, voiceCount: 2));
    }

    [Fact]
    public void Label_UsesTheNameTheUserGaveTheVoice()
    {
        CallSession session = Session(
            participants: ["Кирилл", "Марина"],
            voices: ["A", "B"],
            names: new() { ["B"] = "Марина" });

        Assert.Equal("Марина", Label(Theirs(1, 2, "y", "B"), session, voiceCount: 2));
        Assert.Equal("Голос A", Label(Theirs(0, 1, "x", "A"), session, voiceCount: 2));
    }

    [Fact]
    public void Label_OwnLinesCarryTheOwnersName() =>
        Assert.Equal("Иннокентий", Label(Mine(0, 1, "я"), Session(), voiceCount: 0));

    /// <summary>Один отмеченный собеседник подписывает чужой канал, пока голос там один.</summary>
    [Fact]
    public void Label_SingleParticipantNamesTheWholeOtherSide()
    {
        Assert.Equal("Кирилл", Label(Theirs(0, 1, "x"), Session(participants: ["Кирилл"]), voiceCount: 0));
        Assert.Equal("Кирилл", Label(Theirs(0, 1, "x", "A"), Session(participants: ["Кирилл"], voices: ["A"]), voiceCount: 1));
    }

    /// <summary>
    /// Отметили одного, а голосов разделили несколько — значит, человек сам
    /// сказал «их было больше», и раздавать всем одно имя нельзя.
    /// </summary>
    [Fact]
    public void Label_SingleParticipantDoesNotNameSeveralVoices()
    {
        CallSession session = Session(participants: ["Витя"], voices: ["A", "B"]);

        Assert.Equal("Голос A", Label(Theirs(0, 1, "x", "A"), session, voiceCount: 2));
        Assert.Equal("Голос B", Label(Theirs(1, 2, "y", "B"), session, voiceCount: 2));
        Assert.True(CallSpeakers.NeedsNames(session));
    }

    [Fact]
    public void Label_LoneUnnamedVoiceIsJustTheOtherSide() =>
        Assert.Equal("Собеседник", Label(Theirs(0, 1, "x", "A"), Session(voices: ["A"]), voiceCount: 1));

    [Fact]
    public void NeedsNames_OnlyWhenSeveralVoicesAndOneIsUnnamed()
    {
        Assert.True(CallSpeakers.NeedsNames(Session(voices: ["A", "B"])));
        Assert.True(CallSpeakers.NeedsNames(Session(voices: ["A", "B"], names: new() { ["A"] = "Кирилл" })));
        Assert.False(CallSpeakers.NeedsNames(Session(voices: ["A", "B"], names: new() { ["A"] = "Кирилл", ["B"] = "Марина" })));
        Assert.False(CallSpeakers.NeedsNames(Session(voices: ["A"])));
        Assert.False(CallSpeakers.NeedsNames(Session(participants: ["Кирилл"], voices: ["A"])));
    }

    // --- сколько голосов искать ---------------------------------------------

    [Fact]
    public void ExpectedVoices_FollowsTheParticipantCount()
    {
        var settings = new AppSettings();

        Assert.Equal(0, CallTranscriber.ExpectedVoices(Session(participants: ["Кирилл"]), settings));
        Assert.Equal(2, CallTranscriber.ExpectedVoices(Session(participants: ["Кирилл", "Марина"]), settings));
        Assert.Equal(-1, CallTranscriber.ExpectedVoices(Session(), settings));
        Assert.Equal(0, CallTranscriber.ExpectedVoices(
            Session(participants: ["Кирилл", "Марина"]), settings with { SplitVoices = false }));
    }

    // --- отрисовка ----------------------------------------------------------

    [Fact]
    public void Render_PutsNamesAndVoicesIntoTheTranscript()
    {
        CallSession session = Session(
            participants: ["Кирилл", "Марина"],
            voices: ["A", "B"],
            names: new() { ["A"] = "Кирилл" }) with { Title = "Созвон по релизу" };

        var transcript = new CallTranscript
        {
            Lines =
            [
                Mine(0, 2, "Все здесь?"),
                Theirs(3, 5, "Да, я тут.", "A"),
                Theirs(6, 8, "Слышно меня?", "B"),
            ],
            VoicesSplit = true,
            ExpectedVoices = 2,
        };

        string markdown = CallTranscriptRenderer.Render(
            session, transcript, "Иннокентий", "Собеседник", CallTranscriptLabels.Default);

        Assert.StartsWith("# Созвон по релизу", markdown, StringComparison.Ordinal);
        Assert.Contains("**[0:00] Иннокентий:** Все здесь?", markdown, StringComparison.Ordinal);
        Assert.Contains("**[0:03] Кирилл:** Да, я тут.", markdown, StringComparison.Ordinal);
        Assert.Contains("**[0:06] Voice B:** Слышно меня?", markdown, StringComparison.Ordinal);
        Assert.Contains("Voices told apart: 2 (with the participant count as a hint)", markdown, StringComparison.Ordinal);
    }

    /// <summary>Имя, данное голосу в окне звонка, попадает и в шапку участников.</summary>
    [Fact]
    public void Render_ListsVoiceNamesAmongParticipants()
    {
        CallSession session = Session(
            voices: ["A", "B", "C"],
            names: new() { ["A"] = "Дима", ["B"] = CallSession.Me, ["C"] = "Дима" });
        var transcript = new CallTranscript { Lines = [Theirs(0, 1, "x", "A"), Theirs(2, 3, "y", "B"), Theirs(4, 5, "z", "C")] };

        string markdown = CallTranscriptRenderer.Render(
            session, transcript, "Иннокентий", "Собеседник", CallTranscriptLabels.Default);

        // Себя второй раз не пишем, а голоса Димы — это один Дима.
        Assert.Contains("- Participants: Иннокентий, Дима\n", markdown.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("**[0:02] Иннокентий:** y", markdown, StringComparison.Ordinal);
    }

    // --- файлы --------------------------------------------------------------

    private CallSession CreateCall()
    {
        string directory = Path.Combine(_root, "2026-09-23 14-02-11 (Zoom)");
        Directory.CreateDirectory(directory);

        CallSession session = Session() with { Directory = directory };
        CallMeta.Save(session);
        File.WriteAllBytes(session.MicPath, new byte[4096]);
        return session;
    }

    [Fact]
    public void Store_RoundTripsLinesAndVoices()
    {
        CallSession session = CreateCall();
        var transcript = new CallTranscript
        {
            Lines = [Mine(0, 1.5, "Привет"), Theirs(2, 3.25, "Здравствуйте", "A")],
            VoicesSplit = true,
            ExpectedVoices = 2,
            RemovedByText = 3,
        };

        CallTranscriptStore.Save(session.Directory, transcript);
        CallTranscript? restored = CallTranscriptStore.Load(session.Directory);

        Assert.NotNull(restored);
        Assert.Equal(transcript.Lines, restored.Lines);
        Assert.True(restored.VoicesSplit);
        Assert.Equal(2, restored.ExpectedVoices);
        Assert.Equal(3, restored.RemovedByText);
    }

    /// <summary>
    /// Правка одного поля меты не стирает то, что записал кто-то другой.
    /// </summary>
    /// <remarks>
    /// Окно звонка держало снимок меты, прочитанный до конца распознавания,
    /// и, сохраняя участников, записывало его целиком — вместе с пустым
    /// списком голосов поверх только что найденных.
    /// </remarks>
    [Fact]
    public void MetaUpdate_KeepsFieldsWrittenByOthers()
    {
        CallSession session = CreateCall();
        CallMeta.Update(session.Directory, s => s with { Voices = ["A", "B"] });

        CallMeta.Update(session.Directory, s => s with { Participants = ["Кирилл"] });
        CallSession? restored = CallMeta.Load(session.Directory);

        Assert.NotNull(restored);
        Assert.Equal(["A", "B"], restored.Voices);
        Assert.Equal(["Кирилл"], restored.Participants);
    }

    [Fact]
    public void Library_FlagsCallsWithUnnamedVoices()
    {
        CallSession session = CreateCall();
        File.WriteAllText(session.TranscriptPath, "# звонок");
        CallTranscriptStore.Save(session.Directory, new CallTranscript { Lines = [Theirs(0, 1, "x", "A"), Theirs(1, 2, "y", "B")] });
        CallMeta.Update(session.Directory, s => s with { Voices = ["A", "B"] });

        Assert.Equal(CallState.NeedsNames, CallLibrary.Describe(session.Directory)!.State);

        CallMeta.Update(session.Directory, s => s with
        {
            VoiceNames = new Dictionary<string, string> { ["A"] = "Кирилл", ["B"] = "Марина" },
        });

        CallEntry entry = CallLibrary.Describe(session.Directory)!;
        Assert.Equal(CallState.Ready, entry.State);
        Assert.True(entry.HasTranscriptData);
    }

    /// <summary>Звонок, распознанный до появления transcript.json, честно помечен.</summary>
    [Fact]
    public void Library_TellsOldTranscriptsApart()
    {
        CallSession session = CreateCall();
        File.WriteAllText(session.TranscriptPath, "# звонок");

        CallEntry entry = CallLibrary.Describe(session.Directory)!;

        Assert.Equal(CallState.Ready, entry.State);
        Assert.False(entry.HasTranscriptData);
    }

    [Fact]
    public void RendererWrite_RebuildsTheMarkdownFromDisk()
    {
        CallSession session = CreateCall();
        CallTranscriptStore.Save(session.Directory, new CallTranscript { Lines = [Theirs(0, 1, "Привет", "A"), Theirs(2, 3, "Пока", "B")] });
        CallMeta.Update(session.Directory, s => s with
        {
            Voices = ["A", "B"],
            VoiceNames = new Dictionary<string, string> { ["B"] = "Марина" },
        });

        string? path = CallTranscriptRenderer.Write(session.Directory, "Иннокентий", "Собеседник", CallTranscriptLabels.Default);

        Assert.NotNull(path);
        string markdown = File.ReadAllText(path);
        Assert.Contains("Voice A:** Привет", markdown, StringComparison.Ordinal);
        Assert.Contains("Марина:** Пока", markdown, StringComparison.Ordinal);
    }
}
