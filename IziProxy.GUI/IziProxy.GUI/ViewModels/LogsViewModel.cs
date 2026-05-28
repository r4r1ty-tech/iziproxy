using System;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace IziProxy.GUI.ViewModels;

public partial class LogsViewModel : ObservableObject
{
    public ObservableCollection<string> Logs { get; } = new();

    /// <summary>
    /// IProgress-реализация, которая добавляет строки в Logs из любого потока.
    /// Передаётся в методы IziProxy.Core.
    /// </summary>
    public IProgress<string> ProgressReporter { get; }

    public LogsViewModel()
    {
        ProgressReporter = new Progress<string>(msg =>
        {
            // Progress<T> маршалит callback на UI-поток автоматически
            if (!string.IsNullOrEmpty(msg))
                Logs.Add(msg);
        });
    }

    [RelayCommand]
    void ClearLogs() => Logs.Clear();
}
