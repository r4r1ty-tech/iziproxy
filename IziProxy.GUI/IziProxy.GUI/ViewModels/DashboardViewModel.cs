using System;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IziProxy;

namespace IziProxy.GUI.ViewModels;

/// <summary>
/// ViewModel для экрана Dashboard: статус Xray, проверка конфига, статистика трафика.
/// </summary>
public partial class DashboardViewModel : ObservableObject
{
    private readonly DeployViewModel _deployVm;
    private readonly LogsViewModel   _logsVm;

    // ── Статус сервиса ───────────────────────────────────────────────
    [ObservableProperty] private bool   _isRunning        = false;
    [ObservableProperty] private string _serviceStatus    = "Неизвестно";
    [ObservableProperty] private string _statusColorHex   = "#FF3333";
    [ObservableProperty] private bool   _isConfigValid    = false;
    [ObservableProperty] private string _configCheckText  = string.Empty;

    // ── Загрузка ─────────────────────────────────────────────────────
    [ObservableProperty] private bool   _isBusy           = false;
    [ObservableProperty] private string _lastUpdated      = "—";

    // ── Трафик ───────────────────────────────────────────────────────
    public ObservableCollection<InboundTrafficStat> TrafficStats { get; } = new();

    // ── Подключение ──────────────────────────────────────────────────
    /// <summary>True если есть активное SSH-подключение (после деплоя).</summary>
    public bool IsConnected => _deployVm.ActiveSsh != null;

    public DashboardViewModel(DeployViewModel deployVm, LogsViewModel logsVm)
    {
        _deployVm = deployVm;
        _logsVm   = logsVm;

        // Обновляем IsConnected когда меняется ActiveSsh
        _deployVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DeployViewModel.ActiveSsh))
            {
                OnPropertyChanged(nameof(IsConnected));
                if (IsConnected)
                {
                    _ = RefreshStatus();
                }
            }
        };
    }

    [RelayCommand(CanExecute = nameof(CanExecute))]
    private async Task RefreshStatus()
    {
        if (_deployVm.ActiveSsh == null || _deployVm.ActiveConfig == null) return;

        IsBusy = true;
        try
        {
            var status = await XrayMonitor.GetStatus(_deployVm.ActiveSsh, _deployVm.ActiveConfig, _logsVm.ProgressReporter);

            IsRunning       = status.IsRunning;
            ServiceStatus   = status.IsRunning ? "Запущен ✓" : "Остановлен ✗";
            StatusColorHex  = status.IsRunning ? "#22C55E" : "#FF3333";
            IsConfigValid   = status.IsConfigValid;
            ConfigCheckText = status.ConfigCheckOutput;

            TrafficStats.Clear();
            foreach (var stat in status.TrafficStats)
                TrafficStats.Add(stat);

            LastUpdated = DateTime.Now.ToString("HH:mm:ss");
        }
        catch (Exception ex)
        {
            _logsVm.ProgressReporter.Report("Ошибка Dashboard: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecute))]
    private async Task RestartService()
    {
        if (_deployVm.ActiveSsh == null || _deployVm.ActiveConfig == null) return;

        IsBusy = true;
        try
        {
            await XrayMonitor.RestartService(_deployVm.ActiveSsh, _deployVm.ActiveConfig, _logsVm.ProgressReporter);
            await RefreshStatus();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecute))]
    private async Task ValidateConfig()
    {
        if (_deployVm.ActiveSsh == null || _deployVm.ActiveConfig == null) return;

        IsBusy = true;
        try
        {
            var result = await _deployVm.ActiveSsh.RunSudoCommand(_deployVm.ActiveConfig, "/usr/local/bin/xray -test -config /usr/local/etc/xray/config.json 2>&1");
            ConfigCheckText = result.Result.Trim();
            IsConfigValid   = !ConfigCheckText.Contains("error", StringComparison.OrdinalIgnoreCase);
            _logsVm.ProgressReporter.Report("Проверка конфига: " + ConfigCheckText);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanExecute() => !IsBusy && IsConnected;
}
