using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IziProxy;
using Avalonia.Media.Imaging;
using QRCoder;

namespace IziProxy.GUI.ViewModels;

/// <summary>
/// Модель представления для экрана Deploy: форма ввода + процесс деплоя + VLESS-ссылки.
/// </summary>
public partial class DeployViewModel : ObservableObject
{
    private readonly LogsViewModel _logsVm;

    // ── Форма ввода ─────────────────────────────────────────────────
    [ObservableProperty] private string _host        = string.Empty;
    [ObservableProperty] private string _username    = string.Empty;
    [ObservableProperty] private string _password    = string.Empty;
    [ObservableProperty] private string _sshKeyPath  = string.Empty;

    // ── Состояние ────────────────────────────────────────────────────
    [ObservableProperty] private bool   _isDeploying = false;
    [ObservableProperty] private bool   _isCompleted = false;
    [ObservableProperty] private string _statusText  = string.Empty;

    // ── Результаты ───────────────────────────────────────────────────
    public ObservableCollection<VlessLinkItem> VlessLinks { get; } = new();

    /// <summary>Общий SSH-клиент: переиспользуется и в DashboardViewModel.</summary>
    public SSH? ActiveSsh { get; private set; }
    public ServerConfig? ActiveConfig { get; private set; }

    public DeployViewModel(LogsViewModel logsVm)
    {
        _logsVm = logsVm;
    }

    [RelayCommand(CanExecute = nameof(CanDeploy))]
    private async Task Deploy()
    {
        IsDeploying = true;
        IsCompleted = false;
        VlessLinks.Clear();
        StatusText = "Запуск деплоя...";

        ActiveSsh?.Dispose();
        ActiveSsh = null;

        var progress = _logsVm.ProgressReporter;

        try
        {
            var config = new ServerConfig
            {
                Host       = Host,
                Username   = Username,
                Password   = Password,
                SshKey     = SshKeyPath
            };

            var ssh = new SSH();

            // 1. Подключение
            progress.Report("Подключение к серверу...");
            bool connected = await ssh.TestConnection(config, progress);
            if (!connected)
            {
                StatusText = "❌ Ошибка подключения";
                ssh.Dispose();
                return;
            }
            progress.Report("✓ Подключение установлено");

            // 2. Загрузка и запуск MainInstall.sh
            progress.Report("Загрузка установочного скрипта...");
            bool uploaded = await ssh.UploadTestScript(config, progress);
            if (!uploaded) { StatusText = "❌ Ошибка загрузки скрипта"; ssh.Dispose(); return; }

            progress.Report("Выполнение установочного скрипта (может занять 1-3 мин)...");
            bool ran = await ssh.RunTestScript(config, progress);
            if (!ran) { StatusText = "❌ Ошибка выполнения скрипта"; ssh.Dispose(); return; }

            // 3. Генерация ключей Xray
            var xrayParams = await XrayConfigParams.Generate(ssh, config, progress);

            // 4. Деплой конфига
            var deployer = new DeployScripts();
            bool deployed = await deployer.DeployAndConfigure(ssh, config, xrayParams, progress);
            if (!deployed) { StatusText = "❌ Ошибка деплоя"; ssh.Dispose(); return; }

            // 5. GEO
            try
            {
                string geo = await XrayConfigParams.GetGeoVDS(ssh, config, progress);
                progress.Report("GEO VDS: " + geo);
            }
            catch { /* не критично */ }

            // 6. Генерация VLESS-ссылок
            var links = VlessLinkGenerator.GenerateRealityLinks(config, xrayParams);
            for (int i = 0; i < links.Count; i++)
            {
                VlessLinks.Add(new VlessLinkItem
                {
                    Label = $"Ссылка {i + 1}  |  Порт: {xrayParams.Ports[i]}  |  SNI: {xrayParams.Snis[i]}",
                    Link  = links[i]
                });
            }

            // Сохраняем SSH для Dashboard
            ActiveSsh    = ssh;
            ActiveConfig = config;
            IsCompleted  = true;
            StatusText   = "✅ Деплой завершён!";
        }
        catch (Exception ex)
        {
            StatusText = "❌ " + ex.Message;
            progress.Report("ОШИБКА: " + ex.Message);
        }
        finally
        {
            IsDeploying = false;
        }
    }

    private bool CanDeploy() => !IsDeploying;
}

/// <summary>
/// Элемент списка VLESS-ссылок.
/// </summary>
public partial class VlessLinkItem : ObservableObject
{
    public string Label { get; set; } = string.Empty;
    public string Link  { get; set; } = string.Empty;

    [ObservableProperty] private string _copyLabel = "Скопировать";

    [ObservableProperty] private Bitmap? _qrCodeImage = null;

    [RelayCommand]
    private async Task CopyLink()
    {
        // Clipboard доступен только через Avalonia TopLevel
        // Устанавливаем флаг — View сам сделает clipboard через code-behind
        CopyLabel = "Скопировано ✓";
        await Task.Delay(2000);
        CopyLabel = "Скопировать";
    }

    [RelayCommand]
    private void ToggleQr()
    {
        if (QrCodeImage != null)
        {
            QrCodeImage = null;
        }
        else
        {
            try
            {
                using var qrGenerator = new QRCodeGenerator();
                using var qrCodeData = qrGenerator.CreateQrCode(Link, QRCodeGenerator.ECCLevel.Q);
                using var qrCode = new PngByteQRCode(qrCodeData);
                byte[] qrCodeAsPngByteArr = qrCode.GetGraphic(20);
                
                using var ms = new MemoryStream(qrCodeAsPngByteArr);
                QrCodeImage = new Bitmap(ms);
            }
            catch
            {
                // Игнорируем ошибки генерации
            }
        }
    }
}
