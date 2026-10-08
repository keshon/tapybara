namespace Tapybara.Core.Calls;

/// <summary>Происхождение ручных изменений независимо по полям.</summary>
[Flags]
public enum CallLineEdits
{
    None = 0,
    Text = 1,
    Speaker = 2,
    Timing = 4,
    Structure = 8,
}
