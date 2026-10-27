using System;
using System.Linq;
using System.Reflection;
using OpenBveApi.Trains;
using TrainManager.Trains;

namespace OpenBve
{
    internal static class NativeStationJumpTests
    {
        private sealed class TrafficSnapshot
        {
            internal TrainBase Train;
            internal TrainState State;
            internal object AI;
            internal double[] Axles, Clock;
            internal bool[] Flags;
            internal double Appearance, FleetDeparture;

            internal static double[] Positions(TrainBase train) => train.Cars.SelectMany(c => new[] {
                c.FrontAxle.Follower.TrackPosition, c.RearAxle.Follower.TrackPosition,
                c.FrontBogie.FrontAxle.Follower.TrackPosition, c.FrontBogie.RearAxle.Follower.TrackPosition,
                c.RearBogie.FrontAxle.Follower.TrackPosition, c.RearBogie.RearAxle.Follower.TrackPosition
            }).ToArray();

            internal static double[] Timers(object ai) => ai is TrackFollowingObjectAI
                ? new[] { "TimeLastProcessed", "CurrentPosition", "CurrentMileage", "HeldTime", "InitialElapsed" }
                    .Select(n => (double)typeof(TrackFollowingObjectAI).GetField(n, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ai)).ToArray()
                : Array.Empty<double>();

            internal TrafficSnapshot(TrainBase train)
            {
                Train = train; State = train.State; AI = train.AI;
                Axles = Positions(train); Clock = Timers(AI);
                Flags = train is ScriptedTrain t ? new[] { t.AwaitingTrigger, t.AwaitingHandoff, t.WarmStartPending } : Array.Empty<bool>();
                Appearance = train is ScriptedTrain s ? s.AppearanceTime : 0;
                FleetDeparture = train is ScriptedTrain f && f.Fleet != null ? f.Fleet.NextDepartureTime : 0;
            }

            internal void Verify()
            {
                Check(Train.State == State && ReferenceEquals(Train.AI, AI) && Axles.SequenceEqual(Positions(Train)) && Clock.SequenceEqual(Timers(AI)),
                    "AI state, axles or service timeline changed on player jump");
                if (Train is ScriptedTrain t)
                    Check(Flags.SequenceEqual(new[] { t.AwaitingTrigger, t.AwaitingHandoff, t.WarmStartPending }) &&
                        t.AppearanceTime == Appearance && (t.Fleet == null ? 0 : t.Fleet.NextDepartureTime) == FleetDeparture,
                        "AI service flags or fleet departure changed on player jump");
            }
        }

        private static void Check(bool ok, string reason)
        {
            if (!ok) throw new InvalidOperationException("STATION_JUMP: " + reason);
        }

        internal static void Run()
        {
            Check(!CcoMode.Enabled, "use normal mode to test player repositioning");
            var player = TrainManager.PlayerTrain;
            var route = Program.CurrentRoute;
            var traffic = Program.TrainManager.Trains.Cast<TrainBase>().Concat(Program.TrainManager.TFOs.OfType<TrainBase>())
                .Where(t => !ReferenceEquals(t, player)).Select(t => new TrafficSnapshot(t)).ToArray();
            Check(traffic.Length > 0, "test route must contain scripted traffic");
            int target = Array.FindIndex(route.Stations, s => s.Name?.IndexOf("Manoel", StringComparison.OrdinalIgnoreCase) >= 0);
            Check(target >= 0, "test route must contain Manoel Lacerda");
            int original = Math.Max(0, player.Station);
            double time = route.SecondsSinceMidnight;
            double oldDeparture = route.Stations[target].DepartureTime;
            var ai = player.AI;
            bool radio = CabinAnnouncements.RadioEnabled;
            try {
                // A future timetable must not advance the clock or impose a long artificial dwell.
                route.Stations[target].DepartureTime = time + 3600;
                for (int cycle = 0; cycle < 2; cycle++) {
                    CcoMode.JumpToStation(target);
                    Check(route.SecondsSinceMidnight == time, "jump advanced the simulation clock");
                    Check(Math.Abs(player.FrontCarTrackPosition - route.Stations[target].GetStopPosition(player)) < 0.001,
                        "player did not reach the station stop");
                    Check(player.Station == target && player.AI == ai && CabinAnnouncements.RadioEnabled == radio,
                        "jump changed driving/radio mode or did not select the station");
                    foreach (var snapshot in traffic) snapshot.Verify();
                    typeof(TrainBase).GetMethod("UpdateStation", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, new object[] { 0.01 });
                    Check(player.StationState == TrainStopState.Boarding, "fresh station dwell did not start");
                    Check(Math.Abs(player.StationDepartureTime - time - route.Stations[target].StopTime) < 0.001,
                        "jump must use current-time dwell instead of the future timetable");
                    CcoMode.JumpToStation(original);
                    Check(route.SecondsSinceMidnight == time, "backward jump changed the clock");
                    foreach (var snapshot in traffic) snapshot.Verify();
                }
            } finally {
                route.Stations[target].DepartureTime = oldDeparture;
                route.SecondsSinceMidnight = time;
                CcoMode.JumpToStation(original);
            }
            Console.WriteLine($"STATION_JUMP_OK: forward/backward menu jumps preserve {traffic.Length} AI trains, axle positions, service timers, fleet state and clock; new station uses a fresh dwell.");
        }
    }
}
