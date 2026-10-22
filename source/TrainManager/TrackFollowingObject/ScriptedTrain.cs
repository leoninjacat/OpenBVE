using System;
using System.Collections.Generic;
using System.Linq;
using LibRender2.Trains;
using OpenBveApi.Trains;
using TrainManager.Car;
using TrainManager.SafetySystems;

namespace TrainManager.Trains
{
	/// <summary>A scripted train, which follows a rail and a travel plan</summary>
	public class ScriptedTrain : TrainBase
	{
		/// <summary>The time the train appears in-game</summary>
		public double AppearanceTime;
		/// <summary>The track position at which the train appears</summary>
		public double AppearanceStartPosition;
		/// <summary>The track position at which the train disappears</summary>
		public double AppearanceEndPosition;
		/// <summary>The time at which the train is removed from the game</summary>
		public double LeaveTime;
		/// <summary>Seconds between appearances; zero disables reuse.</summary>
		public double RepeatInterval;
		/// <summary>Optional identity and terminal handoff for reusable services.</summary>
		public string ServiceId;
		public string NextService;
		public bool AwaitingTrigger;
		/// <summary>Requested travel duration in seconds; zero uses the original plan.</summary>
		public double JourneyTime;
		/// <summary>Real seconds to remain at the final position before handoff.</summary>
		public double CompletionWait;
		public Action JourneyCompleted;
		/// <summary>Concurrent instances preloaded from the same XML.</summary>
		public int FleetSize = 1;
		public int FleetSlot;
		public double SpawnInterval;
		public double BirthClearance = 50;
		public ScriptedServiceFleet Fleet;
		public Func<ScriptedTrain, bool> CanIntroduce;
		public bool ContinuousHandoff;
		public bool AwaitingHandoff;
		public ScriptedTrain HandoffFrom;
		public bool WarmStart;
		public bool WarmStartPending;
		public bool ProtectedTraffic;
		public bool PreferStationWaiting;
		public string ConflictZones;
		public ScriptedTrafficController TrafficControl;
		private double LastAppearanceTime;
		private double InternalTimerTimeElapsed;

		public ScriptedTrain(TrainState state) : base(state, TrainType.ScriptedTrain)
		{
			SafetySystems.PassAlarm = new PassAlarm(PassAlarmType.None, null);
		}

		/// <summary>Disposes of the train</summary>
		public override void Dispose()
		{
			State = TrainState.DisposePending;
			for (int i = 0; i < Cars.Length; i++)
			{
				Cars[i].ChangeCarSection(CarSectionType.NotVisible);
			}
			TrainManagerBase.currentHost.StopAllSounds(this);
			if (Fleet != null && string.IsNullOrWhiteSpace(NextService))
			{
				(AI as TrackFollowingObjectAI)?.ResetForRepeat();
				AppearanceTime = TrainManagerBase.currentHost.InGameTime;
				State = TrainState.Pending;
			}
			else if (RepeatInterval > 0 && AI is TrackFollowingObjectAI scriptedAI)
			{
				// Skip missed departures rather than producing a burst after a time jump.
				double now = TrainManagerBase.currentHost.InGameTime;
				AppearanceTime = LastAppearanceTime + RepeatInterval * Math.Max(1, Math.Floor((now - LastAppearanceTime) / RepeatInterval) + 1);
				scriptedAI.ResetForRepeat();
				InternalTimerTimeElapsed = 0;
				State = TrainState.Pending;
			}
		}

		/// <summary>Starts an inactive service after its predecessor has finished.</summary>
		public void ActivateService(ScriptedTrain predecessor = null)
		{
			if (State != TrainState.Pending || !AwaitingTrigger) return;
			(AI as TrackFollowingObjectAI)?.ResetForRepeat();
			AppearanceTime = TrainManagerBase.currentHost.InGameTime;
			HandoffFrom = predecessor;
			AwaitingTrigger = false;
		}

		/// <summary>Hides and parks this service, then activates the next one.</summary>
		public void CompleteJourney()
		{
			if (State != TrainState.Available || AwaitingHandoff) return;
			if (ContinuousHandoff)
			{
				AwaitingHandoff = true;
				CurrentSpeed = 0;
				foreach (var car in Cars) car.CurrentSpeed = 0;
				JourneyCompleted?.Invoke();
				return;
			}
			Dispose();
			(AI as TrackFollowingObjectAI)?.ResetForRepeat();
			AwaitingTrigger = true;
			State = TrainState.Pending;
			JourneyCompleted?.Invoke();
		}

