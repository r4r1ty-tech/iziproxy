using Avalonia.Controls;
using Avalonia.Platform.Storage;
using IziProxy.GUI.ViewModels;

namespace IziProxy.GUI.Views;

public partial class DeployView : UserControl
{
    public DeployView()
    {
        InitializeComponent();
    }

    public async void BrowseSshKey()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите приватный SSH-ключ",
            AllowMultiple = false
        });

        if (files.Count > 0 && DataContext is DeployViewModel vm)
        {
            vm.SshKeyPath = files[0].Path.LocalPath;
        }
    }
}
