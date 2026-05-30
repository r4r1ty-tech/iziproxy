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
    /// Генерирует криптографические ключи, UUID и ShortID, выполняя команды на удаленном сервере через SSH.
    /// </summary>
    /// <param name="sshClient">Подключенный SSH-клиент.</param>
    /// <param name="serverConfig">Конфигурация сервера.</param>
    /// <param name="progress">Получатель прогресса и сообщений об ошибках.</param>
    /// <returns>Экземпляр <see cref="XrayConfigParams"/> с заполненными параметрами.</returns>
    /// <exception cref="Exception">Бросается в случае сбоя при выполнении команд генерации.</exception>
    public static async Task<XrayConfigParams> Generate(SSH sshClient, ServerConfig serverConfig, IProgress<string>? progress = null)
    {
        progress?.Report("Генерация ключей Xray...");

        var xrayConfig = new XrayConfigParams();

        // 1. Генерация x25519 ключей (приватный и публичный/password)
        progress?.Report("[DEBUG] Выполнение команды генерации x25519 ключей: /usr/local/bin/xray x25519");
        var x25519Result = await sshClient.RunSudoCommand(serverConfig, "/usr/local/bin/xray x25519");
        string x25519Output = x25519Result.Result;
        progress?.Report($"[DEBUG] Вывод генерации x25519:\n{x25519Output}");

        if (string.IsNullOrWhiteSpace(x25519Output))
        {
            throw new Exception("Ошибка: xray x25519 вернул пустой результат. Проверьте установку Xray.");
        }

        progress?.Report("[DEBUG] Парсинг ключей x25519...");
        string[] lines = x25519Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines)
        {
            if (line.StartsWith("PrivateKey:"))
            {
                xrayConfig.PrivateKey = line.Substring("PrivateKey:".Length).Trim();
            }
            else if (line.StartsWith("Password (PublicKey):"))
            {
                xrayConfig.Password = line.Substring("Password (PublicKey):".Length).Trim();
            }
            else if (line.StartsWith("Private key:"))
            {
                xrayConfig.PrivateKey = line.Substring("Private key:".Length).Trim();
            }
            else if (line.StartsWith("Public key:"))
            {
                xrayConfig.Password = line.Substring("Public key:".Length).Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(xrayConfig.PrivateKey) || string.IsNullOrWhiteSpace(xrayConfig.Password))
        {
            throw new Exception($"Ошибка: Не удалось найти Private key или Public key в выводе команды. Вывод был:\n{x25519Output}");
        }

        progress?.Report($"[DEBUG] Успешно найден PrivateKey (длина: {xrayConfig.PrivateKey.Length}) и Password/PublicKey (длина: {xrayConfig.Password.Length})");

        // 2. Генерация UUID
        progress?.Report("[DEBUG] Выполнение команды генерации UUID: /usr/local/bin/xray uuid");
        var uuidResult = await sshClient.RunSudoCommand(serverConfig, "/usr/local/bin/xray uuid");
        xrayConfig.Uuid = uuidResult.Result.Trim();
        progress?.Report($"[DEBUG] Вывод UUID: {xrayConfig.Uuid}");

        if (string.IsNullOrWhiteSpace(xrayConfig.Uuid))
        {
            throw new Exception("Ошибка: Не удалось сгенерировать UUID.");
        }

        // 3. Генерация случайного ShortID (8 байт в hex-формате)
        progress?.Report("[DEBUG] Выполнение команды генерации ShortID: openssl rand -hex 8");
        var shortIdResult = await sshClient.RunSudoCommand(serverConfig, "openssl rand -hex 8");
        xrayConfig.ShortId = shortIdResult.Result.Trim();
        progress?.Report($"[DEBUG] Вывод ShortID: {xrayConfig.ShortId}");

        progress?.Report($"Xray Keys Generated:\nUUID: {xrayConfig.Uuid}\nPrivateKey: {xrayConfig.PrivateKey}\nPassword: {xrayConfig.Password}\nShortID: {xrayConfig.ShortId}");

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
        progress?.Report("Запрос геолокации VDS...");
        progress?.Report("[DEBUG] Выполнение команды curl -s ipinfo.io/geo");
        var geoResult = await sshClient.RunSudoCommand(serverConfig, "curl -s ipinfo.io/geo");
        string geoOutput = geoResult.Result.Trim();
        progress?.Report($"[DEBUG] Вывод GEO:\n{geoOutput}");
        return geoOutput;
    }
}