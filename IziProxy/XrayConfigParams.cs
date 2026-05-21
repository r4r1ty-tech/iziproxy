using System;

namespace IziProxy;

public class XrayConfigParams
{
    public string PrivateKey { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Uuid { get; set; } = string.Empty;
    public string ShortId { get; set; } = string.Empty;

    public static XrayConfigParams Generate(SSH sshClient, ServerConfig serverConfig)
    {
        Console.WriteLine("Генерация ключей Xray...");
        
        var xrayConfig = new XrayConfigParams();

        // 1. Генерация x25519
        var x25519Result = sshClient.RunSudoCommand(serverConfig, "xray x25519");
        var x25519Output = x25519Result.Result;
        
        foreach (var line in x25519Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("PrivateKey:"))
            {
                xrayConfig.PrivateKey = line.Substring("PrivateKey:".Length).Trim();
            }
            else if (line.StartsWith("Password (PublicKey):"))
            {
                xrayConfig.Password = line.Substring("Password (PublicKey):".Length).Trim();
            }
        }

        // 2. Генерация UUID
        var uuidResult = sshClient.RunSudoCommand(serverConfig, "xray uuid");
        xrayConfig.Uuid = uuidResult.Result.Trim();

        // 3. Генерация ShortID
        var shortIdResult = sshClient.RunSudoCommand(serverConfig, "openssl rand -hex 8");
        xrayConfig.ShortId = shortIdResult.Result.Trim();

        Console.WriteLine($"Xray Keys Generated:\nUUID: {xrayConfig.Uuid}\nPrivateKey: {xrayConfig.PrivateKey}\nPassword: {xrayConfig.Password}\nShortID: {xrayConfig.ShortId}");
        
        return xrayConfig;
    }
}