using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace BlitzBrief.Windows.Platform;

public sealed class ClipboardPasteService
{
    private const ushort VkControl = 0x11;
    private const ushort VkShift = 0x10;
    private const ushort VkMenu = 0x12;
    private const ushort VkLWin = 0x5B;
    private const ushort VkRWin = 0x5C;
    private const ushort VkV = 0x56;
    private const uint KeyUp = 0x0002;

    public void CopyText(string text)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() => System.Windows.Clipboard.SetText(text), DispatcherPriority.Send);
    }

    public async Task PasteAsync(CancellationToken cancellationToken)
    {
        await WaitForModifierReleaseAsync(cancellationToken);

        var inputs = new[]
        {
            KeyboardInput(VkControl, 0),
            KeyboardInput(VkV, 0),
            KeyboardInput(VkV, KeyUp),
            KeyboardInput(VkControl, KeyUp)
        };
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            AppLog.Write($"SendInput failed sent={sent} expected={inputs.Length} error={Marshal.GetLastWin32Error()}");
        }
    }

    // Physisch gehaltene Modifier (z. B. der Hotkey selbst) würden das injizierte
    // Strg+V zu Strg+Alt+V o. ä. verfälschen.
    private static async Task WaitForModifierReleaseAsync(CancellationToken cancellationToken)
    {
        const int timeoutMs = 1500;
        const int pollMs = 15;
        for (var waited = 0; waited < timeoutMs; waited += pollMs)
        {
            if (!IsAnyModifierDown())
            {
                return;
            }

            await Task.Delay(pollMs, cancellationToken);
        }

        AppLog.Write("WaitForModifierRelease timed out, pasting anyway.");
    }

    private static bool IsAnyModifierDown()
    {
        return IsKeyDown(VkControl) || IsKeyDown(VkShift) || IsKeyDown(VkMenu) || IsKeyDown(VkLWin) || IsKeyDown(VkRWin);
    }

    private static bool IsKeyDown(ushort key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    private static Input KeyboardInput(ushort key, uint flags) => new()
    {
        Type = 1,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInputData
            {
                VirtualKey = key,
                Scan = 0,
                Flags = flags,
                Time = 0,
                ExtraInfo = IntPtr.Zero
            }
        }
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, Input[] inputs, int sizeOfInputStructure);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    // Die Union muss MOUSEINPUT enthalten, damit sizeof(INPUT) den von Windows
    // erwarteten Wert ergibt (40 Bytes auf x64); sonst lehnt SendInput den Aufruf
    // mit ERROR_INVALID_PARAMETER ab.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInputData Mouse;

        [FieldOffset(0)]
        public KeyboardInputData Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}
