using System;
using Avalonia.Markup.Xaml;

namespace IziProxy.GUI;

/// <summary>
/// Локализованная строка в разметке: <c>Text="{l:Tr Deploy_Title}"</c>.
/// </summary>
public class TrExtension : MarkupExtension
{
    public TrExtension(string key) => Key = key;

    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) => Tr.Get(Key);
}
