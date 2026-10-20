using System;
using System.Runtime.InteropServices;
namespace RouteViewer;
internal static class WindowIdentity
{
    [StructLayout(LayoutKind.Sequential)]
    struct ClassHint { public IntPtr Name; public IntPtr Class; }
    [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] static extern int XSetClassHint(IntPtr display,IntPtr window,ref ClassHint hint);
    [DllImport("libX11.so.6")] static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] static extern int XSetInputFocus(IntPtr display,IntPtr window,int revert,ulong time);
    internal static void Focus(IntPtr handle) {
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
        if(pointerDisplay==IntPtr.Zero)pointerDisplay=XOpenDisplay(IntPtr.Zero);
        x=y=0;mask=0;
        return pointerDisplay!=IntPtr.Zero&&XQueryPointer(pointerDisplay,window,out _,out _,out _,out _,out x,out y,out mask);
    }
    internal static void GrabCursor(IntPtr window,bool active) {
        if(pointerDisplay==IntPtr.Zero)pointerDisplay=XOpenDisplay(IntPtr.Zero);
        if(pointerDisplay==IntPtr.Zero)return;
        if(active){if(grabCursor==IntPtr.Zero)grabCursor=XCreateFontCursor(pointerDisplay,52);XDefineCursor(pointerDisplay,window,grabCursor);}
        else XUndefineCursor(pointerDisplay,window);
        // A query flushes the connection, including the cursor update.
        Pointer(window,out _,out _,out _);
    }
    internal static void DisposePointer() {
        if(pointerDisplay==IntPtr.Zero)return;
        if(grabCursor!=IntPtr.Zero)XFreeCursor(pointerDisplay,grabCursor);
        XCloseDisplay(pointerDisplay);pointerDisplay=grabCursor=IntPtr.Zero;
    }
    internal static void Set(IntPtr handle)
    {
        IntPtr display=XOpenDisplay(IntPtr.Zero);
        if(display==IntPtr.Zero)return;
        var hint=new ClassHint {Name=Marshal.StringToCoTaskMemUTF8("openbve-routeviewer"),Class=Marshal.StringToCoTaskMemUTF8("OpenBVE-RouteViewer")};
        try { XSetClassHint(display,handle,ref hint); }
        finally { Marshal.FreeCoTaskMem(hint.Name);Marshal.FreeCoTaskMem(hint.Class);XCloseDisplay(display); }
    }
}
