using System.Globalization;
using System.Runtime.CompilerServices;

namespace IziProxy.Tests;

/// <summary>
/// Фиксирует культуру на время тестов: сообщения из ресурсов всегда
/// английские, независимо от локали машины, где запускаются тесты.
/// </summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void Init()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }
}
