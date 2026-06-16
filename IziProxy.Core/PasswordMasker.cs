namespace IziProxy;

/// <summary>
/// Обёртка над <see cref="IProgress{T}"/>, маскирующая пароль в строках
/// перед пробросом во внутренний репортер. Защищает от утечки пароля в
/// лог-файл, UI-логи, дампы окружения и т.п. — на случай если SSH.NET,
/// серверная команда или чужой код пробросит <c>ex.Message</c> со
/// встроенным паролем.
/// </summary>
/// <remarks>
/// Это best-effort: маскирует только точные вхождения строки-пароля. Если
/// сервер обрезал/изменил пароль (например, echo потерял спецсимволы) —
/// замаскированная подстрока не совпадёт. Для SSH-команд это нормально:
/// мы передаём пароль через stdin байт-в-байт, обрезать там негде.
/// </remarks>
public sealed class PasswordMasker : IProgress<string>
{
    private readonly IProgress<string> _inner;
    private readonly string _password;
    private const string Mask = "***";

    /// <param name="inner">Внутренний репортер, в который уйдут уже замаскированные строки.</param>
    /// <param name="password">Пароль, который нужно маскировать во всех сообщениях.</param>
    public PasswordMasker(IProgress<string> inner, string password)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        // Пустой/короткий пароль — не маскируем (иначе substring replacement
        // может сожрать кусок произвольной строки).
        _password = string.IsNullOrEmpty(password) ? string.Empty : password;
    }

    public void Report(string value)
    {
        if (string.IsNullOrEmpty(_password) || string.IsNullOrEmpty(value))
        {
            _inner.Report(value);
            return;
        }

        // Replace на ивент не подписан — это просто строка, дешевле
        // IndexOf + StringBuilder. Для длинных логов хватит, лог не горячий путь.
        var masked = value.IndexOf(_password, StringComparison.Ordinal) >= 0
            ? value.Replace(_password, Mask, StringComparison.Ordinal)
            : value;

        _inner.Report(masked);
    }
}
