using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using MiniPrinter.Gui;

namespace MiniPrinter.Tray;

/// <summary>
/// Keeps the palette of the whole application in step with Windows (light, dark, high contrast) and with the choice of the
/// user in Settings, Appearance. The palette is the first merged dictionary of the application; replacing it restyles every
/// window that is already open, because the controls refer to it with DynamicResource.
/// </summary>
public sealed class ThemeService : IDisposable
{
    private static readonly Dictionary<EffectiveTheme, string> Dictionaries = new()
    {
        [EffectiveTheme.Light] = "Themes/Theme.Light.xaml",
        [EffectiveTheme.Dark] = "Themes/Theme.Dark.xaml",
        [EffectiveTheme.HighContrast] = "Themes/Theme.HighContrast.xaml",
    };

    private readonly Application _application;
    private ThemeChoice _choice;
    private EffectiveTheme? _applied;

    public ThemeService(Application application, ThemeChoice choice)
    {
        _application = application;
        _choice = choice;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        Refresh();
    }

    /// <summary>The palette in use.</summary>
    public EffectiveTheme Current { get; private set; } = EffectiveTheme.Light;

    /// <summary>Raised on the UI thread after the palette was replaced.</summary>
    public event Action<EffectiveTheme>? Changed;

    public ThemeChoice Choice => _choice;

    public void SetChoice(ThemeChoice choice)
    {
        _choice = choice;
        Refresh();
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Accessibility)
            _application.Dispatcher.BeginInvoke(Refresh);
    }

    private void OnSystemParameterChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
            _application.Dispatcher.BeginInvoke(Refresh);
    }

    /// <summary>Whether Windows is set to light colours for applications (true if it cannot tell).</summary>
    public static bool WindowsUsesLightApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }

    private void Refresh()
    {
        var theme = ThemeResolver.Resolve(_choice, WindowsUsesLightApps(), SystemParameters.HighContrast);
        if (theme == _applied)
            return;
        _applied = theme;
        Current = theme;
        var merged = _application.Resources.MergedDictionaries;
        var dictionary = new ResourceDictionary { Source = new Uri(Dictionaries[theme], UriKind.Relative) };
        if (merged.Count > 0)
            merged[0] = dictionary;
        else
            merged.Insert(0, dictionary);
        Changed?.Invoke(theme);
    }

    // ---- title bar ----------------------------------------------------------------------------------------------------

    private const int UseImmersiveDarkModeBefore20H1 = 19;
    private const int UseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Makes the title bar of a window dark (or light again) to match the palette.</summary>
    public static void ApplyTitleBar(Window window, EffectiveTheme theme)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;
        var dark = theme == EffectiveTheme.Dark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, UseImmersiveDarkModeBefore20H1, ref dark, sizeof(int));
    }
}
