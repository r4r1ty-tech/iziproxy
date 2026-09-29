using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IziProxy;

public class VdsProfile
{
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string SshKeyPath { get; set; } = string.Empty;

    // Linux username: POSIX regex, [a-z_][a-z0-9_-]*, max 32 chars.
    // Источник: man useradd, getpwnam() — строгие правила.
    private static readonly Regex UsernameRegex = new(
        @"^[a-z_][a-z0-9_-]{0,31}$",
        RegexOptions.Compiled);

    // Hostname по RFC 1123: labels из букв/цифр/дефиса, не начинаются
    // и не заканчиваются дефисом, общая длина <= 253.
    // IDN-домены (пример.рф) не покрываем — IziProxy работает с raw host/IP.
    private static readonly Regex HostnameRegex = new(
        @"^(?=.{1,253}$)([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)(\.[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$",
        RegexOptions.Compiled);

    /// <summary>
    /// Валидирует все поля профиля. Возвращает список ошибок (пустой
    /// если всё ок). Не бросает исключения — caller решает что делать
    /// (показать в UI, залогировать, отказать в Save).
    /// </summary>
    /// <remarks>
    /// Правила:
    /// <list type="bullet">
    /// <item><c>Name</c> — обязательное, 1-64 символа, не пустое после Trim</item>
    /// <item><c>Host</c> — обязательное, валидный IPv4 / IPv6 / RFC 1123 hostname</item>
    /// <item><c>Username</c> — обязательное, POSIX username regex</item>
    /// <item><c>Password</c> — обязательное только если <c>SshKeyPath</c> пустой
    ///   (либо пароль, либо ключ — иначе аутентификация не пройдёт)</item>
    /// <item><c>SshKeyPath</c> — если не пустой, не должен содержать <c>..</c>
    ///   (path traversal), абсолютный путь на Linux-style</item>
    /// </list>
    /// </remarks>
    public List<string> Validate()
    {
        var errors = new List<string>();

        // Name
        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add(Tr.Get("Profile_NameEmpty"));
        }
        else if (Name.Trim().Length > 64)
        {
            errors.Add(Tr.F("Profile_NameTooLong", Name.Trim().Length));
        }

        // Host
        if (string.IsNullOrWhiteSpace(Host))
        {
            errors.Add(Tr.Get("Profile_HostEmpty"));
        }
        else if (!IsValidHostOrIp(Host.Trim()))
        {
            errors.Add(Tr.F("Profile_HostInvalid", Host.Trim()));
        }

        // Username
        if (string.IsNullOrWhiteSpace(Username))
        {
            errors.Add(Tr.Get("Profile_UsernameEmpty"));
        }
        else if (!UsernameRegex.IsMatch(Username.Trim()))
        {
            errors.Add(Tr.F("Profile_UsernameInvalid", Username.Trim()));
        }

        // Password / SshKeyPath — нужен хотя бы один
        bool hasPassword = !string.IsNullOrEmpty(Password);
        bool hasKey = !string.IsNullOrWhiteSpace(SshKeyPath);
        if (!hasPassword && !hasKey)
        {
            errors.Add(Tr.Get("Profile_NoAuth"));
        }

        // SshKeyPath — если задан, проверяем на path traversal
        if (hasKey)
        {
            string keyPath = SshKeyPath.Trim();
            if (keyPath.Contains("..", StringComparison.Ordinal))
            {
                errors.Add(Tr.F("Profile_KeyTraversal", keyPath));
            }
            // На Linux ключи почти всегда в /home/*/.ssh/ или /root/.ssh/.
            // Абсолютный путь — must. Relative ("id_rsa") опасен тем, что
            // резолвится относительно CWD процесса, который на Linux
            // непредсказуем (особенно под systemd). Оставляем как warning,
            // не error — caller может переопределить.
            if (!keyPath.StartsWith('/') && !keyPath.StartsWith("~/", StringComparison.Ordinal))
            {
                errors.Add(Tr.F("Profile_KeyNotAbsolute", keyPath));
            }
        }

