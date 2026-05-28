using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia;
using IziProxy.GUI.ViewModels;

namespace IziProxy.GUI.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    private void Control_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            // Устанавливаем флаг мобильного режима
            bool isNarrow = e.NewSize.Width < 700;
            vm.IsNarrowMode = isNarrow;

            // Добавляем или убираем CSS-класс "IsNarrow" для изменения Layout
            if (isNarrow)
            {
                if (!Classes.Contains("IsNarrow"))
                    Classes.Add("IsNarrow");
            }
            else
            {
                Classes.Remove("IsNarrow");
            }
        }
    }
}