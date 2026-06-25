using System;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IziProxy;

namespace IziProxy.GUI.ViewModels;

/// <summary>
/// ViewModel для вкладки Troubleshoot: мониторинг профилей и замена SNI.
/// </summary>
public partial class TroubleshootViewModel : ObservableObject
{
    private readonly DeployViewModel _deployVm;
    private readonly LogsViewModel   _logsVm;

    // ── Статус ──────────────────────────────────────────────────────
    [ObservableProperty] private bool   _isBusy        = false;
    [ObservableProperty] private bool   _isRunning     = false;
    [ObservableProperty] private bool   _isConfigValid = false;
    [ObservableProperty] private string _statusSummary = "Нет подключения";
    [ObservableProperty] private string _lastUpdated   = "—";

    // ── Профили (inbound'ы) ──────────────────────────────────────────
    public ObservableCollection<TroubleshootProfileItem> Profiles { get; } = new();

    public bool IsConnected => _deployVm.ActiveSsh != null;

    public TroubleshootViewModel(DeployViewModel deployVm, LogsViewModel logsVm)
    {
        _deployVm = deployVm;
        _logsVm   = logsVm;

        // Автообновление при появлении SSH-соединения
        _deployVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DeployViewModel.ActiveSsh))
            {
                OnPropertyChanged(nameof(IsConnected));
                if (IsConnected) _ = RefreshAll();
            }
        };
    }

    /// <summary>
    /// Обновляет статус Xray и список SNI-профилей.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExecute))]
    private async Task RefreshAll()
    {
        if (_deployVm.ActiveSsh == null || _deployVm.ActiveConfig == null) return;

        IsBusy = true;
        try
        {
            // 1. Статус сервиса
            var status = await XrayMonitor.GetStatus(
                _deployVm.ActiveSsh, _deployVm.ActiveConfig, _logsVm.ProgressReporter);

            IsRunning     = status.IsRunning;
            IsConfigValid = status.IsConfigValid;
            StatusSummary = status.IsRunning
                ? (status.IsConfigValid
                    ? "✅ Xray работает, конфиг валиден"
                    : "⚠️ Xray работает, но конфиг содержит ошибки")
                : "❌ Xray остановлен";

            // 2. Список SNI-профилей
            var snis = await SniRepairService.ReadCurrentSnis(
                _deployVm.ActiveSsh, _deployVm.ActiveConfig, _logsVm.ProgressReporter);

            Profiles.Clear();
            foreach (var sni in snis)
                Profiles.Add(new TroubleshootProfileItem(this, sni));

            LastUpdated = DateTime.Now.ToString("HH:mm:ss");
        }
        catch (Exception ex)
        {
            _logsVm.ProgressReporter.Report("[ERROR] Troubleshoot: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Применяет новый SNI для конкретного профиля. Вызывается из <see cref="TroubleshootProfileItem"/>.
    /// </summary>
    internal async Task<bool> ApplySniChange(TroubleshootProfileItem item, string newSni)
    {
        if (_deployVm.ActiveSsh == null || _deployVm.ActiveConfig == null) return false;

        bool success = await SniRepairService.ChangeSni(
            _deployVm.ActiveSsh, _deployVm.ActiveConfig,
            item.InboundIndex, newSni,
            _logsVm.ProgressReporter);

        if (success)
        {
            item.CurrentSni    = newSni;
            item.StatusMessage = $"✅ SNI заменён на {newSni}, Xray перезапущен";
        }
        else
        {
            item.StatusMessage = "❌ Ошибка замены SNI";
        }

        return success;
    }

    /// <summary>
    /// Автоподбор лучшего SNI с исключением текущего. Вызывается из <see cref="TroubleshootProfileItem"/>.
    /// </summary>
    internal async Task<string?> AutoSelectSni(string excludeSni)
    {
        if (_deployVm.ActiveSsh == null || _deployVm.ActiveConfig == null) return null;

        return await SniRepairService.AutoSelectBestSni(
            _deployVm.ActiveSsh, _deployVm.ActiveConfig,
            excludeSni,
            _logsVm.ProgressReporter);
    }

    private bool CanExecute() => !IsBusy && IsConnected;
}

/// <summary>
/// Один SNI-профиль (inbound) на вкладке Troubleshoot.
/// </summary>
public partial class TroubleshootProfileItem : ObservableObject
{
    private readonly TroubleshootViewModel _vm;

    /// <summary>Тег inbound'а (например "inbound-1").</summary>
    public string Tag { get; }

    /// <summary>Порт inbound'а.</summary>
    public string Port { get; }

    /// <summary>Индекс inbound'а в массиве config.json (0-based).</summary>
    public int InboundIndex { get; }

    /// <summary>Текущий SNI-домен.</summary>
    [ObservableProperty] private string _currentSni = string.Empty;

    /// <summary>Кастомный SNI, вводимый пользователем вручную.</summary>
    [ObservableProperty] private string _customSni = string.Empty;

    /// <summary>True если идёт операция.</summary>
    [ObservableProperty] private bool _isBusy = false;

    /// <summary>Сообщение о результате последней операции.</summary>
    [ObservableProperty] private string _statusMessage = string.Empty;

    public TroubleshootProfileItem(TroubleshootViewModel vm, InboundSniInfo info)
    {
        _vm          = vm;
        Tag          = info.Tag;
        Port         = info.Port;
        InboundIndex = info.InboundIndex;
        CurrentSni   = info.CurrentSni;
    }

    /// <summary>
    /// Автоматически подбирает лучший SNI, исключая текущий, и сразу применяет его.
    /// </summary>
    [RelayCommand]
    private async Task AutoReplaceSni()
    {
        IsBusy = true;
        StatusMessage = $"🔍 Поиск лучшего SNI (исключая \"{CurrentSni}\")...";
        try
        {
            string? bestSni = await _vm.AutoSelectSni(CurrentSni);
            if (bestSni != null)
            {
                StatusMessage = $"⏳ Найден: {bestSni}. Применение...";
                await _vm.ApplySniChange(this, bestSni);
            }
            else
            {
                StatusMessage = "⚠️ Не удалось подобрать SNI. Попробуйте ввести вручную.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "❌ Ошибка: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Применяет кастомный SNI, введённый пользователем.
    /// </summary>
    [RelayCommand]
    private async Task ApplyCustomSni()
    {
        if (string.IsNullOrWhiteSpace(CustomSni))
        {
            StatusMessage = "⚠️ Введите домен";
            return;
        }

        IsBusy = true;
        StatusMessage = $"⏳ Применение {CustomSni.Trim()}...";
        try
        {
            await _vm.ApplySniChange(this, CustomSni.Trim());
        }
        catch (Exception ex)
        {
            StatusMessage = "❌ Ошибка: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
