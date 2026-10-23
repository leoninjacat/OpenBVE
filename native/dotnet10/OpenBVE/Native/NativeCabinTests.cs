using System;
using OpenBveApi.Trains;
using TrainManager.Car;
using TrainManager.Trains;

namespace OpenBve
{
    /// <summary>Opt-in checks against the loaded route, sound decoder and actual cab source.</summary>
    internal static class NativeCabinTests
    {
        private static void Check(bool result, string message)
        {
            if (!result) throw new InvalidOperationException("CABIN: " + message);
        }

        private static void TestNormalMap()
        {
            var player = TrainManager.PlayerTrain;
            var ai = player.AI;
            double time = Program.CurrentRoute.SecondsSinceMidnight;
            double position = player.FrontCarTrackPosition;
            var mode = Program.Renderer.Camera.CurrentMode;
            var alignment = Program.Renderer.Camera.Alignment;
            bool radio = CabinAnnouncements.RadioEnabled;
            var traffic = Program.TrainManager.TFOs.ToArray();
            var positions = Array.ConvertAll(traffic, t => t.FrontCarTrackPosition);
            var states = Array.ConvertAll(traffic, t => t.State);
            Check(CcoMode.View == CcoMode.TrafficView.Hidden, "normal driving starts with map hidden");
            var key = new OpenTK.Input.KeyboardKeyEventArgs();
            typeof(OpenTK.Input.KeyboardKeyEventArgs).GetProperty("Key").SetValue(key, OpenTK.Input.Key.F7);
            foreach (var expected in new[] { CcoMode.TrafficView.Curves, CcoMode.TrafficView.Diagram, CcoMode.TrafficView.Hidden }) {
                MainLoop.KeyDownEvent(null, key);
                MainLoop.KeyDownEvent(null, key);
                Check(CcoMode.View == expected, "normal F7 cycles once per press");
                MainLoop.KeyUpEvent(null, key);
            }
            Check(!CcoMode.Enabled && player.AI == ai && time == Program.CurrentRoute.SecondsSinceMidnight &&
                position == player.FrontCarTrackPosition && Program.Renderer.Camera.CurrentMode == mode &&
                ReferenceEquals(alignment, Program.Renderer.Camera.Alignment) && CabinAnnouncements.RadioEnabled == radio,
                "normal map preserves camera, manual control, radio, clock and player");
            for (int j = 0; j < traffic.Length; j++)
                Check(traffic[j].FrontCarTrackPosition == positions[j] && traffic[j].State == states[j], "F7 preserves all AI traffic");
            Console.WriteLine("NORMAL_MAP_OK: F7 hidden/map/diagram cycle, key repeat blocked; camera, clock, player, AI traffic and radio preserved.");
        }

