using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

internal static class Program
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["to_date"] = "to_date('1/1/2026','mm/dd/yyyy')",
        ["ASDFC"] = "I will let you know"
    };

    private static readonly StringBuilder Buffer = new();
    private static IntPtr _hook = IntPtr.Zero;
    private static LowLevelKeyboardProc? _proc;
    private static bool _injecting;

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        _proc = HookCallback;
        _hook = SetHook(_proc);
        if (_hook == IntPtr.Zero) { MessageBox.Show("Unable to install keyboard hook."); return; }

        using var tray = new NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Visible = true, Text = "Custom Text Expander" };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Mappings", null, (_, __) => MessageBox.Show("to_date  ->  to_date('1/1/2026','mm/dd/yyyy')\nASDFC  ->  I will let you know", "Custom Text Expander"));
        menu.Items.Add("Exit", null, (_, __) => Application.Exit());
        tray.ContextMenuStrip = menu;
        Application.Run();
        tray.Visible = false;
        UnhookWindowsHookEx(_hook);
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using Process p = Process.GetCurrentProcess();
        using ProcessModule m = p.MainModule!;
        return SetWindowsHookEx(13, proc, GetModuleHandle(m.ModuleName), 0);
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_injecting && (wParam == (IntPtr)0x0100 || wParam == (IntPtr)0x0104))
        {
            int vk = Marshal.ReadInt32(lParam);
            if (vk == 0x20 || vk == 0x09 || vk == 0x0D) // Space, Tab, Enter
            {
                string token = Buffer.ToString();
                Buffer.Clear();
                if (Map.TryGetValue(token, out string? replacement))
                {
                    InjectReplacement(token.Length, replacement, vk);
                    return (IntPtr)1;
                }
            }
            else if (vk == 0x08) // Backspace
            {
                if (Buffer.Length > 0) Buffer.Length--;
            }
            else
            {
                char? ch = VkToChar((uint)vk);
                if (ch.HasValue && !char.IsControl(ch.Value))
                {
                    Buffer.Append(ch.Value);
                    if (Buffer.Length > 100) Buffer.Remove(0, Buffer.Length - 100);
                }
                else Buffer.Clear();
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static char? VkToChar(uint vk)
    {
        byte[] state = new byte[256];
        if (!GetKeyboardState(state)) return null;
        var sb = new StringBuilder(4);
        uint scan = MapVirtualKey(vk, 0);
        IntPtr layout = GetKeyboardLayout(0);
        int r = ToUnicodeEx(vk, scan, state, sb, sb.Capacity, 0, layout);
        return r == 1 ? sb[0] : null;
    }

    private static void InjectReplacement(int charsToDelete, string replacement, int delimiterVk)
    {
        _injecting = true;
        try
        {
            for (int i = 0; i < charsToDelete; i++) SendVirtualKey(0x08);
            foreach (char c in replacement) SendUnicode(c);
            SendVirtualKey((ushort)delimiterVk);
        }
        finally { _injecting = false; }
    }

    private static void SendVirtualKey(ushort vk)
    {
        INPUT[] x = { Key(vk, 0, 0), Key(vk, 0, 2) };
        SendInput((uint)x.Length, x, Marshal.SizeOf<INPUT>());
    }

    private static void SendUnicode(char c)
    {
        INPUT[] x = { Key(0, c, 4), Key(0, c, 6) };
        SendInput((uint)x.Length, x, Marshal.SizeOf<INPUT>());
    }

    private static INPUT Key(ushort vk, ushort scan, uint flags) => new()
    {
        type = 1,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } }
    };

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public UIntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError=true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet=CharSet.Auto)] private static extern IntPtr GetModuleHandle(string lpModuleName);
    [DllImport("user32.dll")] private static extern bool GetKeyboardState(byte[] lpKeyState);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint uCode, uint uMapType);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint idThread);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState, [Out] StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);
    [DllImport("user32.dll", SetLastError=true)] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
