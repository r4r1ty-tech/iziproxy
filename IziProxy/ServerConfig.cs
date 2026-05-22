namespace IziProxy;

public class ServerConfig
{
    private string host = "";
    private string username = "";
    private string password = "";
    public string Host { get => host; set => host = value; }

    public string Username { get => username; set => username = value; }
    public string Password { get => password; set => password = value; }
    public void SetServer()
    {
        Console.WriteLine("Введите IP сервера");
        host = Console.ReadLine() ?? string.Empty;
        Console.WriteLine("Введите username пользователя");
        username = Console.ReadLine() ?? string.Empty;
        Console.WriteLine("Введите пароль от указанного пользователя");
        password = Console.ReadLine() ?? string.Empty;
    }
}