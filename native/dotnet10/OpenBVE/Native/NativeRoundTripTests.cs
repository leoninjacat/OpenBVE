using System;
using System.Linq;
using System.Reflection;
using OpenBveApi.Routes;
using OpenBveApi.Trains;
using TrainManager.Car;
using TrainManager.Handles;
using TrainManager.Trains;

namespace OpenBve
{
    internal static class NativeRoundTripTests
    {
        private static TrainBase player;
        private static RoundTripService service;
        private static readonly MethodInfo StationUpdate = typeof(TrainBase).GetMethod("UpdateStation", BindingFlags.Instance | BindingFlags.NonPublic);
        
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException("ROUNDTRIP: " + message); }
        private static void Tick(double duration)
        {
            for (double elapsed = 0; elapsed < duration - 0.00001; elapsed += 0.05)
            {
                int beforeGeneration = service.Generation;
                Program.CurrentRoute.SecondsSinceMidnight += 0.05;
                StationUpdate.Invoke(player, new object[] { 0.05 });
                player.UpdateDoors(0.05);
                Game.CurrentScore.Update(0.05);
                RoundTripMode.Update();
                if (service.Generation != beforeGeneration)
                {
                    // Render every odd and even reversal, not just the final restored cab.
                    Program.TrainManager.UpdateTrainObjects(0, true);
                    Program.Renderer.RenderScene(0, 0);
                    Console.WriteLine("ROUNDTRIP_REVERSAL_RENDER_OK: " + service.Generation);
                }
            }
        }
        private static void WaitFor(Func<bool> condition, double limit, string reason)
        {
            for (double time = 0; !condition() && time < limit; time += 0.1) Tick(0.1);
            Check(condition(), reason + ": " + service.Status);
        }
        private static void MoveTo(double position)
        {
            int count = 0;
            while (Math.Abs(service.FrontPosition - position) > 0.001)
            {
                double d = position - service.FrontPosition;
                d = Math.Sign(d) * Math.Min(1, Math.Abs(d));
                service.UpdateInterlocking();
                foreach (var sw in Program.CurrentRoute.Switches.Values.Where(x => x.Locked))
                {
                    int setting = sw.CurrentlySetTrack;
                    sw.Toggle();
                    Check(sw.CurrentlySetTrack == setting, "occupied crossover could be switched");
                }
                foreach (var car in player.Cars) car.Move(d);
                Check(!player.Derailed && player.Cars.All(c => !c.Derailed), "derailment while crossing native switches");
                Check(++count < 10000, "movement did not converge");
            }
            service.UpdateInterlocking();
        }
        private static void Rail(int rail)
        {
            foreach (var c in player.Cars)
            {
                var axles = new[] { c.FrontAxle.Follower, c.RearAxle.Follower, c.FrontBogie.FrontAxle.Follower,
                    c.FrontBogie.RearAxle.Follower, c.RearBogie.FrontAxle.Follower, c.RearBogie.RearAxle.Follower };
                Check(axles.All(f => f.TrackIndex == rail), "one or more axles did not follow the switch to rail " + rail);
            }
        }
        internal static void PrepareReturnDeparture()
        {
            player = TrainManager.PlayerTrain; service = player.RoundTrip;
            Check(service != null, "use the IdaVolta test route");
            player.AI = null;
            player.CurrentSpeed = 0;
            foreach (var car in player.Cars) car.CurrentSpeed = 0;
            WaitFor(() => service.NextStation == 1, 60, "initial boarding");
            MoveTo(1290);
            WaitFor(() => service.NextStation == 2, 60, "central boarding");
            MoveTo(2490);
            WaitFor(() => service.Direction < 0, 80, "terminal countdown and reversal");
            player.Handles.Reverser.ApplyState(ReverserPosition.Forwards);
            player.Handles.EmergencyBrake.Release();
            player.Handles.Brake.ApplyState(0, false);
            player.Handles.HoldBrake.Driver = false;
            player.Handles.Power.ApplyState(2, false);
            Console.WriteLine("ROUNDTRIP_DEPARTURE_READY: reversed cab, normal render and physics loop, player controls set to F/P2.");
        }
        internal static void VerifyReturnDeparture()
        {
            Check(service.Direction < 0 && service.Generation == 1, "test must render the first return, not an even number of reversals");
            Check(service.FrontPosition < 2309 && player.CurrentSpeed < 0, "return did not physically leave the terminal");
            Check(player.DriverCar == player.Cars.Length - 1 && Program.Renderer.Camera.AbsoluteDirection.Z < -0.9,
                "rear cab is not looking in the return direction");
            Check(!player.Derailed, "return train derailed");
            Console.WriteLine($"ROUNDTRIP_DEPARTURE_OK: rendered reversed cab while driving, front={service.FrontPosition:0.00}, speed={player.CurrentSpeed:0.00}, cameraZ={Program.Renderer.Camera.AbsoluteDirection.Z:0.00}");
        }
        internal static void RunAutomatic(bool roundTrip)
        {
            player = TrainManager.PlayerTrain; service = player.RoundTrip;
            Check(roundTrip == (service != null), "test fixture itinerary mismatch");
            var seen = new System.Collections.Generic.HashSet<string>();
            if (!roundTrip) {
                var station = Program.CurrentRoute.Stations[1];
                Check(station.Stops.Any(s => s.RailIndex == 1), "parser did not preserve explicit stop rail");
                Check(station.GetStopIndex(player) == -1, "rail 1 stop incorrectly applies to rail 0");
            }
            player.AI = new Game.SimpleHumanDriverAI(player, 12);
            for(int step=0;step<30000;step++) {
                Program.CurrentRoute.SecondsSinceMidnight += .05;
                player.AI.Trigger(.05);
                player.Update(.05);
                service?.UpdateInterlocking();
                if (player.StationState == TrainStopState.Boarding) {
                    string visit = $"{service?.Generation ?? 0}:{player.Station}";
                    if(seen.Add(visit)) Console.WriteLine($"AUTO_STOP_VISIT: {visit}, rail={player.CurrentTrackIndex}, front={player.FrontCarTrackPosition:0.00}, speed={player.CurrentSpeed:0.000}");
                }
                if(roundTrip && service.Generation >= 2) {
                    Check(seen.Contains("0:1") && seen.Contains("1:1"), "AI skipped central station on one leg");
                    player.AI=null; RoundTripMode.Update();
                    Console.WriteLine("ROUNDTRIP_AI_OK: physical driving, switches, station dwell and two reversals"); return;
                }
                if(!roundTrip && player.Station==2 && player.StationState==TrainStopState.Boarding) {
                    Check(seen.Contains("0:1"), "AI skipped station on rail 1");
                    Check(Math.Abs(player.CurrentSpeed)<.1, "AI did not stop at final station");
                    player.AI=null;
                    Console.WriteLine("RAIL_STOPS_AI_OK: rail restriction, native switch traversal, intermediate boarding and terminal stop");return;
                }
                Check(!player.Derailed, "AI derailed during station test");
            }
            throw new Exception($"Automatic service timed out: position={player.FrontCarTrackPosition}, speed={player.CurrentSpeed}, station={player.Station}, state={player.StationState}, status={service?.Status}, rail={player.CurrentTrackIndex}, limit={player.CurrentRouteLimit}/{player.CurrentSectionLimit}, P={player.Handles.Power.Driver}/{player.Handles.Power.Actual}, B={player.Handles.Brake.Driver}/{player.Handles.Brake.Actual}, F={player.Handles.Reverser.Actual}");
        }
        internal static void Run()
        {
            player = TrainManager.PlayerTrain; service = player.RoundTrip;
            Check(service != null && Program.CurrentRoute.Stations.Length == 3, "use the IdaVolta test route");
            Check(Program.CurrentRoute.Switches.Count == 4, "expected four native switches");
            foreach (var log in Interface.GetLogSnapshot().Where(x => x.Type == OpenBveApi.Interface.MessageType.Error || x.FileNotFound)) Console.WriteLine("ROUNDTRIP_LOAD_DIAGNOSTIC: " + log.Text);
            player.AI = null;
            player.CurrentSpeed = 0;
            foreach (var c in player.Cars) c.CurrentSpeed = 0;
            var trackDirections = Program.CurrentRoute.Tracks.ToDictionary(p => p.Key, p => p.Value.Direction);
            int originalDriver = player.DriverCar;
            double initialClock = Program.CurrentRoute.SecondsSinceMidnight;
            for (int cycle = 0; cycle < 2; cycle++)
            {
                if (cycle == 0) { WaitFor(() => service.NextStation == 1, 60, "initial station dwell failed"); }
                MoveTo(1290); Rail(1);
                WaitFor(() => service.NextStation == 2, 60, "via 1 station was not served");
                MoveTo(2490); Rail(0);
                WaitFor(() => service.Remaining > 29, 20, "terminal boarding did not start");
                Check(service.Direction == 1 && service.Remaining > 20, "terminal countdown missing or premature reversal");
                Tick(15); Check(service.Direction == 1, "reversal before countdown expiry");
                if (cycle == 0)
                {
                    var blocker = new TrainBase(TrainState.Available, player.Type);
                    var blockCar = new CarBase(blocker, 0) { Length = 25 };
                    blocker.Cars = new[] { blockCar };
                    blockCar.FrontAxle.Follower.TrackPosition = 2340;
                    blockCar.FrontAxle.Follower.TrackIndex = 1;
                    blockCar.RearAxle.Follower.TrackIndex = 1;
                    Program.TrainManager.Trains.Add(blocker);
                    try
                    {
                        Tick(35);
                        Check(service.Direction == 1 && service.WaitingForTrack, "occupied return berth did not block repositioning");
                        Check(Math.Abs(service.FrontPosition - 2490) < 0.001, "player moved into occupied berth");
                    }
                    finally { Program.TrainManager.Trains.Remove(blocker); }
                }
                WaitFor(() => service.Direction == -1, 60, "return service did not start");
                Check(service.Direction == -1 && service.Generation == cycle * 2 + 1, "return service not activated");
                Check(Math.Abs(service.FrontPosition - 2310) < 0.001, "return nose not positioned at 2310 m");
                Check(player.DriverCar == player.Cars.Length - 1 - originalDriver, "loaded cab not moved to the other end");
                Rail(1);
                Check(player.CurrentDirection == TrackDirection.Reverse && Math.Abs(Program.Renderer.Camera.Alignment.Yaw - Math.PI) < 0.001, "cab or controls not oriented for return");
                Check(Program.Renderer.Camera.AbsolutePosition.Y > 1, "cab camera is on the rails after reversal");
                Check(player.CurrentSpeed == 0 && player.Handles.Power.Driver == 0 && player.Handles.Brake.Driver > 0, "reposition must be stopped and braked");
                player.Handles.Reverser.ApplyState(ReverserPosition.Forwards);
                Check(player.Handles.Reverser.Driver == ReverserPosition.Reverse, "F must drive toward decreasing mileage on return");
                if (cycle == 0)
                {
                    double beforeDriving = service.FrontPosition;
                    player.Handles.EmergencyBrake.Release();
                    player.Handles.Brake.ApplyState(0, false);
                    player.Handles.HoldBrake.Driver = false;
                    player.Handles.Power.ApplyState(player.Handles.Power.MaximumNotch, false);
                    for (int step = 0; step < 240; step++)
                    {
                        Program.CurrentRoute.SecondsSinceMidnight += 0.05;
                        player.Update(0.05);
                    }
                    Check(service.FrontPosition < beforeDriving - 0.2 && player.CurrentSpeed < 0,
                        $"actual return traction did not move backwards: speed={player.CurrentSpeed}, F={player.Handles.Reverser.Actual}, P={player.Handles.Power.Actual}, B={player.Handles.Brake.Actual}");
                    player.Handles.Power.ApplyState(0, false);
                    player.Handles.Power.Actual = 0;
                    player.CurrentSpeed = 0;
                    foreach (var car in player.Cars) car.CurrentSpeed = 0;
                    player.Handles.Brake.ApplyState(player.Handles.Brake.MaximumNotch, false);
                    Console.WriteLine("ROUNDTRIP_TRACTION_OK: normal F/P controls physically drove toward decreasing mileage after turnaround.");
                }
                player.Handles.Reverser.ApplyState(ReverserPosition.Neutral);
                MoveTo(1110); Rail(1);
                WaitFor(() => service.NextStation == 0, 60, "return station was not served");
                MoveTo(110); Rail(0);
                WaitFor(() => service.Direction == 1, 80, "new outbound service did not start");
                Check(service.Direction == 1 && service.Generation == cycle * 2 + 2, "new outbound service not activated");
                Check(Math.Abs(service.FrontPosition - 290) < 0.001 && player.DriverCar == originalDriver, "outbound reset did not restore cab and position");
                Rail(0);
            }
            Check(Program.CurrentRoute.SecondsSinceMidnight > initialClock, "clock was reset during turnaround");
            Check(trackDirections.All(p => Program.CurrentRoute.Tracks[p.Key].Direction == p.Value),
                "round trip changed global track direction");
            player.Handles.Reverser.ApplyState(ReverserPosition.Forwards);
            player.Handles.Reverser.Actual = player.Handles.Reverser.Driver;
            if (Environment.GetEnvironmentVariable("OPENBVE_TEST_EXTERIOR") == "1")
            {
                var camera = Program.Renderer.Camera;
                camera.CurrentMode = OpenBveApi.Runtime.CameraViewMode.Exterior;
                camera.Alignment = new LibRender2.Cameras.CameraAlignment(new OpenBveApi.Math.Vector3(-8, 5, -25), 0.15, -0.1, 0, service.FrontPosition, 0);
                foreach (var car in player.Cars) car.ChangeCarSection(LibRender2.Trains.CarSectionType.Exterior);
                player.Cars[player.DriverCar].UpdateCamera();
                World.UpdateAbsoluteCamera();
                Program.Renderer.UpdateVisibility(true);
            }
            Console.WriteLine("ROUNDTRIP_TEST_OK: two complete cycles, native axle/bogie switches in both directions, occupied-switch locking, via 1 stops, terminal countdowns, occupied-berth hold, cab relocation, F reverser mapping and continuous clock.");
        }
    }
}
