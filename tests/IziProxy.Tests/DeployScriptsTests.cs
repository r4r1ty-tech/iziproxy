using Xunit;

namespace IziProxy.Tests;

public class DeployScriptsTests
{
    [Fact]
    public async Task DeployAndConfigure_ReturnsFalse_WhenNotConnected()
    {
        var deployer = new DeployScripts();
        using var ssh = new SSH();

        var server = new ServerConfig { Host = "1.2.3.4", Username = "root", Password = "pass" };
        var xray = new XrayConfigParams { Uuid = "uuid", PrivateKey = "priv", ShortId = "sid" };

        bool result = await deployer.DeployAndConfigure(ssh, server, xray);

        Assert.False(result);
    }
}
