using System;

namespace IziProxy;

/// <summary>
/// Предоставляет методы для генерации ссылок подключения клиентов.
/// </summary>
public class VlessLinkGenerator
{
    /// <summary>
    /// Генерирует ссылку vless:// для подключения клиента (VLESS + xhttp + REALITY)
    /// </summary>
    /// <param name="serverConfig">Конфигурация целевого сервера VDS.</param>
    /// <param name="xrayParams">Параметры ключей и настроек Xray.</param>
    /// <param name="connectionName">Название подключения, отображаемое на клиенте.</param>
    /// <returns>Готовая ссылка формата vless:// для импорта в клиентское ПО (например, v2rayN, Nekobox).</returns>
    public static string GenerateRealityLink(ServerConfig serverConfig, XrayConfigParams xrayParams, string connectionName = "IziProxy_VDS")
    {
        string port = xrayParams.Port;
        string type = "xhttp";
        string security = "reality";
        string sni = string.IsNullOrWhiteSpace(xrayParams.Sni) ? "www.microsoft.com" : xrayParams.Sni;
        string path = "/xh-query";
        string fp = "chrome"; 
        var queryParams = new[]
        {
            $"type={type}",
            $"security={security}",
            $"pbk={Uri.EscapeDataString(xrayParams.Password)}",
            $"fp={fp}",
            $"sni={Uri.EscapeDataString(sni)}",
            $"sid={Uri.EscapeDataString(xrayParams.ShortId)}",
            $"path={Uri.EscapeDataString(path)}"
        };

        string query = string.Join("&", queryParams);
        
        // Кодируем название соединения (в конце после #)
        string fragment = Uri.EscapeDataString(connectionName);

        // Итоговый формат vless://uuid@host:port?query#name
        return $"vless://{xrayParams.Uuid}@{serverConfig.Host}:{port}?{query}#{fragment}";
    }
}