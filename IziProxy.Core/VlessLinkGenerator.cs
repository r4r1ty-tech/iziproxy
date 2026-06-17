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
    /// <param name="progress">Получатель прогресса (опционально).</param>
    /// <returns>Список готовых ссылок формата vless:// для импорта в клиентское ПО (v2rayN, Nekobox и др.).</returns>
    public static List<string> GenerateRealityLinks(ServerConfig serverConfig, XrayConfigParams xrayParams, string connectionName = "IziProxy_VDS", IProgress<string>? progress = null)
    {
        progress?.Report($"[TRACE] VlessLinkGenerator.GenerateRealityLinks вход: ports={xrayParams.Ports.Count}, snis={xrayParams.Snis.Count}");

        if (xrayParams.Ports.Count == 0 || xrayParams.Snis.Count == 0)
        {
            progress?.Report("[ERROR] Нет портов или SNI для генерации VLESS-ссылок");
            return new List<string>();
        }

        if (xrayParams.Ports.Count != xrayParams.Snis.Count)
        {
            progress?.Report($"[WARN] Количество портов ({xrayParams.Ports.Count}) не совпадает с количеством SNI ({xrayParams.Snis.Count}) — будут сгенерированы ссылки только по минимальному количеству");
        }

        var links = new List<string>();
        int linkCount = Math.Min(xrayParams.Ports.Count, xrayParams.Snis.Count);
        progress?.Report($"[INFO] Генерация {linkCount} VLESS-ссылок (xhttp+REALITY) для {serverConfig.Host}");

        for (int i = 0; i < linkCount; i++)
        {
            string port = xrayParams.Ports[i];
            string sni = xrayParams.Snis[i];

            if (string.IsNullOrWhiteSpace(sni))
            {
                progress?.Report($"[WARN] SNI #{i + 1} пустой — подставляем fallback www.microsoft.com");
                sni = "www.microsoft.com";
            }

            string linkName = connectionName + "_" + (i + 1);

            string link = BuildLink(serverConfig.Host, port, xrayParams.Uuid, xrayParams.Password, sni, xrayParams.ShortId, linkName);
            links.Add(link);
            progress?.Report($"[DEBUG] Ссылка #{i + 1} собрана: port={port}, sni={sni}, name={linkName}, длина={link.Length}");
        }

        progress?.Report($"[INFO] Сгенерировано {links.Count} VLESS-ссылок");
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