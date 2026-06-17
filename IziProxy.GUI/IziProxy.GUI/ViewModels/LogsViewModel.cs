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

    // ── Фильтры по уровням ─────────────────────────────────────────
    // По умолчанию Trace и Debug выключены (очень шумно), Info/Warn/Error включены.
    [ObservableProperty] private bool _showTrace = false;
    [ObservableProperty] private bool _showDebug = false;
    [ObservableProperty] private bool _showInfo  = true;
    [ObservableProperty] private bool _showWarn  = true;
    [ObservableProperty] private bool _showError = true;
    /// <summary>Отражает мобильный режим — устанавливается из MainViewModel.</summary>
    [ObservableProperty] private bool _isNarrowMode = false;

    partial void OnShowTraceChanged(bool value) => ApplyFilter();
    partial void OnShowDebugChanged(bool value) => ApplyFilter();
    partial void OnShowInfoChanged(bool value)  => ApplyFilter();
    partial void OnShowWarnChanged(bool value)  => ApplyFilter();
    partial void OnShowErrorChanged(bool value) => ApplyFilter();

    private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "IziProxy_Log.txt");

    public IProgress<string> ProgressReporter { get; }

    public LogsViewModel()
    {
        try { File.WriteAllText(LogFilePath, $"--- Начало сессии {DateTime.Now} ---\n"); } catch { }
        System.Diagnostics.Debug.WriteLine($"[INFO] LogsViewModel: лог-файл={LogFilePath}");

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

    /// <summary>
    /// Определяет, попадает ли сообщение в один из видимых уровней.
    /// Уровни определяются по префиксу в начале строки:
    ///   [TRACE] → Trace
    ///   [DEBUG] → Debug
    ///   [INFO]  → Info
    ///   [WARN]  → Warn
    ///   [ERROR] → Error
    /// Также Error ловится по словам ERROR/❌/"ошибка" (для обратной
    /// совместимости со старыми сообщениями без явного префикса).
    /// Сообщения без распознанного префикса считаются Info — это
    /// сохраняет старое поведение для уже существующих текстов.
    /// </summary>
    private bool ShouldShow(string msg)
    {
        // Сначала пробуем явные префиксы уровней — порядок важен,
        // длинный префикс [ERROR] проверяем раньше [ERR], и т.п.
        if (msg.StartsWith("[TRACE]", StringComparison.OrdinalIgnoreCase)) return ShowTrace;
        if (msg.StartsWith("[DEBUG]", StringComparison.OrdinalIgnoreCase)) return ShowDebug;
        if (msg.StartsWith("[INFO]",  StringComparison.OrdinalIgnoreCase)) return ShowInfo;
        if (msg.StartsWith("[WARN]",  StringComparison.OrdinalIgnoreCase)) return ShowWarn;
        if (msg.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase)) return ShowError;

        // Фоллбэк для старых сообщений без префикса: ловим по подстрокам.
        bool isError = msg.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
                       msg.Contains("❌", StringComparison.Ordinal) ||
                       msg.Contains("ошибка", StringComparison.OrdinalIgnoreCase);
        if (isError) return ShowError;

        // Всё остальное (включая явные "INFO" посреди текста) — Info.
        return ShowInfo;
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
