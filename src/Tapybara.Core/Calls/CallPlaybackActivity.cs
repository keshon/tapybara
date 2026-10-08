namespace Tapybara.Core.Calls;

/// <summary>Полуоткрытые интервалы: в тишине нет активной реплики, при перекрытии их несколько.</summary>
public static class CallPlaybackActivity
{
    public static bool IsActive(CallLine line, TimeSpan position, CallChannel? channel = null) =>
        (channel is null || line.Channel == channel) && line.Start <= position && position < line.End;
}
