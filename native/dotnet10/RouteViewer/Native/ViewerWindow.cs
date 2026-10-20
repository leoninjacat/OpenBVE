using System;
using System.ComponentModel;
using System.Threading;
using LibRender2.Viewports;
using OpenBveApi.Math;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Input;
using SoundManager;
namespace RouteViewer;
internal enum DragMode { None, Orbit, Pan, Zoom, Look }
internal sealed class ViewerWindow : GameWindow
{
    internal readonly RouteEditor Editor = new();
    internal int ViewWidth => Width;
    internal int ViewHeight => Height;
    internal void UpdateCanvas()
    {
        if(!initialized)return;
        Program.Renderer.Screen.Width=ViewWidth;Program.Renderer.Screen.Height=ViewHeight;
        Program.Renderer.UpdateViewport(ViewportChangeMode.NoChange);
        Program.Renderer.Rectangle.Update();Program.Renderer.OpenGlString.Update();
        Editor.Resize(this);
    }
    internal DragMode Drag;
    internal string Held;
    internal bool InterfaceVisible=true;
    internal bool DarkTheme=true;
    internal bool PointerInteraction;
    int dragX,dragY;
    uint dragButtonMask;
    bool buttonSeen;
    internal void BeginInteraction(MouseButton button) {
        if(Drag==DragMode.None&&Held==null)return;
        PointerInteraction=true;dragButtonMask=button==MouseButton.Left?256u:button==MouseButton.Middle?512u:1024u;
        WindowIdentity.Pointer(WindowInfo.Handle,out dragX,out dragY,out uint mask);buttonSeen=(mask&dragButtonMask)!=0;
        WindowIdentity.GrabCursor(WindowInfo.Handle,Drag!=DragMode.None);
    }
    internal void EndInteraction() {
        PointerInteraction=false;Drag=DragMode.None;Held=null;
        WindowIdentity.GrabCursor(WindowInfo.Handle,false);
    }
    internal void PollInteraction() {
        if(!PointerInteraction||Program.CurrentlyLoading)return;
        if(!WindowIdentity.Pointer(WindowInfo.Handle,out int x,out int y,out uint mask))return;
        PointerX=x;PointerY=y;
        bool pressed=(mask&dragButtonMask)!=0;
        if(buttonSeen&&!pressed){EndInteraction();return;}
        buttonSeen|=pressed;
        if(Held?.EndsWith("slider")==true)ViewerOverlay.Slide(this,x,y);
        else DragBy(x-dragX,y-dragY);
        dragX=x;dragY=y;
    }
    internal void ToggleInterface(){InterfaceVisible=!InterfaceVisible;Help=ShowLog=false;EndInteraction();}
    internal bool Help;
    internal bool ShowLog;
    internal int ExpandedLogMessage=-1, LogScroll;
    internal int PointerX, PointerY;
    private bool initialized;
    internal ViewerWindow() : base(Program.WindowWidth, Program.WindowHeight, new GraphicsMode(32,24,8,0), "Route Viewer Native") { VSync = VSyncMode.Off; TargetRenderFrequency=60; }
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        WindowIdentity.Set(WindowInfo.Handle);
        Program.Renderer.Screen.Width = Width;
        Program.Renderer.Screen.Height = Height;
        Program.Renderer.Initialize();
        Program.Renderer.Lighting.Initialize();
        Program.Renderer.Camera.Reset(new OpenBveApi.Math.Vector3(0, 2.5, -5));
        Program.Renderer.Camera.VerticalViewingAngle = Math.PI / 4;
        Program.Renderer.Camera.OriginalVerticalViewingAngle = Math.PI / 4;
        Program.Renderer.Camera.ForwardViewingDistance = 1000;
        Program.Renderer.Camera.BackwardViewingDistance = 1000;
        Program.Renderer.Camera.ExtraViewingDistance = 200;
        Program.CurrentRoute.CurrentBackground.BackgroundImageDistance = 1000;
        try { Program.Sounds.Initialize(SoundRange.Low); }
        catch (Exception error) { Console.Error.WriteLine("Audio: " + error.Message); }
        Program.Renderer.UpdateViewport(ViewportChangeMode.NoChange);
        initialized = true;
        if (Program.CurrentRouteFile != null) Program.LoadRoute();
    }
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (!initialized || Width < 1 || Height < 1) return;
        UpdateCanvas();
    }
    internal static void DrainJobs()
    {
        var renderer = Program.Renderer;
        while (renderer.RenderThreadJobs.TryDequeue(out ThreadStart job)) {
            job(); lock (job) Monitor.Pulse(job);
        }
        renderer.RenderThreadJobWaiting = false;
    }
    internal static void LoadingScreenLoop()
    {
        while (!Loading.Complete && !Loading.Cancel) {
            var window = Program.Renderer.GameWindow;
            window.ProcessEvents();
            if (window.IsExiting) { Loading.Cancel = true; break; }
            DrainJobs();
            double progress = 0;
            foreach (var plugin in Program.CurrentHost.Plugins) if (plugin.Route?.IsLoading == true) progress = plugin.Route.CurrentProgress;
            Program.Renderer.Loading.DrawLoadingScreen(Program.Renderer.Fonts.SmallFont, progress);
            window.SwapBuffers();
            Thread.Sleep(8);
        }
        Program.Renderer.Loading.CompleteLoading();
        if (!Loading.Cancel) Program.Renderer.ReloadShadowSettings();
    }
    protected override void OnUpdateFrame(FrameEventArgs e)
    {
        base.OnUpdateFrame(e);
        ProcessEvents();
        if (IsExiting) return;
        Editor.Pump(this);
        if (IsExiting) return;
        if (Program.CurrentlyLoading) return;
        if (Program.PendingRoute != null) {
            string route = Program.PendingRoute; Program.PendingRoute = null;
            Program.CurrentRouteFile = route; Program.LoadRoute();
        } else if (Program.Reload && Program.CurrentRouteFile != null) {
            Program.Reload = false; Program.LoadRoute();
        }
        PollInteraction();
        if (!Loading.Complete || Program.CurrentRouteFile == null) return;
        if (!Focused) Program.Camera.Cruise=0;
        float elapsed = (float)Math.Min(e.Time, .05);
        if ((Focused||PointerInteraction) && !Help && !ShowLog) {
            var keys = Keyboard.GetState();
            float x = (keys.IsKeyDown(Key.D) ? 1 : 0) - (keys.IsKeyDown(Key.A) ? 1 : 0);
            float y = (keys.IsKeyDown(Key.E) ? 1 : 0) - (keys.IsKeyDown(Key.Q) ? 1 : 0);
            float z = (keys.IsKeyDown(Key.W) ? 1 : 0) - (keys.IsKeyDown(Key.S) ? 1 : 0);
            bool track=Program.Camera.Mode==NavigationMode.Track;
            x += (keys.IsKeyDown(Key.Keypad6)?1:0)-(keys.IsKeyDown(Key.Keypad4)?1:0);
            y += (keys.IsKeyDown(Key.Keypad8)?1:0)-(keys.IsKeyDown(Key.Keypad2)?1:0);
            if(track) {
                z += (keys.IsKeyDown(Key.Keypad9)?1:0)-(keys.IsKeyDown(Key.Keypad3)?1:0);
                Program.Camera.Look(((keys.IsKeyDown(Key.Left)?1:0)-(keys.IsKeyDown(Key.Right)?1:0))*elapsed*160,
                    ((keys.IsKeyDown(Key.Up)?1:0)-(keys.IsKeyDown(Key.Down)?1:0))*elapsed*160);
            }
            switch (Held) { case "rail-forward": z++; break; case "rail-back": z--; break; case "left": x--; break; case "right": x++; break; case "up": y++; break; case "down": y--; break; case "forward": z++; break; case "back": z--; break; }
            var movement = new System.Numerics.Vector3(x,y,z);
            if (movement.LengthSquared() > 1) movement = System.Numerics.Vector3.Normalize(movement);
            float fast = keys.IsKeyDown(Key.ShiftLeft) || keys.IsKeyDown(Key.ShiftRight) ? 4 : 1;
            Program.Camera.Move(movement.X, movement.Y, movement.Z, elapsed, fast);
        }
        Program.Camera.Apply();
        Game.SecondsSinceMidnight += elapsed;
        Program.CurrentRoute.SecondsSinceMidnight = Game.SecondsSinceMidnight;
        ObjectManager.UpdateAnimatedWorldObjects(elapsed, false);
        Program.Sounds.Update(elapsed);
    }
    protected override void OnRenderFrame(FrameEventArgs e)
    {
        base.OnRenderFrame(e);
        ProcessEvents();
        if (IsExiting) return;
        if (Program.CurrentlyLoading) return;
        if (Program.SmokeSeconds > 0 && Program.CurrentRouteFile == null) { Environment.ExitCode = 1; Close(); return; }
        DrainJobs();
        Program.Renderer.FrameRate = RenderFrequency;
        Program.Renderer.Lighting.UpdateLighting(Program.CurrentRoute.SecondsSinceMidnight, Program.CurrentRoute.LightDefinitions);
        GL.Viewport(0,Height-ViewHeight,ViewWidth,ViewHeight);
        Program.Renderer.RenderScene(Math.Min(e.Time, .05));
        ViewerEditorTest.Rendered(this);
        ViewerFeedbackTest.Rendered(this);
        ViewerSmoke.Rendered(this);
        ViewerUiTest.Rendered(this);
        if (!IsExiting) SwapBuffers();
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if(Program.FeedbackTest)Console.WriteLine($"FEEDBACK_DOWN: {e.X},{e.Y} button={e.Button} canvas={Width}x{Height} loading={Program.CurrentlyLoading}");
        WindowIdentity.Focus(WindowInfo.Handle);
        PointerX = e.X; PointerY = e.Y;
        if (Program.CurrentlyLoading || e.X>=ViewWidth || e.Y>=ViewHeight) return;
        if (e.Button == MouseButton.Left && ViewerOverlay.Press(this, e.X, e.Y)) {BeginInteraction(e.Button);return;}
        if (!Loading.Complete || Help || ShowLog) return;
        if (e.Button == MouseButton.Middle) {
            var keys = Keyboard.GetState();
            Drag = keys.IsKeyDown(Key.ShiftLeft) || keys.IsKeyDown(Key.ShiftRight) ? DragMode.Pan :
                keys.IsKeyDown(Key.ControlLeft) || keys.IsKeyDown(Key.ControlRight) ? DragMode.Zoom : DragMode.Orbit;
        } else if (e.Button == MouseButton.Right) Drag = DragMode.Look;
        BeginInteraction(e.Button);
    }
    internal void DragBy(float dx, float dy)
    {
        switch (Drag) {
            case DragMode.Orbit: Program.Camera.Look(dx,dy); break;
            case DragMode.Pan: if(Program.Camera.Mode==NavigationMode.Free) Program.Camera.Pose.Pan(dx,dy,ViewHeight); else Program.Camera.Cab.Offset+=new System.Numerics.Vector3(-dx*.02f,dy*.02f,0); break;
            case DragMode.Zoom: Program.Camera.Zoom(-dy*.08f); break;
            case DragMode.Look: Program.Camera.Look(dx,dy); break;
        }
    }
    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        base.OnMouseMove(e);
        if(!PointerInteraction){PointerX=e.X;PointerY=e.Y;}
    }
    protected override void OnMouseUp(MouseButtonEventArgs e) {
        base.OnMouseUp(e);
        // Use the release event position, not a newer server position after
        // the pointer has already moved towards another control.
        if(PointerInteraction&&!Program.CurrentlyLoading) {
            if(Held?.EndsWith("slider")==true)ViewerOverlay.Slide(this,e.X,e.Y);
            else DragBy(e.X-dragX,e.Y-dragY);
        }
        EndInteraction();
    }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if(ShowLog&&!Help) {LogScroll=Math.Max(0,LogScroll-(int)Math.Sign(e.DeltaPrecise)*3);return;}
        if (Loading.Complete && !Help && !ShowLog && !Program.CurrentlyLoading) Program.Camera.Zoom(e.DeltaPrecise);
    }
    protected override void OnFocusedChanged(EventArgs e) { base.OnFocusedChanged(e); if(!Focused&&!PointerInteraction)EndInteraction(); }
    protected override void OnKeyDown(KeyboardKeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Program.CurrentlyLoading) return;
        if(e.Key==Key.F10){ToggleInterface();return;}
        if(e.Key==Key.F8){Editor.ToggleVisible();return;}
        if (e.Key == Key.Escape) { Help = false; ShowLog = false; Drag = DragMode.None; Held = null; return; }
        if (e.Key == Key.F1) { Help = !Help;if(Help)InterfaceVisible=true; return; }
        if (e.Key == Key.F7 || e.Control && e.Key == Key.O) { Program.OpenPicker(); return; }
        if (e.Key == Key.F9) { ShowLog = !ShowLog;if(ShowLog)InterfaceVisible=true; return; }
        if (!Loading.Complete || Help || ShowLog) return;
        if(e.Key==Key.Tab) {
            Program.Camera.SetMode(Program.Camera.Mode==NavigationMode.Track?NavigationMode.Free:NavigationMode.Track);
            Drag=DragMode.None;Held=null;return;
        }
        switch (e.Key) {
            case Key.F5: Program.Reload = true; break;
            case Key.Home: case Key.Keypad5: Program.Camera.ResetView(); break;
            case Key.PageDown: Program.Camera.Station(1); break;
            case Key.PageUp: Program.Camera.Station(-1); break;
            case Key.Space: if(Program.Camera.Mode==NavigationMode.Track)Program.Camera.Cruise=Program.Camera.Cruise==0?1:0;break;
            case Key.KeypadPlus: Program.Camera.Zoom(1); break;
            case Key.KeypadMinus: Program.Camera.Zoom(-1); break;
            case Key.F: Program.Renderer.OptionWireFrame = !Program.Renderer.OptionWireFrame; break;
        }
    }
    protected override void OnFileDrop(FileDropEventArgs e)
    {
        base.OnFileDrop(e);
        if (Program.CurrentlyLoading) return;
        try { Editor.RequestRoute(Program.ResolvePath(e.FileName)); }
        catch (Exception error) { Program.Status = error.Message; }
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (Program.CurrentlyLoading) { Loading.Cancel = true; e.Cancel = true; return; }
        if(Editor.Running && !Editor.CloseApproved) { Editor.Send("QUIT");e.Cancel=true;return; }
        EndInteraction();
        WindowIdentity.DisposePointer();
        Editor.Dispose();
        ViewerIcons.Dispose();
        Program.Renderer.VisibilityThreadShouldRun = false;
        // BaseRenderer.DeInitialize also disposes GameWindow; leave ownership
        // with OpenTK so closing does not unmap the same X11 window twice.
        Program.Renderer.GameWindow = null;
        Program.Renderer.DeInitialize();
        Interface.CurrentOptions.Save(OpenBveApi.Path.CombineFile(Program.FileSystem.SettingsFolder, "1.5.0/options_rv.cfg"));
        base.OnClosing(e);
    }
}
