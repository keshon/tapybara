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

    [Theory]
    [InlineData("Кирилл", 2, "Кириллу")]
    [InlineData("Кирилл", 4, "Кириллом")]
    [InlineData("Павел", 1, "Павла")]
    [InlineData("Павел", 0, "Павел")]
    [InlineData("Витя", 3, "Витю")]
    [InlineData("Саша", 1, "Саши")]
    [InlineData("Маша", 4, "Машей")]
    [InlineData("Андрей", 2, "Андрею")]
    [InlineData("Игорь", 4, "Игорем")]
    [InlineData("Участник 2", 2, "Участник 2")]
    [InlineData("John", 2, "John")]
    public void Inflect_DeclinesRussianNamesAndLeavesTheRest(string name, int grammaticalCase, string expected) =>
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
    public void Replace_PutsTheNewNameInTheSameCase()
    {
        CallTranscript call = Call("Скажу Кириллу, что с кириллом всё решили.");

        (CallTranscript renamed, int replaced) = PersonNames.Replace(call, PersonNames.Find([call], "Кирилл"), "Шериф");

        Assert.Equal(2, replaced);
        Assert.Equal("Скажу Шерифу, что с Шерифом всё решили.", renamed.Lines[0].Text);
        Assert.True(renamed.EditedByHand);
    }

    [Fact]
    public void Rename_ChangesParticipantsAndVoices_AndMergesWithSomeoneAlreadyThere()
    {
        var session = new CallSession
        {
            Directory = @"C:\calls\x",
            StartedAt = DateTimeOffset.Now,
            Participants = ["Кирилл", "Витя"],
            VoiceNames = new Dictionary<string, string> { ["A"] = "кирилл", ["B"] = "Витя" },
        };

        CallSession renamed = PersonNames.Rename(session, "Кирилл", "Витя");

        Assert.Equal(["Витя"], renamed.Participants);
        Assert.Equal("Витя", renamed.VoiceNames["A"]);
    }

    [Fact]
    public void Anonymize_ChangesNamesAndMentions_ButNotTheOriginal()
    {
        var session = new CallSession
        {
            Directory = @"C:\calls\x",
            StartedAt = DateTimeOffset.Now,
            Title = "Созвон с Кириллом",
            VoiceNames = new Dictionary<string, string> { ["A"] = "Кирилл" },
        };
        CallTranscript call = Call("Кириллу я уже писал.");

        (CallSession outSession, CallTranscript outCall) = PersonNames.Anonymize(
            session, call, new Dictionary<string, string> { ["Кирилл"] = "Шериф" }, mentions: true);

        Assert.Equal("Шериф", outSession.VoiceNames["A"]);
        Assert.Equal("Созвон с Шерифом", outSession.Title);
        Assert.Equal("Шерифу я уже писал.", outCall.Lines[0].Text);
        Assert.Equal("Кириллу я уже писал.", call.Lines[0].Text);
    }
}