		/// <summary>Atomically exchanges the visible terminal train with its successor.</summary>
		public void Introduce()
		{
			if (HandoffFrom != null)
			{
				var predecessor = HandoffFrom;
				predecessor.Dispose();
				(predecessor.AI as TrackFollowingObjectAI)?.ResetForRepeat();
				predecessor.AwaitingTrigger = true;
				predecessor.AwaitingHandoff = false;
				predecessor.State = TrainState.Pending;
				HandoffFrom = null;
			}
			WarmStartPending = false;
			LastAppearanceTime = TrainManagerBase.currentHost.InGameTime;
			State = TrainState.Available;
			foreach (var car in Cars)
			{
				car.ChangeCarSection(CarSectionType.Exterior);
				if (car.TractionModel.ProvidesPower && car.Sounds.Loop != null) car.Sounds.Loop.Play(car, true);
			}
		}

		/// <summary>Checks birth occupancy before reserving a fleet departure.</summary>
		public bool TryReserveDeparture(double now)
		{
			if (State != TrainState.Pending || AwaitingTrigger || now < AppearanceTime) return false;
			if (CanIntroduce != null && !CanIntroduce(this)) return false;
			if (ProtectedTraffic && TrafficControl != null && !TrafficControl.CanAdvance(this, 0)) return false;
			// A seed delayed by occupancy becomes a normal queued departure.
			if (WarmStartPending && now > AppearanceTime + 1) WarmStartPending = false;
			return WarmStartPending || Fleet == null || Fleet.TryDepart(this, now);
		}

		/// <summary>Checks complete car bodies on the birth rail, including mixed-rail cars.</summary>
		public bool IsBirthAreaFree(IEnumerable<AbstractTrain> trains)
		{
			foreach (AbstractTrain other in trains)
			{
				if (ReferenceEquals(this, other) || ReferenceEquals(HandoffFrom, other) || other.State != TrainState.Available || !(other is TrainBase otherTrain)) continue;
				foreach (CarBase ownCar in Cars)
				{
					double ownCenter = ownCar.FrontAxle.Follower.TrackPosition - ownCar.FrontAxle.Position;
					double low = ownCenter - ownCar.Length / 2 - BirthClearance;
					double high = ownCenter + ownCar.Length / 2 + BirthClearance;
					foreach (var otherCar in otherTrain.Cars)
					{
						int rail = ownCar.FrontAxle.Follower.TrackIndex;
						if (otherCar.FrontAxle.Follower.TrackIndex != rail && otherCar.RearAxle.Follower.TrackIndex != rail) continue;
						double center = otherCar.FrontAxle.Follower.TrackPosition - otherCar.FrontAxle.Position;
						if (center + otherCar.Length / 2 >= low && center - otherCar.Length / 2 <= high) return false;
					}
				}
			}
			return true;
		}

