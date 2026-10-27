using System;
using System.Runtime.InteropServices;
namespace RouteViewer;
internal static class WindowIdentity
{
    // After SetParent, the foreground top-level window belongs to the desktop
    // host. OpenTK's top-level activation flag is not the child's keyboard focus.
    internal static bool HasKeyboardFocus(IntPtr handle,bool fallback)
    {
        if(!OperatingSystem.IsWindows())return fallback;
        var info=new GuiThreadInfo { Size=Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(0,ref info) && info.Focus==handle;
    }
    internal static bool KeyDown(int virtualKey)=>GetAsyncKeyState(virtualKey)<0;
    [StructLayout(LayoutKind.Sequential)]
    struct ClassHint { public IntPtr Name; public IntPtr Class; }
    [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] static extern int XSetClassHint(IntPtr display,IntPtr window,ref ClassHint hint);
    [DllImport("libX11.so.6")] static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] static extern int XSetInputFocus(IntPtr display,IntPtr window,int revert,ulong time);
    internal static void Focus(IntPtr handle) {
        if(OperatingSystem.IsWindows()){SetFocus(handle);return;}
        var display=XOpenDisplay(IntPtr.Zero);if(display==IntPtr.Zero)return;
        XSetInputFocus(display,handle,2,0);XCloseDisplay(display);
    }
    [DllImport("libX11.so.6")] static extern bool XQueryPointer(IntPtr display,IntPtr window,out IntPtr root,out IntPtr child,out int rx,out int ry,out int x,out int y,out uint mask);
    [DllImport("libX11.so.6")] static extern IntPtr XCreateFontCursor(IntPtr display,uint shape);
    [DllImport("libX11.so.6")] static extern int XDefineCursor(IntPtr display,IntPtr window,IntPtr cursor);
    [DllImport("libX11.so.6")] static extern int XUndefineCursor(IntPtr display,IntPtr window);
    [DllImport("libX11.so.6")] static extern int XFreeCursor(IntPtr display,IntPtr cursor);
    static IntPtr pointerDisplay,grabCursor;
    internal static bool Pointer(IntPtr window,out int x,out int y,out uint mask) {
        if(OperatingSystem.IsWindows()) {
            bool ok=GetCursorPos(out var point)&&ScreenToClient(window,ref point);
            x=point.X;y=point.Y;mask=0;
            if(GetAsyncKeyState(1)<0)mask|=256;
            if(GetAsyncKeyState(4)<0)mask|=512;
            if(GetAsyncKeyState(2)<0)mask|=1024;
            return ok;
        }
        if(pointerDisplay==IntPtr.Zero)pointerDisplay=XOpenDisplay(IntPtr.Zero);
        x=y=0;mask=0;
        return pointerDisplay!=IntPtr.Zero&&XQueryPointer(pointerDisplay,window,out _,out _,out _,out _,out x,out y,out mask);
    }
    internal static void GrabCursor(IntPtr window,bool active) {
        if(OperatingSystem.IsWindows()){if(active)SetCapture(window);else ReleaseCapture();return;}
        if(pointerDisplay==IntPtr.Zero)pointerDisplay=XOpenDisplay(IntPtr.Zero);
        if(pointerDisplay==IntPtr.Zero)return;
        if(active){if(grabCursor==IntPtr.Zero)grabCursor=XCreateFontCursor(pointerDisplay,52);XDefineCursor(pointerDisplay,window,grabCursor);}
        else XUndefineCursor(pointerDisplay,window);
        // A query flushes the connection, including the cursor update.
        Pointer(window,out _,out _,out _);
    }
    internal static void DisposePointer() {
        if(OperatingSystem.IsWindows()){ReleaseCapture();return;}
        if(pointerDisplay==IntPtr.Zero)return;
        if(grabCursor!=IntPtr.Zero)XFreeCursor(pointerDisplay,grabCursor);
        XCloseDisplay(pointerDisplay);pointerDisplay=grabCursor=IntPtr.Zero;
    }
    internal static void Set(IntPtr handle)
    {
        if(OperatingSystem.IsWindows()) {
            ExtractIconEx(Environment.ProcessPath,0,out var large,out var small,1);
            if(large!=IntPtr.Zero)SendMessage(handle,0x80,new IntPtr(1),large);
            if(small!=IntPtr.Zero)SendMessage(handle,0x80,IntPtr.Zero,small);
            return;
        }
        if(!OperatingSystem.IsLinux())return;
        IntPtr display=XOpenDisplay(IntPtr.Zero);
        if(display==IntPtr.Zero)return;
        var hint=new ClassHint {Name=Marshal.StringToCoTaskMemUTF8("openbve-routeviewer"),Class=Marshal.StringToCoTaskMemUTF8("OpenBVE-RouteViewer")};
        try { XSetClassHint(display,handle,ref hint); }
        finally { Marshal.FreeCoTaskMem(hint.Name);Marshal.FreeCoTaskMem(hint.Class);XCloseDisplay(display); }
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern uint ExtractIconEx(string path,int index,out IntPtr large,out IntPtr small,uint count);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X,Y; }
    [DllImport("user32.dll")] static extern IntPtr SetFocus(IntPtr window);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr window,ref Point point);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern IntPtr SetCapture(IntPtr window);
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [StructLayout(LayoutKind.Sequential)] struct GuiThreadInfo {
        public int Size,Flags;
        public IntPtr Active,Focus,Capture,MenuOwner,MoveSize,Caret;
        public int Left,Top,Right,Bottom;
    }
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread,ref GuiThreadInfo info);
}
