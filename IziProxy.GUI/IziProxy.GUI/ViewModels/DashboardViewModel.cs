using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IziProxy;
using Avalonia.Media.Imaging;
using QRCoder;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace IziProxy.GUI.ViewModels;

/// <summary>
/// ViewModel для экрана Dashboard: статус Xray, проверка конфига, статистика трафика,
/// и управление SNI-профилями для починки VPN-ссылок.
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

    // ── SNI-профили ──────────────────────────────────────────────────
    public ObservableCollection<SniProfileItem> SniProfiles { get; } = new();

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
                    _ = LoadSniProfiles();
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
            ServiceStatus   = status.IsRunning ? "Запущен" : "Остановлен";
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

    /// <summary>
    /// Загружает текущие SNI-профили с сервера и создаёт карточки.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExecute))]
    private async Task LoadSniProfiles()
    {
        if (_deployVm.ActiveSsh == null || _deployVm.ActiveConfig == null) return;

        IsBusy = true;
        try
        {
            var snis = await SniRepairService.ReadCurrentSnis(
                _deployVm.ActiveSsh, _deployVm.ActiveConfig, _logsVm.ProgressReporter);

            SniProfiles.Clear();
            foreach (var sni in snis)
            {
                SniProfiles.Add(new SniProfileItem(this, sni));
            }
        }
        catch (Exception ex)
        {
            _logsVm.ProgressReporter.Report("Ошибка загрузки SNI: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Заменяет SNI для конкретного inbound'а. Вызывается из <see cref="SniProfileItem"/>.
    /// </summary>
    internal async Task<bool> ApplySniChange(SniProfileItem item, string newSni)
    {
        if (_deployVm.ActiveSsh == null || _deployVm.ActiveConfig == null) return false;

        bool success = await SniRepairService.ChangeSni(
            _deployVm.ActiveSsh, _deployVm.ActiveConfig,
            item.InboundIndex, newSni,
            _logsVm.ProgressReporter);

        if (success)
        {
            item.CurrentSni = newSni;
            item.StatusMessage = "SNI заменён, Xray перезапущен";

            // Перегенерируем VLESS-ссылку если есть параметры
            if (_deployVm.ActiveXrayParams != null)
            {
                string link = VlessLinkGenerator.GenerateRealityLinks(
                    _deployVm.ActiveConfig,
                    BuildXrayParamsForInbound(_deployVm.ActiveXrayParams, item, newSni)
                ).FirstOrDefault() ?? string.Empty;

                item.GeneratedLink = link;
            }

            await RefreshStatus();
        }
        else
        {
            item.StatusMessage = "Ошибка замены SNI";
        }

        return success;
    }

    /// <summary>
    /// Автоподбор лучшего SNI. Вызывается из <see cref="SniProfileItem"/>.
    /// </summary>
    internal async Task<string?> RunAutoSelectSni()
    {
        if (_deployVm.ActiveSsh == null || _deployVm.ActiveConfig == null) return null;

        return await SniRepairService.AutoSelectBestSni(
            _deployVm.ActiveSsh, _deployVm.ActiveConfig,
            excludeSni: null,
            _logsVm.ProgressReporter);
    }

    /// <summary>
    /// Создаёт копию <see cref="XrayConfigParams"/> с одним портом/SNI для генерации одной ссылки.
    /// </summary>
    private static XrayConfigParams BuildXrayParamsForInbound(
        XrayConfigParams source, SniProfileItem item, string newSni)
    {
        return new XrayConfigParams
        {
            PrivateKey = source.PrivateKey,
            Password   = source.Password,
            Uuid       = source.Uuid,
            ShortId    = source.ShortId,
            Ports      = new System.Collections.Generic.List<string> { item.Port },
            Snis       = new System.Collections.Generic.List<string> { newSni }
        };
    }

    private bool CanExecute() => !IsBusy && IsConnected;
}

/// <summary>
/// Один SNI-профиль (inbound) на дашборде. Показывает текущий SNI,
/// позволяет заменить вручную или автоподбором.
/// </summary>
public partial class SniProfileItem : ObservableObject
{
    private readonly DashboardViewModel _dashboardVm;

    /// <summary>Тег inbound'а (например "inbound-1").</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>Порт inbound'а.</summary>
    public string Port { get; set; } = string.Empty;

    /// <summary>Индекс inbound'а в массиве config.json (0-based).</summary>
    public int InboundIndex { get; set; }

    /// <summary>Текущий SNI-домен.</summary>
    [ObservableProperty] private string _currentSni = string.Empty;

    /// <summary>Введённый пользователем новый SNI.</summary>
    [ObservableProperty] private string _newSni = string.Empty;

    /// <summary>True если панель ремонта раскрыта.</summary>
    [ObservableProperty] private bool _isExpanded = false;

    /// <summary>True если идёт операция (замена/автоподбор).</summary>
    [ObservableProperty] private bool _isBusy = false;

    /// <summary>Сообщение о статусе операции.</summary>
    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>Перегенерированная VLESS-ссылка после смены SNI.</summary>
    [ObservableProperty] private string _generatedLink = string.Empty;

    /// <summary>Текст кнопки копирования.</summary>
    [ObservableProperty] private string _copyLabel = "Скопировать";

    /// <summary>QR-код для ссылки.</summary>
    [ObservableProperty] private Bitmap? _qrCodeImage = null;

    public SniProfileItem(DashboardViewModel dashboardVm, InboundSniInfo info)
    {
        _dashboardVm = dashboardVm;
        Tag          = info.Tag;
        Port         = info.Port;
        InboundIndex = info.InboundIndex;
        CurrentSni   = info.CurrentSni;
    }

    [RelayCommand]
    private void ToggleRepairPanel()
    {
        IsExpanded = !IsExpanded;
        if (!IsExpanded)
        {
            StatusMessage = string.Empty;
            NewSni = string.Empty;
        }
    }

    [RelayCommand]
    private async Task ApplyManualSni()
    {
        if (string.IsNullOrWhiteSpace(NewSni))
        {
            StatusMessage = "Введите домен";
            return;
        }

        IsBusy = true;
        StatusMessage = "Замена SNI...";
        try
        {
            await _dashboardVm.ApplySniChange(this, NewSni.Trim());
        }
        catch (Exception ex)
        {
            StatusMessage = "Ошибка: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AutoSelectSni()
    {
        IsBusy = true;
        StatusMessage = "Подбор лучшего SNI (может занять 30-60 сек)...";
        try
        {
            string? bestSni = await _dashboardVm.RunAutoSelectSni();
            if (bestSni != null)
            {
                NewSni = bestSni;
                StatusMessage = $"Лучший домен: {bestSni}. Нажмите «Применить» для замены.";
            }
            else
            {
                StatusMessage = "Не удалось подобрать SNI. Попробуйте ввести вручную.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "Ошибка автоподбора: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CopyLink()
    {
        if (string.IsNullOrEmpty(GeneratedLink)) return;

        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.Clipboard is { } clipboard)
        {
            await clipboard.SetValueAsync(DataFormat.Text, GeneratedLink);
        }

        CopyLabel = "Скопировано!";
        await Task.Delay(2000);
        CopyLabel = "Скопировать";
    }

    [RelayCommand]
    private void ToggleQr()
    {
        if (QrCodeImage != null)
        {
            QrCodeImage = null;
            return;
        }

        if (string.IsNullOrEmpty(GeneratedLink)) return;

        try
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(GeneratedLink, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            byte[] qrCodeAsPngByteArr = qrCode.GetGraphic(20);

            using var ms = new MemoryStream(qrCodeAsPngByteArr);
            QrCodeImage = new Bitmap(ms);
        }
        catch
        {
            // Игнорируем ошибки генерации QR
        }
    }
}
