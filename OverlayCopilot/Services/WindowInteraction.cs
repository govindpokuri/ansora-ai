using System;
using System.Runtime.InteropServices;

namespace OverlayCopilot.Services;

public static class WindowInteraction
{
    private const int ExtendedStyle = -20;
    private const int Transparent = 0x20;
    private const int NoActivate = 0x08000000;
    private const int ToolWindow = 0x80;
    private const int AppWindow = 0x40000;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    public static bool IsClickThrough(IntPtr hwnd) => (GetWindowLong(hwnd, ExtendedStyle) & Transparent) != 0;
    public static bool IsExcludedFromTaskbar(IntPtr hwnd)
    {
        var style = GetWindowLong(hwnd, ExtendedStyle);
        return (style & ToolWindow) != 0 && (style & AppWindow) == 0;
    }
    public static bool ExcludeFromTaskbar(IntPtr hwnd)
    {
        var style = GetWindowLong(hwnd, ExtendedStyle);
        SetWindowLong(hwnd, ExtendedStyle, (style | ToolWindow) & ~AppWindow);
        return IsExcludedFromTaskbar(hwnd);
    }
    public static bool SetClickThrough(IntPtr hwnd, bool enabled)
    {
        var style = GetWindowLong(hwnd, ExtendedStyle);
        var next = enabled ? style | Transparent | NoActivate : style & ~(Transparent | NoActivate);
        SetWindowLong(hwnd, ExtendedStyle, next);
        return IsClickThrough(hwnd) == enabled;
    }
}
