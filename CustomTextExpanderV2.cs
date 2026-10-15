using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    static readonly Dictionary<string,string> Map = new(StringComparer.OrdinalIgnoreCase) {
        ["to_date"] = "to_date('1/1/2026','mm/dd/yyyy')",
        ["ASDFC"] = "I will let you know"
    };
    static readonly StringBuilder Buffer = new();
    static LowLevelKeyboardProc? Proc;
    static IntPtr Hook;
    static bool Injecting;

    [STAThread] static void Main() {
        Application.EnableVisualStyles();
        Proc = Callback;
        Hook = SetWindowsHookEx(WH_KEYBOARD_LL, Proc, GetModuleHandle(null), 0);
        if (Hook == IntPtr.Zero) { MessageBox.Show("Keyboard hook failed. Win32 error: " + Marshal.GetLastWin32Error(), "Custom Text Expander V2"); return; }
        using var tray = new NotifyIcon { Visible=true, Text="Custom Text Expander V2", Icon=System.Drawing.SystemIcons.Information };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Test app is running", null, (_,__) => MessageBox.Show("V2 is running. Open Notepad and type ASDFC then Space.", "Custom Text Expander V2"));
        menu.Items.Add("Mappings", null, (_,__) => MessageBox.Show("ASDFC -> I will let you know\r\nto_date -> to_date('1/1/2026','mm/dd/yyyy')", "Mappings"));
        menu.Items.Add("Exit", null, (_,__) => Application.Exit());
        tray.ContextMenuStrip = menu;
        Application.ApplicationExit += (_,__) => { if (Hook != IntPtr.Zero) UnhookWindowsHookEx(Hook); };
        Application.Run();
    }

    static IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam) {
        if (nCode < 0 || Injecting || wParam != (IntPtr)WM_KEYDOWN) return CallNextHookEx(Hook,nCode,wParam,lParam);
        int vk = Marshal.ReadInt32(lParam);
        if (vk == VK_SPACE || vk == VK_TAB || vk == VK_RETURN) {
            var token = Buffer.ToString();
            Buffer.Clear();
            if (Map.TryGetValue(token, out var replacement)) {
                Injecting = true;
                try {
                    // Let the physical trigger key finish before injecting replacement.
                    Thread.Sleep(10);
                    for(int i=0;i<token.Length;i++) SendVk(VK_BACK);
                    SendText(replacement);
                    SendVk(vk);
                } finally { Injecting=false; }
                return (IntPtr)1; // suppress original delimiter
            }
            return CallNextHookEx(Hook,nCode,wParam,lParam);
        }
        if (vk == VK_BACK) {
            if (Buffer.Length > 0) Buffer.Length--;
            return CallNextHookEx(Hook,nCode,wParam,lParam);
        }
        if (IsResetKey(vk)) { Buffer.Clear(); return CallNextHookEx(Hook,nCode,wParam,lParam); }
        var c = Translate(vk);
        if (c.HasValue) {
            Buffer.Append(c.Value);
            if (Buffer.Length > 64) Buffer.Remove(0, Buffer.Length - 64);
        }
        return CallNextHookEx(Hook,nCode,wParam,lParam);
    }

    static bool IsResetKey(int vk) => vk==VK_ESCAPE || vk==VK_LEFT || vk==VK_RIGHT || vk==VK_UP || vk==VK_DOWN || vk==VK_DELETE;

    static char? Translate(int vk) {
        // Reliable handling for the shortcuts used by this prototype: letters, underscore and digits.
        bool shift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
        bool caps = (GetKeyState(VK_CAPITAL) & 1) != 0;
        if (vk >= 'A' && vk <= 'Z') {
            char c=(char)vk;
            if (!(shift ^ caps)) c=char.ToLowerInvariant(c);
            return c;
        }
        if (vk >= '0' && vk <= '9') return (char)vk;
        if (vk == VK_OEM_MINUS) return shift ? '_' : '-';
        return null;
    }

    static void SendText(string text) {
        foreach(char c in text) {
            INPUT[] a={ Unicode(c,0), Unicode(c,KEYEVENTF_KEYUP) };
            SendInput((uint)a.Length,a,Marshal.SizeOf<INPUT>());
        }
    }
    static void SendVk(int vk) {
        INPUT[] a={ Virtual((ushort)vk,0), Virtual((ushort)vk,KEYEVENTF_KEYUP) };
        SendInput((uint)a.Length,a,Marshal.SizeOf<INPUT>());
    }
    static INPUT Virtual(ushort vk,uint f) => new(){type=INPUT_KEYBOARD,U=new(){ki=new(){wVk=vk,dwFlags=f}}};
    static INPUT Unicode(char c,uint f) => new(){type=INPUT_KEYBOARD,U=new(){ki=new(){wScan=c,dwFlags=KEYEVENTF_UNICODE|f}}};

    const int WH_KEYBOARD_LL=13, WM_KEYDOWN=0x0100, VK_BACK=0x08, VK_TAB=0x09, VK_RETURN=0x0D, VK_SHIFT=0x10, VK_CAPITAL=0x14, VK_ESCAPE=0x1B, VK_SPACE=0x20, VK_LEFT=0x25, VK_UP=0x26, VK_RIGHT=0x27, VK_DOWN=0x28, VK_DELETE=0x2E, VK_OEM_MINUS=0xBD;
    const uint INPUT_KEYBOARD=1, KEYEVENTF_KEYUP=0x0002, KEYEVENTF_UNICODE=0x0004;
    delegate IntPtr LowLevelKeyboardProc(int nCode,IntPtr wParam,IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk,wScan; public uint dwFlags,time; public UIntPtr dwExtraInfo; }
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int idHook,LowLevelKeyboardProc lpfn,IntPtr hMod,uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk,int nCode,IntPtr wParam,IntPtr lParam);
    [DllImport("kernel32.dll",CharSet=CharSet.Auto)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] static extern short GetKeyState(int nVirtKey);
    [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint nInputs,INPUT[] inputs,int cbSize);
}
