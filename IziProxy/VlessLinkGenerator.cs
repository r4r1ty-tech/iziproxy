using System;

namespace IziProxy;

public class VlessLinkGenerator
{
    /// <summary>
    /// Генерирует ссылку vless:// для подключения клиента (VLESS + xhttp + REALITY)
    /// </summary>
    public static string GenerateRealityLink(ServerConfig serverConfig, XrayConfigParams xrayParams, string connectionName = "IziProxy_VDS")
    {
        string port = xrayParams.Port;
        string type = "xhttp";
        string security = "reality";
        string sni = "www.microsoft.com";
        string path = "/xh-query";
        string fp = "chrome"; // Рекомендуемый fingerprint для маскировки
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