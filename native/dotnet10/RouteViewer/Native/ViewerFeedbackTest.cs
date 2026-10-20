using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using OpenBveApi.Interface;
namespace RouteViewer;
// Opt-in regression: send native events to the embedded scene, then move the
// server pointer between frames. This exercises focus, event delivery and polling.
internal static class ViewerFeedbackTest
{
    [StructLayout(LayoutKind.Sequential)] struct ButtonEvent {
        public int Type;public ulong Serial;public int Send;public IntPtr Display,Window,Root,Child;
        public ulong Time;public int X,Y,RootX,RootY;public uint State,Button;public int SameScreen;
    }
    [StructLayout(LayoutKind.Explicit,Size=192)] struct Event { [FieldOffset(0)]public ButtonEvent Button; }
    [DllImport("libX11.so.6")]static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")]static extern int XWarpPointer(IntPtr display,IntPtr source,IntPtr destination,int sx,int sy,uint width,uint height,int x,int y);
    [DllImport("libX11.so.6")]static extern int XSendEvent(IntPtr display,IntPtr window,bool propagate,long mask,ref Event value);
    [DllImport("libX11.so.6")]static extern int XSync(IntPtr display,bool discard);
    [DllImport("libX11.so.6")]static extern int XCloseDisplay(IntPtr display);
    static int phase;static Stopwatch timer,step;
    static (float X,float Y) point;static Vector3 eye,target;static double track;static float speed;
    internal static bool Complete=>phase>=15;
    static void Move(ViewerWindow w,float x,float y) {
        WindowIdentity.Focus(w.WindowInfo.Handle);
        var display=XOpenDisplay(IntPtr.Zero);
        try {
            XWarpPointer(display,IntPtr.Zero,w.WindowInfo.Handle,0,0,0,0,(int)x,(int)y);XSync(display,false);
            WindowIdentity.Pointer(w.WindowInfo.Handle,out int px,out int py,out _);
            // OpenTK builds button arguments from its cached mouse position.
            // Send the matching motion event before the synthetic button event.
            var value=new Event {Button=new ButtonEvent {Type=6,Send=1,Display=display,Window=w.WindowInfo.Handle,X=px,Y=py,SameScreen=1}};
            XSendEvent(display,w.WindowInfo.Handle,false,64,ref value);XSync(display,false);
            Console.WriteLine($"FEEDBACK_POINTER: requested={x:0},{y:0} actual={px},{py} canvas={w.Width}x{w.Height}");
        }finally{XCloseDisplay(display);}
    }
    static void Button(ViewerWindow w,bool down) {
        var display=XOpenDisplay(IntPtr.Zero);
        try {
            WindowIdentity.Pointer(w.WindowInfo.Handle,out int x,out int y,out _);
            var value=new Event {Button=new ButtonEvent {Type=down?4:5,Send=1,Display=display,Window=w.WindowInfo.Handle,X=x,Y=y,Button=1,SameScreen=1}};
            XSendEvent(display,w.WindowInfo.Handle,false,down?4:8,ref value);XSync(display,false);
        }finally{XCloseDisplay(display);}
    }
    static void Press(ViewerWindow w,string id) {ViewerOverlay.Build(w);point=ViewerOverlay.Center(id);Move(w,point.X,point.Y);Console.WriteLine("FEEDBACK_PRESS: "+id);Button(w,true);}
    static void Click(ViewerWindow w,string id){Press(w,id);Button(w,false);}
    internal static void Rendered(ViewerWindow w)
    {
        if(!Program.FeedbackTest||!Loading.Complete||Complete)return;
        timer??=Stopwatch.StartNew();step??=Stopwatch.StartNew();
        if(timer.Elapsed.TotalSeconds>35)throw new Exception("Feedback test timed out at phase "+phase);
        if(step.Elapsed.TotalSeconds<.4)return;
        switch(phase) {
            case 0:
                if(timer.Elapsed.TotalSeconds<1.5||!w.Editor.Running||!w.Editor.CanvasMatches(w,0))return;
                if(RouteInfo.StationLines().Length<3)throw new Exception("Station info missing");
                RouteInfo.Failure="Falha de carregamento de teste";RouteInfo.Refresh();
                if(RouteInfo.Errors<1||RouteInfo.FirstError!=RouteInfo.Failure)throw new Exception("Fatal load failure missing from card");
                RouteInfo.Failure=null;
                Interface.AddMessage(MessageType.Error,false,"Feedback diagnostic test");RouteInfo.RefreshIfChanged();
                if(RouteInfo.Errors<1||RouteInfo.FirstError==null)throw new Exception("Load diagnostics missing");
                Click(w,"route-info");break;
            case 1:
                if(!w.ShowLog)throw new Exception("Diagnostic card did not open log");
                w.ShowLog=false;lock(Interface.LogLock)Interface.LogMessages.RemoveAll(m=>m.Text=="Feedback diagnostic test");RouteInfo.Refresh();
                speed=Program.Camera.Speed;Press(w,"speed-slider");break;
            case 2:
                if(!w.PointerInteraction||w.Held!="speed-slider")throw new Exception("Native slider press lost focus/drag");
                speed=Program.Camera.Speed;Move(w,point.X+45,point.Y);break;
            case 3:
                if(Math.Abs(Program.Camera.Speed-speed)<1)throw new Exception("Native slider drag did not change speed");
                Button(w,false);Program.Camera.SetMode(NavigationMode.Track);Program.Camera.ResetView();eye=Program.Camera.Pose.Position;target=Program.Camera.Pose.Target;track=Program.Camera.Cab.Position;
                Press(w,"orbit");break;
            case 4:
                if(!w.PointerInteraction||w.Drag!=DragMode.Orbit)throw new Exception("Globe grab lost on focus transition");
                Move(w,point.X+65,point.Y+20);break;
            case 5:
                if(Vector3.Distance(eye,Program.Camera.Pose.Position)>.02||Vector3.Distance(target,Program.Camera.Pose.Target)<1||Program.Camera.Cab.Position!=track)throw new Exception("Native track globe drag failed");
                eye=Program.Camera.Pose.Position;Button(w,false);Click(w,"mode-free");break;
            case 6:
                if(Vector3.Distance(eye,Program.Camera.Pose.Position)>.01)throw new Exception("Camera jumped after globe release");
                if(Program.Camera.Mode!=NavigationMode.Free)throw new Exception("Camera button failed");
                Program.Camera.Move(1,1,1,.8f);Program.Camera.Apply();
                eye=Program.Camera.Pose.Position;target=Program.Camera.Pose.Target;Press(w,"orbit");break;
            case 7:Move(w,point.X-55,point.Y-15);break;
            case 8:
                if(Vector3.Distance(eye,Program.Camera.Pose.Position)>.02||Vector3.Distance(target,Program.Camera.Pose.Target)<1)throw new Exception("Native free globe must rotate at its current position");
                Button(w,false);Click(w,"toggle-ui");break;
            case 9:
                if(w.InterfaceVisible)throw new Exception("Eye did not hide interface");
                ViewerOverlay.Build(w);if(ViewerOverlay.Controls.Any(c=>c.Id=="orbit"))throw new Exception("Hidden interface still interactive");
                Click(w,"toggle-ui");Click(w,"toggle-editor");break;
            case 10:
                if(!w.InterfaceVisible||w.Editor.Visible)return;
                Click(w,"toggle-editor");break;
            case 11:
                if(!w.Editor.Visible)return;
                w.Editor.Send("TEST-THEME","light");break;
            case 12:
                if(w.DarkTheme)return;
                w.Editor.Send("TEST-THEME","dark");break;
            case 13:
                if(!w.DarkTheme)return;
                w.Editor.Send("TEST-THEME","default");break;
            case 14:
                Console.WriteLine("VIEWER_FEEDBACK_OK: native slider and globe drags, fixed cab eye, fixed off-track free eye, station/error card, UI/editor toggles, live light/dark theme");break;
        }
        phase++;step.Restart();
    }
}
