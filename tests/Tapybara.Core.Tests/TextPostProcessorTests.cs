using Tapybara.Core.Speech;
using Xunit;

namespace Tapybara.Core.Tests;

/// <summary>Пост-обработка распознанного текста.</summary>
public class TextPostProcessorTests
{
    // --- фильтр галлюцинаций ------------------------------------------------

    [Theory]
    [InlineData("Продолжение следует...")]
    [InlineData("Спасибо за просмотр!")]
    [InlineData("Субтитры сделал DimaTorzok")]
    [InlineData("Thanks for watching")]
    [InlineData("[музыка]")]
    [InlineData("(Аплодисменты)")]
    [InlineData("♪♪♪")]
    [InlineData("...")]
    [InlineData("   ")]
    public void IsHallucination_DetectsKnownJunk(string text) =>
        Assert.True(TextPostProcessor.IsHallucination(text));

    [Theory]
    [InlineData("Спасибо, я подумаю.")]
    [InlineData("Всем пока, до завтра.")]
    [InlineData("Мне нравится музыка Шостаковича.")]
    [InlineData("Это звучит логично.")]
    [InlineData("Thanks for the update.")]
    public void IsHallucination_KeepsOrdinarySpeech(string text) =>
        Assert.False(TextPostProcessor.IsHallucination(text));

    /// <summary>
    /// Главный тест этого файла.
    /// </summary>
    /// <remarks>
    /// Прежняя реализация проверяла на галлюцинацию ВЕСЬ склеенный текст
    /// диктовки и искала маркер в любом его месте. Пятиминутная диктовка, в
    /// которой человек произнёс «подписывайтесь на канал», выбрасывалась
    /// целиком — вместе со звуком, которого уже нет.
    /// </remarks>
    [Fact]
    public void RemoveHallucinations_DropsOnlyTheOffendingSegment()
    {
        TranscriptSegment[] segments =
        [
            new(TimeSpan.Zero, TimeSpan.FromSeconds(3), "Надо переписать раздел про подписки."),
            new(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5), "Подписывайтесь на канал"),
            new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(9), "И добавить пример с формой."),
        ];

        IReadOnlyList<TranscriptSegment> kept = TextPostProcessor.RemoveHallucinations(segments);

        Assert.Equal(2, kept.Count);
        Assert.Equal("Надо переписать раздел про подписки.", kept[0].Text);
        Assert.Equal("И добавить пример с формой.", kept[1].Text);
    }

    [Fact]
    public void RemoveHallucinations_KeepsEverythingWhenNothingMatches()
    {
        TranscriptSegment[] segments =
        [
            new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "Первое."),
            new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), "Второе."),
        ];

        Assert.Equal(2, TextPostProcessor.RemoveHallucinations(segments).Count);
    }

    // --- склейка в абзацы ---------------------------------------------------

    [Fact]
    public void JoinSegments_StartsNewParagraphAfterLongPause()
    {
        TranscriptSegment[] segments =
        [
            new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "Первая мысль."),
            new(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6), "Вторая мысль."),
        ];

        string text = TextPostProcessor.JoinSegments(segments, TimeSpan.FromSeconds(1.5));

        Assert.Equal("Первая мысль.\n\nВторая мысль.", text);
    }

    [Fact]
    public void JoinSegments_JoinsWithSpaceWhenPauseIsShort()
    {
        TranscriptSegment[] segments =
        [
            new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "Начало фразы"),
            new(TimeSpan.FromSeconds(2.2), TimeSpan.FromSeconds(4), "и её продолжение."),
        ];

        string text = TextPostProcessor.JoinSegments(segments, TimeSpan.FromSeconds(1.5));

        Assert.Equal("Начало фразы и её продолжение.", text);
    }

    [Fact]
    public void JoinSegments_NeverSplitsWhenPauseIsZero()
    {
        TranscriptSegment[] segments =
        [
            new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "Раз."),
            new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(32), "Два."),
        ];

        Assert.Equal("Раз. Два.", TextPostProcessor.JoinSegments(segments, TimeSpan.Zero));
    }

    [Fact]
    public void JoinSegments_ReturnsEmptyForNoSegments() =>
        Assert.Equal(string.Empty, TextPostProcessor.JoinSegments([], TimeSpan.FromSeconds(1)));

    // --- словарь замен ------------------------------------------------------

    [Fact]
    public void ApplyReplacements_MatchesWholeWordsIgnoringCase()
    {
        var replacements = new Dictionary<string, string> { ["хакинг фейс"] = "Hugging Face" };

        Assert.Equal(
            "Модель лежит на Hugging Face.",
            TextPostProcessor.ApplyReplacements("Модель лежит на Хакинг Фейс.", replacements));
    }

    [Fact]
    public void ApplyReplacements_DoesNotMatchInsideAnotherWord()
    {
        var replacements = new Dictionary<string, string> { ["кот"] = "пёс" };

        Assert.Equal("котлета", TextPostProcessor.ApplyReplacements("котлета", replacements));
    }

    /// <summary>
    /// То, ради чего словарь замен обычно и заводят.
    /// </summary>
    /// <remarks>
    /// Шаблон вида <c>\bКЛЮЧ\b</c> для ключа, оканчивающегося небуквенным
    /// символом, не совпадает НИ С ЧЕМ: границы слова после «#» не существует.
    /// «C#», «C++» и «.NET» — как раз те названия, которые ASR стабильно
    /// коверкает.
    /// </remarks>
    [Theory]
    [InlineData("Пишу на си шарп каждый день.", "си шарп", "C#", "Пишу на C# каждый день.")]
    [InlineData("Это си плюс плюс.", "си плюс плюс", "C++", "Это C++.")]
    [InlineData("Работает на дотнет.", "дотнет", ".NET", "Работает на .NET.")]
    public void ApplyReplacements_HandlesKeysAndValuesWithPunctuation(
        string input,
        string from,
        string to,
        string expected)
    {
        var replacements = new Dictionary<string, string> { [from] = to };

        Assert.Equal(expected, TextPostProcessor.ApplyReplacements(input, replacements));
    }

    [Fact]
    public void ApplyReplacements_MatchesKeyThatEndsWithPunctuation()
    {
        var replacements = new Dictionary<string, string> { ["C#"] = "C-sharp" };

        Assert.Equal("Люблю C-sharp.", TextPostProcessor.ApplyReplacements("Люблю C#.", replacements));
    }

    [Fact]
    public void ApplyReplacements_TreatsDollarInValueAsLiteral()
    {
        var replacements = new Dictionary<string, string> { ["доллар"] = "$1" };

        Assert.Equal("Цена $1.", TextPostProcessor.ApplyReplacements("Цена доллар.", replacements));
    }

    [Fact]
    public void ApplyReplacements_IgnoresEmptyKey()
    {
        var replacements = new Dictionary<string, string> { [string.Empty] = "нечто" };

        Assert.Equal("без изменений", TextPostProcessor.ApplyReplacements("без изменений", replacements));
    }

    [Fact]
    public void ApplyReplacements_ReturnsInputWhenDictionaryIsEmpty() =>
        Assert.Equal("как было", TextPostProcessor.ApplyReplacements("как было", new Dictionary<string, string>()));
}
