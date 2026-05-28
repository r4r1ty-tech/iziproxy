using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace IziProxy.GUI;

public class VdsProfile
{
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string SshKeyPath { get; set; } = string.Empty;
}

public static class VdsProfileService
{
    private static readonly string FolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IziProxy"
    );
    private static readonly string FilePath = Path.Combine(FolderPath, "profiles.json");

    public static List<VdsProfile> LoadProfiles()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<VdsProfile>();
            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<VdsProfile>>(json) ?? new List<VdsProfile>();
        }
        catch
        {
            return new List<VdsProfile>();
        }
    }

    public static void SaveProfiles(List<VdsProfile> profiles)
    {
        try
        {
            if (!Directory.Exists(FolderPath))
                Directory.CreateDirectory(FolderPath);
            string json = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Игнорируем ошибки записи
        }
    }
}
