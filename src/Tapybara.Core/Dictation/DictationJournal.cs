using System.Text;
using System.Text.Json;
using Tapybara.Core.Diagnostics;

namespace Tapybara.Core.Dictation;

/// <summary>Одна сохранённая диктовка.</summary>
/// <param name="At">Когда вставлена.</param>
/// <param name="Text">Что вставлено — уже после замен и абзацев.</param>
public sealed record DictationEntry(DateTimeOffset At, string Text);

/// <summary>
/// История диктовок на диске.
/// </summary>
/// <remarks>
/// <para>
/// Раньше история жила в памяти — десять последних, обрезанных до шестидесяти
/// символов в подменю трея, — и пропадала с выходом из программы. Потерянная
/// диктовка — это потерянная работа: вставка ушла не в то окно, текст
/// перетёрли, программа закрылась. История на диске — страховка от этого.
/// </para>
/// <para>
/// Формат — JSON Lines: одна диктовка на строку. Новая запись — дописывание
/// строки в конец, без чтения и переписывания всего файла, а оборванная
/// запись портит одну строку, а не всю историю.
/// </para>
/// <para>
/// Диктуют и пароли, и переписку, поэтому история отключается в настройках
/// и чистится одной кнопкой. Файл лежит там же, где настройки, и никуда
/// не уходит.
/// </para>
/// </remarks>
public sealed class DictationJournal(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Lock _gate = new();

    /// <summary>Где лежит файл.</summary>
    public string FilePath => path;

    /// <summary>История изменилась: добавили, удалили, очистили.</summary>
    /// <remarks>Приходит в том потоке, где сделана правка.</remarks>
    public event Action? Changed;

    /// <summary>Все диктовки, свежие первыми.</summary>
    /// <remarks>
    /// Испорченные строки пропускаются, а не роняют чтение: одна оборванная
    /// запись не должна отнимать у человека всю остальную историю.
    /// </remarks>
    public IReadOnlyList<DictationEntry> Load()
    {
        lock (_gate)
        {
            var entries = new List<DictationEntry>();
            try
            {
                if (!File.Exists(path))
                {
                    return entries;
                }

                foreach (string line in File.ReadLines(path, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    try
                    {
                        if (JsonSerializer.Deserialize<DictationEntry>(line, Options) is { Text.Length: > 0 } entry)
                        {
                            entries.Add(entry);
                        }
                    }
                    catch (JsonException)
                    {
                        // Строка, оборванная на полуслове, — пропускаем её одну.
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Не удалось прочитать историю диктовок.", ex);
            }

            entries.Reverse();
            return entries;
        }
    }

    /// <summary>Дописать диктовку в конец.</summary>
    public void Append(DictationEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.Text))
        {
            return;
        }

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, JsonSerializer.Serialize(entry, Options) + "\n", Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // История — страховка, а не условие работы: вставка текста
                // уже случилась, и сбой записи истории не должен её портить.
                AppLog.Warn("Не удалось записать диктовку в историю.", ex);
                return;
            }
        }

        Changed?.Invoke();
    }

    /// <summary>Убрать одну диктовку.</summary>
    public void Remove(DictationEntry entry)
    {
        lock (_gate)
        {
            List<DictationEntry> kept = [.. Load().Where(e => e != entry)];
            kept.Reverse();
            Rewrite(kept);
        }

        Changed?.Invoke();
    }

    /// <summary>Стереть всю историю.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Не удалось очистить историю диктовок.", ex);
            }
        }

        Changed?.Invoke();
    }

    private void Rewrite(IEnumerable<DictationEntry> entries)
    {
        try
        {
            string temp = path + ".tmp";
            File.WriteAllLines(temp, entries.Select(e => JsonSerializer.Serialize(e, Options)), Encoding.UTF8);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Не удалось переписать историю диктовок.", ex);
        }
    }
}
