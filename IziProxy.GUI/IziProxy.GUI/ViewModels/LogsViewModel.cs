using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace IziProxy.GUI.ViewModels;

public partial class LogsViewModel : ObservableObject
{
    public ObservableCollection<string> Logs { get; } = new();
    private readonly List<string> _allLogs = new();

    [ObservableProperty] private bool _showDebug = false;
    [ObservableProperty] private bool _showInfo = true;
    [ObservableProperty] private bool _showError = true;
    /// <summary>Отражает мобильный режим — устанавливается из MainViewModel.</summary>
    [ObservableProperty] private bool _isNarrowMode = false;

    partial void OnShowDebugChanged(bool value) => ApplyFilter();
    partial void OnShowInfoChanged(bool value) => ApplyFilter();
    partial void OnShowErrorChanged(bool value) => ApplyFilter();

    private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "IziProxy_Log.txt");

    public IProgress<string> ProgressReporter { get; }

    public LogsViewModel()
    {
        try { File.WriteAllText(LogFilePath, $"--- Начало сессии {DateTime.Now} ---\n"); } catch { }

        ProgressReporter = new Progress<string>(msg =>
        {
            if (!string.IsNullOrEmpty(msg))
            {
                _allLogs.Add(msg);
                if (ShouldShow(msg)) Logs.Add(msg);
                try { File.AppendAllText(LogFilePath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); } catch { }
            }
        });
    }

    private void ApplyFilter()
    {
        Logs.Clear();
        foreach (var msg in _allLogs)
        {
            if (ShouldShow(msg)) Logs.Add(msg);
        }
    }

    private bool ShouldShow(string msg)
    {
        bool isDebug = msg.Contains("[DEBUG]", StringComparison.OrdinalIgnoreCase);
        bool isError = msg.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || 
                       msg.Contains("❌", StringComparison.Ordinal) ||
                       msg.Contains("ошибка", StringComparison.OrdinalIgnoreCase);
        bool isInfo = msg.Contains("INFO", StringComparison.OrdinalIgnoreCase);

        if (isDebug) return ShowDebug;
        if (isError) return ShowError;
        if (isInfo) return ShowInfo;
        
        return true; 
    }

    [RelayCommand]
    void ClearLogs() 
    {
        _allLogs.Clear();
        Logs.Clear();
        try { File.WriteAllText(LogFilePath, $"--- Лог очищен {DateTime.Now} ---\n"); } catch { }
    }

    [RelayCommand]
    void OpenLogFile()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = LogFilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logs.Add($"Не удалось открыть файл лога: {ex.Message}");
        }
    }
}
