using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using MiniPrinter.Control;
using MiniPrinter.Gui;

namespace MiniPrinter.Tray;

/// <summary>System-wide hotkey through Win32 RegisterHotKey on a hidden message window.</summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int Id = 0x4D50; // "MP"
    private readonly HwndSource _window;
    private bool _registered;

    public GlobalHotkey()
    {
        _window = new HwndSource(new HwndSourceParameters("MiniPrinterHotkey") { Width = 0, Height = 0, WindowStyle = 0 });
        _window.AddHook(WndProc);
    }

    public event Action? Pressed;

    /// <summary>Registers e.g. "Ctrl+Alt+P". Returns false when the text is invalid or another program owns the hotkey.</summary>
    public bool Register(string hotkey)
    {
        Unregister();
        if (!TryParse(hotkey, out var modifiers, out var key))
            return false;
        _registered = RegisterHotKey(_window.Handle, Id, modifiers | 0x4000 /* MOD_NOREPEAT */, (uint)KeyInterop.VirtualKeyFromKey(key));
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
            UnregisterHotKey(_window.Handle, Id);
        _registered = false;
    }

    public static bool TryParse(string? text, out uint modifiers, out Key key)
    {
        modifiers = 0;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= 0x2; break;
                case "alt": modifiers |= 0x1; break;
                case "shift": modifiers |= 0x4; break;
                case "win": modifiers |= 0x8; break;
                default:
                    if (!Enum.TryParse(part, ignoreCase: true, out key))
                        return false;
                    break;
            }
        }
        return key != Key.None && modifiers != 0;
    }

    public void Dispose()
    {
        Unregister();
        _window.RemoveHook(WndProc);
        _window.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == Id)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

/// <summary>What a quick print tells the user: the text, and whether it is a warning (nothing printed, or files skipped).</summary>
public sealed record QuickPrintResult(string Message, bool Warning);

/// <summary>Clipboard, dropped and "Send to" files: everything goes to the service.</summary>
public static class QuickPrint
{
    public static readonly string[] SupportedExtensions = [".png", ".jpg", ".jpeg", ".pdf", ".pwg", ".txt"];

    /// <summary>Prints the clipboard (image, text or copied files).</summary>
    public static async Task<QuickPrintResult> PrintClipboardAsync(ControlClient client)
    {
        if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var png = new MemoryStream();
            encoder.Save(png);
            await client.PrintFileAsync(png.ToArray(), "portapapeles.png", "image/png");
            return new QuickPrintResult(Strings.Get("Quick.ClipboardImageSent"), false);
        }
        if (Clipboard.ContainsFileDropList())
            return await PrintFilesAsync(client, Clipboard.GetFileDropList().Cast<string>().ToArray());
        if (Clipboard.ContainsText() && !string.IsNullOrWhiteSpace(Clipboard.GetText()))
        {
            await client.PrintTextAsync(Clipboard.GetText());
            return new QuickPrintResult(Strings.Get("Quick.ClipboardTextSent"), false);
        }
        return new QuickPrintResult(Strings.Get("Quick.ClipboardEmpty"), true);
    }

    /// <summary>One job per supported file, in order; unsupported files are reported.</summary>
    public static async Task<QuickPrintResult> PrintFilesAsync(ControlClient client, IReadOnlyList<string> paths)
    {
        var sent = 0;
        var skipped = new List<string>();
        foreach (var path in paths)
        {
            if (!SupportedExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant()) || !File.Exists(path))
            {
                skipped.Add(System.IO.Path.GetFileName(path));
                continue;
            }
            await client.PrintFileAsync(await File.ReadAllBytesAsync(path), path);
            sent++;
        }
        var message = sent == 1 ? Strings.Get("Quick.OneFileSent") : Strings.Get("Quick.FilesSent", sent);
        return skipped.Count == 0
            ? new QuickPrintResult(message, false)
            : new QuickPrintResult($"{message} {Strings.Get("Quick.Skipped", string.Join(", ", skipped))}", true);
    }

    /// <summary>Creates the "Send to > MiniPrinter" shortcut for the current user if it is missing.</summary>
    public static void EnsureSendToShortcut(string trayExe)
    {
        var sendTo = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
        var link = System.IO.Path.Combine(sendTo, "MiniPrinter.lnk");
        if (File.Exists(link) || !File.Exists(trayExe))
            return;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
                return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(link);
            shortcut.TargetPath = trayExe;
            shortcut.Arguments = "--print";
            shortcut.Description = "Imprimir en la impresora térmica";
            shortcut.IconLocation = trayExe + ",0";
            shortcut.Save();
        }
        catch (Exception)
        {
            // Optional convenience; the installer also creates it.
        }
    }
}
