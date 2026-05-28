using System;

namespace IziProxy;

public sealed class SystemConsole : IConsole
{
    public void WriteLine(string? message)
    {
        Console.WriteLine(message);
    }

    public string? ReadLine()
    {
        return Console.ReadLine();
    }
}
