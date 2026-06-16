using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace IziProxy;

public class VdsProfile
{
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string SshKeyPath { get; set; } = string.Empty;
}

/// <summary>
/// Хранит профили VDS-серверов в JSON в %LOCALAPPDATA%/IziProxy/profiles.json
/// (Windows) или ~/.local/share/IziProxy/profiles.json (Linux).
/// </summary>
/// <remarks>
/// Поле <see cref="VdsProfile.Password"/> в файле хранится зашифрованным
/// через <see cref="IziProxy.SecureField"/> (Microsoft.AspNetCore.DataProtection,
/// purpose="IziProxy.VdsProfile.Password"). Ключи лежат в user-private
/// директории (DPAPI на Windows, filesystem permissions на Linux).
/// При чтении пароль расшифровывается, при записи — шифруется.
///
/// Файл profiles.json создаётся с правами 0600 — readable только владельцу.
/// Старые профили в plaintext распознаются префиксом и при первом
/// сохранении автоматически перешифровываются.
/// </remarks>
public static class VdsProfileService
{
    // Дефолтный путь к файлу профилей — в %LOCALAPPDATA%/IziProxy/profiles.json.
    // Используется когда caller не передаёт явный путь (например, GUI).
    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IziProxy",
        "profiles.json");

    // Lazy-init, чтобы DataProtectionProvider не поднимался на старте приложения
    // (это занимает ~50ms и читает/создаёт ключи на диске).
    private static readonly Lazy<SecureField> _secureField = new(
        () => new SecureField("IziProxy.VdsProfile.Password"));

    public static List<VdsProfile> LoadProfiles(string? filePath = null)
    {
        var path = filePath ?? DefaultFilePath;
        try
        {
            if (!File.Exists(path)) return new List<VdsProfile>();
            string json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<List<VdsProfile>>(json)
                ?? new List<VdsProfile>();

            // Расшифровываем Password у каждого профиля. Legacy plain-text
            // (без префикса enc:v1:) SecureField.Unprotect вернёт as is.
            var sf = _secureField.Value;
            foreach (var p in loaded)
            {
                p.Password = sf.Unprotect(p.Password);
            }
            return loaded;
        }
        catch
        {
            return new List<VdsProfile>();
        }
    }

    public static void SaveProfiles(List<VdsProfile> profiles, string? filePath = null)
    {
        var path = filePath ?? DefaultFilePath;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Шифруем Password каждого профиля перед сериализацией.
            // Делаем копию чтобы не мутировать переданный список (UI
            // продолжает работать с plain-значениями).
            var toSerialize = new List<VdsProfile>(profiles.Count);
            var sf = _secureField.Value;
            foreach (var p in profiles)
            {
                toSerialize.Add(new VdsProfile
                {
                    Name       = p.Name,
                    Host       = p.Host,
                    Username   = p.Username,
                    Password   = sf.Protect(p.Password),
                    SshKeyPath = p.SshKeyPath,
                });
            }

            string json = JsonSerializer.Serialize(toSerialize, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);

            // Ограничиваем доступ к файлу: на Linux/macOS ставим 0600
            // (read/write только владельцу). На Windows File.SetUnixFileMode
            // бросает PlatformNotSupportedException, поэтому вызываем
            // только под *nix — там ACL по умолчанию уже защищает файл
            // от других пользователей в %LOCALAPPDATA%.
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch
        {
            // Игнорируем ошибки записи
        }
    }
}
