using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls;

namespace IziProxy.GUI.ViewModels;

/// <summary>
/// Корневой ViewModel: управляет навигацией между экранами и адаптивным режимом.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    // ── Дочерние VM ─────────────────────────────────────────────────
    public LogsViewModel      Logs      { get; }
    public DeployViewModel    Deploy    { get; }
    public DashboardViewModel Dashboard { get; }

    // ── Навигация ────────────────────────────────────────────────────
    /// <summary>0 = Deploy, 1 = Logs, 2 = Dashboard</summary>
    [ObservableProperty] private int _selectedTabIndex = 0;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsDeployVisible));
        OnPropertyChanged(nameof(IsLogsVisible));
        OnPropertyChanged(nameof(IsDashboardVisible));

        // Автоматически закрываем Drawer при клике на пункт меню на мобилках
        if (IsNarrowMode)
        {
            IsMenuPaneOpen = false;
        }
    }

    public bool IsDeployVisible => SelectedTabIndex == 0;
    public bool IsLogsVisible => SelectedTabIndex == 1;
    public bool IsDashboardVisible => SelectedTabIndex == 2;

    // ── Адаптивный режим и SplitView ──────────────────────────────────
    /// <summary>
    /// True когда ширина окна < 700px (мобильный режим).
    /// Обновляется из code-behind при изменении размера окна.
    /// </summary>
    [ObservableProperty] private bool _isNarrowMode = false;

    partial void OnIsNarrowModeChanged(bool value)
    {
        OnPropertyChanged(nameof(MenuDisplayMode));
        // На десктопе меню должно быть открыто всегда, на мобилке по умолчанию скрыто
        IsMenuPaneOpen = !value;
    }

    /// <summary>
    /// Состояние открытости Drawer-меню
    /// </summary>
    [ObservableProperty] private bool _isMenuPaneOpen = true;

    /// <summary>
    /// Режим отображения меню: Overlay для мобильных, Inline для ПК
    /// </summary>
    public SplitViewDisplayMode MenuDisplayMode => IsNarrowMode ? SplitViewDisplayMode.Overlay : SplitViewDisplayMode.Inline;

    [RelayCommand]
    private void ToggleMenuPane()
    {
        IsMenuPaneOpen = !IsMenuPaneOpen;
    }

    public MainViewModel()
    {
        Logs      = new LogsViewModel();
        Deploy    = new DeployViewModel(Logs);
        Dashboard = new DashboardViewModel(Deploy, Logs);
        
        // Инициализируем начальное состояние на основе режима
        IsMenuPaneOpen = !IsNarrowMode;
    }

    [RelayCommand] void GoToDeploy()    => SelectedTabIndex = 0;
    [RelayCommand] void GoToLogs()      => SelectedTabIndex = 1;
    [RelayCommand] void GoToDashboard() => SelectedTabIndex = 2;
}
