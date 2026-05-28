using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
    }

    public bool IsDeployVisible => SelectedTabIndex == 0;
    public bool IsLogsVisible => SelectedTabIndex == 1;
    public bool IsDashboardVisible => SelectedTabIndex == 2;

    // ── Адаптивный режим ─────────────────────────────────────────────
    /// <summary>
    /// True когда ширина окна < 700px (мобильный режим).
    /// Обновляется из code-behind при изменении размера окна.
    /// </summary>
    [ObservableProperty] private bool _isNarrowMode = false;

    public MainViewModel()
    {
        Logs      = new LogsViewModel();
        Deploy    = new DeployViewModel(Logs);
        Dashboard = new DashboardViewModel(Deploy, Logs);
    }

    [RelayCommand] void GoToDeploy()    => SelectedTabIndex = 0;
    [RelayCommand] void GoToLogs()      => SelectedTabIndex = 1;
    [RelayCommand] void GoToDashboard() => SelectedTabIndex = 2;
}
