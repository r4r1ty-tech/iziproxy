using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace IziProxy;

/// <summary>
/// Информация об SNI одного inbound'а Xray.
/// </summary>
public class InboundSniInfo
{
    /// <summary>Тег inbound'а (например "inbound-1").</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>Порт, на котором слушает inbound.</summary>
    public string Port { get; set; } = string.Empty;

    /// <summary>Текущий SNI-домен из realitySettings.serverNames[0].</summary>
    public string CurrentSni { get; set; } = string.Empty;

    /// <summary>Индекс в массиве inbounds[] (0-based).</summary>
    public int InboundIndex { get; set; }
}

/// <summary>
/// Сервис для чтения, замены и автоподбора SNI-доменов в конфигурации Xray на сервере.
/// </summary>
/// <remarks>
/// Конфигурация Xray находится в <c>/usr/local/etc/xray/config.json</c>.
/// Каждый inbound содержит <c>streamSettings.realitySettings.dest</c> и
/// <c>streamSettings.realitySettings.serverNames[]</c> — это SNI-домены,
/// используемые для маскировки трафика. Замена SNI нужна когда
/// провайдер/DPI блокирует конкретный домен.
/// </remarks>
public static class SniRepairService
{
    private const string XrayConfigPath = "/usr/local/etc/xray/config.json";

    /// <summary>
    /// Список популярных доменов для автоподбора SNI.
    /// Выбраны за стабильность TLS 1.3, HTTP/2, высокую доступность.
    /// </summary>
    private static readonly string[] CandidateDomains =
    {
        "speed.cloudflare.com",
        "cdn.jsdelivr.net",
        "cdnjs.cloudflare.com",
        "www.cloudflare.com",
        "static.cloudflareinsights.com",
        "fonts.gstatic.com",
        "www.microsoft.com",
        "www.apple.com",
        "addons.mozilla.org",
        "ajax.cloudflare.com"
    };

    /// <summary>
    /// Читает текущие SNI-домены из конфигурации Xray на сервере.
    /// </summary>
    /// <param name="ssh">Подключённый SSH-клиент.</param>
    /// <param name="config">Конфигурация сервера.</param>
    /// <param name="progress">Получатель прогресса.</param>
    /// <returns>Список <see cref="InboundSniInfo"/> для каждого inbound'а.</returns>
    public static async Task<List<InboundSniInfo>> ReadCurrentSnis(
        SSH ssh, ServerConfig config, IProgress<string>? progress = null)
    {
        progress?.Report("[INFO] Чтение текущей конфигурации Xray...");

        var result = await ssh.RunSudoCommand(config, $"cat {XrayConfigPath}");
        string json = result.Result?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            progress?.Report("[ERROR] Конфиг Xray пустой или не найден");
            return new List<InboundSniInfo>();
        }

