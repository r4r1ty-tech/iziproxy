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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;

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
    [ObservableProperty] private bool   _isDeploying  = false;
    [ObservableProperty] private bool   _isCompleted  = false;
    [ObservableProperty] private string _statusText   = string.Empty;
    /// <summary>Отражает мобильный режим — устанавливается из MainViewModel.</summary>
    [ObservableProperty] private bool   _isNarrowMode = false;

    // ── Результаты ───────────────────────────────────────────────────
    public ObservableCollection<VlessLinkItem> VlessLinks { get; } = new();

    /// <summary>Общий SSH-клиент: переиспользуется и в DashboardViewModel.</summary>
    [ObservableProperty] private SSH? _activeSsh;
    [ObservableProperty] private ServerConfig? _activeConfig;
    [ObservableProperty] private XrayConfigParams? _activeXrayParams;

    // ── Профили ──────────────────────────────────────────────────────
    public ObservableCollection<VdsProfile> Profiles { get; } = new();
    [ObservableProperty] private VdsProfile? _selectedProfile;
    [ObservableProperty] private string _newProfileName = string.Empty;

    partial void OnSelectedProfileChanged(VdsProfile? value)
    {
        if (value == null) return;
        Host = value.Host;
        Username = value.Username;
        Password = value.Password;
        SshKeyPath = value.SshKeyPath;
        NewProfileName = value.Name;
    }

    [RelayCommand]
    private void SaveCurrentProfile()
    {
        if (string.IsNullOrWhiteSpace(Host) || string.IsNullOrWhiteSpace(NewProfileName)) return;

        var existing = Profiles.FirstOrDefault(p => p.Name.Equals(NewProfileName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Host = Host;
            existing.Username = Username;
            existing.Password = Password;
            existing.SshKeyPath = SshKeyPath;
        }
        else
        {
            var profile = new VdsProfile
            {
                Name = NewProfileName,
                Host = Host,
                Username = Username,
                Password = Password,
                SshKeyPath = SshKeyPath
            };
            Profiles.Add(profile);
        }
        VdsProfileService.SaveProfiles(Profiles.ToList());
        
        var savedName = NewProfileName;
        LoadProfilesFromDisk();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Name == savedName);
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile == null) return;
        Profiles.Remove(SelectedProfile);
        VdsProfileService.SaveProfiles(Profiles.ToList());
        SelectedProfile = null;
        NewProfileName = string.Empty;
    }

    private void LoadProfilesFromDisk()
    {
        Profiles.Clear();
        foreach (var profile in VdsProfileService.LoadProfiles())
        {
            Profiles.Add(profile);
        }
    }

    public DeployViewModel(LogsViewModel logsVm)
    {
        _logsVm = logsVm;
        LoadProfilesFromDisk();
    }

    [RelayCommand(CanExecute = nameof(CanDeploy))]
    private async Task Deploy()
    {
        IsDeploying = true;
        IsCompleted = false;
        VlessLinks.Clear();
        StatusText = Tr.Get("Deploy_Starting");

        ActiveSsh?.Dispose();
        ActiveSsh = null;

        var rawProgress = _logsVm.ProgressReporter;

        // Строим ServerConfig ДО обёртки progress, чтобы пробросить пароль
        // в PasswordMasker. Если пароль пустой (используется SSH-ключ) —
        // маскирование не нужно, передаём rawProgress как есть.
        var config = new ServerConfig
        {
            Host       = Host,
            Username   = Username,
            Password   = Password,
            SshKey     = SshKeyPath
        };
        IProgress<string> progress = string.IsNullOrEmpty(Password)
            ? rawProgress
            : new PasswordMasker(rawProgress, Password);

        try
        {
            var ssh = new SSH();

            // 1. Подключение
            progress.Report(Tr.Get("Deploy_Connecting"));
            bool connected = await ssh.TestConnection(config, progress);
            if (!connected)
            {
                StatusText = Tr.Get("Deploy_ConnectFailed");
                ssh.Dispose();
                return;
            }
            progress.Report(Tr.Get("Deploy_Connected"));

            // 2. Загрузка и запуск MainInstall.sh
            progress.Report(Tr.Get("Deploy_UploadingScript"));
            bool uploaded = await ssh.UploadTestScript(config, progress);
            if (!uploaded) { StatusText = Tr.Get("Deploy_UploadFailed"); ssh.Dispose(); return; }

            progress.Report(Tr.Get("Deploy_RunningScript"));
            bool ran = await ssh.RunTestScript(config, progress);
            if (!ran) { StatusText = Tr.Get("Deploy_ScriptFailed"); ssh.Dispose(); return; }

            // 3. Генерация ключей Xray
            var xrayParams = await XrayConfigParams.Generate(ssh, config, progress);

            // 4. Деплой конфига
            var deployer = new DeployScripts();
            bool deployed = await deployer.DeployAndConfigure(ssh, config, xrayParams, progress);
            if (!deployed) { StatusText = Tr.Get("Deploy_Failed"); ssh.Dispose(); return; }

            // 5. GEO
            try
            {
                string geo = await XrayConfigParams.GetGeoVDS(ssh, config, progress);
                progress.Report("GEO VDS: " + geo);
            }
            catch { /* не критично */ }

            // 6. Генерация VLESS-ссылок
            progress.Report("\n" + Tr.Get("Deploy_GeneratingLinks"));
            var links = VlessLinkGenerator.GenerateRealityLinks(config, xrayParams);
            for (int i = 0; i < links.Count; i++)
            {
                string label = Tr.F("Deploy_LinkLabel", i + 1, xrayParams.Ports[i], xrayParams.Snis[i]);
                VlessLinks.Add(new VlessLinkItem
                {
                    Label = label,
                    Link  = links[i]
                });
                progress.Report($"✓ {label}");
            }

            // Сохраняем SSH и параметры для Dashboard
            ActiveConfig     = config;
            ActiveXrayParams = xrayParams;
            ActiveSsh        = ssh;
            IsCompleted  = true;
            StatusText   = Tr.Get("Deploy_Done");

            progress.Report("\n=================================================");
            progress.Report(Tr.Get("Deploy_DoneBanner"));
            progress.Report(Tr.Get("Deploy_GoCopyLinks"));
            progress.Report("=================================================");
        }
        catch (Exception ex)
        {
            StatusText = "❌ " + ex.Message;
            progress.Report(Tr.F("Deploy_Error", ex.Message));
        }
        finally
        {
            IsDeploying = false;
        }
    }

    private bool CanDeploy() => !IsDeploying;

    [RelayCommand]
    private async Task TestConnection()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            StatusText = Tr.Get("Deploy_EnterIp");
            return;
        }

        IsDeploying = true;
        StatusText = Tr.Get("Deploy_Testing");
        var rawProgress = _logsVm.ProgressReporter;
        rawProgress.Report(Tr.F("Deploy_TestStart", Host));

        try
        {
            var config = new ServerConfig
            {
                Host       = Host,
                Username   = Username,
                Password   = Password,
                SshKey     = SshKeyPath
            };

            // Маскируем пароль в логах на случай если SSH.NET пробросит его в ex.Message
            IProgress<string> progress = string.IsNullOrEmpty(Password)
                ? rawProgress
                : new PasswordMasker(rawProgress, Password);

            using var ssh = new SSH();
            bool connected = await ssh.TestConnection(config, progress);
            if (connected)
            {
                StatusText = Tr.Get("Deploy_TestOk");
                progress.Report(Tr.Get("Deploy_TestOkLog"));
            }
            else
            {
                StatusText = Tr.Get("Deploy_TestFailed");
                progress.Report(Tr.Get("Deploy_TestAuthFailed"));
            }
        }
        catch (Exception ex)
        {
            StatusText = Tr.F("Deploy_TestError", ex.Message);
            _logsVm.ProgressReporter.Report(Tr.F("Deploy_SshError", ex.Message));
        }
        finally
        {
            IsDeploying = false;
        }
    }
}

/// <summary>
/// Элемент списка VLESS-ссылок.
/// </summary>
public partial class VlessLinkItem : ObservableObject
{
    public string Label { get; set; } = string.Empty;
    public string Link  { get; set; } = string.Empty;

    [ObservableProperty] private string _copyLabel = Tr.Get("Common_Copy");

    [ObservableProperty] private Bitmap? _qrCodeImage = null;

    [RelayCommand]
    private async Task CopyLink()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.Clipboard is { } clipboard)
        {
            await clipboard.SetValueAsync(DataFormat.Text, Link);
        }
        
        CopyLabel = Tr.Get("Common_Copied");
        await Task.Delay(2000);
        CopyLabel = Tr.Get("Common_Copy");
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
