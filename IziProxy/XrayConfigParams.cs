using System;

namespace IziProxy;

/// <summary>
/// Представляет параметры конфигурации Xray, включая криптографические ключи, UUID, ShortID, порт и SNI.
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
    /// Порт, на котором будет слушать Xray на сервере.
    /// </summary>
    public string Port { get; set; } = "443";

    /// <summary>
    /// Доменное имя (SNI), используемое для маскировки TLS-трафика (Reality dest).
    /// </summary>
    public string Sni { get; set; } = "www.microsoft.com";

    /// <summary>
    /// Генерирует криптографические ключи, UUID и ShortID, выполняя команды на удаленном сервере через SSH.
    /// </summary>
    /// <param name="sshClient">Подключенный SSH-клиент.</param>
    /// <param name="serverConfig">Конфигурация сервера.</param>
    /// <returns>Экземпляр <see cref="XrayConfigParams"/> с заполненными параметрами.</returns>
    /// <exception cref="Exception">Бросается в случае сбоя при выполнении команд генерации.</exception>
    public static XrayConfigParams Generate(SSH sshClient, ServerConfig serverConfig)
    {
        Console.WriteLine("Генерация ключей Xray...");
        
        var xrayConfig = new XrayConfigParams();

        // 1. Генерация x25519 ключей (приватный и публичный/password)
        var x25519Result = sshClient.RunSudoCommand(serverConfig, "xray x25519");
        var x25519Output = x25519Result.Result;
        
        foreach (var line in x25519Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("PrivateKey:"))
            {
                xrayConfig.PrivateKey = line.Substring("PrivateKey:".Length).Trim();
            }
            else if (line.StartsWith("Password (PublicKey):"))
            {
                xrayConfig.Password = line.Substring("Password (PublicKey):".Length).Trim();
            }
        }

        // 2. Генерация UUID
        var uuidResult = sshClient.RunSudoCommand(serverConfig, "xray uuid");
        xrayConfig.Uuid = uuidResult.Result.Trim();

        // 3. Генерация случайного ShortID (8 байт в hex-формате)
        var shortIdResult = sshClient.RunSudoCommand(serverConfig, "openssl rand -hex 8");
        xrayConfig.ShortId = shortIdResult.Result.Trim();

        Console.WriteLine($"Xray Keys Generated:\nUUID: {xrayConfig.Uuid}\nPrivateKey: {xrayConfig.PrivateKey}\nPassword: {xrayConfig.Password}\nShortID: {xrayConfig.ShortId}");
        
        return xrayConfig;
    }

    /// <summary>
    /// Запрашивает географическое положение VDS сервера, используя внешний сервис ipinfo.io.
    /// </summary>
    /// <param name="sshClient">Подключенный SSH-клиент.</param>
    /// <param name="serverConfig">Конфигурация сервера.</param>
    /// <returns>Строка с JSON-информацией о геопозиции сервера.</returns>
    public static string GetGeoVDS(SSH sshClient, ServerConfig serverConfig)
    {
        var geoResult = sshClient.RunSudoCommand(serverConfig, "curl -s ipinfo.io/geo");
        return geoResult.Result.Trim();
    }
}