        return errors;
    }

    /// <summary>
    /// Проверяет что строка — валидный IPv4, IPv6 или RFC 1123 hostname.
    /// </summary>
    private static bool IsValidHostOrIp(string host)
    {
        if (string.IsNullOrEmpty(host)) return false;

        // IPv4 или IPv6
        if (System.Net.IPAddress.TryParse(host, out _)) return true;

        // Hostname (FQDN или простое имя)
        return HostnameRegex.IsMatch(host);
    }
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

    public static List<VdsProfile> LoadProfiles(string? filePath = null, IProgress<string>? progress = null)
    {
        var path = filePath ?? DefaultFilePath;
        progress?.Report($"[TRACE] VdsProfileService.LoadProfiles enter: path={path}");
        try
        {
            if (!File.Exists(path))
            {
                progress?.Report("[INFO] " + Tr.F("Profile_FileMissing", path));
                return new List<VdsProfile>();
            }

            string json = File.ReadAllText(path);
            progress?.Report($"[DEBUG] Read profiles JSON, length={json.Length} bytes");

            var loaded = JsonSerializer.Deserialize<List<VdsProfile>>(json)
                ?? new List<VdsProfile>();
            progress?.Report($"[DEBUG] Deserialized {loaded.Count} profiles from JSON");

            // Расшифровываем Password у каждого профиля. Legacy plain-text
            // (без префикса enc:v1:) SecureField.Unprotect вернёт as is.
            var sf = _secureField.Value;
            int legacyCount = 0;
            foreach (var p in loaded)
            {
                string before = p.Password;
                p.Password = sf.Unprotect(p.Password);
                // SecureField не помечает legacy plain vs decrypted — на
                // глаз их не отличить, считаем legacy все, что не начинается
                // с префикса enc:v1:. Не критично, просто статистика.
                if (!string.IsNullOrEmpty(before) && !before.StartsWith(SecureField.Prefix, StringComparison.Ordinal))
                {
                    legacyCount++;
                }
            }
            if (legacyCount > 0)
            {
                progress?.Report("[WARN] " + Tr.F("Profile_Legacy", legacyCount));
            }
            progress?.Report("[INFO] " + Tr.F("Profile_Loaded", loaded.Count, path));
            return loaded;
        }
        catch (Exception ex)
        {
            progress?.Report("[ERROR] " + Tr.F("Profile_LoadError", path, ex.Message));
            return new List<VdsProfile>();
        }
    }

    public static void SaveProfiles(List<VdsProfile> profiles, string? filePath = null, IProgress<string>? progress = null)
    {
        var path = filePath ?? DefaultFilePath;
        progress?.Report($"[TRACE] VdsProfileService.SaveProfiles enter: profiles={profiles.Count}, path={path}");
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                progress?.Report($"[DEBUG] Created directory {dir}");
            }

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
            progress?.Report($"[DEBUG] Encrypted {toSerialize.Count} passwords with DataProtection");

            string json = JsonSerializer.Serialize(toSerialize, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            progress?.Report($"[DEBUG] JSON written to {path}, size={json.Length} bytes");

            // Ограничиваем доступ к файлу: на Linux/macOS ставим 0600
            // (read/write только владельцу). На Windows File.SetUnixFileMode
            // бросает PlatformNotSupportedException, поэтому вызываем
            // только под *nix — там ACL по умолчанию уже защищает файл
            // от других пользователей в %LOCALAPPDATA%.
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                progress?.Report("[DEBUG] File mode set to 0600 (owner only)");
            }

            progress?.Report("[INFO] " + Tr.F("Profile_Saved", profiles.Count, path));
        }
        catch (Exception ex)
        {
            progress?.Report("[ERROR] " + Tr.F("Profile_SaveError", path, ex.Message));
            // Игнорируем ошибки записи
        }
    }
}
