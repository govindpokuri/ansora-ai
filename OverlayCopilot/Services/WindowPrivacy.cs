using System;
using System.Runtime.InteropServices;

namespace OverlayCopilot.Services;
public static class WindowPrivacy
{
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    public static string Apply(IntPtr hwnd)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return "Capture exclusion unavailable · requires Windows 10 2004+";
        if (!SetWindowDisplayAffinity(hwnd, 0x11)) return $"Capture exclusion failed · Windows error {Marshal.GetLastWin32Error()}";
        return GetWindowDisplayAffinity(hwnd, out var value) && value == 0x11
            ? "Capture exclusion enabled · verify in your sharing app"
            : "Capture exclusion could not be verified";
    }
}
