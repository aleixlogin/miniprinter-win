using System.Windows;
using System.Windows.Controls;

namespace MiniPrinter.Tray;

public enum RecreateChoice
{
    Cancel,
    SaveOnly,
    Recreate,
}

/// <summary>
/// Confirmation before the Windows print queue is recreated (spec paper-sizes-settings): explains what is lost and,
/// when settings are being saved, offers to save without recreating (Windows then keeps the old sizes).
/// </summary>
public sealed class RecreateQueueDialog : Window
{
    public RecreateChoice Choice { get; private set; } = RecreateChoice.Cancel;

    public RecreateQueueDialog(Window owner, bool offerSaveOnly)
    {
        Owner = owner;
        Title = "Recrear la impresora de Windows";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(16), MaxWidth = 440 };
        panel.Children.Add(new TextBlock
        {
            Text = "Para que Windows vea los cambios hay que volver a crear la impresora de Windows.",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
        });
        panel.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Text = "• Se perderán las preferencias de impresión de esa impresora (papel elegido, orientación…).\n" +
                   "• Los trabajos pendientes en su cola de Windows deben haber terminado.\n" +
                   "• Si es tu impresora predeterminada, seguirá siéndolo.\n" +
                   "• Si algo falla, la impresora anterior sigue funcionando.",
        });
        if (offerSaveOnly)
            panel.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.DimGray,
                Text = "«Guardar sin recrear» guarda los ajustes, pero Windows seguirá con los tamaños antiguos hasta que la recrees (botón «Recrear impresora de Windows»).",
            });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(Button("Continuar", RecreateChoice.Recreate, isDefault: true));
        if (offerSaveOnly)
            buttons.Children.Add(Button("Guardar sin recrear", RecreateChoice.SaveOnly));
        buttons.Children.Add(Button("Cancelar", RecreateChoice.Cancel, isCancel: true));
        panel.Children.Add(buttons);
        Content = panel;
    }

    private Button Button(string text, RecreateChoice choice, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button { Content = text, IsDefault = isDefault, IsCancel = isCancel, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        button.Click += (_, _) =>
        {
            Choice = choice;
            DialogResult = choice != RecreateChoice.Cancel;
        };
        return button;
    }
}
