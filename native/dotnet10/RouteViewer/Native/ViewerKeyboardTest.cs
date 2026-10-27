using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RouteViewer;

// Inject keyboard messages into the real Win32 child, not the camera API. Check
// movement and shortcuts, then move focus to the editor and reject input there.
internal static class ViewerKeyboardTest
{
    static readonly Stopwatch clock=new();
    static double step,position;
    static int phase,layout;
    internal static bool Complete=>phase==6;
    internal static void Rendered(ViewerWindow window)
    {
        if(!Program.KeyboardTest||!Loading.Complete||Complete)return;
        if(!clock.IsRunning)clock.Start();
        if(clock.Elapsed.TotalSeconds>30)throw new Exception($"Keyboard test timed out: phase={phase}, layout={layout}, nativeFocus={window.ViewportFocused}, opentkFocus={window.Focused}");
        if(clock.Elapsed.TotalSeconds-step<.4)return;
        switch(phase) {
            case 0:
                if(!window.Editor.Running)return;
                window.Editor.Send("TEST-FOCUS","viewport");phase=1;break;
            case 1:
                if(!window.ViewportFocused)return;
                position=Program.Camera.Cab.Position;
                Key(window,0x57,true);phase=2;break;
            case 2:
                Key(window,0x57,false);
                if(Program.Camera.Cab.Position<=position)throw new Exception("W did not move the viewport camera");
                Console.WriteLine($"VIEWER_KEYBOARD_LAYOUT_OK: layout={layout}, nativeFocus={window.ViewportFocused}, opentkFocus={window.Focused}");
                if(++layout<3){window.Editor.Send("TEST-LAYOUT",layout.ToString());window.Editor.Send("TEST-FOCUS","viewport");phase=1;}
                else {window.Editor.Send("TEST-FOCUS","editor");phase=3;}
                break;
            case 3:
                if(window.ViewportFocused)return;
                position=Program.Camera.Cab.Position;
                Message(window,0x57,true);Message(window,0x70,true);Message(window,0x70,false);phase=4;break;
            case 4:
                Message(window,0x57,false);
                if(Math.Abs(Program.Camera.Cab.Position-position)>.001||window.Help)throw new Exception("Editor keyboard input leaked into the viewport");
                window.Editor.Send("TEST-FOCUS","viewport");phase=5;break;
            case 5:
                if(!window.ViewportFocused)return;
                Key(window,0x70,true);Key(window,0x70,false);phase=7;break;
            case 7:
                if(!window.Help)throw new Exception("F1 did not reach viewport");
                Key(window,0x70,true);Key(window,0x70,false);
                Key(window,0x09,true);Key(window,0x09,false);phase=8;break;
            case 8:
                if(window.Help||Program.Camera.Mode!=NavigationMode.Free)throw new Exception("Viewport F1/Tab shortcuts failed");
                Console.WriteLine("VIEWER_KEYBOARD_OK: W movement in three layouts, editor focus isolation, F1 and Tab shortcuts");phase=6;break;
        }
        step=clock.Elapsed.TotalSeconds;
    }
    static void Key(ViewerWindow window,uint key,bool down)
    {
        Message(window,key,down);
    }
    static void Message(ViewerWindow window,uint key,bool down)
    {
        long flags=1L|((long)MapVirtualKey(key,0)<<16);
        if(!down)flags|=0xC0000000L;
        if(!PostMessage(window.WindowInfo.Handle,down?0x100u:0x101u,new IntPtr(key),new IntPtr(flags)))throw new Exception("PostMessage failed");
    }
    [DllImport("user32.dll")]static extern bool PostMessage(IntPtr window,uint message,IntPtr wparam,IntPtr lparam);
    [DllImport("user32.dll")]static extern uint MapVirtualKey(uint key,uint type);
}
