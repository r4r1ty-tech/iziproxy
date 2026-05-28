namespace IziProxy;

/// <summary>
/// Представляет конфигурацию подключения к удаленному серверу (VDS).
/// </summary>
public class ServerConfig
{
    private string host = "";
    private string username = "";
    private string password = "";
    private string sshKey = "";

    /// <summary>
    /// IP-адрес или хост удаленного сервера.
    /// </summary>
    public string Host { get => host; set => host = value; }

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
    public void SetServer()
    {
        Console.WriteLine("Введите IP сервера");
        host = Console.ReadLine() ?? string.Empty;
        Console.WriteLine("Введите username пользователя");
        username = Console.ReadLine() ?? string.Empty;
        Console.WriteLine("Введите пароль от указанного пользователя");
        password = Console.ReadLine() ?? string.Empty;
        Console.WriteLine("Укажите путь до файла SSH ключ если он есть, если нет нажмите Enter");
        sshKey = Console.ReadLine() ?? string.Empty;
    }
}