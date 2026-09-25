using Tapybara.Core.Settings;

namespace Tapybara.Core.Models;

/// <summary>Строка страницы моделей: модель одного типа — из каталога или своя.</summary>
/// <param name="Kind">Тип модели.</param>
/// <param name="FileName">Имя файла в папке моделей.</param>
/// <param name="Catalog">Запись каталога; <c>null</c> — свой файл, положенный руками.</param>
/// <param name="Installed">Файл на диске; <c>null</c> — не скачана.</param>
/// <param name="InUse">Именно ею сейчас работает приложение.</param>
public sealed record ShelfModel(
    ModelKind Kind,
    string FileName,
    CatalogModel? Catalog,
    InstalledModel? Installed,
    bool InUse)
{
    public bool IsInstalled => Installed is not null;

    public bool IsCustom => Catalog is null;
}

/// <summary>
/// Модели одного типа — одним списком, как бы они ни попали на диск.
/// </summary>
/// <remarks>
/// <para>
/// Раньше модель распознавания выбиралась в карточке «Активная модель»,
/// детектор и голосовые модели — выпадающими списками в «Дополнительно»,
/// скачивались они в третьем месте, а удалялись в четвёртом, «Установлены»,
/// куда голосовые модели не попадали вовсе. Состояние модели теперь одно:
/// не скачана, скачана или используется, — и видно его в одной строке.
/// </para>
/// <para>
/// Свои файлы — дообученная модель, положенная в папку руками, — стоят в
/// своей группе наравне с каталогом: их тоже выбирают и удаляют.
/// </para>
/// </remarks>
public static class ModelShelf
{
    /// <summary>Модели типа: сначала каталог в его порядке, затем свои файлы.</summary>
    /// <param name="kind">Тип.</param>
    /// <param name="installed">Что лежит в папке моделей (<see cref="ModelStorage.List"/>).</param>
    /// <param name="settings">Какая модель выбрана.</param>
    public static IReadOnlyList<ShelfModel> Of(ModelKind kind, IReadOnlyList<InstalledModel> installed, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(settings);

        List<InstalledModel> own = [.. installed.Where(m => m.Kind == kind)];
        string? used = InUse(kind, own, settings);

        InstalledModel? OnDisk(string fileName) =>
            own.FirstOrDefault(m => string.Equals(m.FileName, fileName, StringComparison.OrdinalIgnoreCase));

        bool Used(string fileName) => string.Equals(fileName, used, StringComparison.OrdinalIgnoreCase);

        var shelf = new List<ShelfModel>();
        foreach (CatalogModel model in ModelCatalog.All.Where(m => m.Kind == kind))
        {
            shelf.Add(new ShelfModel(kind, model.FileName, model, OnDisk(model.FileName), Used(model.FileName)));
        }

        foreach (InstalledModel file in own.Where(m => ModelCatalog.Find(m.FileName) is null))
        {
            shelf.Add(new ShelfModel(kind, file.FileName, null, file, Used(file.FileName)));
        }

        return shelf;
    }

    /// <summary>Какой файл этого типа выбран в настройках.</summary>
    public static string Chosen(AppSettings settings, ModelKind kind)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return kind switch
        {
            ModelKind.VoiceSegmentation => settings.VoiceSegmentationModelFileName,
            ModelKind.VoiceEmbedding => settings.VoiceEmbeddingModelFileName,
            ModelKind.SpeechDetector => settings.VadModelFileName ?? string.Empty,
            _ => settings.ModelFileName ?? string.Empty,
        };
    }

    /// <summary>Выбрать файл для его типа.</summary>
    public static AppSettings Choose(AppSettings settings, ModelKind kind, string fileName)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return kind switch
        {
            ModelKind.VoiceSegmentation => settings with { VoiceSegmentationModelFileName = fileName },
            ModelKind.VoiceEmbedding => settings with { VoiceEmbeddingModelFileName = fileName },
            ModelKind.SpeechDetector => settings with { VadModelFileName = fileName },
            _ => settings with { ModelFileName = fileName },
        };
    }

    /// <summary>
    /// Каким файлом приложение работает на самом деле.
    /// </summary>
    /// <remarks>
    /// Выбранный — если он на диске. Нет — распознавание и детектор берут
    /// первый попавшийся своего типа (<see cref="ModelLocator.ResolveAnyAvailable"/>,
    /// <see cref="ModelLocator.ResolveVadModel"/>), и отмеченным должен быть
    /// он, а не выбранный, которого нет. Голосовым моделям замены нет: не
    /// скачана выбранная — не работает ничего, и отмеченной не будет ни одна.
    /// </remarks>
    private static string? InUse(ModelKind kind, IReadOnlyList<InstalledModel> own, AppSettings settings)
    {
        string chosen = Chosen(settings, kind);
        if (own.Any(m => string.Equals(m.FileName, chosen, StringComparison.OrdinalIgnoreCase)))
        {
            return chosen;
        }

        return kind is ModelKind.Recognition or ModelKind.SpeechDetector
            ? own.Select(m => m.FileName).Order(StringComparer.Ordinal).FirstOrDefault()
            : null;
    }
}
