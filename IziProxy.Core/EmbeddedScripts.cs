using System.Reflection;

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
        return reader.ReadToEnd();
    }

    private static Stream Open(string fileName)
    {
        string resourceName = Prefix + fileName;
        var stream = _asm.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException(
                $"Встроенный ресурс '{resourceName}' не найден. " +
                $"Доступные ресурсы: {string.Join(", ", _asm.GetManifestResourceNames())}");
        return stream;
    }
}