        internal static void Run()
        {
            if (!CcoMode.Enabled) TestNormalMap();
            var state = new CabinAnnouncements.DepartureState();
            Check(state.Update(0, true, true, false) == CabinAnnouncements.Advisory.Wait, "occupied departure announces wait");
            Check(state.Update(1, true, true, true) == CabinAnnouncements.Advisory.None, "wait is not repeated each frame");
            Check(state.Update(2, false, true, true) == CabinAnnouncements.Advisory.None, "release is debounced");
            Check(state.Update(2.5, false, false, true) == CabinAnnouncements.Advisory.None, "unknown or red signal cannot release");
            Check(state.Update(3, false, true, true) == CabinAnnouncements.Advisory.None, "unknown state resets debounce");
            Check(state.Update(4, false, true, true) == CabinAnnouncements.Advisory.Clear, "stable clear announces release");
            Check(state.Update(5, false, true, true) == CabinAnnouncements.Advisory.None, "clear is not repeated");
            Check(state.Update(6, true, true, true) == CabinAnnouncements.Advisory.Wait, "new obstruction revokes clear");
            var early = new CabinAnnouncements.DepartureState();
            early.Update(0, false, true, false);
            Check(early.Update(2, false, true, false) == CabinAnnouncements.Advisory.None, "no premature departure prompt");
            Check(early.Update(3, false, true, true) == CabinAnnouncements.Advisory.Clear, "prompt after scheduled stop time expires");

            var player = TrainManager.PlayerTrain;
            var other = new ScriptedTrain(TrainState.Available);
            other.Cars = new[] { new CarBase(other, 0) };
            other.Cars[0].Length = 20;
            var front = other.Cars[0].FrontAxle.Follower;
            var rear = other.Cars[0].RearAxle.Follower;
            front.TrackIndex = rear.TrackIndex = 0;
            front.TrackPosition = 500 + other.Cars[0].FrontAxle.Position;
            Check(CabinAnnouncements.Obstructs(other, player, 0, 200, 700, 1), "same rail train ahead blocks");
            Check(!CabinAnnouncements.Obstructs(other, player, 1, 200, 700, 1), "adjacent line does not block");
            Check(!CabinAnnouncements.Obstructs(other, player, 0, 600, 700, 1), "train behind does not block");
            Check(CabinAnnouncements.Obstructs(other, player, 0, 700, 200, -1), "reverse circulation checks behind in mileage");
            Check(!CabinAnnouncements.Obstructs(other, player, 0, 200, 400, 1), "outside departure segment ignored");
            other.State = TrainState.Pending;
            Check(!CabinAnnouncements.Obstructs(other, player, 0, 200, 700, 1), "pending services ignored");

            Check(CabinAnnouncements.RadioEnabled && CabinAnnouncements.RadioAudioReady, "actual CCO WAVs decode");
            bool cameraMode = CcoMode.Enabled;
            var ai = player.AI;
            CabinAnnouncements.ToggleRadio();
            Check(!CabinAnnouncements.RadioEnabled && player.AI == ai && CcoMode.Enabled == cameraMode,
                "radio toggle must preserve manual driving, AI and camera mode");
            CabinAnnouncements.ToggleRadio();

            int oldStation = player.Station;
            var oldState = player.StationState;
            double oldSpeed = player.CurrentSpeed, oldDeparture = player.StationDepartureTime;
            double oldTime = Program.CurrentRoute.SecondsSinceMidnight;
            try {
                int gatos = Array.FindIndex(Program.CurrentRoute.Stations,
                    s => string.Equals(s.Name?.Trim(), "Gatos", StringComparison.OrdinalIgnoreCase));
                Check(gatos >= 0, "test route contains Gatos");
                player.Station = gatos;
                player.StationState = TrainStopState.Boarding;
                player.CurrentSpeed = 0;
                player.StationDepartureTime = Program.CurrentRoute.SecondsSinceMidnight + 60;
                int before = CabinAnnouncements.StationClipsPlayed;
                CabinAnnouncements.Update();
                Check(CabinAnnouncements.StationClipsPlayed == before + 1, "player station arrival announces once");
                var source = CabinAnnouncements.CurrentSource;
                Check(source != null && ReferenceEquals(source.Parent, player.Cars[player.DriverCar]), "announcement attached to driver car");
                Check(source.Buffer.Duration > 0 && source.IsPlaying(), "actual station WAV decodes and queues for playback");
                string railway = Program.FileSystem.GetRailwayFolder(Program.CurrentRoute.Information.RouteFile, AppDomain.CurrentDomain.BaseDirectory);
                var destinationClip = Program.Sounds.RegisterBuffer(System.IO.Path.Combine(railway, "Sound", "Mobitrens", "Anuncios", "destino_cachorros.wav"), 10);
                Check(ReferenceEquals(source.Buffer, destinationClip), "Gatos cab uses destino_cachorros.wav instead of announce_gatos.wav");
                CabinAnnouncements.Update();
                Check(CabinAnnouncements.StationClipsPlayed == before + 1, "boarding cannot repeat station announcement");
                Program.Sounds.StopSound(source);
                other.State = TrainState.Available;
                front.TrackIndex = rear.TrackIndex = player.Cars[0].FrontAxle.Follower.TrackIndex;
                double nextStop = Program.CurrentRoute.Stations[gatos + 1].GetStopPosition(player);
                front.TrackPosition = nextStop + other.Cars[0].FrontAxle.Position;
                Check(CabinAnnouncements.Obstructs(other, player, front.TrackIndex,
                    player.FrontCarTrackPosition, nextStop, 1), "train stopped exactly at next station blocks departure");
                Program.TrainManager.TFOs.Add(other);
                CabinAnnouncements.Update();
                Check(CabinAnnouncements.RadioClipsPlayed == 0, "occupied track cannot trigger radio during dwell");
                Program.CurrentRoute.SecondsSinceMidnight = player.StationDepartureTime + 0.1;
                CabinAnnouncements.Update();
                Check(CabinAnnouncements.RadioClipsPlayed == 1, "wait WAV starts only after stop time expires");
                Check(CabinAnnouncements.CurrentSource.Buffer.Duration > 0 &&
                    ReferenceEquals(CabinAnnouncements.CurrentSource.Parent, player.Cars[player.DriverCar]),
                    "actual CCO wait WAV plays from driver car");
            } finally {
                Program.TrainManager.TFOs.Remove(other);
                Program.CurrentRoute.SecondsSinceMidnight = oldTime;
                player.Station = oldStation;
                player.StationState = oldState;
                player.CurrentSpeed = oldSpeed;
                player.StationDepartureTime = oldDeparture;
                CabinAnnouncements.Initialize();
            }
            Check(player.AI == ai && CcoMode.Enabled == cameraMode, "tests preserve driving mode");
            Console.WriteLine("CABIN_TEST_OK: Gatos destination WAV, occupied next station, dwell-time gate, red-signal debounce, rail filtering and driver-car audio.");
        }
    }
}
