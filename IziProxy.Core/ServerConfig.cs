namespace IziProxy;

/// <summary>
/// Представляет конфигурацию подключения к удаленному серверу (VDS).
/// </summary>
public class ServerConfig
{
    private string host = "";
    private int port = 22; // SSH по умолчанию
    private string username = "";
    private string password = "";
    private string sshKey = "";

    /// <summary>
    /// IP-адрес или хост удаленного сервера.
    /// </summary>
    public string Host { get => host; set => host = value; }

    /// <summary>
    /// Порт SSH. По умолчанию 22 (стандартный SSH). Можно переопределить
    /// для нестандартных серверов или integration-тестов (TEST_SSHD_PORT=2222).
    /// </summary>
    public int Port { get => port; set => port = value; }

    /// <summary>
    /// Имя пользователя для SSH/SFTP подключения.
    /// </summary>
    public string Username { get => username; set => username = value; }

    /// <summary>
    /// Пароль пользователя для SSH/SFTP подключения и выполнения sudo-команд.
    /// </summary>
    public string Password { get => password; set => password = value; }
    /// <summary>
    /// SSH ключ от ВДС пользователя если он есть
    /// </summary>
    public string SshKey { get => sshKey; set => sshKey = value; }

    /// <summary>
    /// Запрашивает у пользователя через консоль параметры подключения к серверу.
    /// </summary>
    /// <param name="progress">Получатель логов (опционально). User-prompts идут в Console всегда,
    /// но в лог пишется итоговое состояние заполненных полей.</param>
    public void SetServer(IProgress<string>? progress = null)
    {
        progress?.Report("[TRACE] ServerConfig.SetServer вход: интерактивный ввод параметров сервера");

        Console.WriteLine("Введите IP сервера");
        host = Console.ReadLine() ?? string.Empty;
        progress?.Report($"[DEBUG] ServerConfig: введён Host='{host}'");

        Console.WriteLine("Введите username пользователя");
        username = Console.ReadLine() ?? string.Empty;
        progress?.Report($"[DEBUG] ServerConfig: введён Username='{username}'");

        Console.WriteLine("Введите пароль от указанного пользователя");
        password = Console.ReadLine() ?? string.Empty;
        // Никогда не пишем сам пароль в лог — только факт что введён, и его длину.
        progress?.Report($"[DEBUG] ServerConfig: введён Password (длина={password.Length})");

        Console.WriteLine("Укажите путь до файла SSH ключ если он есть, если нет нажмите Enter");
        sshKey = Console.ReadLine() ?? string.Empty;
        progress?.Report($"[DEBUG] ServerConfig: введён SshKey='{sshKey}'");

        // Проверка итогового состояния. Не валидируем по правилам POSIX
        // (это делает VdsProfile.Validate), но сигналим о явных проблемах
        // чтобы оператор заметил опечатку до попытки подключения.
        if (string.IsNullOrWhiteSpace(host))
        {
            progress?.Report("[WARN] ServerConfig: Host пустой — подключение заведомо провалится");
        }
        if (string.IsNullOrWhiteSpace(username))
        {
            progress?.Report("[WARN] ServerConfig: Username пустой — подключение заведомо провалится");
        }
        if (string.IsNullOrEmpty(password) && string.IsNullOrWhiteSpace(sshKey))
        {
            progress?.Report("[ERROR] ServerConfig: не указан ни пароль, ни SSH-ключ — аутентификация невозможна");
        }
        else
        {
            string auth = string.IsNullOrWhiteSpace(sshKey) ? "password" : $"sshkey ({sshKey})";
            progress?.Report($"[INFO] ServerConfig заполнен: host={host}, user={username}, auth={auth}, port={port}");
        }
    }
}