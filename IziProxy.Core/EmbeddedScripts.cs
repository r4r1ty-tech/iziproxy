using System.Reflection;
using System.Diagnostics;

namespace IziProxy;

/// <summary>
/// Предоставляет доступ к скриптам VDS_setup, встроенным в сборку как EmbeddedResource.
/// </summary>
public static class EmbeddedScripts
{
    private static readonly Assembly _asm = typeof(EmbeddedScripts).Assembly;

    // Имена ресурсов совпадают со структурой папок:
    // IziProxy.Core.VDS_setup.MainInstall.sh
    private const string Prefix = "IziProxy.Core.VDS_setup.";

    /// <summary>Возвращает поток для встроенного скрипта MainInstall.sh.</summary>
    public static Stream OpenMainInstall() => Open("MainInstall.sh");

    /// <summary>Возвращает поток для встроенного скрипта Deploy.sh.</summary>
    public static Stream OpenDeploy() => Open("Deploy.sh");

    /// <summary>Возвращает содержимое встроенного config.json как строку.</summary>
    public static string ReadConfigJson()
    {
        using var stream = Open("config.json");
        using var reader = new StreamReader(stream);
        string content = reader.ReadToEnd();
        Debug.WriteLine($"[DEBUG] EmbeddedScripts.ReadConfigJson: read config.json template, length={content.Length} bytes");
        return content;
    }

    private static Stream Open(string fileName)
    {
        string resourceName = Prefix + fileName;
        Debug.WriteLine($"[DEBUG] EmbeddedScripts.Open: looking for resource '{resourceName}'");
        var stream = _asm.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException(
                $"Embedded resource '{resourceName}' not found. " +
                $"Available resources: {string.Join(", ", _asm.GetManifestResourceNames())}");
        Debug.WriteLine($"[DEBUG] EmbeddedScripts.Open: resource '{resourceName}' found, opened stream of {stream.Length} bytes");
        return stream;
    }
}
