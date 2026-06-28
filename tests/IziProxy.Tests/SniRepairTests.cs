using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace IziProxy.Tests;

public class SniRepairTests
{
    private const string ValidConfigJson = @"{
  ""inbounds"": [
    {
      ""port"": 443,
      ""protocol"": ""vless"",
      ""streamSettings"": {
        ""realitySettings"": {
          ""dest"": ""www.microsoft.com:443"",
          ""serverNames"": [
            ""www.microsoft.com""
          ]
        }
      },
      ""tag"": ""inbound-1""
    },
    {
      ""port"": 8443,
      ""protocol"": ""vless"",
      ""streamSettings"": {
        ""realitySettings"": {
          ""dest"": ""www.cloudflare.com:443"",
          ""serverNames"": [
            ""www.cloudflare.com""
          ]
        }
      },
      ""tag"": ""inbound-2""
    },
    {
      ""port"": 10085,
      ""protocol"": ""dokodemo-door"",
      ""tag"": ""api""
    }
  ]
}";

    [Fact]
    public void ParseInboundSnis_ValidJson_ReturnsCorrectInboundInfos()
    {
        var result = SniRepairService.ParseInboundSnis(ValidConfigJson);

        Assert.Equal(2, result.Count);

        Assert.Equal("inbound-1", result[0].Tag);
        Assert.Equal("443", result[0].Port);
        Assert.Equal("www.microsoft.com", result[0].CurrentSni);
        Assert.Equal(0, result[0].InboundIndex);

        Assert.Equal("inbound-2", result[1].Tag);
        Assert.Equal("8443", result[1].Port);
        Assert.Equal("www.cloudflare.com", result[1].CurrentSni);
        Assert.Equal(1, result[1].InboundIndex);
    }

    [Fact]
    public void ParseInboundSnis_JsonWithoutInbounds_ReturnsEmpty()
    {
        const string json = @"{ ""other"": 123 }";
        var result = SniRepairService.ParseInboundSnis(json);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseInboundSnis_InvalidJson_ReturnsEmpty()
    {
        const string json = @"{ ""inbounds"": [ { ";
        var result = SniRepairService.ParseInboundSnis(json);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseInboundSnis_ApiInbound_IsSkipped()
    {
        // Проверяем, что входящее подключение с тегом api отфильтровано
        var result = SniRepairService.ParseInboundSnis(ValidConfigJson);
        Assert.All(result, info => Assert.NotEqual("api", info.Tag));
    }

    [Fact]
    public void UpdateSniInJson_ValidJson_UpdatesDestAndServerNames()
    {
        const string newSni = "speed.cloudflare.com";
        
        // Обновляем первый inbound (index = 0)
        string updatedJson = SniRepairService.UpdateSniInJson(ValidConfigJson, 0, newSni);

        // Проверяем корректность обновления полей в первом inbound
        using var doc = JsonDocument.Parse(updatedJson);
        var inbounds = doc.RootElement.GetProperty("inbounds");
        
        var inbound1 = inbounds[0];
        var dest1 = inbound1.GetProperty("streamSettings")
                            .GetProperty("realitySettings")
                            .GetProperty("dest")
                            .GetString();
        var serverName1 = inbound1.GetProperty("streamSettings")
                                 .GetProperty("realitySettings")
                                 .GetProperty("serverNames")[0]
                                 .GetString();

        Assert.Equal("speed.cloudflare.com:443", dest1);
        Assert.Equal("speed.cloudflare.com", serverName1);

        // Проверяем, что второй inbound не изменился
        var inbound2 = inbounds[1];
        var dest2 = inbound2.GetProperty("streamSettings")
                            .GetProperty("realitySettings")
                            .GetProperty("dest")
                            .GetString();
        var serverName2 = inbound2.GetProperty("streamSettings")
                                 .GetProperty("realitySettings")
                                 .GetProperty("serverNames")[0]
                                 .GetString();

        Assert.Equal("www.cloudflare.com:443", dest2);
        Assert.Equal("www.cloudflare.com", serverName2);
    }

    [Fact]
    public void UpdateSniInJson_NonExistentIndex_ReturnsUnmodifiedJson()
    {
        // Передаем несуществующий индекс. JSON не должен измениться
        string updatedJson = SniRepairService.UpdateSniInJson(ValidConfigJson, 999, "new.domain.com");

        using var docOriginal = JsonDocument.Parse(ValidConfigJson);
        using var docUpdated = JsonDocument.Parse(updatedJson);

        // Сравниваем кол-во inbounds и их параметры
        var origInbounds = docOriginal.RootElement.GetProperty("inbounds");
        var updatedInbounds = docUpdated.RootElement.GetProperty("inbounds");

        Assert.Equal(origInbounds.GetArrayLength(), updatedInbounds.GetArrayLength());
        
        var origDest = origInbounds[0].GetProperty("streamSettings")
                                      .GetProperty("realitySettings")
                                      .GetProperty("dest")
                                      .GetString();
        var updatedDest = updatedInbounds[0].GetProperty("streamSettings")
                                            .GetProperty("realitySettings")
                                            .GetProperty("dest")
                                            .GetString();

        Assert.Equal(origDest, updatedDest);
    }
}
