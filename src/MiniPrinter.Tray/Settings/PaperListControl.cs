using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using MiniPrinter.Gui;

namespace MiniPrinter.Tray;

/// <summary>
/// Hosts the paper list (built in code, see <see cref="PaperSettingsPanel"/>) inside the Paper section: it shows the sizes of
/// the section's view model and hands every edit back to it, which is what marks the section as having unsaved changes.
/// </summary>
public sealed class PaperListControl : StackPanel
{
    private PaperSettingsPanel? _panel;
    private PaperSectionViewModel? _section;

    public PaperListControl()
    {
        DataContextChanged += (_, _) => Connect();
        Unloaded += (_, _) => Disconnect();
        Loaded += (_, _) => Connect();
    }

    private void Connect()
    {
        Disconnect();
        if (DataContext is not PaperSectionViewModel section || Window.GetWindow(this) is not { } owner)
            return;
        _section = section;
        _panel ??= CreatePanel(owner);
        section.PropertyChanged += OnSectionChanged;
        _panel.Load(section.Sizes, section.DefaultId);
    }

    private PaperSettingsPanel CreatePanel(Window owner)
    {
        var panel = new PaperSettingsPanel(owner, this);
        panel.Edited += (sizes, defaultId) => _section?.SetSizes(sizes, defaultId);
        return panel;
    }

    private void Disconnect()
    {
        if (_section is not null)
            _section.PropertyChanged -= OnSectionChanged;
        _section = null;
    }

    private void OnSectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PaperSectionViewModel.Sizes) or nameof(PaperSectionViewModel.DefaultId) or "" && _section is not null)
            _panel?.Load(_section!.Sizes, _section.DefaultId);
    }
}
