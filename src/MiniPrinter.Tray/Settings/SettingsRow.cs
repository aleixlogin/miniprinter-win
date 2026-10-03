using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using MiniPrinter.Gui;

namespace MiniPrinter.Tray;

/// <summary>
/// Marks a row of a settings section with the name of its setting, so that the search box can highlight it: while the text
/// of the search matches the setting, the row is drawn with the "warning" background and an accent bar at its side.
/// </summary>
public static class SettingsRow
{
    public static readonly DependencyProperty FieldProperty = DependencyProperty.RegisterAttached(
        "Field", typeof(string), typeof(SettingsRow), new PropertyMetadata(null, OnFieldChanged));

    public static string? GetField(DependencyObject element) => (string?)element.GetValue(FieldProperty);

    public static void SetField(DependencyObject element, string? value) => element.SetValue(FieldProperty, value);

    private static void OnFieldChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not Border row)
            return;
        row.Loaded += (_, _) => Attach(row);
        row.Unloaded += (_, _) => Detach(row);
    }

    private static readonly DependencyProperty HandlerProperty = DependencyProperty.RegisterAttached(
        "Handler", typeof(PropertyChangedEventHandler), typeof(SettingsRow));

    private static void Attach(Border row)
    {
        Detach(row);
        if (row.DataContext is not SettingsSectionViewModel section)
            return;
        PropertyChangedEventHandler handler = (_, _) => Update(row, section);
        row.SetValue(HandlerProperty, handler);
        section.PropertyChanged += handler;
        Update(row, section);
    }

    private static void Detach(Border row)
    {
        if (row.DataContext is SettingsSectionViewModel section && row.GetValue(HandlerProperty) is PropertyChangedEventHandler handler)
            section.PropertyChanged -= handler;
        row.ClearValue(HandlerProperty);
    }

    private static void Update(Border row, SettingsSectionViewModel section)
    {
        if (GetField(row) is { } field && section.Highlights(field))
        {
            row.SetResourceReference(Border.BackgroundProperty, "Brush.WarnBackground");
            row.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
            row.BorderThickness = new Thickness(3, 0, 0, 0);
        }
        else
        {
            row.ClearValue(Border.BackgroundProperty);
            row.ClearValue(Border.BorderBrushProperty);
            row.BorderThickness = new Thickness(0);
        }
    }
}
