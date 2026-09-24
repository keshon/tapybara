using System.Windows;
using Tapybara.App.Localization;
using Tapybara.Core.Dictation;
using Wpf.Ui.Controls;

namespace Tapybara.App;

/// <summary>Разделы главного окна.</summary>
public enum MainPage
{
    Dictations,
    Calls,
    Dictionary,
}

/// <summary>
/// Главное окно: диктовки, звонки, словарь.
/// </summary>
/// <remarks>
/// Страницы создаются один раз и живут, пока открыто окно: переключение
/// раздела не должно сбрасывать ни выбранный звонок, ни прокрутку, ни
/// недописанную заметку.
/// </remarks>
public partial class MainWindow : FluentWindow, IDisposable
{
    private readonly DictationsPage _dictations;
    private readonly CallsPage _calls;
    private readonly DictionaryPage _dictionary;

    public MainWindow(CallsServices calls, DictationJournal journal, Action openSettings, Action dictate, Action openModels)
    {
        InitializeComponent();

        _calls = new CallsPage(calls);
        _dictations = new DictationsPage(journal, calls.Settings, openSettings, calls.Live, dictate, openModels);
        _dictionary = new DictionaryPage(calls.Settings, calls.Voices);

        _calls.AttentionChanged += ShowCallsAttention;

        SettingsRequested = openSettings;

        Closed += (_, _) => Dispose();

        ApplyLanguage();
        Show(MainPage.Dictations);

        // Точку «ждёт имён» нужно показать сразу, а не когда человек
        // случайно заглянет в раздел звонков. Страница просмотрела папку ещё
        // в своём конструкторе — до того, как мы подписались на событие.
        _calls.RefreshCalls();
        ShowCallsAttention(_calls.NeedsAttention);
    }

    private void ShowCallsAttention(bool needsNames) =>
        CallsAttention.Visibility = needsNames ? Visibility.Visible : Visibility.Collapsed;

    private Action SettingsRequested { get; }

    /// <summary>Дописать правки страниц и отпустить проигрыватель. Зовётся при закрытии.</summary>
    public void Dispose()
    {
        _calls.Dispose();
        _dictations.Dispose();
        _dictionary.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Какой раздел открыт.</summary>
    public MainPage Page { get; private set; }

    /// <summary>Открыть раздел.</summary>
    public void Show(MainPage page)
    {
        Page = page;
        (page switch
        {
            MainPage.Calls => CallsNav,
            MainPage.Dictionary => DictionaryNav,
            _ => DictationsNav,
        }).IsChecked = true;

        PageHost.Content = page switch
        {
            MainPage.Calls => _calls,
            MainPage.Dictionary => _dictionary,
            _ => (object)_dictations,
        };
    }

    /// <summary>Открыть звонок — например, по щелчку на уведомлении.</summary>
    public void SelectCall(string directory)
    {
        Show(MainPage.Calls);
        _calls.Select(directory);
    }

    /// <summary>Работа над звонком закончилась — перечитать список.</summary>
    public void RefreshCalls() => _calls.RefreshCalls();

    /// <summary>Подставить надписи текущего языка.</summary>
    public void ApplyLanguage()
    {
        DictationsNavText.Text = L.S.NavDictations;
        CallsNavText.Text = L.S.NavCalls;
        DictionaryNavText.Text = L.S.NavDictionary;
        SettingsNavText.Text = L.S.NavSettings;

        _calls.ApplyLanguage();
        _dictations.ApplyLanguage();
        _dictionary.ApplyLanguage();
    }

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        MainPage page = sender == CallsNav
            ? MainPage.Calls
            : sender == DictionaryNav ? MainPage.Dictionary : MainPage.Dictations;

        if (page != Page || PageHost.Content is null)
        {
            Show(page);
        }
    }

    /// <summary>Настройки — кнопка, а не вкладка: отметка на ней не должна залипать.</summary>
    /// <remarks>
    /// Checked, а не Click: экранный диктор и UI Automation «нажимают»
    /// переключатель через выбор, и Click при этом не приходит вовсе.
    /// </remarks>
    private void OnSettingsChecked(object sender, RoutedEventArgs e)
    {
        SettingsNav.IsChecked = false;
        SettingsRequested();
    }
}
