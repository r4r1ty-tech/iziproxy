using System;
using System.Collections.Generic;

namespace IziProxy;

/// <summary>
/// Представляет параметры конфигурации Xray, включая криптографические ключи, UUID, ShortID, порты и SNI.
/// </summary>
public class XrayConfigParams
{
    /// <summary>
    /// Приватный ключ x25519 для Reality.
    /// </summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>
    /// Публичный ключ x25519 (в формате Xray используется как Password для VLESS Reality).
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Уникальный идентификатор пользователя (UUID).
    /// </summary>
    public string Uuid { get; set; } = string.Empty;

    /// <summary>
    /// Короткий шестнадцатеричный идентификатор (Short ID) для Reality.
    /// </summary>
    public string ShortId { get; set; } = string.Empty;

    /// <summary>
    /// Порты, на которых слушает Xray. Индекс 0 — приоритетный (443 или рандом),
    /// индекс 1 — второй (8443 или рандом), индекс 2 — рандомный.
    /// Заполняется после деплоя скриптом Deploy.sh.
    /// </summary>
    public List<string> Ports { get; set; } = new List<string>();

    /// <summary>
    /// Домены SNI для каждого inbound. Индекс соответствует индексу в Ports.
    /// Заполняется после деплоя скриптом Deploy.sh.
    /// </summary>
    public List<string> Snis { get; set; } = new List<string>();

    /// <summary>
    /// Парсит вывод команды <c>/usr/local/bin/xray x25519</c> и возвращает
    /// пару (PrivateKey, PublicKey).
    /// </summary>
    /// <remarks>
    /// Xray-core менял формат вывода между версиями:
    /// <list type="bullet">
    /// <item>Старый формат: <c>PrivateKey: ...</c> + <c>Password (PublicKey): ...</c></item>
    /// <item>Альтернативный старый: <c>Private key: ...</c> + <c>Public key: ...</c></item>
    /// <item>Новый (xray v25.3.6+): <c>PrivateKey: ...</c> + <c>Password: ...</c> + <c>Hash32: ...</c></item>
    /// </list>
    /// Эта функция принимает все три формата. Если ни один не распознан —
    /// кидает <see cref="FormatException"/> с указанием ожидаемых префиксов.
    /// </remarks>
    /// <param name="raw">Полный вывод <c>xray x25519</c> (stdout).</param>
    /// <returns>Tuple (PrivateKey, PublicKey) — base64-строки.</returns>
    /// <exception cref="ArgumentException">Пустой или null вход.</exception>
    /// <exception cref="FormatException">Не найден ни PrivateKey, ни PublicKey.</exception>
    public static (string PrivateKey, string PublicKey) ParseX25519Output(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new ArgumentException(
                "xray x25519 вернул пустой результат. Проверьте установку Xray.",
                nameof(raw));
        }

        string? privateKey = null;
        string? publicKey = null;

        foreach (string line in raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();

            // PrivateKey. Поддерживаем оба варианта написания: "PrivateKey:"
            // (основной) и "Private key:" (встречается в старых выводах).
            if (trimmed.StartsWith("PrivateKey:", StringComparison.Ordinal))
            {
                privateKey = trimmed["PrivateKey:".Length..].Trim();
            }
            else if (trimmed.StartsWith("Private key:", StringComparison.Ordinal))
            {
                privateKey = trimmed["Private key:".Length..].Trim();
            }

            // PublicKey. Три варианта:
            //   - "Password (PublicKey):" — длинный формат (старый xray)
            //   - "Password:" — короткий формат (xray v25.3.6+, появился вместе с Hash32)
            //   - "Public key:" — альтернативный старый
            // Проверяем "Password (PublicKey):" ПЕРЕД "Password:" чтобы не
            // захватить лишнее "(PublicKey)" в base64.
            else if (trimmed.StartsWith("Password (PublicKey):", StringComparison.Ordinal))
            {
                publicKey = trimmed["Password (PublicKey):".Length..].Trim();
            }
            else if (trimmed.StartsWith("Password:", StringComparison.Ordinal)
                     && !trimmed.StartsWith("Password (", StringComparison.Ordinal))
            {
                publicKey = trimmed["Password:".Length..].Trim();
            }
            else if (trimmed.StartsWith("Public key:", StringComparison.Ordinal))
            {
                publicKey = trimmed["Public key:".Length..].Trim();
            }
        }

        if (string.IsNullOrEmpty(privateKey) || string.IsNullOrEmpty(publicKey))
        {
            throw new FormatException(
                $"Не удалось найти PrivateKey/PublicKey в выводе xray x25519. " +
                $"Ожидаются префиксы 'PrivateKey:'/'Password:' или 'Private key:'/'Public key:'. " +
                $"Вывод был:\n{raw}");
        }

