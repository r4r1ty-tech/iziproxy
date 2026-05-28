using System;
using Xunit;

namespace IziProxy.Tests;

public class SshTests
{
    [Fact]
    public async Task TestConnection_ReturnsFalse_ForEmptyHost()
    {
        using var ssh = new SSH();
        var config = new ServerConfig { Host = string.Empty, Username = string.Empty, Password = string.Empty };

        bool result = await ssh.TestConnection(config);

        Assert.False(result);
    }

    [Fact]
    public async Task UploadTestScript_ReturnsFalse_WhenNotConnected()
    {
        using var ssh = new SSH();

        bool result = await ssh.UploadTestScript(new ServerConfig());

        Assert.False(result);
    }

    [Fact]
    public async Task UploadFile_ReturnsFalse_WhenNotConnected()
    {
        using var ssh = new SSH();

        bool result = await ssh.UploadFile("missing.txt", "remote.txt", new ServerConfig());

        Assert.False(result);
    }

    [Fact]
    public async Task RunTestScript_ReturnsFalse_WhenNotConnected()
    {
        using var ssh = new SSH();

        bool result = await ssh.RunTestScript(new ServerConfig());

        Assert.False(result);
    }

    [Fact]
    public async Task RunSudoCommand_Throws_WhenNotConnected()
    {
        using var ssh = new SSH();

        await Assert.ThrowsAsync<InvalidOperationException>(() => ssh.RunSudoCommand(new ServerConfig(), "whoami"));
    }
}
