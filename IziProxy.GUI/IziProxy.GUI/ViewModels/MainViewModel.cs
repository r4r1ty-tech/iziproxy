using CommunityToolkit.Mvvm.ComponentModel;

namespace IziProxy.GUI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _greeting = "Welcome to Avalonia!";
}
