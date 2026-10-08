namespace Tapybara.Core.Storage;

/// <summary>
/// Атомарная замена файла с одной предыдущей копией для ручного восстановления.
/// </summary>
/// <remarks>
/// Это страховочная копия, не история Undo. Вызывающий слой определяет
/// границы транзакции и согласованность нескольких файлов документа.
/// </remarks>
internal static class RecoverableFile
{
    /// <summary>Имя страховочной копии рядом с исходным файлом.</summary>
    public static string PreviousPath(string path) => path + ".previous";

    /// <summary>Сохраняет прежние байты до замены; ошибка копирования отменяет запись.</summary>
    public static void WriteAllText(string path, string contents)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        if (File.Exists(path))
        {
            if (File.ReadAllText(path) == contents)
            {
                File.Delete(temp);
                return;
            }

            File.Replace(temp, path, PreviousPath(path));
        }
        else
        {
            File.Move(temp, path);
        }
    }
}
