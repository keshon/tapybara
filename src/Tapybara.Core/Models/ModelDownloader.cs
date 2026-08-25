using Tapybara.Core.Diagnostics;

namespace Tapybara.Core.Models;

/// <summary>Ход скачивания.</summary>
/// <param name="ReceivedBytes">Сколько уже получено.</param>
/// <param name="TotalBytes">Сколько всего, или <c>null</c>, если сервер не сказал.</param>
/// <param name="BytesPerSecond">Средняя скорость с начала.</param>
public sealed record DownloadProgress(long ReceivedBytes, long? TotalBytes, double BytesPerSecond)
{
    /// <summary>Доля 0..1, если размер известен.</summary>
    public double? Fraction => TotalBytes is > 0 ? Math.Clamp((double)ReceivedBytes / TotalBytes.Value, 0, 1) : null;

    /// <summary>Сколько осталось, если размер и скорость известны.</summary>
    public TimeSpan? Remaining => TotalBytes is > 0 && BytesPerSecond > 1
        ? TimeSpan.FromSeconds((TotalBytes.Value - ReceivedBytes) / BytesPerSecond)
        : null;
}

/// <summary>
/// Скачивание моделей.
/// </summary>
/// <remarks>
/// <para>
/// Файл пишется под именем <c>*.part</c> и переименовывается только после
/// полной и проверенной загрузки. Иначе оборванная закачка оставила бы в папке
/// моделей файл правильного имени и неправильного размера — и приложение
/// падало бы на нём при каждом запуске, не понимая, почему.
/// </para>
/// <para>
/// Оборванная закачка продолжается с места обрыва: полтора гигабайта по
/// нестабильной сети иначе означают «начать сначала», а начинать сначала можно
/// бесконечно.
/// </para>
/// </remarks>
public sealed class ModelDownloader : IDisposable
{
    private readonly HttpClient _client;

    public ModelDownloader()
    {
        _client = new HttpClient
        {
            // Скачивание большое, но каждый отдельный ответ приходить обязан
            // быстро: таймаут здесь про установление соединения и заголовки,
            // тело читается потоком и под таймаут не подпадает.
            Timeout = TimeSpan.FromMinutes(30),
        };

        _client.DefaultRequestHeaders.UserAgent.ParseAdd("Tapybara/1.0");
    }

    /// <summary>Скачать модель в папку. Возвращает путь к готовому файлу.</summary>
    /// <param name="model">Что качаем.</param>
    /// <param name="targetDirectory">Папка моделей.</param>
    /// <param name="progress">Куда сообщать о ходе.</param>
    /// <param name="cancellationToken">Отмена. Незавершённый <c>.part</c> остаётся для докачки.</param>
    public async Task<string> DownloadAsync(
        CatalogModel model,
        string targetDirectory,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(targetDirectory);

        string finalPath = Path.Combine(targetDirectory, model.FileName);
        string partPath = finalPath + ".part";

        long alreadyHave = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, model.Url);
        if (alreadyHave > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(alreadyHave, null);
        }

        using HttpResponseMessage response = await _client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        // Сервер вправе не поддержать докачку и ответить целым файлом —
        // тогда начинаем заново, а не дописываем в середину.
        bool resuming = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
        if (!resuming)
        {
            alreadyHave = 0;
        }

        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength is { } length ? length + alreadyHave : null;

        await using (var target = new FileStream(
            partPath,
            resuming ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1 << 16,
            useAsync: true))
        {
            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            byte[] buffer = new byte[1 << 16];
            long received = alreadyHave;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            long reportedAt = 0;

            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;

                // Раз в четверть мегабайта, а не на каждый буфер: интерфейс
                // не должен перерисовываться тысячу раз в секунду.
                if (received - reportedAt >= 256 * 1024)
                {
                    reportedAt = received;
                    double speed = (received - alreadyHave) / Math.Max(clock.Elapsed.TotalSeconds, 0.001);
                    progress?.Report(new DownloadProgress(received, total, speed));
                }
            }

            progress?.Report(new DownloadProgress(received, total ?? received, received / Math.Max(clock.Elapsed.TotalSeconds, 0.001)));

            if (total is { } expected && received != expected)
            {
                throw new IOException(
                    $"Скачано {received} байт вместо {expected} — файл неполон.");
            }
        }

        File.Move(partPath, finalPath, overwrite: true);
        AppLog.Info($"Модель скачана: {model.FileName}");
        return finalPath;
    }

    /// <summary>Узнать размер, не скачивая. <c>null</c>, если сервер не ответил.</summary>
    public async Task<long?> ProbeSizeAsync(CatalogModel model, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, model.Url);
            using HttpResponseMessage response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? response.Content.Headers.ContentLength : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    public void Dispose() => _client.Dispose();
}
