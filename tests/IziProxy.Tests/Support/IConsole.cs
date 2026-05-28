using System;

namespace IziProxy;

public interface IConsole
{
    void WriteLine(string? message);
    string? ReadLine();
}
