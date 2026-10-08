using Tapybara.Core.Diagnostics;
using Tapybara.Core.Windows;

namespace Tapybara.App;

/// <summary>Копирование локального фрагмента; доставка полного документа находится в Export.</summary>
public partial class CallsPage
{
    private static void Copy(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        try
        {
            // В историю буфера — можно: это не диктовка, а текст, который
            // человек сам решил скопировать.
            ClipboardWriter.SetText(text, excludeFromHistory: false);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            AppLog.Warn("Не удалось скопировать транскрипт.", ex);
        }
    }
}
