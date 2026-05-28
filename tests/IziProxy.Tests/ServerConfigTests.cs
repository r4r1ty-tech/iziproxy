using System;
using System.IO;
using Xunit;

namespace IziProxy.Tests;

public class ServerConfigTests
{
    [Fact]
    public void SetServer_ReadsInputsFromConsole()
    {
        var input = new StringReader("1.2.3.4\nuser\npass\n");
        var output = new StringWriter();
        var originalIn = Console.In;
        var originalOut = Console.Out;

        try
        {
            Console.SetIn(input);
            Console.SetOut(output);

            var config = new ServerConfig();
            config.SetServer();

            Assert.Equal("1.2.3.4", config.Host);
            Assert.Equal("user", config.Username);
            Assert.Equal("pass", config.Password);

            string outputText = output.ToString();
            Assert.Contains("Введите IP сервера", outputText);
            Assert.Contains("Введите username пользователя", outputText);
            Assert.Contains("Введите пароль от указанного пользователя", outputText);
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
        }
    }
}
