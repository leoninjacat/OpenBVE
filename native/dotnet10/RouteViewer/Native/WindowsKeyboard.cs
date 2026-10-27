using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using OpenTK.Input;

namespace RouteViewer;

// Receive legacy Win32 key messages at the child HWND itself. This bypasses
// OpenTK's raw keyboard backend, which assumes a standalone active window.
internal sealed class WindowsKeyboard : IDisposable
{
    delegate IntPtr WindowProcedure(IntPtr window,uint message,IntPtr wparam,IntPtr lparam);
    readonly ConcurrentQueue<(Key Key,bool Down)> events=new();
    WindowProcedure procedure;
    IntPtr window,previous;
    internal void Attach(IntPtr handle)
    {
        if(!OperatingSystem.IsWindows())return;
        window=handle;procedure=Dispatch;
        previous=SetWindowLongPtr(handle,-4,Marshal.GetFunctionPointerForDelegate(procedure));
        if(previous==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    IntPtr Dispatch(IntPtr handle,uint message,IntPtr wparam,IntPtr lparam)
    {
        if(message is 0x100 or 0x101 or 0x104 or 0x105) {
            Key key=Map((int)wparam);
            if(key!=Key.Unknown) {
                events.Enqueue((key,message is 0x100 or 0x104));
                return IntPtr.Zero;
            }
        }
        return CallWindowProc(previous,handle,message,wparam,lparam);
    }
    internal void Drain(Action<Key,bool> accept){while(events.TryDequeue(out var e))accept(e.Key,e.Down);}
    static Key Map(int key)=>key switch {
        0x57=>Key.W,0x41=>Key.A,0x53=>Key.S,0x44=>Key.D,0x51=>Key.Q,0x45=>Key.E,0x46=>Key.F,0x4F=>Key.O,0x4E=>Key.N,
        0x25=>Key.Left,0x26=>Key.Up,0x27=>Key.Right,0x28=>Key.Down,
        0x10 or 0xA0=>Key.ShiftLeft,0xA1=>Key.ShiftRight,0x11 or 0xA2=>Key.ControlLeft,0xA3=>Key.ControlRight,
        0x62=>Key.Keypad2,0x63=>Key.Keypad3,0x64=>Key.Keypad4,0x65=>Key.Keypad5,
        0x66=>Key.Keypad6,0x68=>Key.Keypad8,0x69=>Key.Keypad9,0x6B=>Key.KeypadPlus,0x6D=>Key.KeypadMinus,
        0x70=>Key.F1,0x74=>Key.F5,0x76=>Key.F7,0x77=>Key.F8,0x78=>Key.F9,0x79=>Key.F10,
        0x09=>Key.Tab,0x1B=>Key.Escape,0x20=>Key.Space,0x24=>Key.Home,0x21=>Key.PageUp,0x22=>Key.PageDown,
        _=>Key.Unknown
    };
    public void Dispose()
    {
        if(previous!=IntPtr.Zero){SetWindowLongPtr(window,-4,previous);previous=IntPtr.Zero;}
        GC.KeepAlive(procedure);
    }
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)]static extern IntPtr SetWindowLongPtr(IntPtr window,int index,IntPtr value);
    [DllImport("user32.dll",EntryPoint="CallWindowProcW")]static extern IntPtr CallWindowProc(IntPtr procedure,IntPtr window,uint message,IntPtr wparam,IntPtr lparam);
}
