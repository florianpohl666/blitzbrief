using System.Runtime.InteropServices;
using Blitztext.Core.Models;

namespace Blitztext.Windows.Platform;

public sealed class HotkeyManager : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;

    private readonly LowLevelKeyboardProc hookProc;
    private readonly Dictionary<WorkflowType, HotkeyRegistration> registrations = [];
    private readonly HashSet<int> pressedKeys = [];
    private readonly HashSet<WorkflowType> activeCombos = [];
    private IntPtr hookHandle;

    public event EventHandler<WorkflowType>? HotkeyPressed;
    public event EventHandler<WorkflowType>? HotkeyReleased;

    public HotkeyManager()
    {
        hookProc = HookCallback;
        hookHandle = SetWindowsHookEx(WhKeyboardLl, hookProc, IntPtr.Zero, 0);
    }

    public bool TryRegister(WorkflowType type, string hotkey, out string error)
    {
        error = "";
        if (!HotkeyParser.TryParse(hotkey, out var registration))
        {
            error = $"Hotkey ungültig: {hotkey}";
            return false;
        }

        registrations[type] = registration;
        return true;
    }

    public void UnregisterAll()
    {
        registrations.Clear();
        activeCombos.Clear();
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var message = wParam.ToInt32();
            var key = Marshal.ReadInt32(lParam);
            if (message is WmKeyDown or WmSysKeyDown)
            {
                pressedKeys.Add(key);
                FirePressedCombos();
            }
            else if (message is WmKeyUp or WmSysKeyUp)
            {
                pressedKeys.Remove(key);
                FireReleasedCombos();
            }
        }

        return CallNextHookEx(hookHandle, code, wParam, lParam);
    }

    private void FirePressedCombos()
    {
        foreach (var (type, registration) in registrations)
        {
            if (activeCombos.Contains(type) || !registration.IsPressed(pressedKeys))
            {
                continue;
            }

            activeCombos.Add(type);
            HotkeyPressed?.Invoke(this, type);
        }
    }

    private void FireReleasedCombos()
    {
        foreach (var type in activeCombos.ToArray())
        {
            if (registrations.TryGetValue(type, out var registration) && registration.IsPressed(pressedKeys))
            {
                continue;
            }

            activeCombos.Remove(type);
            HotkeyReleased?.Invoke(this, type);
        }
    }

    public void Dispose()
    {
        if (hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(hookHandle);
            hookHandle = IntPtr.Zero;
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr moduleHandle, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
}

public sealed record HotkeyRegistration(bool Ctrl, bool Shift, bool Alt, bool Win, int Key)
{
    public bool IsPressed(HashSet<int> pressedKeys)
    {
        return pressedKeys.Contains(Key) &&
               Ctrl == IsAnyPressed(pressedKeys, 0x11, 0xA2, 0xA3) &&
               Shift == IsAnyPressed(pressedKeys, 0x10, 0xA0, 0xA1) &&
               Alt == IsAnyPressed(pressedKeys, 0x12, 0xA4, 0xA5) &&
               Win == IsAnyPressed(pressedKeys, 0x5B, 0x5C);
    }

    private static bool IsAnyPressed(HashSet<int> pressedKeys, params int[] keys) => keys.Any(pressedKeys.Contains);
}

public static class HotkeyParser
{
    public static bool TryParse(string value, out HotkeyRegistration registration)
    {
        var ctrl = false;
        var shift = false;
        var alt = false;
        var win = false;
        var key = 0;

        var parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    ctrl = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                case "alt":
                    alt = true;
                    break;
                case "win":
                case "windows":
                    win = true;
                    break;
                case "space":
                    key = 0x20;
                    break;
                case "plus":
                    key = 0xBB;
                    break;
                case "minus":
                    key = 0xBD;
                    break;
                case "comma":
                    key = 0xBC;
                    break;
                case "period":
                    key = 0xBE;
                    break;
                default:
                    if (part.Length == 1)
                    {
                        key = char.ToUpperInvariant(part[0]);
                    }
                    else if (part.StartsWith('F') && int.TryParse(part[1..], out var functionKey) && functionKey is >= 1 and <= 24)
                    {
                        key = 0x70 + functionKey - 1;
                    }
                    else
                    {
                        registration = new HotkeyRegistration(false, false, false, false, 0);
                        return false;
                    }
                    break;
            }
        }

        registration = new HotkeyRegistration(ctrl, shift, alt, win, key);
        return key != 0 && (ctrl || shift || alt || win);
    }
}
