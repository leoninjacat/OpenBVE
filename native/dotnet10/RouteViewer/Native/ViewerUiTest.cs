using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenTK;
namespace RouteViewer;
// Opt-in OS event test: starts empty, sends a native F1 event and maximizes.
internal static class ViewerUiTest
{
    [StructLayout(LayoutKind.Sequential)]
    struct KeyEvent {
        public int Type;public ulong Serial;public int Send;public IntPtr Display,Window,Root,Subwindow;
        public ulong Time;public int X,Y,XRoot,YRoot;public uint State,Keycode;public int SameScreen;
    }
    [StructLayout(LayoutKind.Explicit,Size=192)]
    struct Event { [FieldOffset(0)]public KeyEvent Key; }
    [DllImport("libX11.so.6")]static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")]static extern byte XKeysymToKeycode(IntPtr display,ulong keysym);
    [DllImport("libX11.so.6")]static extern int XSendEvent(IntPtr display,IntPtr window,bool propagate,long mask,ref Event value);
    [DllImport("libX11.so.6")]static extern int XResizeWindow(IntPtr display,IntPtr window,uint width,uint height);
    [DllImport("libX11.so.6")]static extern int XSync(IntPtr display,bool discard);
    [DllImport("libX11.so.6")]static extern int XCloseDisplay(IntPtr display);
    static Stopwatch timer;
    static int phase;
    static void Key(ViewerWindow window)
    {
        var display=XOpenDisplay(IntPtr.Zero);
        if(display==IntPtr.Zero)throw new Exception("UI test requires X11");
        try {
            var ev=new Event {Key=new KeyEvent {Type=2,Send=1,Display=display,Window=window.WindowInfo.Handle,Keycode=XKeysymToKeycode(display,0xffbe),SameScreen=1}};
            XSendEvent(display,window.WindowInfo.Handle,false,1,ref ev);
            ev.Key.Type=3;XSendEvent(display,window.WindowInfo.Handle,false,2,ref ev);XSync(display,false);
        }finally{XCloseDisplay(display);}
    }
    static void ClickHelp(ViewerWindow window)
    {
        ViewerOverlay.Build(window);
        var point=ViewerOverlay.Center("help");
        var display=XOpenDisplay(IntPtr.Zero);
        try {
            var ev=new Event {Key=new KeyEvent {Type=6,Send=1,Display=display,Window=window.WindowInfo.Handle,X=(int)point.X,Y=(int)point.Y,SameScreen=1}};
            XSendEvent(display,window.WindowInfo.Handle,false,64,ref ev);
            ev.Key.Type=4;ev.Key.Keycode=1;XSendEvent(display,window.WindowInfo.Handle,false,4,ref ev);
            ev.Key.Type=5;XSendEvent(display,window.WindowInfo.Handle,false,8,ref ev);
            XSync(display,false);
        }finally{XCloseDisplay(display);}
    }
    internal static void Rendered(ViewerWindow window)
    {
        if(!Program.UiTest)return;
        timer??=Stopwatch.StartNew();
        if(timer.Elapsed.TotalSeconds>18)throw new Exception("UI events/resize test timed out");
        switch(phase) {
            case 0:
                if(timer.Elapsed.TotalSeconds<1)return;
                Key(window);phase=1;break;
            case 1:
                if(!window.Help)return;
                Console.WriteLine("VIEWER_UI_INPUT_OK: native F1 processed after empty startup");
                Key(window);
                var display=XOpenDisplay(IntPtr.Zero);
                try {XResizeWindow(display,window.WindowInfo.Handle,960,640);XSync(display,false);}finally{XCloseDisplay(display);}
                phase=2;break;
            case 2:
                if(window.Help || window.Width!=960 || window.Height!=640)return;
                window.WindowState=WindowState.Maximized;phase=3;break;
            case 3:
                if(window.WindowState!=WindowState.Maximized || window.Width<=960 || window.Height<=640)return;
                if(Program.Renderer.Screen.Width!=window.Width || Program.Renderer.Screen.Height!=window.Height)throw new Exception("Resize did not update renderer");
                ClickHelp(window);phase=4;break;
            case 4:
                if(!window.Help)return;
                Console.WriteLine($"VIEWER_UI_MAXIMIZE_OK: {window.Width}x{window.Height}, native keyboard and help-icon mouse click remain responsive");
                window.Help=false;
                if(Program.CapturePath!=null)ViewerSmoke.Capture(window,Program.CapturePath);
                Console.WriteLine("VIEWER_UI_OK");
                phase=5;window.Close();break;
        }
    }
}
