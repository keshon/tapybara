using Tapybara.Core.Calls;
using Xunit;

namespace Tapybara.Core.Tests;

/// <summary>Имя человека в тексте: формы, склонение, переименование, анонимизация.</summary>
public sealed class PersonNamesTests
{
    private static CallTranscript Call(params string[] lines) => new()
    {
        Lines = [.. lines.Select((t, i) => new CallLine(CallChannel.Theirs, TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(i + 1), t, "A"))],
    };

    private static CallSession Session(Dictionary<string, string> names, params string[] participants) => new()
    {
        Directory = @"C:\calls\x",
        StartedAt = DateTimeOffset.Now,
        Participants = participants,
        VoiceNames = names,
    };

    [Theory]
    [InlineData("Кирилл", GrammaticalCase.Dative, "Кириллу")]
    [InlineData("Кирилл", GrammaticalCase.Instrumental, "Кириллом")]
    [InlineData("Павел", GrammaticalCase.Genitive, "Павла")]
    [InlineData("Павел", GrammaticalCase.Nominative, "Павел")]
    [InlineData("Лев", GrammaticalCase.Dative, "Льву")]
    [InlineData("Витя", GrammaticalCase.Accusative, "Витю")]
    [InlineData("Мария", GrammaticalCase.Dative, "Марии")]
    [InlineData("Мария", GrammaticalCase.Prepositional, "Марии")]
    [InlineData("Саша", GrammaticalCase.Genitive, "Саши")]
    [InlineData("Маша", GrammaticalCase.Instrumental, "Машей")]
    [InlineData("Андрей", GrammaticalCase.Prepositional, "Андрее")]
    [InlineData("Дмитрий", GrammaticalCase.Prepositional, "Дмитрии")]
    [InlineData("Игорь", GrammaticalCase.Instrumental, "Игорем")]
    [InlineData("Участник 2", GrammaticalCase.Dative, "Участник 2")]
    [InlineData("John", GrammaticalCase.Dative, "John")]
    public void Inflect_DeclinesRussianNamesAndLeavesTheRest(string name, GrammaticalCase grammaticalCase, string expected) =>
        Assert.Equal(expected, PersonNames.Inflect(name, grammaticalCase));

    [Fact]
    public void Find_CountsFormsOfTheNameOnly()
    {
        CallTranscript call = Call("Скажу Кириллу, что с кириллом всё решили.", "Кирилл, привет. Кириллица тут ни при чём.");

        IReadOnlyList<NameMention> mentions = PersonNames.Find([call], "Кирилл");

        Assert.Equal(["кирилл", "кириллу", "кириллом"], mentions.Select(m => m.Form));
        Assert.All(mentions, m => Assert.Equal(1, m.Count));
    }

    /// <summary>«Костя» и «кости» — одно и то же по буквам: формы решают, а не похожесть.</summary>
    [Fact]
    public void Find_KeepsToTheNamesForms()
    {
        IReadOnlyList<NameMention> mentions = PersonNames.Find([Call("Костя сказал, что Косте виднее.", "Костыль не Костя.")], "Костя");

        Assert.Equal(["костя", "косте"], mentions.Select(m => m.Form));
    }

    [Fact]
    public void Find_FindsNamesOfSeveralWords()
    {
        IReadOnlyList<NameMention> mentions = PersonNames.Find([Call("Участник 2 не пришёл, Участник 22 тоже.")], "Участник 2");

        Assert.Equal(1, Assert.Single(mentions).Count);
    }

    [Fact]
    public void Replace_PutsTheNewNameInTheSameCase()
    {
        CallTranscript call = Call("Скажу Кириллу, что с кириллом всё решили.");

        (CallTranscript renamed, int replaced) = PersonNames.Replace(call, PersonNames.Find([call], "Кирилл"), "Шериф");

        Assert.Equal(2, replaced);
        Assert.Equal("Скажу Шерифу, что с Шерифом всё решили.", renamed.Lines[0].Text);
        Assert.True(renamed.EditedByHand);
    }

    /// <summary>Регрессия: форма, только что вставленная заменой, не переписывается следующей.</summary>
    [Fact]
    public void Replace_DoesNotRewriteWhatItJustWrote()
    {
        CallTranscript call = Call("Ян сказал, что у Яна всё готово.");

        (CallTranscript renamed, _) = PersonNames.Replace(call, PersonNames.Find([call], "Ян"), "Яна");

        Assert.Equal("Яна сказал, что у Яны всё готово.", renamed.Lines[0].Text);
    }

    [Fact]
    public void Rename_ChangesParticipantsAndVoices_AndMergesWithSomeoneAlreadyThere()
    {
        CallSession session = Session(new() { ["A"] = "кирилл", ["B"] = "Витя" }, "Кирилл", "Витя");

        CallSession renamed = PersonNames.Rename(session, "Кирилл", "Витя");

        Assert.Equal(["Витя"], renamed.Participants);
        Assert.Equal("Витя", renamed.VoiceNames["A"]);
    }

    [Fact]
    public void Anonymize_ChangesNamesAndMentions_ButNotTheOriginal()
    {
        CallSession session = Session(new() { ["A"] = "Кирилл" }) with { Title = "Созвон с Кириллом" };
        CallTranscript call = Call("Кириллу я уже писал.");

        (CallSession outSession, CallTranscript outCall) = PersonNames.Anonymize(
            session, call, new Dictionary<string, string> { ["Кирилл"] = "Шериф" }, mentions: true);

        Assert.Equal("Шериф", outSession.VoiceNames["A"]);
        Assert.Equal("Созвон с Шерифом", outSession.Title);
        Assert.Equal("Шерифу я уже писал.", outCall.Lines[0].Text);
        Assert.Equal("Кириллу я уже писал.", call.Lines[0].Text);
    }

    /// <summary>Регрессия: обмен двух имён не сливает людей в одного.</summary>
    [Fact]
    public void Anonymize_SwapsNamesAtOnce()
    {
        CallSession session = Session(new() { ["A"] = "Кирилл", ["B"] = "Павел" }, "Кирилл", "Павел");

        (CallSession outSession, CallTranscript outCall) = PersonNames.Anonymize(
            session,
            Call("Кирилл и Павел"),
            new Dictionary<string, string> { ["Кирилл"] = "Павел", ["Павел"] = "Кирилл" },
            mentions: true);

        Assert.Equal(["Павел", "Кирилл"], outSession.Participants);
        Assert.Equal("Павел и Кирилл", outCall.Lines[0].Text);
    }

    /// <summary>Свой голос в чужой дорожке не мешает подписать единственного собеседника.</summary>
    [Fact]
    public void NameOf_DoesNotCountTheOwnersVoiceAsAnotherPerson()
    {
        CallSession session = Session(new() { ["A"] = CallSession.Me }, "Витя") with { Voices = ["A", "B"] };

        Assert.Equal("Витя", CallSpeakers.NameOf(session, "B"));
        Assert.False(CallSpeakers.NeedsNames(session));
    }
}