        return ParseInboundSnis(json, progress);
    }

    /// <summary>
    /// Парсит JSON-конфигурацию Xray и извлекает SNI-информацию из каждого inbound'а.
    /// </summary>
    /// <param name="json">Содержимое config.json.</param>
    /// <param name="progress">Получатель прогресса.</param>
    /// <returns>Список <see cref="InboundSniInfo"/>.</returns>
    public static List<InboundSniInfo> ParseInboundSnis(string json, IProgress<string>? progress = null)
    {
        var snis = new List<InboundSniInfo>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("inbounds", out var inbounds) ||
                inbounds.ValueKind != JsonValueKind.Array)
            {
                progress?.Report("[WARN] Конфиг не содержит массив inbounds");
                return snis;
            }

            for (int i = 0; i < inbounds.GetArrayLength(); i++)
            {
                var inbound = inbounds[i];

                string tag = inbound.TryGetProperty("tag", out var tagProp)
                    ? tagProp.GetString() ?? $"inbound-{i + 1}"
                    : $"inbound-{i + 1}";

                // api-inbound не имеет realitySettings — пропускаем
                if (tag == "api") continue;

                string port = inbound.TryGetProperty("port", out var portProp)
                    ? portProp.ToString()
                    : "?";

                string sni = string.Empty;
                if (inbound.TryGetProperty("streamSettings", out var ss) &&
                    ss.TryGetProperty("realitySettings", out var rs) &&
                    rs.TryGetProperty("serverNames", out var names) &&
                    names.ValueKind == JsonValueKind.Array &&
                    names.GetArrayLength() > 0)
                {
                    sni = names[0].GetString() ?? string.Empty;
                }

                snis.Add(new InboundSniInfo
                {
                    Tag = tag,
                    Port = port,
                    CurrentSni = sni,
                    InboundIndex = i
                });
            }

            progress?.Report($"[INFO] Прочитано {snis.Count} inbound-профилей");
        }
        catch (JsonException ex)
        {
            progress?.Report($"[ERROR] Ошибка парсинга config.json: {ex.Message}");
        }

        return snis;
    }

    /// <summary>
    /// Заменяет SNI-домен для конкретного inbound'а в конфигурации Xray на сервере.
    /// После замены перезапускает сервис Xray.
    /// </summary>
    /// <param name="ssh">Подключённый SSH-клиент.</param>
    /// <param name="config">Конфигурация сервера.</param>
    /// <param name="inboundIndex">Индекс inbound'а в массиве (0-based, без учёта api).</param>
    /// <param name="newSni">Новый SNI-домен.</param>
    /// <param name="progress">Получатель прогресса.</param>
    /// <returns>True, если замена и рестарт прошли успешно.</returns>
    public static async Task<bool> ChangeSni(
        SSH ssh, ServerConfig config,
        int inboundIndex, string newSni,
        IProgress<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(newSni))
        {
            progress?.Report("[ERROR] Новый SNI не может быть пустым");
            return false;
        }

        progress?.Report($"[INFO] Замена SNI для inbound[{inboundIndex}] на {newSni}...");

        // 1. Читаем текущий конфиг
        var catResult = await ssh.RunSudoCommand(config, $"cat {XrayConfigPath}");
        string json = catResult.Result?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            progress?.Report("[ERROR] Не удалось прочитать config.json");
            return false;
        }

        // 2. Модифицируем JSON на стороне C#
        string updatedJson;
        try
        {
            updatedJson = UpdateSniInJson(json, inboundIndex, newSni);
        }
        catch (Exception ex)
        {
            progress?.Report($"[ERROR] Ошибка модификации JSON: {ex.Message}");
            return false;
        }

        // 3. Загружаем обратно через SFTP + sudo cp
        string homeDir = config.Username == "root"
            ? "/root" : $"/home/{config.Username}";
        string tempRemotePath = $"{homeDir}/xray_config_repair.json";

        // Записываем во временный локальный файл
        string tempLocalPath = Path.Combine(Path.GetTempPath(), "iziproxy_sni_repair.json");
        await Task.Run(() => File.WriteAllText(tempLocalPath, updatedJson));

        bool uploaded = await ssh.UploadFile(tempLocalPath, "xray_config_repair.json", config, progress);

        // Удаляем локальный temp-файл
        await Task.Run(() =>
        {
            if (File.Exists(tempLocalPath)) File.Delete(tempLocalPath);
        });

        if (!uploaded)
        {
            progress?.Report("[ERROR] Не удалось загрузить обновлённый конфиг на сервер");
            return false;
        }

        // 4. Подменяем конфиг и перезапускаем Xray
        string applyCmd = $"cp {tempRemotePath} {XrayConfigPath} && " +
                          $"rm -f {tempRemotePath} && " +
                          "systemctl restart xray && " +
                          "sleep 2 && " +
                          "systemctl is-active --quiet xray && echo SNI_CHANGE_OK || echo SNI_CHANGE_FAIL";

        var applyResult = await ssh.RunSudoCommand(config, applyCmd);
        string output = applyResult.Result ?? string.Empty;

        if (output.Contains("SNI_CHANGE_OK"))
        {
            progress?.Report($"[INFO] SNI успешно заменён на {newSni}, Xray перезапущен");
            return true;
        }

        progress?.Report($"[ERROR] Xray не запустился после замены SNI: {output}");
        return false;
    }

    /// <summary>
    /// Автоматически подбирает лучший SNI-домен, выполняя curl-пробы с сервера.
    /// </summary>
    /// <param name="ssh">Подключённый SSH-клиент.</param>
    /// <param name="config">Конфигурация сервера.</param>
    /// <param name="excludeSni">Домен, который нужно исключить из кандидатов (например текущий SNI).</param>
    /// <param name="progress">Получатель прогресса.</param>
    /// <returns>Лучший домен или null, если ни один не доступен.</returns>
    public static async Task<string?> AutoSelectBestSni(
        SSH ssh, ServerConfig config,
        string? excludeSni = null,
        IProgress<string>? progress = null)
    {
        progress?.Report("[INFO] Автоподбор лучшего SNI-домена...");

        // Фильтруем исключённый домен на стороне C#
        var candidates = string.IsNullOrWhiteSpace(excludeSni)
            ? CandidateDomains
            : CandidateDomains.Where(d => !d.Equals(excludeSni.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();

        if (candidates.Length == 0)
        {
            progress?.Report("[WARN] Все кандидаты исключены — список доменов пуст");
            return null;
        }

        if (!string.IsNullOrWhiteSpace(excludeSni))
            progress?.Report($"[INFO] Текущий SNI '{excludeSni}' исключён из кандидатов");

        // Формируем bash-скрипт для проверки доменов.
        // Для каждого домена делаем curl с замером времени.
        string domains = string.Join(" ", candidates);
        string script = $@"
best_domain=""""
best_time=""999""
for domain in {domains}; do
    time_total=$(curl -o /dev/null -sS --connect-timeout 4 --max-time 8 \
        -w '%{{time_total}}' ""https://$domain"" 2>/dev/null) || continue
    if [ -n ""$time_total"" ]; then
        echo ""SNI_PROBE=$domain|$time_total""
        is_better=$(awk -v t=""$time_total"" -v b=""$best_time"" 'BEGIN {{print (t < b) ? 1 : 0}}')
        if [ ""$is_better"" = ""1"" ]; then
            best_domain=""$domain""
            best_time=""$time_total""
        fi
    fi
done
if [ -n ""$best_domain"" ]; then
    echo ""SNI_BEST=$best_domain|$best_time""
else
    echo ""SNI_BEST=NONE""
fi
";

        var result = await ssh.RunSudoCommand(config, script);
        string output = result.Result ?? string.Empty;

        // Парсим результат
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("SNI_PROBE=", StringComparison.Ordinal))
            {
                string probe = line["SNI_PROBE=".Length..];
                progress?.Report($"[DEBUG] Проба: {probe}");
            }
        }

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("SNI_BEST=", StringComparison.Ordinal))
            {
                string best = line["SNI_BEST=".Length..].Trim();
                if (best == "NONE" || string.IsNullOrEmpty(best))
                {
                    progress?.Report("[WARN] Ни один домен не прошёл проверку");
                    return null;
                }

                string domain = best.Contains('|') ? best[..best.IndexOf('|')] : best;
                progress?.Report($"[INFO] Лучший SNI: {domain}");
                return domain;
            }
        }

        progress?.Report("[WARN] Не удалось определить лучший SNI");
        return null;
    }

    /// <summary>
    /// Модифицирует JSON-конфиг Xray, заменяя SNI у указанного inbound'а.
    /// Обновляет <c>realitySettings.dest</c> и <c>realitySettings.serverNames[]</c>.
    /// </summary>
    /// <param name="json">Исходный JSON конфига.</param>
    /// <param name="inboundIndex">Индекс inbound'а в массиве (0-based).</param>
    /// <param name="newSni">Новый SNI-домен.</param>
    /// <returns>Модифицированный JSON.</returns>
    internal static string UpdateSniInJson(string json, int inboundIndex, string newSni)
    {
        using var doc = JsonDocument.Parse(json);
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            WriteElementWithSniUpdate(writer, doc.RootElement, inboundIndex, newSni, "", -1);
        }

        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// Рекурсивно переписывает JSON-элемент, подменяя SNI-поля в нужном inbound'е.
    /// </summary>
    private static void WriteElementWithSniUpdate(
        Utf8JsonWriter writer, JsonElement element,
        int targetInboundIndex, string newSni,
        string currentPath, int currentArrayIndex)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject())
                {
                    writer.WritePropertyName(prop.Name);
                    string childPath = string.IsNullOrEmpty(currentPath)
                        ? prop.Name
                        : $"{currentPath}.{prop.Name}";

                    // Подмена dest: "oldSni:443" → "newSni:443"
                    if (childPath == $"inbounds[{targetInboundIndex}].streamSettings.realitySettings.dest" &&
                        prop.Value.ValueKind == JsonValueKind.String)
                    {
                        writer.WriteStringValue($"{newSni}:443");
                        continue;
                    }

                    // Подмена serverNames: ["oldSni"] → ["newSni"]
                    if (childPath == $"inbounds[{targetInboundIndex}].streamSettings.realitySettings.serverNames" &&
                        prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        writer.WriteStartArray();
                        writer.WriteStringValue(newSni);
                        writer.WriteEndArray();
                        continue;
                    }

                    WriteElementWithSniUpdate(writer, prop.Value, targetInboundIndex, newSni, childPath, -1);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                int idx = 0;
                foreach (var item in element.EnumerateArray())
                {
                    string arrayPath = currentPath.EndsWith("inbounds")
                        ? $"{currentPath}[{idx}]"
                        : currentPath;
                    WriteElementWithSniUpdate(writer, item, targetInboundIndex, newSni, arrayPath, idx);
                    idx++;
                }
                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                if (element.TryGetInt64(out long l))
                    writer.WriteNumberValue(l);
                else
                    writer.WriteNumberValue(element.GetDouble());
                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
        }
    }
}
