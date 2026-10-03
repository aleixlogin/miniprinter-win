using System.Windows.Markup;
using MiniPrinter.Gui;

namespace MiniPrinter.Tray;

/// <summary>XAML access to the interface texts: <c>Text="{loc:T Some.Key}"</c>. A missing key shows its name.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string key) : MarkupExtension
{
    [ConstructorArgument("key")]
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key);
}
