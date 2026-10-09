using System.Windows;

namespace Tapybara.App;

public partial class CallsPage
{
    private double _libraryWidth;
    private double _peopleWidth;
    private bool _peopleRequested;
    private bool _arrangingPanels;

    private static double PaneWidth(double value, double minimum, double maximum, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    private void InitializePanelSizing()
    {
        _libraryWidth = PaneWidth(Settings.CallsLibraryWidth, Tokens.LibraryMinWidth, Tokens.LibraryMaxWidth, Tokens.LibraryWidth);
        _peopleWidth = PaneWidth(Settings.CallsPeopleWidth, Tokens.PeopleMinWidth, Tokens.PeopleMaxWidth, Tokens.PeopleWidth);
        LibraryColumn.Width = new GridLength(_libraryWidth);
        PageRoot.SizeChanged += (_, _) => ArrangePanels();
        LibrarySplitter.DragCompleted += (_, e) =>
        {
            if (!e.Canceled) { CapturePanelWidth(library: true); }
        };
        PeopleSplitter.DragCompleted += (_, e) =>
        {
            if (!e.Canceled) { CapturePanelWidth(library: false); }
        };
        LibrarySplitter.KeyUp += (_, e) =>
        {
            if (e.Key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right) { CapturePanelWidth(library: true); }
        };
        PeopleSplitter.KeyUp += (_, e) =>
        {
            if (e.Key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right) { CapturePanelWidth(library: false); }
        };
        LibrarySplitter.MouseDoubleClick += (_, e) =>
        {
            _libraryWidth = Tokens.LibraryWidth;
            SavePanelWidths(); ArrangePanels(); e.Handled = true;
        };
        PeopleSplitter.MouseDoubleClick += (_, e) =>
        {
            _peopleWidth = Tokens.PeopleWidth;
            SavePanelWidths(); ArrangePanels(); e.Handled = true;
        };
    }

    private void CapturePanelWidth(bool library)
    {
        // GridSplitter меняет Width до следующего arrange; ActualWidth здесь ещё может быть старой.
        if (library)
        {
            _libraryWidth = PaneWidth(LibraryColumn.Width.Value, Tokens.LibraryMinWidth, Tokens.LibraryMaxWidth, Tokens.LibraryWidth);
        }
        else
        {
            _peopleWidth = PaneWidth(VoicesColumn.Width.Value, Tokens.PeopleMinWidth, Tokens.PeopleMaxWidth, Tokens.PeopleWidth);
        }
        SavePanelWidths(); ArrangePanels();
    }

    private void SavePanelWidths() => _services.Settings.Update(settings => settings with
    {
        CallsLibraryWidth = _libraryWidth,
        CallsPeopleWidth = _peopleWidth,
    });

    private void ArrangePanels()
    {
        if (_arrangingPanels || PageRoot.ActualWidth <= 0) { return; }
        _arrangingPanels = true;
        try
        {
            bool people = _peopleRequested && PageRoot.ActualWidth >= Tokens.LibraryMinWidth + Tokens.EditorMinWidth
                + Tokens.PeopleMinWidth + 2 * Tokens.PanelGutter;
            double peopleWidth = people ? Math.Min(_peopleWidth,
                PageRoot.ActualWidth - Tokens.LibraryMinWidth - Tokens.EditorMinWidth - 2 * Tokens.PanelGutter) : 0;
            double libraryMaximum = PageRoot.ActualWidth - Tokens.EditorMinWidth - Tokens.PanelGutter
                - (people ? peopleWidth + Tokens.PanelGutter : 0);
            LibraryColumn.Width = new GridLength(Math.Clamp(_libraryWidth, Tokens.LibraryMinWidth, Math.Max(Tokens.LibraryMinWidth, libraryMaximum)));
            VoicesColumn.MinWidth = people ? Tokens.PeopleMinWidth : 0;
            VoicesColumn.Width = new GridLength(peopleWidth);
            PeopleGutter.Width = new GridLength(people ? Tokens.PanelGutter : 0);
            VoicesPane.Visibility = PeopleSplitter.Visibility = people ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _arrangingPanels = false; }
    }
}
