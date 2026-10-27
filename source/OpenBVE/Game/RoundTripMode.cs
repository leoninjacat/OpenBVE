using System;
using System.IO;
using System.Linq;
using LibRender2.Cameras;
using LibRender2.Trains;
using OpenBveApi.Interface;
using OpenBveApi.Runtime;
using OpenBveApi.Math;
using TrainManager.Car;
using TrainManager.Trains;

namespace OpenBve
{
    internal static class RoundTripMode
    {
        private static int generation;
        internal static void Initialize()
        {
            var player = TrainManager.PlayerTrain;
            player.RoundTrip = null;
            generation = 0;
            if (CcoMode.Enabled) return;
            string route = Program.CurrentRoute.Information.RouteFile;
            if (string.IsNullOrEmpty(route)) return;
            string path = Path.ChangeExtension(route, ".roundtrip.xml");
            if (!File.Exists(path)) return;
            try
            {
                player.RoundTrip = RoundTripService.Load(path, player,
                    () => Program.TrainManager.Trains.Concat(Program.TrainManager.TFOs.OfType<TrainBase>()));
                foreach (var car in player.Cars)
                    if (car.Up.NormSquared() < 0.5) car.Up = car.FrontAxle.Follower.WorldUp;
                player.RoundTrip.UpdateInterlocking();
                Console.WriteLine("ROUNDTRIP_READY: " + path);
            }
            catch (Exception ex)
            {
                Program.CurrentHost.AddMessage(MessageType.Error, false, "Configuracao de ida e volta invalida: " + ex.Message);
            }
        }
        internal static void Update()
        {
            var train = TrainManager.PlayerTrain;
            var service = train?.RoundTrip;
            if (service == null) return;
            service.UpdateInterlocking();
            if (generation == service.Generation) return;
            generation = service.Generation;
            CabinAnnouncements.ResetVisit();
            var camera = Program.Renderer.Camera;
            camera.IsTransitioning = false;
            camera.TargetCameraCar = -1;
            camera.ModeTransitionTimer = 1;
            camera.AlignmentDirection = new CameraAlignment();
            camera.AlignmentSpeed = new CameraAlignment();
            camera.Alignment = new CameraAlignment(Vector3.Zero, service.Direction < 0 ? Math.PI : 0, 0, 0,
                train.Cars[train.DriverCar].TrackPosition, 0);
            train.CameraCar = train.DriverCar;
            foreach (var car in train.Cars) car.Up = car.FrontAxle.Follower.WorldUp;
            bool interior = camera.CurrentMode == CameraViewMode.Interior || camera.CurrentMode == CameraViewMode.InteriorLookAhead;
            foreach (var car in train.Cars)
                car.ChangeCarSection(interior ? (car.Index == train.DriverCar ? CarSectionType.Interior : CarSectionType.NotVisible) : CarSectionType.Exterior);
            Program.Renderer.CameraTrackFollower.TrackIndex = train.Cars[train.DriverCar].FrontAxle.Follower.TrackIndex;
            train.Cars[train.DriverCar].UpdateCamera();
            World.UpdateAbsoluteCamera();
            Program.Renderer.UpdateVisibility(true);
            Console.WriteLine($"ROUNDTRIP_TURNAROUND: generation={generation}, direction={service.Direction}, front={service.FrontPosition:0.00}, rail={train.Cars[train.DriverCar].FrontAxle.Follower.TrackIndex}");
        }
    }
}
