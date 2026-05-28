using System;
using Xunit;

namespace IziProxy.Tests;

public class SshTests
{
    [Fact]
    public void TestConnection_ReturnsFalse_ForEmptyHost()
    {
        using var ssh = new SSH();
        var config = new ServerConfig { Host = string.Empty, Username = string.Empty, Password = string.Empty };

        bool result = ssh.TestConnection(config);

        Assert.False(result);
    }

    [Fact]
    public void UploadTestScript_ReturnsFalse_WhenNotConnected()
    {
        using var ssh = new SSH();

        bool result = ssh.UploadTestScript(new ServerConfig());

        Assert.False(result);
    }

    [Fact]
    public void UploadFile_ReturnsFalse_WhenNotConnected()
    {
        using var ssh = new SSH();

        bool result = ssh.UploadFile("missing.txt", "remote.txt", new ServerConfig());

        Assert.False(result);
    }

    [Fact]
    public void RunTestScript_ReturnsFalse_WhenNotConnected()
    {
        using var ssh = new SSH();

        bool result = ssh.RunTestScript(new ServerConfig());

        Assert.False(result);
    }

    [Fact]
    public void RunSudoCommand_Throws_WhenNotConnected()
    {
        using var ssh = new SSH();

        Assert.Throws<InvalidOperationException>(() => ssh.RunSudoCommand(new ServerConfig(), "whoami"));
    }
}
