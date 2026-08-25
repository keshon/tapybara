using System.Buffers.Binary;
using Tapybara.Core.Diagnostics;

namespace Tapybara.Core.Calls;

/// <summary>
/// Починка записей, оборванных не по-хорошему.
/// </summary>
/// <remarks>
/// <para>
/// В WAV длина данных лежит в заголовке, а заголовок пишется в конце. Пока
/// файл дописывается регулярно (см. <see cref="TimelineWavWriter"/>), в нём
/// стоит длина на момент последнего обновления — но между обновлением и
/// падением всегда остаётся хвост, о котором заголовок не знает.
/// </para>
/// <para>
/// Здесь этот хвост возвращается: реальный размер файла известен, размер
/// заголовка фиксирован, остальное — данные. Проверка запускается при старте
/// приложения, потому что человек, потерявший часовой разговор, не станет
/// искать в документации команду восстановления.
/// </para>
/// </remarks>
public static class CallRepair
{
    /// <summary>Смещение поля размера в блоке RIFF.</summary>
    private const int RiffSizeOffset = 4;

    /// <summary>Минимальный разумный заголовок: RIFF + fmt + начало data.</summary>
    private const int MinimumHeaderBytes = 44;

    /// <summary>
    /// Проверить и починить обе дорожки звонка.
    /// </summary>
    /// <returns>Сколько файлов пришлось чинить.</returns>
    public static int RepairCall(CallSession session)
    {
        int repaired = 0;
        foreach (string path in new[] { session.MicPath, session.SystemPath })
        {
            if (RepairWav(path))
            {
                repaired++;
            }
        }

        return repaired;
    }

    /// <summary>Пройтись по всем звонкам в папке и починить недописанные.</summary>
    /// <returns>Сколько звонков было починено.</returns>
    public static int RepairAll(string callsRoot)
    {
        if (!Directory.Exists(callsRoot))
        {
            return 0;
        }

        int calls = 0;
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(callsRoot))
            {
                if (CallMeta.Load(directory) is not { } session)
                {
                    continue;
                }

                if (RepairCall(session) > 0)
                {
                    calls++;

                    // Длительность в мете тоже врёт: её пишут при штатной
                    // остановке, которой не было.
                    TimeSpan measured = MeasureDuration(session.MicPath);
                    if (measured > session.Duration)
                    {
                        CallMeta.Save(session with { Duration = measured });
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Не удалось проверить папку звонков.", ex);
        }

        if (calls > 0)
        {
            AppLog.Info($"Восстановлено оборванных записей: {calls}.");
        }

        return calls;
    }

    /// <summary>
    /// Привести размеры в заголовке в соответствие с реальным размером файла.
    /// </summary>
    /// <returns><c>true</c>, если файл был испорчен и починен.</returns>
    public static bool RepairWav(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MinimumHeaderBytes)
            {
                return false;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Span<byte> header = stackalloc byte[12];
            if (stream.Read(header) != header.Length)
            {
                return false;
            }

            if (!header[..4].SequenceEqual("RIFF"u8) || !header[8..12].SequenceEqual("WAVE"u8))
            {
                return false; // не WAV — не наше дело
            }

            long dataSizeOffset = FindDataChunkSizeOffset(stream);
            if (dataSizeOffset < 0)
            {
                return false;
            }

            long dataStart = dataSizeOffset + 4;
            long actualData = stream.Length - dataStart;
            if (actualData <= 0)
            {
                return false;
            }

            stream.Position = dataSizeOffset;
            Span<byte> declared = stackalloc byte[4];
            stream.ReadExactly(declared);
            uint declaredData = BinaryPrimitives.ReadUInt32LittleEndian(declared);

            if (declaredData == actualData)
            {
                return false; // всё в порядке
            }

            // Сэмпл 16-битный моно: обрезаем возможный половинный сэмпл в хвосте.
            long alignedData = actualData - (actualData % 2);

            Write(stream, dataSizeOffset, (uint)alignedData);
            Write(stream, RiffSizeOffset, (uint)(dataStart + alignedData - 8));
            stream.Flush();

            AppLog.Info(
                $"Починен заголовок {Path.GetFileName(path)}: было {declaredData}, стало {alignedData} байт данных.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            AppLog.Warn($"Не удалось починить {Path.GetFileName(path)}.", ex);
            return false;
        }
    }

    /// <summary>
    /// Длительность дорожки.
    /// </summary>
    /// <remarks>
    /// Читаем заголовок, а не считаем по размеру файла минус константа:
    /// длина служебной части у WAV не фиксирована, и вычитание «сорока
    /// четырёх байт» даёт неверный ответ на файлах, где перед данными лежит
    /// что-то ещё.
    /// </remarks>
    private static TimeSpan MeasureDuration(string path)
    {
        try
        {
            using var reader = new NAudio.Wave.WaveFileReader(path);
            return reader.TotalTime;
        }
        catch (Exception ex) when (ex is IOException or FormatException or ArgumentException)
        {
            return TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Найти, где в файле лежит размер области data.
    /// </summary>
    /// <remarks>
    /// Перебором областей, а не по фиксированному смещению 40: между fmt и data
    /// вполне законно лежат LIST, fact и прочее, и жёсткое смещение чинило бы
    /// не тот байт.
    /// </remarks>
    private static long FindDataChunkSizeOffset(FileStream stream)
    {
        stream.Position = 12;
        Span<byte> chunk = stackalloc byte[8];

        while (stream.Position + 8 <= stream.Length)
        {
            long chunkStart = stream.Position;
            if (stream.Read(chunk) != chunk.Length)
            {
                return -1;
            }

            uint size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            if (chunk[..4].SequenceEqual("data"u8))
            {
                return chunkStart + 4;
            }

            // Области выровнены по чётному числу байт.
            long next = chunkStart + 8 + size + (size % 2);
            if (next <= chunkStart || next > stream.Length)
            {
                return -1;
            }

            stream.Position = next;
        }

        return -1;
    }

    private static void Write(FileStream stream, long offset, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Position = offset;
        stream.Write(buffer);
    }
}
