using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace IziProxy;

/// <summary>
/// Обёртка над Microsoft.AspNetCore.DataProtection для безопасного
/// хранения строковых секретов (паролей) в файлах конфигурации.
/// </summary>
/// <remarks>
/// <para>
/// На каждой платформе ключи шифрования хранятся в месте, защищённом
/// операционной системой:
///   - Windows: %LOCALAPPDATA%\ASP.NET\DataProtection-Keys (DPAPI)
///   - Linux:   ~/.aspnet/DataProtection-Keys (filesystem permissions 0700)
///   - macOS:   ~/Library/Application Support/ASP.NET/DataProtection-Keys
/// </para>
/// <para>
/// Зашифрованное значение предваряется префиксом <c>enc:v1:</c> —
/// это даёт нам версионирование формата (на случай если в будущем
/// понадобится мигрировать на другой keyring) и однозначно отличает
/// зашифрованное значение от legacy plain-text, оставшегося от
/// старых версий IziProxy.
/// </para>
/// <para>
/// Threat model:
///   - Другой пользователь на той же системе НЕ прочтёт зашифрованный
///     пароль (ключи лежат в его собственной домашней директории).
///   - Бэкап-админ с правами root НА LINUX/WINDOWS может прочесть
///     ключи и расшифровать — это фундаментальное ограничение любого
///     user-space encryption, не связанного с TPM/secure enclave.
///   - Защита от случайной утечки JSON: наивно открыть
///     profiles.json в редакторе пароль уже не даст.
/// </para>
/// </remarks>
public sealed class SecureField
{
    /// <summary>Префикс версии формата. Меняется при breaking-изменениях схемы.</summary>
    public const string Prefix = "enc:v1:";

    private readonly IDataProtector _protector;

    /// <param name="purpose">
    /// Дополнительный "purpose" — изолирует области применения.
    /// Передаётся как SUB-purpose от root "IziProxy.SecureField", чтобы
    /// SecureField("A") и SecureField("B") не могли расшифровать blob
    /// друг друга. Согласно документации DataProtection, изоляция
    /// гарантируется только для sub-purposes от общего root, не для
    /// двух разных root-уровневых purposes.
    /// </param>
    public SecureField(string purpose)
    {
        // DataProtectionProvider.Create с именем приложения создаёт
        // провайдер с дефолтным хранилищем ключей. На Linux ключи
        // автоматически создаются в ~/.aspnet/DataProtection-Keys/ при
        // первом обращении.
        var provider = DataProtectionProvider.Create("IziProxy");
        var root = provider.CreateProtector("IziProxy.SecureField");
        _protector = root.CreateProtector(purpose);
    }

    /// <summary>Зашифровать plain-строку. Пустая строка → пустая строка.</summary>
    public string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return string.Empty;
        var bytes = _protector.Protect(Encoding.UTF8.GetBytes(plain));
        return Prefix + Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Расшифровать строку. Поддерживает три формата:
    ///   - пустая строка → пустая строка
    ///   - строка с префиксом <c>enc:v1:</c> → расшифровка
    ///   - строка без префикса → возвращается как есть (legacy plain
    ///     из profiles.json старой версии; caller решает — перешифровать
    ///     и сохранить обратно или выкинуть).
    /// </summary>
    public string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return string.Empty;
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            // Legacy: plaintext от старой версии. Возвращаем as is.
            return stored;
        }

        try
        {
            var b64 = stored[Prefix.Length..];
            var bytes = _protector.Unprotect(Convert.FromBase64String(b64));
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException)
        {
            // Битый формат / ключи были пересозданы (например, после смены
            // hostname) — возвращаем пустую строку. Юзер должен ввести
            // пароль заново.
            return string.Empty;
        }
    }
}
