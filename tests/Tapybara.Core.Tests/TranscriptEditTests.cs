using Tapybara.Core.Calls;
using Xunit;

namespace Tapybara.Core.Tests;

/// <summary>Правка текста готового транскрипта.</summary>
public sealed class TranscriptEditTests
{
    private static CallLine Line(double at, string text, CallChannel channel = CallChannel.Theirs) =>
        new(channel, TimeSpan.FromSeconds(at), TimeSpan.FromSeconds(at + 3), text, "A");

    private static CallTranscript Call(params string[] texts) =>
        new() { Lines = [.. texts.Select((t, i) => Line(i * 5, t))] };

    /// <summary>Искажения и падежи одного слова находятся вместе, чужие слова — нет.</summary>
    [Fact]
    public void SimilarForms_FindsMisspellingsAndCases()
    {
        CallTranscript call = Call(
            "Данные в рикстате за прошлый год.",
            "Рекстат не отдаёт статусы, в рекстате пусто.",
            "Рикстат и битрикс — разные системы, рекстата нет.",
            "Мы это стартуем завтра.");

        IReadOnlyList<TranscriptEdit.WordForm> forms = TranscriptEdit.SimilarForms(call, "рикстате");

        Assert.Equal("рикстате", forms[0].Form);
        Assert.Contains(forms, f => f.Form == "рекстат" && f.Count == 1);
        Assert.Contains(forms, f => f.Form == "рикстат" && f.Count == 1);
        Assert.Contains(forms, f => f.Form == "рекстате");
        Assert.Contains(forms, f => f.Form == "рекстата");
        Assert.DoesNotContain(forms, f => f.Form is "битрикс" or "стартуем" or "статусы");
    }

    [Fact]
    public void SimilarForms_KeepsShortWordsExact()
    {
        CallTranscript call = Call("Кот ест код, кот спит.");

        IReadOnlyList<TranscriptEdit.WordForm> forms = TranscriptEdit.SimilarForms(call, "кот");

        Assert.Equal([new TranscriptEdit.WordForm("кот", 2)], forms);
    }

    [Fact]
    public void Replace_ChangesWholeWordsOnly_AndCounts()
    {
        CallTranscript call = Call("Рикстат и рекстата.", "Рикстатный отчёт не трогаем, рикстат — да.");

        (CallTranscript fixedCall, int replaced) = TranscriptEdit.Replace(call, ["рикстат", "рекстата"], "Regstat");

        Assert.Equal(3, replaced);
        Assert.Equal("Regstat и Regstat.", fixedCall.Lines[0].Text);
        Assert.Equal("Рикстатный отчёт не трогаем, Regstat — да.", fixedCall.Lines[1].Text);
        Assert.True(fixedCall.EditedByHand);
        Assert.Equal("A", fixedCall.Lines[0].Voice);
    }

    [Fact]
    public void Replace_WithNothingFound_LeavesTheCallUntouched()
    {
        CallTranscript call = Call("Всё верно.");

        (CallTranscript same, int replaced) = TranscriptEdit.Replace(call, ["рикстат"], "Regstat");

        Assert.Equal(0, replaced);
        Assert.Same(call, same);
        Assert.False(same.EditedByHand);
    }

    [Fact]
    public void ReplaceAt_ChangesOnlyThatPlace()
    {
        CallTranscript call = Call("рикстат тут и рикстат там");

        CallTranscript fixedCall = TranscriptEdit.ReplaceAt(call, call.Lines[0], 14, 7, "Regstat");

        Assert.Equal("рикстат тут и Regstat там", fixedCall.Lines[0].Text);
    }

    [Fact]
    public void EditLine_ReplacesTheText()
    {
        CallTranscript call = Call("Было так", "И так");

        CallTranscript fixedCall = TranscriptEdit.EditLine(call, call.Lines[1], "  Стало иначе  ");

        Assert.Equal("Стало иначе", fixedCall.Lines[1].Text);
        Assert.Equal("Было так", fixedCall.Lines[0].Text);
        Assert.True(fixedCall.EditedByHand);
    }

    [Theory]
    [InlineData("мы в рикстате", 7, 5, 8)]
    [InlineData("мы в рикстате", 5, 5, 8)]
    [InlineData("мы в рикстате", 13, 5, 8)]
    public void WordAt_FindsTheWordUnderTheCaret(string text, int index, int start, int length) =>
        Assert.Equal((start, length), TranscriptEdit.WordAt(text, index));

    /// <summary>Словарь к готовому звонку: все варианты одного слова, правильное не считается правкой.</summary>
    [Fact]
    public void ApplyDictionary_FixesEveryVariant()
    {
        CallTranscript call = Call("Рикстат и рекстат, а Regstat уже верно.", "Хакинг фейс тут.");
        var dictionary = new Dictionary<string, string>
        {
            ["рикстат"] = "Regstat",
            ["рекстат"] = "Regstat",
            ["regstat"] = "Regstat",
            ["хакинг фейс"] = "Hugging Face",
        };

        (CallTranscript fixedCall, int replaced) = TranscriptEdit.ApplyDictionary(call, dictionary);

        Assert.Equal(3, replaced);
        Assert.Equal("Regstat и Regstat, а Regstat уже верно.", fixedCall.Lines[0].Text);
        Assert.Equal("Hugging Face тут.", fixedCall.Lines[1].Text);
    }

    [Fact]
    public void Distance_CountsEdits()
    {
        Assert.Equal(0, TranscriptEdit.Distance("рекстат", "рекстат"));
        Assert.Equal(1, TranscriptEdit.Distance("рикстат", "рекстат"));
        Assert.Equal(2, TranscriptEdit.Distance("рикстат", "рекстата"));
    }
}
