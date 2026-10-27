using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
namespace RouteViewer;
internal static class ViewerSmoke
{
    static Stopwatch timer;
    static bool captured, tested, texturesTested;
    static int frames, phase, originalObjects;
    static Vector3 savedEye;
    internal static void Rendered(ViewerWindow window)
    {
        if(!Loading.Complete || Program.CurrentRouteFile==null) return;
        timer ??= Stopwatch.StartNew();
        frames++;
        if(Program.TextureTest&&!texturesTested&&frames>10) {
            int count=Program.Renderer.TextureManager.RegisteredTexturesCount,failed=0;
            for(int i=0;i<count;i++) {
                var texture=LibRender2.Textures.TextureManager.RegisteredTextures[i];
                if(texture==null)continue;
                if(!Program.CurrentHost.LoadTexture(ref texture,OpenBveApi.Textures.OpenGlTextureWrapMode.ClampClamp)) {
                    failed++;
                    Console.Error.WriteLine("VIEWER_TEXTURE_FAILED: "+(texture?.Origin is OpenBveApi.Textures.PathOrigin origin?origin.Path:$"texture {i}"));
                }
            }
            foreach(var message in Interface.GetLogSnapshot()) {
                if(message.Type is not (OpenBveApi.Interface.MessageType.Error or OpenBveApi.Interface.MessageType.Critical))continue;
                bool textureDiagnostic=message.Text.IndexOf(".bmp",StringComparison.OrdinalIgnoreCase)>=0
                    || message.Text.IndexOf(" at LoadTexture",StringComparison.OrdinalIgnoreCase)>=0
                    || message.Text.IndexOf(" at QueryTextureDimensions",StringComparison.OrdinalIgnoreCase)>=0
                    || message.Text.IndexOf(" at CanLoadTexture",StringComparison.OrdinalIgnoreCase)>=0
                    || message.Text.StartsWith("No plugin found that is capable of loading texture",StringComparison.OrdinalIgnoreCase)
                    || message.Text.StartsWith("Zero-byte texture file encountered",StringComparison.OrdinalIgnoreCase);
                if(!textureDiagnostic)continue;
                failed++;Console.Error.WriteLine("VIEWER_TEXTURE_DIAGNOSTIC: "+message.Text);
            }
            if(failed>0)throw new Exception($"Texture GPU upload failed: {failed}/{count}");
            texturesTested=true;
            Console.WriteLine($"VIEWER_TEXTURES_OK: uploaded={count}, failed=0");
        }
        if(Program.CameraSmoke && !tested && frames>10) {
            tested=true;
            var rig=Program.Camera;
            ViewerOverlay.Build(window);
            var trackButton=ViewerOverlay.Center("mode-track");
            ViewerOverlay.Press(window,trackButton.X,trackButton.Y);
            var available = rig.TrackIds;
            if (available.Length > 1) {
                ViewerOverlay.Build(window);
                var select = ViewerOverlay.Center("select-track");
                ViewerOverlay.Press(window, select.X, select.Y);
                if (!ViewerOverlay.TrackDropdownOpen) throw new Exception("Track dropdown did not open");
                ViewerOverlay.Build(window);
                var choice=ViewerOverlay.Center($"track-option-{available[1]}");
                ViewerOverlay.Press(window,choice.X,choice.Y);
                if (ViewerOverlay.TrackDropdownOpen) throw new Exception("Track dropdown did not close");
                int selected = rig.SelectedTrack;
                if (selected == 0) throw new Exception("Track selection did not change");
                rig.Move(0,0,1,1); rig.Apply();
                var selectedFollower = Program.Renderer.CameraTrackFollower;
                var selectedEye = CameraRig.ToV(selectedFollower.WorldPosition) + CameraRig.ToV(selectedFollower.WorldUp)*2.5f;
                if (selectedFollower.TrackIndex != selected || Vector3.Distance(rig.Pose.Position,selectedEye)>.01f)
                    throw new Exception("Camera did not follow selected track position and height");
                Console.WriteLine($"VIEWER_SELECTED_TRACK_OK: via={selected}, eye follows rail basis");
                if (Array.IndexOf(available, 2) >= 0) {
                    rig.SelectedTrack = 2; rig.Cab.Position = 0; rig.Apply();
                    if (Program.Renderer.CameraTrackFollower.TrackIndex != 2 || rig.Cab.Position < rig.Limits.Min ||
                        float.IsNaN(rig.Pose.Position.Y)) throw new Exception("Secondary track start failed");
                    Console.WriteLine($"VIEWER_BRANCH_START_OK: via=2 position={rig.Cab.Position}");
                }
                rig.SelectedTrack=0; rig.Cab.Position=0; rig.Apply();
            }
            double original=rig.Cab.Position;
            var offset=rig.Cab.Offset;
            rig.Move(0,0,1,1);
            rig.Apply();
            if(rig.Cab.Position<=original || Program.Renderer.CameraTrackFollower.TrackIndex!=0)throw new Exception("Track zero advance failed");
            rig.Move(1,1,0,1);rig.Look(60,20);rig.ResetView();
            if(Math.Abs(rig.Cab.Position-original-rig.Speed)>.1 || Vector3.Distance(rig.Cab.Offset,new Vector3(0,2.5f,0))>.001 || rig.Cab.Yaw!=0)throw new Exception("Cab reset moved track position");
            var trackEye=rig.Pose.Position;
            var trackPivot=rig.Pose.Target;
            var trackPosition=rig.Cab.Position;
            ViewerOverlay.Build(window);
            var trackOrbit=ViewerOverlay.Center("orbit");
            ViewerOverlay.Press(window,trackOrbit.X,trackOrbit.Y);window.DragBy(50,20);rig.Apply();
            if(window.Drag!=DragMode.Orbit || Vector3.Distance(trackEye,rig.Pose.Position)>.01 || Vector3.Distance(trackPivot,rig.Pose.Target)<1 || rig.Cab.Position!=trackPosition)throw new Exception("Track orbit control failed");
            window.Drag=DragMode.None;rig.ResetView();
            Console.WriteLine("VIEWER_TRACK_ORBIT_OK: shared globe drag, cab eye fixed, direction changed, track anchor preserved");
            var f=Program.Renderer.CameraTrackFollower;
            var expected=CameraRig.ToV(f.WorldPosition)+CameraRig.ToV(f.WorldUp)*2.5f;
            if(Vector3.Distance(CameraRig.ToV(Program.Renderer.Camera.AbsolutePosition),expected)>.01)throw new Exception("Cab did not follow track basis");
            ViewerOverlay.Build(window);
            var freeButton=ViewerOverlay.Center("mode-free");
            ViewerOverlay.Press(window,freeButton.X,freeButton.Y);
            var freeEye=rig.Pose.Position;rig.Move(1,0,0,1);rig.Apply();
            if(Vector3.Distance(freeEye,rig.Pose.Position)<1)throw new Exception("Free flight failed");
            var resetEye=rig.Pose.Position;
            rig.ResetView();
            if(Vector3.Distance(resetEye,rig.Pose.Position)>.001)throw new Exception("Free reset returned to track");
            Console.WriteLine("VIEWER_MODES_OK: track zero movement, cab basis, reset at same distance, independent free flight");
            var pose=Program.Camera.Pose;
            Vector3 pivot=pose.Target,eye=pose.Position;
            // Drive exactly the same on-screen controls and drag path as the UI.
            ViewerOverlay.Build(window);
            var orbit=ViewerOverlay.Center("orbit");
            ViewerOverlay.Press(window,orbit.X,orbit.Y);
            window.DragBy(70,20);
            if(window.Drag!=DragMode.Orbit || Vector3.Distance(eye,pose.Position)>.01 || Vector3.Distance(pivot,pose.Target)<1) throw new Exception("Orbit control failed");
            pivot=pose.Target;
            window.Drag=DragMode.Pan;
            window.DragBy(45,-20);
            if(Vector3.Distance(pivot,pose.Target)<.1)throw new Exception("Pan control failed");
            float distance=pose.Distance;
            window.Drag=DragMode.Zoom;window.DragBy(0,-30);
            if(pose.Distance>=distance)throw new Exception("Zoom control failed");
            window.Drag=DragMode.None;
            pose.Snap('Y');
            if(Vector3.Dot(pose.Forward,-Vector3.UnitY)<.99)throw new Exception("Axis view failed");
            Program.Camera.ResetFromTrack();
            Console.WriteLine("VIEWER_CAMERA_OK: on-screen orbit, pan, zoom, axis, reset");
        }
        if (Program.ReloadSmoke && tested && phase == 0 && timer.Elapsed.TotalSeconds > 2) {
            originalObjects = Program.Renderer.StaticObjectStates.Count;
            savedEye = Program.Camera.Pose.Position;
            Program.Reload = true;
            phase = 1;
        }
        if (phase == 1 && Program.LoadCount >= 2) {
            if (Program.Renderer.StaticObjectStates.Count != originalObjects || Vector3.Distance(savedEye,Program.Camera.Pose.Position)>.01) throw new Exception("Reload did not preserve camera or object count");
            Console.WriteLine("VIEWER_RELOAD_OK: camera preserved, object count stable");
            phase = 2;
            if (Program.SwitchSmoke != null) Program.PendingRoute = Program.SwitchSmoke;
        }
        if (phase == 2 && Program.SwitchSmoke != null && Program.LoadCount >= 3) {
            if (Program.CurrentRouteFile != Program.SwitchSmoke) throw new Exception("Route switch failed");
            Console.WriteLine("VIEWER_SWITCH_OK: " + Program.CurrentRouteFile);
            phase = 3;
        }
        bool scenarioDone = (!Program.KeyboardTest || ViewerKeyboardTest.Complete) && (!Program.FeedbackTest || ViewerFeedbackTest.Complete) && (!Program.EditorTest || ViewerEditorTest.Complete) && (!Program.ReloadSmoke || phase >= (Program.SwitchSmoke != null ? 3 : 2));
        if(scenarioDone && !captured && Program.CapturePath!=null && timer.Elapsed.TotalSeconds>3 && frames>15) {
            Capture(window,Program.CapturePath);captured=true;
            Console.WriteLine("VIEWER_CAPTURE: "+Program.CapturePath);
        }
        if(scenarioDone && frames>=30 && (Program.CapturePath==null || captured) && Program.SmokeSeconds>0 && timer.Elapsed.TotalSeconds>=Program.SmokeSeconds) {
            if(Program.Renderer.StaticObjectStates.Count==0 || Program.Renderer.VisibleObjects.OpaqueFaces.Count==0)throw new Exception("Empty route scene");
            Console.WriteLine($"VIEWER_SMOKE_OK: frames={frames}, objects={Program.Renderer.StaticObjectStates.Count}, opaque={Program.Renderer.VisibleObjects.OpaqueFaces.Count}");
            Console.WriteLine($"VIEWER_TRACK_RANGE: {Program.Camera.Limits.Min}..{Program.Camera.Limits.Max}");
            if(Program.MessageLogPath!=null) {
                using var log=new StreamWriter(Program.MessageLogPath);
                foreach(var message in Interface.GetLogSnapshot())log.WriteLine(message.Type+": "+message.Text);
            }
            window.Close();
        }
    }
    internal static void Capture(ViewerWindow window,string path)
    {
        int width=window.Width,height=window.Height;
        byte[] pixels=new byte[checked(width*height*4)];
        GL.PixelStore(PixelStoreParameter.PackAlignment,1);
        GL.ReadBuffer(ReadBufferMode.Back);
        GL.ReadPixels(0,0,width,height,PixelFormat.Rgba,PixelType.UnsignedByte,pixels);
        using var image=new SKBitmap(new SKImageInfo(width,height,SKColorType.Rgba8888,SKAlphaType.Opaque));
        for(int y=0;y<height;y++)System.Runtime.InteropServices.Marshal.Copy(pixels,(height-y-1)*width*4,IntPtr.Add(image.GetPixels(),y*image.RowBytes),width*4);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using var output=File.Create(path);using var encoded=image.Encode(SKEncodedImageFormat.Png,100);encoded.SaveTo(output);
    }
}