		/// <summary>Call this method to update the train</summary>
		/// <param name="TimeElapsed">The elapsed time this frame</param>
		public override void Update(double TimeElapsed)
		{
			if (State == TrainState.Pending)
			{
				// pending train
				if (AwaitingTrigger) return;
				if (TrainManagerBase.currentHost.InGameTime >= AppearanceTime)
				{
					double PlayerTrainTrackPosition = TrainManagerBase.PlayerTrain.Cars[0].FrontAxle.Follower.TrackPosition + 0.5 * TrainManagerBase.PlayerTrain.Cars[0].Length - TrainManagerBase.PlayerTrain.Cars[0].FrontAxle.Position;
					if (PlayerTrainTrackPosition < AppearanceStartPosition || (PlayerTrainTrackPosition > AppearanceEndPosition && AppearanceEndPosition > AppearanceStartPosition))
					{
						return;
					}

					if (!TryReserveDeparture(TrainManagerBase.currentHost.InGameTime)) return;

					Introduce();
				}
			}
			else if (State == TrainState.Available)
			{
				// available train
				UpdatePhysicsAndControls(TimeElapsed);
				for (int i = 0; i < Cars.Length; i++)
				{
					byte dnb;
					{
						float b = (float)(Cars[i].Brightness.NextTrackPosition - Cars[i].Brightness.PreviousTrackPosition);

						//1.0f represents a route brightness value of 255
						//0.0f represents a route brightness value of 0

						if (b != 0.0f)
						{
							b = (float)(Cars[i].FrontAxle.Follower.TrackPosition - Cars[i].Brightness.PreviousTrackPosition) / b;
							if (b < 0.0f) b = 0.0f;
							if (b > 1.0f) b = 1.0f;
							b = Cars[i].Brightness.PreviousBrightness * (1.0f - b) + Cars[i].Brightness.NextBrightness * b;
						}
						else
						{
							b = Cars[i].Brightness.PreviousBrightness;
						}

						//Calculate the cab brightness
						double ccb = Math.Round(255.0 * (1.0 - b));
						//DNB then must equal the smaller of the cab brightness value & the dynamic brightness value
						dnb = (byte)Math.Min(TrainManagerBase.Renderer.Lighting.DynamicCabBrightness, ccb);
					}
					
					if (Cars[i].CarSections.TryGetValue(Cars[i].CurrentCarSection, out CarSection currentCarSection) && currentCarSection.Groups.Length > 0)
					{
						for (int k = 0; k < currentCarSection.Groups[0].Elements.Length; k++)
						{
							if (currentCarSection.Groups[0].Elements[k].internalObject != null)
							{
								currentCarSection.Groups[0].Elements[k].internalObject.DaytimeNighttimeBlend = dnb;
							}
						}
					}

				}
				AI?.Trigger(TimeElapsed);
			}
			else if (State == TrainState.Bogus)
			{
				// bogus train
				AI?.Trigger(TimeElapsed);
			}
		}

		/// <summary>Updates the physics and controls for this train</summary>
		/// <param name="TimeElapsed">The time elapsed</param>
		private void UpdatePhysicsAndControls(double TimeElapsed)
		{
			if (TimeElapsed == 0.0 || TimeElapsed > 1000)
			{
				//HACK: The physics engine really does not like update times above 1000ms
				//This works around a bug experienced when jumping to a station on a steep hill
				//causing exessive acceleration
				return;
			}

			if (Type == TrainType.PreTrain)
			{
				UpdateStation(TimeElapsed);
			}
			// update station and doors
			UpdateDoors(TimeElapsed);

			// Update Run and Motor sounds
			foreach (CarBase Car in Cars)
			{
				Car.Run.Update(TimeElapsed);
				Car.TractionModel?.Update(TimeElapsed);
				for (int j = 0; j < Car.Sounds.ControlledSounds.Count; j++)
				{
					Car.Sounds.ControlledSounds[j].Update(TimeElapsed);
				}
			}

			// infrequent updates
			InternalTimerTimeElapsed += TimeElapsed;
			if (InternalTimerTimeElapsed > 10.0)
			{
				InternalTimerTimeElapsed -= 10.0;
				Synchronize();
			}
		}

		public override void Jump(int stationIndex, int trackIndex)
		{
			Dispose();
			State = TrainState.Pending;
			TrainManagerBase.currentHost.ProcessJump(this, stationIndex, 0);
		}
	}

	/// <summary>A bounded pool of concurrent compositions with a shared minimum headway.</summary>
	public sealed class ScriptedServiceFleet
	{
		public readonly List<ScriptedTrain> Members = new List<ScriptedTrain>();
		public readonly double Interval;
		public double NextDepartureTime { get; private set; } = double.NegativeInfinity;

		public ScriptedServiceFleet(double interval)
		{
			if (interval <= 0 || double.IsNaN(interval) || double.IsInfinity(interval)) throw new ArgumentOutOfRangeException(nameof(interval));
			Interval = interval;
		}

		public void SeedHeadway(double now, double youngestElapsed)
		{
			NextDepartureTime = now + Math.Max(0, Interval - youngestElapsed);
		}

		public void Add(ScriptedTrain train)
		{
			train.FleetSlot = Members.Count;
			train.Fleet = this;
			Members.Add(train);
		}

