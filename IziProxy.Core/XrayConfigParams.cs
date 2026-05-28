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
        var x25519Result = await sshClient.RunSudoCommand(serverConfig, "xray x25519");
        var x25519Output = x25519Result.Result;

        foreach (var line in x25519Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("PrivateKey:"))
                xrayConfig.PrivateKey = line.Substring("PrivateKey:".Length).Trim();
            else if (line.StartsWith("Password (PublicKey):"))
                xrayConfig.Password = line.Substring("Password (PublicKey):".Length).Trim();
        }

        // 2. Генерация UUID
        var uuidResult = await sshClient.RunSudoCommand(serverConfig, "xray uuid");
        xrayConfig.Uuid = uuidResult.Result.Trim();

        // 3. Генерация случайного ShortID (8 байт в hex-формате)
        var shortIdResult = await sshClient.RunSudoCommand(serverConfig, "openssl rand -hex 8");
        xrayConfig.ShortId = shortIdResult.Result.Trim();

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
        var geoResult = await sshClient.RunSudoCommand(serverConfig, "curl -s ipinfo.io/geo");
        return geoResult.Result.Trim();
    }
}