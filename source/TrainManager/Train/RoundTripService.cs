using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using OpenBveApi.Routes;
using OpenBveApi.Runtime;
using OpenBveApi.Trains;
using TrainManager.Handles;

namespace TrainManager.Trains
{
    /// <summary>Opt-in player service over existing tracks, without changing global track directions.</summary>
    public sealed class RoundTripService
    {
        public sealed class Stop { public int Station, Rail; public double Position, Dwell; public bool Left; }
        private sealed class Throat { public double Start, End; public int[] Rails; }
        private readonly TrainBase train;
        private readonly Func<IEnumerable<TrainBase>> traffic;
        private readonly Stop[][] legs;
        private readonly Throat[] throats;
        private int leg, next;
        private double arrival = double.NaN;
        private bool closing;
        public int Direction => leg == 0 ? 1 : -1;
        public int Generation { get; private set; }
        public int NextStation => Target.Station;
        public string Status { get; private set; }
        public double Remaining { get; private set; }
        public bool WaitingForTrack { get; private set; }
        public bool IsDwelling => !double.IsNaN(arrival) || closing;
        public double FrontPosition => Direction > 0 ? Bounds(train).Item2 : Bounds(train).Item1;
        public double RearPosition => Direction > 0 ? Bounds(train).Item1 : Bounds(train).Item2;
        public Stop Target => legs[leg][next];
        public bool PlatformLeft => Direction > 0 ? Target.Left : !Target.Left;
        private RoundTripService(TrainBase player, Func<IEnumerable<TrainBase>> all, Stop[][] services, Throat[] zones)
        {
            train = player; traffic = all; legs = services; throats = zones;
            Status = "Ida: " + Name(legs[0].Last());
        }
        public static RoundTripService Load(string path, TrainBase player, Func<IEnumerable<TrainBase>> all)
        {
            var root = XDocument.Load(path).Root;
            if (root == null || root.Name != "RoundTrip") throw new FormatException("Expected RoundTrip root.");
            var services = new[] { "Outbound", "Return" }.Select(name => {
                var element = root.Element(name);
                if (element == null) throw new FormatException("Missing " + name);
                return element.Elements("Stop").Select(s => new Stop {
                    Station = Integer(s, "station"), Rail = Integer(s, "rail"),
                    Position = Number(s, "position"), Dwell = Number(s, "dwell"),
                    Left = (string)s.Attribute("doors") == "L"
                }).ToArray();
            }).ToArray();
            for (int l = 0; l < 2; l++)
            {
                if (services[l].Length < 2) throw new FormatException("Each service needs two stops.");
                for (int i = 0; i < services[l].Length; i++)
                {
                    var s = services[l][i];
                    if (s.Station < 0 || s.Station >= TrainManagerBase.CurrentRoute.Stations.Length ||
                        !TrainManagerBase.CurrentRoute.Tracks.ContainsKey(s.Rail) || s.Dwell < 0 || s.Position < 0 ||
                        (i > 0 && (s.Position - services[l][i - 1].Position) * (l == 0 ? 1 : -1) <= 0))
                        throw new FormatException("Invalid round-trip stop or stop order.");
                }
            }
            var zones = root.Elements("Throat").Select(z => new Throat {
                Start = Number(z, "start"), End = Number(z, "end"),
                Rails = ((string)z.Attribute("rails") ?? "").Split(',').Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray()
            }).ToArray();
            if (zones.Any(z => z.Start >= z.End || z.Rails.Any(r => !TrainManagerBase.CurrentRoute.Tracks.ContainsKey(r))))
                throw new FormatException("Invalid switch throat.");
            return new RoundTripService(player, all, services, zones);
        }
        private static double Number(XElement e, string a)
        {
            double v = double.Parse((string)e.Attribute(a) ?? "", CultureInfo.InvariantCulture);
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new FormatException("Non-finite " + a);
            return v;
        }
        private static int Integer(XElement e, string a) => int.Parse((string)e.Attribute(a) ?? "", CultureInfo.InvariantCulture);
        private static string Name(Stop s) => TrainManagerBase.CurrentRoute.Stations[s.Station].Name;
        public static Tuple<double, double> Bounds(TrainBase t)
        {
            double low = double.PositiveInfinity, high = double.NegativeInfinity;
            foreach (var c in t.Cars)
            {
                double center = c.FrontAxle.Follower.TrackPosition - c.FrontAxle.Position;
                low = Math.Min(low, center - c.Length / 2); high = Math.Max(high, center + c.Length / 2);
            }
            return Tuple.Create(low, high);
        }
        public static IEnumerable<TrackFollower> Followers(TrainBase t)
        {
            foreach (var c in t.Cars)
            {
                yield return c.FrontAxle.Follower; yield return c.RearAxle.Follower;
                yield return c.FrontBogie.FrontAxle.Follower; yield return c.FrontBogie.RearAxle.Follower;
                yield return c.RearBogie.FrontAxle.Follower; yield return c.RearBogie.RearAxle.Follower;
                yield return c.BeaconReceiver;
            }
        }
        private bool Occupied(int[] rails, double low, double high, bool includePlayer)
        {
            return traffic().Any(t => (includePlayer || t != train) && t.State == TrainState.Available && t.Cars.Length > 0 &&
                Followers(t).Any(f => f.Car != null && rails.Contains(f.TrackIndex)) && Bounds(t).Item1 < high && Bounds(t).Item2 > low);
        }
        public void UpdateInterlocking()
        {
            foreach (var zone in throats)
            {
                bool occupied = Occupied(zone.Rails, zone.Start - 10, zone.End + 10, true);
                foreach (var sw in TrainManagerBase.CurrentRoute.Switches.Values)
                    if (sw.TrackPosition >= zone.Start && sw.TrackPosition <= zone.End) sw.Locked = occupied;
            }
        }
        /// <summary>Called after movement, before existing door animation.</summary>
        public void Update(double elapsed)
        {
            var stop = Target;
            double distance = Direction * (stop.Position - FrontPosition);
            train.StationDistanceToStopPoint = distance;
            bool aligned = train.Cars.All(c => c.FrontAxle.Follower.TrackIndex == stop.Rail && c.RearAxle.Follower.TrackIndex == stop.Rail);
            bool stopped = Math.Abs(train.CurrentSpeed) < 0.05 && train.Cars.All(c => Math.Abs(c.CurrentSpeed) < 0.05);
            train.Station = distance < 250 && distance > -30 && aligned ? stop.Station : -1;
            if (double.IsNaN(arrival))
            {
                train.StationState = TrainStopState.Pending;
                Status = (Direction > 0 ? "Ida" : "Volta") + " | Proxima: " + Name(stop) + $" ({distance:0} m)";
                if (!aligned || !stopped || Math.Abs(distance) > 5 || train.Derailed) return;
                bool left = PlatformLeft;
                train.OpenDoors(left, !left);
                if (train.Cars.Any(c => c.Doors[left ? 0 : 1].State < 0.9)) return;
                arrival = TrainManagerBase.CurrentRoute.SecondsSinceMidnight;
                train.StationArrivalTime = arrival; train.StationDepartureTime = arrival + stop.Dwell;
                train.LastStation = stop.Station;
            }
            bool terminal = next == legs[leg].Length - 1;
            if (!aligned || !stopped || Math.Abs(distance) > 5)
            {
                arrival = double.NaN; closing = false; Remaining = 0; return;
            }
            train.StationState = TrainStopState.Boarding;
            if (terminal) Hold();
            Remaining = Math.Max(0, arrival + stop.Dwell - TrainManagerBase.CurrentRoute.SecondsSinceMidnight);
            Status = terminal ? $"{Name(stop)} | Viagem de {(Direction > 0 ? "volta" : "ida")} em {Math.Ceiling(Remaining):00} s"
                : $"{Name(stop)} | Embarque: {Math.Ceiling(Remaining):00} s";
            if (Remaining > 0) return;
            if (!closing) { train.CloseDoors(true, true); closing = true; }
            if (train.Cars.Any(c => c.Doors.Any(d => d.State > 0))) { Status = "Fechando portas..."; return; }
            if (terminal)
            {
                var origin = legs[1 - leg][0];
                double length = Bounds(train).Item2 - Bounds(train).Item1;
                double low = Direction > 0 ? origin.Position : origin.Position - length;
                WaitingForTrack = Occupied(new[] { origin.Rail }, low - 10, low + length + 10, false);
                if (WaitingForTrack) { Status = "Aguardando liberacao da via de partida..."; return; }
                TurnAround(origin, length);
            }
            else
            {
                next++; arrival = double.NaN; closing = false;
                train.StationState = TrainStopState.Completed;
                Status = "Partida autorizada | Destino: " + Name(legs[leg].Last());
            }
        }
        private void Hold()
        {
            train.Handles.Power.ApplyState(0, false);
            train.Handles.Brake.ApplyState(train.Handles.Brake.MaximumNotch, false);
            if (train.Handles.Brake is AirBrakeHandle) train.Handles.Brake.ApplyState(AirBrakeHandleState.Service);
        }
        private void TurnAround(Stop origin, double length)
        {
            // Reuse the loaded cab and train. Do not fire events along the repositioning path.
            var old = Followers(train).ToDictionary(f => f, f => f.TriggerType);
            foreach (var f in old.Keys) f.TriggerType = EventTriggerType.None;
            try
            {
                train.Plugin?.BeginJump((InitializationModes)TrainManagerBase.CurrentOptions.TrainStart);
                train.Reverse(true, true);
                leg = 1 - leg;
                train.PlaceCars(origin.Position + (Direction < 0 ? length : 0));
                foreach (var f in Followers(train))
                {
                    f.TrackIndex = origin.Rail; f.LastTrackElement = 0; f.UpdateWorldCoordinates(false);
                }
                train.Plugin?.EndJump();
            }
            finally { foreach (var p in old) p.Key.TriggerType = p.Value; }
            train.CurrentSpeed = 0;
            foreach (var car in train.Cars) { car.CurrentSpeed = 0; car.Specs.PerceivedSpeed = 0; }
            train.Handles.Reverser.ApplyState(ReverserPosition.Neutral);
            Hold();
            train.Station = origin.Station; train.LastStation = origin.Station;
            train.StationState = TrainStopState.Completed;
            next = 1; arrival = double.NaN; closing = false; WaitingForTrack = false;
            Status = (Direction < 0 ? "Volta" : "Ida") + " pronta | Selecione F e libere os freios | Destino: " + Name(legs[leg].Last());
            Generation++;
        }
    }
}
