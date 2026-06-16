using System;
using System.IO;
using System.Collections.Generic;
using Xunit;

namespace IziProxy.Tests;

/// <summary>
/// Smoke-тесты для SecureField + VdsProfileService (ISS-02).
/// Покрывают три сценария: round-trip шифрования, миграция legacy plaintext,
/// реальный save/load с проверкой file mode 0600.
/// </summary>
public class SecureProfileTests
{
    [Fact]
    public void SecureField_RoundTrip_EncryptsAndDecrypts()
    {
        var sf = new SecureField("IziProxy.Tests.SecureProfile.RoundTrip");
        const string secret = "P@ssw0rd!#$%^&*()_+-=[]{}|;':\",./<>?`~";

        string encrypted = sf.Protect(secret);
        string decrypted = sf.Unprotect(encrypted);

        Assert.NotEqual(secret, encrypted);
        Assert.StartsWith(SecureField.Prefix, encrypted);
        Assert.DoesNotContain(secret, encrypted, StringComparison.Ordinal);
        Assert.Equal(secret, decrypted);
    }

    [Fact]
    public void SecureField_EmptyString_ReturnsEmpty()
    {
        var sf = new SecureField("IziProxy.Tests.SecureProfile.Empty");
        Assert.Equal(string.Empty, sf.Protect(string.Empty));
        Assert.Equal(string.Empty, sf.Protect(null!));
        Assert.Equal(string.Empty, sf.Unprotect(string.Empty));
    }

    [Fact]
    public void SecureField_LegacyPlaintext_ReturnsAsIs()
    {
        // Старые профили до ISS-02 лежали в JSON открытым текстом.
        // SecureField должен вернуть их as-is, чтобы UI не упал.
        var sf = new SecureField("IziProxy.Tests.SecureProfile.Legacy");
        const string legacyPlain = "old-plain-password-12345";

        string result = sf.Unprotect(legacyPlain);

        Assert.Equal(legacyPlain, result);
    }

    [Fact]
    public void SecureField_DifferentInstances_SameSecret_ProducesDifferentCiphertext()
    {
        // Каждый Protect() использует уникальный nonce внутри DataProtection,
        // поэтому один и тот же secret шифруется в разные blob'ы. Это
        // базовая гарантия semantic security.
        var sf = new SecureField("IziProxy.Tests.SecureProfile.Nonce");
        const string secret = "same-secret";

        string blob1 = sf.Protect(secret);
        string blob2 = sf.Protect(secret);

        Assert.NotEqual(blob1, blob2);
        Assert.Equal(secret, sf.Unprotect(blob1));
        Assert.Equal(secret, sf.Unprotect(blob2));
    }

    [Fact]
    public void VdsProfileService_SaveAndLoad_PreservesPlaintext()
    {
        // Используем явный путь к временному файлу — не трогаем реальный
        // %LOCALAPPDATA% / ~/.local/share. Это работает потому что
        // VdsProfileService.LoadProfiles/SaveProfiles принимает опциональный
        // параметр filePath.
        var tempFile = Path.Combine(
            Path.GetTempPath(),
            "IziProxy.Tests_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var profile = new VdsProfile
            {
                Name = "test-server",
                Host = "1.2.3.4",
                Username = "root",
                Password = "S3cret!@#$%^&*()_+",
                SshKeyPath = "/root/.ssh/id_rsa"
            };
            var profiles = new List<VdsProfile> { profile };

            VdsProfileService.SaveProfiles(profiles, tempFile);

            Assert.True(File.Exists(tempFile), $"File not created at {tempFile}");

            // Файл НЕ должен содержать пароль открытым текстом
            string json = File.ReadAllText(tempFile);
            Assert.DoesNotContain(profile.Password, json, StringComparison.Ordinal);
            Assert.Contains(SecureField.Prefix, json, StringComparison.Ordinal);

            // На *nix — файл должен быть 0600
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
            {
                var mode = File.GetUnixFileMode(tempFile);
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    mode);
            }

            // Загружаем обратно — пароль должен расшифроваться
            var loaded = VdsProfileService.LoadProfiles(tempFile);
            Assert.Single(loaded);
            Assert.Equal(profile.Password, loaded[0].Password);
            Assert.Equal(profile.Name, loaded[0].Name);
            Assert.Equal(profile.Host, loaded[0].Host);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public void VdsProfileService_LoadMissingFile_ReturnsEmpty()
    {
        var tempFile = Path.Combine(
            Path.GetTempPath(),
            "IziProxy.Tests_Missing_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var loaded = VdsProfileService.LoadProfiles(tempFile);
            Assert.Empty(loaded);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public void VdsProfileService_LoadLegacyPlaintext_UpgradesOnNextSave()
    {
        // Старые профили хранили пароль в plaintext (без префикса enc:v1:).
        // При Load они расшифровываются как есть (legacy pass-through),
        // при следующем Save — перешифровываются с префиксом.
        var tempFile = Path.Combine(
            Path.GetTempPath(),
            "IziProxy.Tests_Legacy_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            const string legacyPlain = "old-plain-password-12345";
            // Пишем JSON руками, как будто это файл от старой версии
            File.WriteAllText(tempFile, $$"""
                [
                  {
                    "Name": "legacy-server",
                    "Host": "10.0.0.1",
                    "Username": "root",
                    "Password": "{{legacyPlain}}",
                    "SshKeyPath": ""
                  }
                ]
                """);

            // Load: пароль должен прийти plain
            var loaded = VdsProfileService.LoadProfiles(tempFile);
            Assert.Single(loaded);
            Assert.Equal(legacyPlain, loaded[0].Password);

            // Save: пароль должен быть перешифрован
            VdsProfileService.SaveProfiles(loaded, tempFile);
            string json = File.ReadAllText(tempFile);
            Assert.DoesNotContain(legacyPlain, json, StringComparison.Ordinal);
            Assert.Contains(SecureField.Prefix, json, StringComparison.Ordinal);

            // Reload после upgrade — пароль всё ещё plain в памяти
            var reloaded = VdsProfileService.LoadProfiles(tempFile);
            Assert.Equal(legacyPlain, reloaded[0].Password);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }
}
