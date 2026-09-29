using System.Globalization;
using System.Resources;

namespace IziProxy;

/// <summary>
/// Локализованные строки интерфейса и сообщений.
/// Английский — язык по умолчанию (Resources/Strings.resx), русский
/// подхватывается из Resources/Strings.ru.resx, если CurrentUICulture — ru.
/// </summary>
public static class Tr
{
    private static readonly ResourceManager Resources =
        new("IziProxy.Core.Resources.Strings", typeof(Tr).Assembly);

    /// <summary>Строка по ключу; если ключа нет — возвращает сам ключ.</summary>
    public static string Get(string key) =>
        Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    /// <summary>Строка по ключу с подстановкой аргументов через string.Format.</summary>
    public static string F(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