		public bool TryDepart(ScriptedTrain train, double now)
		{
			if (now < NextDepartureTime) return false;
			// Clear a visible terminal predecessor before admitting a fresh seed there.
			// Otherwise FIFO can deadlock: the seed waits for the predecessor, whose
			// successor is itself queued behind that seed.
			ScriptedTrain first = Members.Where(x => x.State == TrainState.Pending && !x.AwaitingTrigger && x.AppearanceTime <= now)
				.OrderBy(x => x.HandoffFrom == null ? 1 : 0).ThenBy(x => x.AppearanceTime).ThenBy(x => x.FleetSlot).FirstOrDefault();
			if (!ReferenceEquals(train, first)) return false;
			// Use actual departure, avoiding bursts after blockage or a clock jump.
			NextDepartureTime = now + Interval;
			return true;
		}
	}

	/// <summary>Local separation and exclusive terminal throats for scripted services.</summary>
	public sealed class ScriptedTrafficController
	{
		private sealed class Zone
		{
			internal double Low, High;
			internal HashSet<int> Rails;
			internal string Group;
			internal Reservation Lock;
		}
		private sealed class Reservation
		{
			internal ScriptedTrain Owner;
			internal bool Entered;
		}
		private readonly Func<IEnumerable<AbstractTrain>> Trains;
		private readonly List<Zone> Zones = new List<Zone>();
		public ScriptedTrafficController(Func<IEnumerable<AbstractTrain>> trains) { Trains = trains; }

		public void AddZone(double low, double high, IEnumerable<int> rails, string group = null)
		{
			if (double.IsNaN(low) || double.IsNaN(high) || double.IsInfinity(low) || double.IsInfinity(high) || high <= low) throw new ArgumentException("Invalid conflict zone");
			var indices = new HashSet<int>(rails);
			if (indices.Count == 0 || indices.Any(x => x < 0)) throw new ArgumentException("Invalid conflict rails");
			if (!Zones.Any(z => z.Low == low && z.High == high && z.Rails.SetEquals(indices) && z.Group == group))
			{
				var reservation = group == null ? new Reservation() : Zones.FirstOrDefault(z => z.Group == group)?.Lock ?? new Reservation();
				Zones.Add(new Zone { Low = low, High = high, Rails = indices, Group = group, Lock = reservation });
			}
		}

		private static double Center(CarBase car) => car.FrontAxle.Follower.TrackPosition - car.FrontAxle.Position;
		private static bool OnRail(CarBase car, int rail) => car.FrontAxle.Follower.TrackIndex == rail || car.RearAxle.Follower.TrackIndex == rail;
		private static bool Touches(TrainBase train, Zone zone, double delta = 0, double ahead = 0)
		{
			foreach (var car in train.Cars)
			{
				if (!zone.Rails.Contains(car.FrontAxle.Follower.TrackIndex) && !zone.Rails.Contains(car.RearAxle.Follower.TrackIndex)) continue;
				double center = Center(car);
				double low = Math.Min(center, center + delta) - car.Length / 2 + Math.Min(0, ahead);
				double high = Math.Max(center, center + delta) + car.Length / 2 + Math.Max(0, ahead);
				if (high >= zone.Low && low <= zone.High) return true;
			}
			return false;
		}

		/// <summary>Keep seeded traffic clear of throats, rather than materializing inside a manœuvre.</summary>
		public bool IsNearConflictZone(TrainBase train, double clearance)
		{
			return Zones.Any(z => train.Cars.Any(car =>
				(z.Rails.Contains(car.FrontAxle.Follower.TrackIndex) || z.Rails.Contains(car.RearAxle.Follower.TrackIndex)) &&
				Center(car) + car.Length / 2 >= z.Low - clearance && Center(car) - car.Length / 2 <= z.High + clearance));
		}

