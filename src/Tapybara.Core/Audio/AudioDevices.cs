using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using Tapybara.Core.Diagnostics;

namespace Tapybara.Core.Audio;

/// <summary>Устройство записи или вывода в списке выбора.</summary>
/// <param name="Id">Идентификатор WASAPI. Уникален и переживает переподключение.</param>
/// <param name="Name">Как устройство называется в системе.</param>
/// <param name="IsDefault">Является ли устройством по умолчанию прямо сейчас.</param>
public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault);

/// <summary>
/// Перечисление звуковых устройств.
/// </summary>
/// <remarks>
/// Выбор устройства — не роскошь. Типичная схема «гарнитура для звонков,
/// колонки по умолчанию» ломает запись звонка полностью: приложение,
/// записывающее «устройство по умолчанию», пишет тишину из колонок, пока
/// разговор идёт в гарнитуре, и узнаёт об этом пользователь уже по пустому
/// транскрипту.
/// </remarks>
public static class AudioDevices
{
    /// <summary>Микрофоны и прочие входы.</summary>
    public static IReadOnlyList<AudioDeviceInfo> Inputs() => Enumerate(DataFlow.Capture);

    /// <summary>Устройства вывода — с них снимается системный звук.</summary>
    public static IReadOnlyList<AudioDeviceInfo> Outputs() => Enumerate(DataFlow.Render);

    /// <summary>
    /// Найти устройство по идентификатору.
    /// </summary>
    /// <returns>
    /// <c>null</c>, если идентификатор пуст или устройство отключено — вызывающий
    /// код тогда берёт устройство по умолчанию. Молча брать «что-нибудь» на
    /// месте пропавшей гарнитуры лучше, чем не записать разговор вовсе.
    /// </returns>
    public static MMDevice? Resolve(string? deviceId, DataFlow flow)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return null;
        }

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            MMDevice device = enumerator.GetDevice(deviceId);
            if (device.State == DeviceState.Active && device.DataFlow == flow)
            {
                return device;
            }

            device.Dispose();
            AppLog.Warn($"Устройство {deviceId} недоступно — беру устройство по умолчанию.");
            return null;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            AppLog.Warn($"Устройство {deviceId} не найдено — беру устройство по умолчанию.", ex);
            return null;
        }
    }

    /// <summary>Имя устройства по идентификатору — для показа в настройках.</summary>
    public static string? NameOf(string? deviceId, DataFlow flow)
    {
        using MMDevice? device = Resolve(deviceId, flow);
        return device?.FriendlyName;
    }

    private static List<AudioDeviceInfo> Enumerate(DataFlow flow)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();

            string? defaultId = null;
            if (enumerator.HasDefaultAudioEndpoint(flow, Role.Console))
            {
                using MMDevice defaultDevice = enumerator.GetDefaultAudioEndpoint(flow, Role.Console);
                defaultId = defaultDevice.ID;
            }

            var result = new List<AudioDeviceInfo>();
            foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                using (device)
                {
                    result.Add(new AudioDeviceInfo(
                        device.ID,
                        device.FriendlyName,
                        string.Equals(device.ID, defaultId, StringComparison.Ordinal)));
                }
            }

            return result;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            AppLog.Error("Не удалось перечислить звуковые устройства.", ex);
            return [];
        }
    }
}
