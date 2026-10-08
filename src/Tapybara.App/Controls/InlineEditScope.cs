namespace Tapybara.App.Controls;

/// <summary>Координатор полей одного документа: непринятый черновик нельзя обойти сменой фокуса.</summary>
public sealed class InlineEditScope
{
    public InlineTextEditor? Active { get; private set; }

    public bool TryActivate(InlineTextEditor editor)
    {
        if (Active == editor)
        {
            return true;
        }
        if (!TryLeave())
        {
            return false;
        }
        Active = editor;
        return true;
    }

    public bool TryLeave() => Active?.Flush() != false;

    public bool TryClear()
    {
        if (!TryLeave())
        {
            return false;
        }
        Active = null;
        return true;
    }
}
