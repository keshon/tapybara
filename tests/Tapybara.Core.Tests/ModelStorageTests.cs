using Tapybara.Core.Models;
using Xunit;

namespace Tapybara.Core.Tests;

/// <summary>Файлы моделей на диске.</summary>
public class ModelStorageTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "tapybara-tests", Guid.NewGuid().ToString("N"));

    private string From => Path.Combine(_root, "from");

    private string To => Path.Combine(_root, "to");

    public ModelStorageTests()
    {
        Directory.CreateDirectory(From);
        Directory.CreateDirectory(To);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Временная папка — не то, из-за чего тест должен падать.
        }

        GC.SuppressFinalize(this);
    }

    private static string Write(string directory, string name, int bytes = 16)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    // --- перечисление ------------------------------------------------------

    [Fact]
    public void List_IsEmptyForAMissingDirectory() =>
        Assert.Empty(ModelStorage.List(Path.Combine(_root, "nope")));

    [Fact]
    public void List_IsEmptyForNull() => Assert.Empty(ModelStorage.List(null));

    [Fact]
    public void List_IgnoresFilesThatAreNotModels()
    {
        Write(From, "ggml-small-q5_1.bin");
        Write(From, "notes.txt");
        Write(From, "something.bin");

        Assert.Single(ModelStorage.List(From));
    }

    /// <summary>
    /// Детектор и модель распознавания различаются только именем.
    /// </summary>
    /// <remarks>
    /// На этом разделении держатся оба списка в интерфейсе: silero в выборе
    /// модели распознавания выглядел бы как вариант, который ничего не
    /// распознаёт.
    /// </remarks>
    [Fact]
    public void List_SeparatesDetectorsFromRecognitionModels()
    {
        Write(From, "ggml-small-q5_1.bin");
        Write(From, "ggml-silero-v6.2.0.bin");

        IReadOnlyList<InstalledModel> models = ModelStorage.List(From);

        Assert.Equal(2, models.Count);
        Assert.Single(models, m => m.Kind == ModelKind.Recognition);
        Assert.Single(models, m => m.Kind == ModelKind.SpeechDetector);
    }

    [Theory]
    [InlineData("ggml-silero-v6.2.0.bin", true)]
    [InlineData("GGML-SILERO-V5.1.2.BIN", true)]
    [InlineData("ggml-small-q5_1.bin", false)]
    [InlineData(@"C:\models\ggml-silero-v6.2.0.bin", true)]
    public void IsSpeechDetector_RecognisesByName(string name, bool expected) =>
        Assert.Equal(expected, ModelStorage.IsSpeechDetector(name));

    [Fact]
    public void TotalBytes_SumsOnlyModels()
    {
        Write(From, "ggml-a.bin", 100);
        Write(From, "ggml-b.bin", 250);
        Write(From, "readme.txt", 9999);

        Assert.Equal(350, ModelStorage.TotalBytes(From));
    }

    // --- удаление ----------------------------------------------------------

    [Fact]
    public void Delete_RemovesTheFile()
    {
        string path = Write(From, "ggml-small-q5_1.bin");

        Assert.Null(ModelStorage.Delete(path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Delete_ReportsWhyItCouldNotWhenTheFileIsHeldOpen()
    {
        string path = Write(From, "ggml-locked.bin");

        using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        string? failure = ModelStorage.Delete(path);

        Assert.NotNull(failure);
        Assert.True(File.Exists(path));
    }

    // --- перенос -----------------------------------------------------------

    [Fact]
    public async Task MoveAllAsync_MovesEveryModel()
    {
        Write(From, "ggml-small-q5_1.bin");
        Write(From, "ggml-silero-v6.2.0.bin");

        MoveResult result = await ModelStorage.MoveAllAsync(From, To);

        Assert.Equal(2, result.Moved);
        Assert.Empty(result.Failed);
        Assert.Empty(ModelStorage.List(From));
        Assert.Equal(2, ModelStorage.List(To).Count);
    }

    [Fact]
    public async Task MoveAllAsync_LeavesNonModelsAlone()
    {
        Write(From, "ggml-small-q5_1.bin");
        Write(From, "notes.txt");

        await ModelStorage.MoveAllAsync(From, To);

        Assert.True(File.Exists(Path.Combine(From, "notes.txt")));
        Assert.False(File.Exists(Path.Combine(To, "notes.txt")));
    }

    /// <summary>
    /// Одноимённый файл в новой папке не перезаписывается.
    /// </summary>
    /// <remarks>
    /// Молча затереть чужую модель — худший из возможных исходов: файл на
    /// сотни мегабайт исчезает без следа, и восстановить его можно только
    /// новой закачкой.
    /// </remarks>
    [Fact]
    public async Task MoveAllAsync_DoesNotOverwriteAnExistingModel()
    {
        Write(From, "ggml-small-q5_1.bin", 100);
        Write(To, "ggml-small-q5_1.bin", 999);

        MoveResult result = await ModelStorage.MoveAllAsync(From, To);

        Assert.Equal(0, result.Moved);
        Assert.Single(result.Failed);
        Assert.Equal(999, new FileInfo(Path.Combine(To, "ggml-small-q5_1.bin")).Length);
        Assert.True(File.Exists(Path.Combine(From, "ggml-small-q5_1.bin")));
    }

    [Fact]
    public async Task MoveAllAsync_CreatesTheTargetDirectory()
    {
        Write(From, "ggml-small-q5_1.bin");
        string fresh = Path.Combine(_root, "fresh");

        MoveResult result = await ModelStorage.MoveAllAsync(From, fresh);

        Assert.Equal(1, result.Moved);
        Assert.True(Directory.Exists(fresh));
    }

    [Fact]
    public async Task MoveAllAsync_DoesNothingWhenThereIsNothingToMove()
    {
        MoveResult result = await ModelStorage.MoveAllAsync(From, To);

        Assert.Equal(0, result.Moved);
        Assert.Empty(result.Failed);
    }

    [Fact]
    public async Task MoveAllAsync_ReportsProgressPerFile()
    {
        Write(From, "ggml-a.bin");
        Write(From, "ggml-b.bin");

        var seen = new List<string>();
        await ModelStorage.MoveAllAsync(From, To, new Progress<string>(seen.Add));

        // Progress<T> доставляет асинхронно — дождёмся, но недолго.
        for (int i = 0; i < 50 && seen.Count < 2; i++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(2, seen.Count);
    }
}
