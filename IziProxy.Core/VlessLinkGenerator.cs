using System;
using System.Collections.Generic;

namespace IziProxy;

/// <summary>
/// Предоставляет методы для генерации ссылок подключения клиентов.
/// </summary>
public class VlessLinkGenerator
{
    /// <summary>
    /// Генерирует список ссылок vless:// для каждого inbound (VLESS + xhttp + REALITY).
    /// Количество ссылок равно количеству портов в xrayParams.Ports.
    /// </summary>
    /// <param name="serverConfig">Конфигурация целевого сервера VDS.</param>
    /// <param name="xrayParams">Параметры ключей и настроек Xray. Должны быть заполнены Ports и Snis.</param>
    /// <param name="connectionName">Базовое название подключения, отображаемое на клиенте.</param>
    /// <returns>Список готовых ссылок формата vless:// для импорта в клиентское ПО (v2rayN, Nekobox и др.).</returns>
    public static List<string> GenerateRealityLinks(ServerConfig serverConfig, XrayConfigParams xrayParams, string connectionName = "IziProxy_VDS")
    {
        var links = new List<string>();

        for (int i = 0; i < xrayParams.Ports.Count; i++)
        {
            string port = xrayParams.Ports[i];
            string sni = xrayParams.Snis[i];

            if (string.IsNullOrWhiteSpace(sni))
            {
                sni = "www.microsoft.com";
            }

            string linkName = connectionName + "_" + (i + 1);

            string link = BuildLink(serverConfig.Host, port, xrayParams.Uuid, xrayParams.Password, sni, xrayParams.ShortId, linkName);
            links.Add(link);
        }

        return links;
    }

    /// <summary>
    /// Собирает одну ссылку vless:// из переданных параметров.
    /// </summary>
    private static string BuildLink(string host, string port, string uuid, string publicKey, string sni, string shortId, string linkName)
    {
        string type = "xhttp";
        string security = "reality";
        string path = "/xh-query";
        string fingerprint = "chrome";

        var queryParams = new[]
        {
            "type=" + type,
            "security=" + security,
            "pbk=" + Uri.EscapeDataString(publicKey),
            "fp=" + fingerprint,
            "sni=" + Uri.EscapeDataString(sni),
            "sid=" + Uri.EscapeDataString(shortId),
            "path=" + Uri.EscapeDataString(path)
        };

        string query = string.Join("&", queryParams);
        string fragment = Uri.EscapeDataString(linkName);

        return "vless://" + uuid + "@" + host + ":" + port + "?" + query + "#" + fragment;
    }
}