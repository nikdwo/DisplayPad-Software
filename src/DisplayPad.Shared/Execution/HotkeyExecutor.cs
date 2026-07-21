using System.Runtime.InteropServices;

namespace DisplayPad.Shared.Execution;

/// <summary>
/// Sendet Tastenkombinationen wie "Ctrl+Alt+F1" per SendInput an die aktive Sitzung.
/// Einschränkung (Windows): keine Eingaben auf dem Secure Desktop (UAC-Prompt, Sperrbildschirm).
/// </summary>
public static class HotkeyExecutor
{
    private static readonly Dictionary<string, ushort> Modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = 0xA2,     // VK_LCONTROL
        ["strg"] = 0xA2,
        ["control"] = 0xA2,
        ["shift"] = 0xA0,    // VK_LSHIFT
        ["alt"] = 0xA4,      // VK_LMENU
        ["altgr"] = 0xA5,    // VK_RMENU
        ["win"] = 0x5B,      // VK_LWIN
    };

    private static readonly Dictionary<string, ushort> Keys = BuildKeyTable();

    private static Dictionary<string, ushort> BuildKeyTable()
    {
        var map = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["enter"] = 0x0D, ["return"] = 0x0D,
            ["esc"] = 0x1B, ["escape"] = 0x1B,
            ["tab"] = 0x09,
            ["space"] = 0x20, ["leertaste"] = 0x20,
            ["backspace"] = 0x08,
            ["delete"] = 0x2E, ["del"] = 0x2E, ["entf"] = 0x2E,
            ["insert"] = 0x2D, ["einfg"] = 0x2D,
            ["home"] = 0x24, ["pos1"] = 0x24,
            ["end"] = 0x23, ["ende"] = 0x23,
            ["pageup"] = 0x21, ["bildauf"] = 0x21,
            ["pagedown"] = 0x22, ["bildab"] = 0x22,
            ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28,
            ["printscreen"] = 0x2C, ["druck"] = 0x2C,
            ["pause"] = 0x13,
            ["capslock"] = 0x14,
            ["numlock"] = 0x90,
            ["volumemute"] = 0xAD, ["volumedown"] = 0xAE, ["volumeup"] = 0xAF,
            ["medianext"] = 0xB0, ["mediaprev"] = 0xB1, ["mediastop"] = 0xB2, ["mediaplaypause"] = 0xB3,
        };

        for (int i = 0; i < 26; i++)
            map[((char)('a' + i)).ToString()] = (ushort)(0x41 + i);
        for (int i = 0; i <= 9; i++)
            map[i.ToString()] = (ushort)(0x30 + i);
        for (int i = 1; i <= 24; i++)
            map["f" + i] = (ushort)(0x70 + i - 1);
        for (int i = 0; i <= 9; i++)
            map["numpad" + i] = (ushort)(0x60 + i);

        return map;
    }

    public static void Send(string hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
            throw new ArgumentException("Hotkey ist leer.");

        var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var modifierVks = new List<ushort>();
        var keyVks = new List<ushort>();

        foreach (var part in parts)
        {
            if (Modifiers.TryGetValue(part, out var modVk))
                modifierVks.Add(modVk);
            else if (Keys.TryGetValue(part, out var keyVk))
                keyVks.Add(keyVk);
            else
                throw new ArgumentException($"Unbekannte Taste: '{part}'");
        }

        if (keyVks.Count == 0 && modifierVks.Count == 0)
            throw new ArgumentException("Keine gültigen Tasten im Hotkey.");

        var inputs = new List<INPUT>();
        foreach (var vk in modifierVks)
            inputs.Add(MakeInput(vk, keyUp: false));
        foreach (var vk in keyVks)
            inputs.Add(MakeInput(vk, keyUp: false));
        foreach (var vk in Enumerable.Reverse(keyVks))
            inputs.Add(MakeInput(vk, keyUp: true));
        foreach (var vk in Enumerable.Reverse(modifierVks))
            inputs.Add(MakeInput(vk, keyUp: true));

        var arr = inputs.ToArray();
        uint sent = SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
        if (sent != arr.Length)
            throw new InvalidOperationException($"SendInput hat nur {sent}/{arr.Length} Events gesendet (Win32-Fehler {Marshal.GetLastWin32Error()}).");
    }

    /// <summary>Sendet rohe Virtual-Key-Codes: in Reihenfolge drücken, rückwärts loslassen.</summary>
    public static void SendVirtualKeys(IReadOnlyList<int> vkCodes)
    {
        if (vkCodes.Count == 0)
            throw new ArgumentException("Keine Tastencodes angegeben.");

        var inputs = new List<INPUT>();
        foreach (var vk in vkCodes)
            inputs.Add(MakeInput((ushort)vk, keyUp: false));
        foreach (var vk in vkCodes.Reverse())
            inputs.Add(MakeInput((ushort)vk, keyUp: true));

        var arr = inputs.ToArray();
        uint sent = SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
        if (sent != arr.Length)
            throw new InvalidOperationException($"SendInput hat nur {sent}/{arr.Length} Events gesendet (Win32-Fehler {Marshal.GetLastWin32Error()}).");
    }

    private static INPUT MakeInput(ushort vk, bool keyUp)
    {
        const uint KEYEVENTF_KEYUP = 0x0002;
        const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        // Navigations-/Medientasten sind Extended Keys
        bool extended = vk is >= 0x21 and <= 0x2E or >= 0xAD and <= 0xB3 or 0x90 or 0xA5 or 0x5B;

        return new INPUT
        {
            type = 1, // INPUT_KEYBOARD
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = (ushort)MapVirtualKey(vk, 0),
                    dwFlags = (keyUp ? KEYEVENTF_KEYUP : 0) | (extended ? KEYEVENTF_EXTENDEDKEY : 0),
                }
            }
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
