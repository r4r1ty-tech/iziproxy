using System;
using Xunit;

namespace IziProxy.Tests;

public class XrayConfigParamsTests
{
    [Fact]
    public async Task Generate_Throws_WhenNotConnected()
    {
        using var ssh = new SSH();

        await Assert.ThrowsAsync<InvalidOperationException>(() => XrayConfigParams.Generate(ssh, new ServerConfig()));
    }

    [Fact]
    public async Task GetGeoVDS_Throws_WhenNotConnected()
    {
        using var ssh = new SSH();

        await Assert.ThrowsAsync<InvalidOperationException>(() => XrayConfigParams.GetGeoVDS(ssh, new ServerConfig()));
    }
}
