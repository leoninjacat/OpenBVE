using System;
using System.Numerics;
namespace RouteViewer;

/// <summary>Orbit camera with a movable pivot; independent of renderer and input.</summary>
internal sealed class CameraPose
{
    internal Vector3 Target;
    internal float Distance = 35;
    internal float Yaw;
    internal float Pitch;
    internal Vector3 Forward => Vector3.Normalize(new Vector3(MathF.Sin(Yaw) * MathF.Cos(Pitch), MathF.Sin(Pitch), MathF.Cos(Yaw) * MathF.Cos(Pitch)));
    internal Vector3 Right => new Vector3(MathF.Cos(Yaw), 0, -MathF.Sin(Yaw));
    internal Vector3 Up => Vector3.Normalize(Vector3.Cross(Forward, Right));
    internal Vector3 Position => Target - Forward * Distance;
    internal void LookAt(Vector3 eye, Vector3 target)
    {
        Target = target;
        Vector3 delta = target - eye;
        Distance = Math.Clamp(delta.Length(), 0.5f, 20000);
        if (delta.LengthSquared() < 1e-6) return;
        delta = Vector3.Normalize(delta);
        Yaw = MathF.Atan2(delta.X, delta.Z);
        Pitch = MathF.Asin(Math.Clamp(delta.Y, -1, 1));
    }
    internal void Orbit(float dx, float dy)
    {
        Yaw = MathF.IEEERemainder(Yaw - dx * .006f, 2 * MathF.PI);
        Pitch = Math.Clamp(Pitch + dy * .006f, -MathF.PI / 2 + .001f, MathF.PI / 2 - .001f);
    }
    internal void Pan(float dx, float dy, int viewportHeight)
    {
        // 45-degree vertical field of view, matching the viewer's fixed projection.
        float scale = 2 * Distance * MathF.Tan(MathF.PI / 8) / Math.Max(1, viewportHeight);
        Target += (-Right * dx + Up * dy) * scale;
    }
    internal void Dolly(float delta) => Distance = Math.Clamp(Distance * MathF.Exp(-delta * .14f), .5f, 20000);
    internal void Fly(float right, float up, float forward, float seconds, float speed) =>
        Target += (Right * right + Vector3.UnitY * up + Forward * forward) * seconds * speed;
    internal void Look(float dx, float dy)
    {
        Vector3 eye = Position;
        Orbit(dx, dy);
        Target = eye + Forward * Distance;
    }
    internal void Snap(char axis, bool opposite = false)
    {
        // Looking towards the pivot from the positive axis (Blender's front is -Y).
        Vector3 direction = axis switch { 'X' => -Vector3.UnitX, 'Y' => -Vector3.UnitY, _ => -Vector3.UnitZ };
        if (opposite) direction = -direction;
        LookAt(Target - direction * Distance, Target);
    }
}