		public bool CanAdvance(ScriptedTrain train, double delta, double requestedSpeed = 0)
		{
			var others = Trains().OfType<TrainBase>().Where(x => x.State == TrainState.Available && !ReferenceEquals(x, train) && !ReferenceEquals(x, train.HandoffFrom)).ToArray();
			double speed = Math.Max(Math.Abs(train.CurrentSpeed), requestedSpeed);
			double margin = train.BirthClearance + speed * speed / (2 * 0.45);
			int direction = Math.Sign(delta);
			foreach (var own in train.Cars)
			{
				double center = Center(own);
				double low = Math.Min(center, center + delta) - own.Length / 2;
				double high = Math.Max(center, center + delta) + own.Length / 2;
				foreach (var other in others)
				foreach (var car in other.Cars)
				{
					if (!OnRail(car, own.FrontAxle.Follower.TrackIndex) && !OnRail(car, own.RearAxle.Follower.TrackIndex)) continue;
					double c = Center(car), left = c - car.Length / 2, right = c + car.Length / 2;
					if (direction != 0 && train.AI is TrackFollowingObjectAI itinerary)
					{
						double obstacle = direction > 0 ? left : right;
						if (!itinerary.UsesRailAtPosition(own.FrontAxle.Follower.TrackIndex, obstacle) && !itinerary.UsesRailAtPosition(own.RearAxle.Follower.TrackIndex, obstacle)) continue;
					}

					if (direction > 0 && right >= center && left <= high + margin && right >= low) return false;
					if (direction < 0 && left <= center && right >= low - margin && left <= high) return false;
					if (direction == 0 && right >= low && left <= high) return false;
				}
			}
			return TryReserveZones(train, others, zone => Touches(train, zone, delta, direction * margin));
		}

		/// <summary>Checks the complete next interstation, including the destination platform.</summary>
		public bool CanDepartStation(ScriptedTrain train, TravelStopData origin, TravelStopData destination, TravelData[] path)
		{
			var others = Trains().OfType<TrainBase>().Where(x => x.State == TrainState.Available && !ReferenceEquals(x, train) && !ReferenceEquals(x, train.HandoffFrom)).ToArray();
			int direction = (int)origin.Direction;
			double low = Math.Min(origin.Position, destination.Position) - train.Length - train.BirthClearance;
			double high = Math.Max(origin.Position, destination.Position) + train.BirthClearance;
			foreach (var other in others)
			foreach (var car in other.Cars)
			{
				double center = Center(car), left = center - car.Length / 2, right = center + car.Length / 2;
				if (right < low || left > high) continue;
				// Do not mistake a train on the far side of a switch for one on our itinerary.
				double obstacle = Math.Max(Math.Min(origin.Position, destination.Position), Math.Min(Math.Max(origin.Position, destination.Position), direction > 0 ? left : right));
				if (train.AI is TrackFollowingObjectAI itinerary &&
				    (itinerary.UsesRailAtPosition(car.FrontAxle.Follower.TrackIndex, obstacle) || itinerary.UsesRailAtPosition(car.RearAxle.Follower.TrackIndex, obstacle))) return false;
			}
			bool UsesZone(Zone zone)
			{
				if (Touches(train, zone)) return true;
				for (int i = 0; i < path.Length - 1; i++)
				{
					double start = Math.Min(path[i].Position, path[i + 1].Position) - train.Length;
					double end = Math.Max(path[i].Position, path[i + 1].Position);
					if (end >= zone.Low && start <= zone.High && zone.Rails.Contains(path[i].RailIndex)) return true;
				}
				return false;
			}
			return TryReserveZones(train, others, UsesZone);
		}

		private bool TryReserveZones(ScriptedTrain train, TrainBase[] others, Func<Zone, bool> requests)
		{
			var requested = new List<Reservation>();
			foreach (var reservation in Zones.Select(z => z.Lock).Distinct())
			{
				var sections = Zones.Where(z => ReferenceEquals(z.Lock, reservation)).ToArray();
				bool Inside(TrainBase t) => sections.Any(z => Touches(t, z));
				if (reservation.Owner != null)
				{
					if (reservation.Owner.State != TrainState.Available || (reservation.Entered && !Inside(reservation.Owner))) { reservation.Owner = null; reservation.Entered = false; }
					else if (Inside(reservation.Owner)) reservation.Entered = true;
				}
				if (!sections.Any(requests)) continue;
				if (reservation.Owner != null && !ReferenceEquals(reservation.Owner, train) && !ReferenceEquals(reservation.Owner, train.HandoffFrom)) return false;
				if (others.Any(x => Inside(x))) return false;
				requested.Add(reservation);
			}
			// A throat and its destination platform are acquired as one atomic route.
			foreach (var reservation in requested)
			{
				bool inside = Zones.Any(z => ReferenceEquals(z.Lock, reservation) && Touches(train, z));
				reservation.Entered = ReferenceEquals(reservation.Owner, train) ? reservation.Entered || inside : inside;
				reservation.Owner = train;
			}
			return true;
		}
	}

}