        return (privateKey, publicKey);
    }

    /// <summary>
    /// Генерирует криптографические ключи, UUID и ShortID, выполняя команды на удаленном сервере через SSH.
    /// </summary>
    /// <param name="sshClient">Подключенный SSH-клиент.</param>
    /// <param name="serverConfig">Конфигурация сервера.</param>
    /// <param name="progress">Получатель прогресса и сообщений об ошибках.</param>
    /// <returns>Экземпляр <see cref="XrayConfigParams"/> с заполненными параметрами.</returns>
    /// <exception cref="Exception">Бросается в случае сбоя при выполнении команд генерации.</exception>
    public static async Task<XrayConfigParams> Generate(SSH sshClient, ServerConfig serverConfig, IProgress<string>? progress = null)
    {
        progress?.Report("[TRACE] XrayConfigParams.Generate вход: генерируем ключи/UUID/ShortID на сервере");
        progress?.Report("[INFO] Генерация ключей Xray (x25519, UUID, ShortID)");

        var xrayConfig = new XrayConfigParams();

        // 1. Генерация x25519 ключей (приватный и публичный/password)
        progress?.Report("[DEBUG] Выполнение команды генерации x25519 ключей: /usr/local/bin/xray x25519");
        var x25519Result = await sshClient.RunSudoCommand(serverConfig, "/usr/local/bin/xray x25519");
        // Result может быть null если SSH.NET вернул пустой SshCommand —
        // ParseX25519Output всё равно бросит ArgumentException на пустом входе,
        // но нормализуем здесь чтобы flow analysis не жаловался на CS8604.
        string x25519Output = x25519Result.Result ?? string.Empty;
        progress?.Report($"[DEBUG] Вывод генерации x25519 (длина={x25519Output.Length}):\n{x25519Output}");

        // Парсинг ключей. Поддерживает три формата вывода: старый
        // "PrivateKey:/Password (PublicKey):", альтернативный "Private key:/Public key:",
        // и новый (xray v25.3.6+) "PrivateKey:/Password:/Hash32:".
        (string privateKey, string publicKey) = ParseX25519Output(x25519Output);
        xrayConfig.PrivateKey = privateKey;
        xrayConfig.Password = publicKey;

        progress?.Report($"[DEBUG] Успешно найден PrivateKey (длина: {xrayConfig.PrivateKey.Length}) и Password/PublicKey (длина: {xrayConfig.Password.Length})");
        progress?.Report("[INFO] x25519 ключи получены");

        // 2. Генерация UUID
        progress?.Report("[DEBUG] Выполнение команды генерации UUID: /usr/local/bin/xray uuid");
        var uuidResult = await sshClient.RunSudoCommand(serverConfig, "/usr/local/bin/xray uuid");
        xrayConfig.Uuid = uuidResult.Result.Trim();
        progress?.Report($"[DEBUG] Вывод UUID: {xrayConfig.Uuid}");

        if (string.IsNullOrWhiteSpace(xrayConfig.Uuid))
        {
            progress?.Report("[ERROR] Не удалось сгенерировать UUID (пустой вывод)");
            throw new Exception("Ошибка: Не удалось сгенерировать UUID.");
        }
        progress?.Report("[INFO] UUID получен");

        // 3. Генерация случайного ShortID (8 байт в hex-формате)
        progress?.Report("[DEBUG] Выполнение команды генерации ShortID: openssl rand -hex 8");
        var shortIdResult = await sshClient.RunSudoCommand(serverConfig, "openssl rand -hex 8");
        xrayConfig.ShortId = shortIdResult.Result.Trim();
        progress?.Report($"[DEBUG] Вывод ShortID: {xrayConfig.ShortId}");

        if (string.IsNullOrWhiteSpace(xrayConfig.ShortId))
        {
            progress?.Report("[WARN] ShortID пустой (openssl ничего не вернул) — клиенты могут не подключаться");
        }
        else
        {
            progress?.Report("[INFO] ShortID получен");
        }

        progress?.Report($"[INFO] Xray Keys Generated: UUID={xrayConfig.Uuid}, PrivateKey={xrayConfig.PrivateKey}, Password={xrayConfig.Password}, ShortID={xrayConfig.ShortId}");

        return xrayConfig;
    }

    /// <summary>
    /// Запрашивает географическое положение VDS сервера, используя внешний сервис ipinfo.io.
    /// </summary>
    /// <param name="sshClient">Подключенный SSH-клиент.</param>
    /// <param name="serverConfig">Конфигурация сервера.</param>
    /// <param name="progress">Получатель прогресса и сообщений об ошибках.</param>
    /// <returns>Строка с JSON-информацией о геопозиции сервера.</returns>
    public static async Task<string> GetGeoVDS(SSH sshClient, ServerConfig serverConfig, IProgress<string>? progress = null)
    {
        progress?.Report("[TRACE] XrayConfigParams.GetGeoVDS вход: запрашиваем geo через ipinfo.io");
        progress?.Report("[INFO] Запрос геолокации VDS");
        progress?.Report("[DEBUG] Выполнение команды curl -s ipinfo.io/geo");
        var geoResult = await sshClient.RunSudoCommand(serverConfig, "curl -s ipinfo.io/geo");
        string geoOutput = geoResult.Result.Trim();
        progress?.Report($"[DEBUG] Вывод GEO (длина={geoOutput.Length}):\n{geoOutput}");
        if (string.IsNullOrWhiteSpace(geoOutput))
        {
            progress?.Report("[WARN] GEO пустое (ipinfo.io недоступен с сервера?)");
        }
        return geoOutput;
    }
}