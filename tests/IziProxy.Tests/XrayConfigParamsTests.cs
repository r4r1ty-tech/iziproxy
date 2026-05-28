using System;
using Xunit;

namespace IziProxy.Tests;

public class XrayConfigParamsTests
{
    [Fact]
    public void Generate_Throws_WhenNotConnected()
    {
        using var ssh = new SSH();

        Assert.Throws<InvalidOperationException>(() => XrayConfigParams.Generate(ssh, new ServerConfig()));
    }

    [Fact]
    public void GetGeoVDS_Throws_WhenNotConnected()
    {
        using var ssh = new SSH();

        Assert.Throws<InvalidOperationException>(() => XrayConfigParams.GetGeoVDS(ssh, new ServerConfig()));
    }
}
