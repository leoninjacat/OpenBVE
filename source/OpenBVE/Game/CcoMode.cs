using System;
using System.Collections.Generic;
using LibRender2.Cameras;
using LibRender2.Trains;
using OpenBveApi.Runtime;
using LibRender2.Screens;
using LibRender2.Viewports;
using OpenBveApi.Math;
using TrainManager.Car;

namespace OpenBve
{
    /// <summary>Station observation cameras, independent of the AI train.</summary>
    internal static class CcoMode
    {
        internal static bool Enabled;
        private static readonly Dictionary<int, CameraAlignment> Cameras = new Dictionary<int, CameraAlignment>();
        private static int station = -1;

        internal static void Initialize()
        {
            Cameras.Clear();
            station = -1;
            if (Enabled && Program.CurrentRoute.Stations.Length > 0)
                SelectStation(Math.Max(0, TrainManager.PlayerTrain.Station));
        }

        internal static void Cycle(int direction)
        {
            int count = Program.CurrentRoute.Stations.Length;
            if (count > 0) SelectStation((station + direction + count) % count);
        }

        internal static string StationName => Enabled && station >= 0 && station < Program.CurrentRoute.Stations.Length
            ? Program.CurrentRoute.Stations[station].Name : string.Empty;

        internal static bool ResetCamera()
        {
            if (!Enabled || station < 0 || Program.Renderer.Camera.CurrentMode != CameraViewMode.Track)
                return false;
            SelectStation(station, true);
            return true;
        }

        internal static void SelectStation(int index, bool reset = false)
        {
            if (!Enabled || index < 0 || index >= Program.CurrentRoute.Stations.Length) return;
            var camera = Program.Renderer.Camera;
            if (!reset && station >= 0 && camera.CurrentMode == CameraViewMode.Track)
                Cameras[station] = Copy(camera.Alignment);
            CameraAlignment alignment;
            if (reset || !Cameras.TryGetValue(index, out alignment))
            {
                double position = Program.CurrentRoute.Stations[index].GetStopPosition(TrainManager.PlayerTrain);
                alignment = new CameraAlignment(new Vector3(6, 4, 0), -0.35, -0.12, 0, position, 0);
            }
            station = index;
            camera.CurrentMode = CameraViewMode.Track;
            camera.Alignment = Copy(alignment);
            camera.AlignmentDirection = new CameraAlignment();
            camera.AlignmentSpeed = new CameraAlignment();
            Program.Renderer.CameraTrackFollower.UpdateAbsolute(alignment.TrackPosition, true, false);
            foreach (var car in TrainManager.PlayerTrain.Cars)
            {
                car.ChangeCarSection(CarSectionType.Exterior);
                car.FrontBogie.ChangeSection(0);
                car.RearBogie.ChangeSection(0);
                car.Coupler.ChangeSection(0);
            }
            camera.ApplyZoom();
            Program.Renderer.UpdateViewport(ViewportChangeMode.NoChange);
            World.UpdateAbsoluteCamera();
            Program.Renderer.UpdateViewingDistances(Program.CurrentRoute.CurrentBackground.BackgroundImageDistance);
            Program.Renderer.UpdateVisibility(true);
        }

        private static CameraAlignment Copy(CameraAlignment source)
        {
            return new CameraAlignment(source.Position, source.Yaw, source.Pitch, source.Roll, source.TrackPosition, source.Zoom);
        }
    }
}
