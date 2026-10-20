using System;
using OpenBveApi.Runtime;
using V = System.Numerics.Vector3;
using WorldVector = OpenBveApi.Math.Vector3;
namespace RouteViewer;
internal enum NavigationMode { Track, Free }
internal sealed class CameraRig
{
    internal readonly CameraPose Pose = new CameraPose();
    internal float Speed = 20 / 3.6f;
    internal NavigationMode Mode = NavigationMode.Track;
    internal readonly TrackCameraState Cab = new();
    internal float Cruise;
    internal float FieldOfView = MathF.PI / 4;
    internal (double Min,double Max) Limits {
        get { var e=Program.CurrentRoute.Tracks[0].Elements; return e==null || e.Length==0 ? (0,1) : (e[0].StartingTrackPosition,e[^1].StartingTrackPosition); }
    }
    internal void SetMode(NavigationMode mode)
    {
        if (Mode == mode) return;
        Cruise=0;
        if (mode == NavigationMode.Track) { Cab.Position=Program.Renderer.CameraTrackFollower.TrackPosition; Cab.Reset(); }
        Mode=mode;
        Apply();
    }
    internal void Move(float x,float y,float z,float seconds,float multiplier=1)
    {
        if (Mode==NavigationMode.Free) Pose.Fly(x,y,z,seconds,Speed*multiplier);
        else {
            var limits=Limits;
            Cab.Move(x,y,z+Cruise,seconds,Speed*multiplier,limits.Min,limits.Max);
            if(Cab.Position>=limits.Max && Cruise>0 || Cab.Position<=limits.Min && Cruise<0)Cruise=0;
        }
    }
    internal void Look(float dx,float dy) { if(Mode==NavigationMode.Track) Cab.Look(dx,dy); else Pose.Look(dx,dy); }
    internal void Zoom(float delta) {
        if(Mode==NavigationMode.Free) Pose.Dolly(delta);
        else { FieldOfView=Math.Clamp(FieldOfView*MathF.Exp(-delta*.08f),.15f,1.4f); SetProjection(); }
    }
    void SetProjection() {
        Program.Renderer.Camera.VerticalViewingAngle=FieldOfView;
        Program.Renderer.UpdateViewport(LibRender2.Viewports.ViewportChangeMode.NoChange);
    }
    internal void ResetView() {
        if(Mode==NavigationMode.Track) { Cab.Reset(); FieldOfView=MathF.PI/4; SetProjection(); Apply(); }
        else {
            var eye=Pose.Position;
            Pose.LookAt(eye,eye+V.UnitZ*Pose.Distance);
            FieldOfView=MathF.PI/4; SetProjection(); Apply();
        }
    }
    private double anchor = double.NaN;
    internal static V ToV(WorldVector vector) => new V((float)vector.X, (float)vector.Y, (float)vector.Z);
    internal static WorldVector ToWorld(V vector) => new WorldVector(vector.X, vector.Y, vector.Z);
    internal void Invalidate() => anchor = double.NaN;
    internal void ResetFromTrack()
    {
        var follower = Program.Renderer.CameraTrackFollower;
        Cab.Position=follower.TrackPosition; Cab.Reset(); Cruise=0;
        V position = ToV(follower.WorldPosition);
        V direction = V.Normalize(ToV(follower.WorldDirection));
        Pose.LookAt(position + V.UnitY * 5, position + direction * 35 + V.UnitY * 2.5f);
        anchor = double.NaN;
        Apply();
    }
    internal void Station(int direction)
    {
        double current = Mode==NavigationMode.Track ? Cab.Position : Program.Renderer.CameraTrackFollower.TrackPosition;
        double best = direction > 0 ? double.MaxValue : double.MinValue;
        foreach (var station in Program.CurrentRoute.Stations) foreach (var stop in station.Stops) {
            double p = stop.TrackPosition;
            if (direction > 0 ? p > current + 1 && p < best : p < current - 1 && p > best) best = p;
        }
        if (best == double.MaxValue || best == double.MinValue) { Program.Status = "Não há outra estação nessa direção."; return; }
        Program.Renderer.CameraTrackFollower.UpdateAbsolute(best, true, false);
        ResetFromTrack();
    }
    internal void Apply()
    {
        if (!Loading.Complete) return;
        var renderer = Program.Renderer;
        var camera = renderer.Camera;
        if(Mode==NavigationMode.Track) {
            var limits=Limits;
            Cab.Position=Math.Clamp(Cab.Position,limits.Min,limits.Max);
            var follower=renderer.CameraTrackFollower;
            follower.TrackIndex=0;
            follower.UpdateAbsolute(Cab.Position,true,false);
            var basis=Cab.Compose(ToV(follower.WorldPosition),ToV(follower.WorldDirection),ToV(follower.WorldUp),ToV(follower.WorldSide));
            Pose.LookAt(basis.Eye,basis.Eye+basis.Forward*35);
            camera.AbsolutePosition=ToWorld(basis.Eye);
            camera.AbsoluteDirection=ToWorld(basis.Forward);
            camera.AbsoluteUp=ToWorld(basis.Up);
            camera.AbsoluteSide=ToWorld(V.Normalize(V.Cross(basis.Up,basis.Forward)));
            camera.Alignment.TrackPosition=Cab.Position;
            camera.Alignment.Position=ToWorld(Cab.Offset);
            camera.CurrentMode=CameraViewMode.Track;
            renderer.UpdateViewingDistances(Program.CurrentRoute.CurrentBackground.BackgroundImageDistance);
            return;
        }
        camera.Alignment.Position=new WorldVector(0,0,0);
        camera.AbsolutePosition = ToWorld(Pose.Position);
        camera.AbsoluteDirection = ToWorld(Pose.Forward);
        camera.AbsoluteSide = ToWorld(Pose.Right);
        camera.AbsoluteUp = ToWorld(Pose.Up);
        camera.CurrentMode = CameraViewMode.Track;
        // Route visibility and environment events need a track anchor even when
        // the user is flying away from the rails. Choose the nearest main-track
        // segment, projecting onto it rather than snapping in whole block steps.
        var elements = Program.CurrentRoute.Tracks[0].Elements;
        double bestDistance = double.MaxValue, track = 0;
        for (int i = 0; i < elements.Length; i++) {
            V start = ToV(elements[i].WorldPosition);
            V end = i + 1 < elements.Length ? ToV(elements[i + 1].WorldPosition) : start;
            V delta = end - start;
            float fraction = delta.LengthSquared() > 1e-6 ? Math.Clamp(V.Dot(Pose.Position - start, delta) / delta.LengthSquared(), 0, 1) : 0;
            double distance = V.DistanceSquared(Pose.Position, start + delta * fraction);
            if (distance < bestDistance) {
                bestDistance = distance;
                double length = i + 1 < elements.Length ? elements[i + 1].StartingTrackPosition - elements[i].StartingTrackPosition : 0;
                track = elements[i].StartingTrackPosition + fraction * length;
            }
        }
        if (double.IsNaN(anchor) || Math.Abs(track - anchor) > .1) {
            renderer.CameraTrackFollower.UpdateAbsolute(track, true, false);
            camera.Alignment.TrackPosition = track;
            anchor = track;
        }
        renderer.UpdateViewingDistances(Program.CurrentRoute.CurrentBackground.BackgroundImageDistance);
    }
}
