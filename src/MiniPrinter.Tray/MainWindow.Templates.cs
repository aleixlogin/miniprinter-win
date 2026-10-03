using System.Windows;
using MiniPrinter.Control;
using MiniPrinter.Gui;

namespace MiniPrinter.Tray;

/// <summary>The Templates tab: the gallery over the panel that already builds the form, the preview and the printing.</summary>
public partial class MainWindow
{
    private TemplateGalleryViewModel _gallery = null!;

    /// <summary>The gallery of the Templates tab (UiShots fills it to photograph the panel).</summary>
    public TemplateGalleryViewModel Gallery => _gallery;

    private void InitTemplates()
    {
        var app = (App)Application.Current;
        _gallery = new TemplateGalleryViewModel(new ServiceThumbnails(_service), app.Preferences.TemplatesView, app.Preferences.RecentTemplates);
        TemplatesRoot.DataContext = _gallery;

        _gallery.ViewChanged += view => app.UpdatePreferences(p => p with { TemplatesView = view });
        _gallery.Chosen += name =>
        {
            if (name is not null && _templates.CurrentName != name)
                _templates.SelectTemplate(name);
        };
        _templates.SelectionChanged += name => _gallery.SelectedName = name;
        _templates.ListChanged += list =>
        {
            _gallery.SetTemplates(list);
            _ = _gallery.LoadThumbnailsAsync();
        };
        _templates.Edited += name =>
        {
            _gallery.Invalidate(name);
            _ = _gallery.LoadThumbnailsAsync();
        };
        // The window can open on this tab (it remembers the last one): then nobody "selects" it, so the list is asked for here.
        Loaded += async (_, _) =>
        {
            if (Tabs.SelectedItem == TemplatesTab)
                await _templates.LoadAsync();
        };
        _templates.Printed += name =>
        {
            app.RecordRecentTemplate(name);
            _gallery.SetRecent(app.Preferences.RecentTemplates);
        };
    }

    /// <summary>Shows the Templates tab with a template chosen (from the menu of the icon), without printing.</summary>
    public async void ShowTemplate(string name)
    {
        Tabs.SelectedItem = TemplatesTab;
        await _templates.LoadAsync();
        _templates.SelectTemplate(name);
    }
}

/// <summary>The pictures of the templates, drawn by the service.</summary>
internal sealed class ServiceThumbnails(ServiceConnection service) : IThumbnailSource
{
    public Task<byte[]?> GetAsync(string name, int width) => service.Client.GetTemplateThumbnailAsync(name, width);
}
