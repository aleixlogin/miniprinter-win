using System.Windows;
using System.Windows.Controls;
using MiniPrinter.Gui;

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
public sealed class RecreateQueueDialog : ThemedWindow
{
    public RecreateChoice Choice { get; private set; } = RecreateChoice.Cancel;

    public RecreateQueueDialog(Window owner, bool offerSaveOnly)
    {
        Owner = owner;
        Title = Strings.Get("Recreate.Title");
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(16), MaxWidth = 440 };
        panel.Children.Add(new TextBlock
        {
            Text = Strings.Get("Recreate.Intro"),
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
        });
        panel.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Text = Strings.Get("Recreate.Details"),
        });
        if (offerSaveOnly)
            panel.Children.Add(Themed.Brush(new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Text = Strings.Get("Recreate.SaveOnlyNote"),
            }, Themed.Muted));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(Button(Strings.Get("Recreate.Continue"), RecreateChoice.Recreate, isDefault: true));
        if (offerSaveOnly)
            buttons.Children.Add(Button(Strings.Get("Recreate.SaveOnly"), RecreateChoice.SaveOnly));
        buttons.Children.Add(Button(Strings.Get("Dialog.Cancel"), RecreateChoice.Cancel, isCancel: true));
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
