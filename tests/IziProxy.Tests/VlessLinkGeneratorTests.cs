using System;
using Xunit;

namespace IziProxy.Tests;

public class VlessLinkGeneratorTests
{
    [Fact]
    public void GenerateRealityLink_BuildsExpectedLink()
    {
        var serverConfig = new ServerConfig { Host = "example.com" };
        var xrayParams = new XrayConfigParams
        {
            Uuid = "uuid-123",
            Password = "pub key",
            ShortId = "short-id",
            Ports = new List<string> { "8443" },
            Snis = new List<string> { "www.microsoft.com" }
        };

        List<string> links = VlessLinkGenerator.GenerateRealityLinks(serverConfig, xrayParams, "My Link");

        Assert.Single(links);
        string link = links[0];

        string query = string.Join("&", new[]
        {
            "type=xhttp",
            "security=reality",
            $"pbk={Uri.EscapeDataString(xrayParams.Password)}",
            "fp=chrome",
            $"sni={Uri.EscapeDataString("www.microsoft.com")}",
            $"sid={Uri.EscapeDataString(xrayParams.ShortId)}",
            $"path={Uri.EscapeDataString("/xh-query")}",
        });

        string expected = $"vless://{xrayParams.Uuid}@{serverConfig.Host}:8443?{query}#{Uri.EscapeDataString("My Link_1")}";

        Assert.Equal(expected, link);
    }
}
