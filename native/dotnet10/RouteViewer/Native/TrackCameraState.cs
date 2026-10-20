using System;
using System.Numerics;
namespace RouteViewer;

// Local coordinates of an invisible cab carried by track zero.
internal sealed class TrackCameraState
{
    internal double Position;
    internal Vector3 Offset = new(0, 2.5f, 0);
    internal float Yaw, Pitch;
    internal void Reset() { Offset = new(0, 2.5f, 0); Yaw = Pitch = 0; }
    internal void Move(float side, float up, float forward, float seconds, float speed, double minimum, double maximum)
    {
        var shift = new Vector3(side, up, 0) * seconds * speed;
        Offset += shift;
        Position = Math.Clamp(Position + forward * seconds * speed, minimum, maximum);
    }
    internal void Look(float dx,float dy)
    {
        Rotate(dx, dy);
    }
    private void Rotate(float dx, float dy)
    {
        Yaw = MathF.IEEERemainder(Yaw - dx*.006f, 2*MathF.PI);
        Pitch = Math.Clamp(Pitch + dy*.006f, -1.55f, 1.55f);
    }
    internal (Vector3 Eye,Vector3 Forward,Vector3 Up) Compose(Vector3 origin, Vector3 direction, Vector3 up, Vector3 side)
    {
        var eye = origin + side*Offset.X + up*Offset.Y + direction*Offset.Z;
        var horizontal = direction*MathF.Cos(Yaw) + side*MathF.Sin(Yaw);
        var right = side*MathF.Cos(Yaw) - direction*MathF.Sin(Yaw);
        var forward = Vector3.Normalize(horizontal*MathF.Cos(Pitch) + up*MathF.Sin(Pitch));
        return (eye,forward,Vector3.Normalize(Vector3.Cross(forward,right)));
    }
}
