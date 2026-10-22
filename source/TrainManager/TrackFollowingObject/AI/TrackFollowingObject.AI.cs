//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2025, S520, The OpenBVE Project
//
//Redistribution and use in source and binary forms, with or without
//modification, are permitted provided that the following conditions are met:
//
//1. Redistributions of source code must retain the above copyright notice, this
//   list of conditions and the following disclaimer.
//2. Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution.
//
//THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
//ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
//WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
//DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
//ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
//(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
//LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
//ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
//(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
//SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using System;
using System.Linq;
using OpenBveApi;
using OpenBveApi.Interface;
using OpenBveApi.Motor;
using OpenBveApi.Routes;
using OpenBveApi.Trains;
using TrainManager.Car;
using TrainManager.Handles;
using TrainManager.Motor;

namespace TrainManager.Trains
{
	/// <summary>The AI class used to operate a TFO</summary>
	public class TrackFollowingObjectAI : GeneralAI
	{
		private readonly ScriptedTrain Train;

		// Time to operate the door
		private readonly double OpeningDoorsTime;
		private readonly double ClosingDoorsTime;

		/// <summary>Travel plan of train</summary>
		private readonly TravelData[] Data;

		private double AppearanceTime;
		private double LeaveTime;

		private double TimeLastProcessed;
		private double CurrentPosition;
		private double CurrentMileage;
		private TravelDirection CurrentDirection;
		private double HeldTime;
		private double InitialElapsed;
		public double WaitingStationPosition { get; private set; } = double.NaN;
		public double JourneyDuration => Train.JourneyTime > 0 ? Train.JourneyTime : Data[Data.Length - 1].ArrivalTime;

		public TrackFollowingObjectAI(ScriptedTrain train, TravelData[] data)
		{
			Train = train;
			Data = data;
			TimeLastProcessed = double.NaN;

			if (Train.Cars[0].Specs.DoorOpenFrequency != 0.0)
			{
				OpeningDoorsTime = 1.0 / Train.Cars[0].Specs.DoorOpenFrequency;
			}

			if (Train.Cars[0].Specs.DoorCloseFrequency != 0.0)
			{
				ClosingDoorsTime = 1.0 / Train.Cars[0].Specs.DoorCloseFrequency;
			}

			SetupTravelData();
			CheckTravelData();
			CurrentDirection = ((TravelStopData)Data[0]).Direction;
		}

		/// <summary>Rewinds a hidden composition without rebuilding its travel plan.</summary>
		public void ResetForRepeat()
		{
			TimeLastProcessed = double.NaN;
			HeldTime = 0;
			WaitingStationPosition = double.NaN;
			InitialElapsed = 0;
			CurrentPosition = Data[0].Position;
			CurrentMileage = 0;
			CurrentDirection = ((TravelStopData)Data[0]).Direction;
			Train.CurrentSpeed = 0;
			Train.Specs.CurrentAverageAcceleration = 0;
			Train.PlaceCars(CurrentPosition);
			foreach (CarBase car in Train.Cars)
			{
				car.CurrentSpeed = 0;
				car.Specs.PerceivedSpeed = 0;
				car.Specs.Acceleration = 0;
				car.FrontAxle.Follower.TrackIndex = Data[0].RailIndex;
				car.RearAxle.Follower.TrackIndex = Data[0].RailIndex;
				car.FrontBogie.FrontAxle.Follower.TrackIndex = Data[0].RailIndex;
				car.FrontBogie.RearAxle.Follower.TrackIndex = Data[0].RailIndex;
				car.RearBogie.FrontAxle.Follower.TrackIndex = Data[0].RailIndex;
				car.RearBogie.RearAxle.Follower.TrackIndex = Data[0].RailIndex;
				foreach (Door door in car.Doors)
				{
					door.State = 0;
					door.AnticipatedOpen = false;
					door.AnticipatedReopen = false;
					door.DoorLockDuration = 0;
				}
			}
		}

		/// <summary>Rail the itinerary will use at an obstacle, after upcoming switches.</summary>
		public bool UsesRailAtPosition(int rail, double position)
		{
			double mileage = CurrentMileage + (int)CurrentDirection * (position - CurrentPosition);
			return (Data.LastOrDefault(x => x.Mileage <= mileage)?.RailIndex ?? Data[0].RailIndex) == rail;
		}

		private double PlanTime(double elapsed)
		{
			double arrival = Data[Data.Length - 1].ArrivalTime;
			return Train.JourneyTime > 0 && arrival > 0 ?
				(elapsed < Train.JourneyTime ? elapsed * arrival / Train.JourneyTime : arrival + elapsed - Train.JourneyTime) : elapsed;
		}

		private double ElapsedAtPlanTime(double time)
		{
			double arrival = Data[Data.Length - 1].ArrivalTime;
			return Train.JourneyTime > 0 && arrival > 0 ?
				(time < arrival ? time * Train.JourneyTime / arrival : Train.JourneyTime + time - arrival) : time;
		}

		private double WaitForStationExit(double elapsed, double frameTime)
		{
			WaitingStationPosition = double.NaN;
			if (!Train.PreferStationWaiting || !Train.ProtectedTraffic || Train.TrafficControl == null) return elapsed;
			double before = PlanTime(Math.Max(0, elapsed - frameTime));
			double after = PlanTime(elapsed);
			for (int i = 0; i < Data.Length - 1; i++)
			{
				if (!(Data[i] is TravelStopData station) || before > station.DepartureTime + 0.000001 || after < station.ClosingDoorsStartTime) continue;
				int next = i + 1;
				while (next < Data.Length && !(Data[next] is TravelStopData)) next++;
				if (next == Data.Length) continue;
				var destination = (TravelStopData)Data[next];
				if (Train.TrafficControl.CanDepartStation(Train, station, destination, Data.Skip(i).Take(next - i + 1).ToArray())) continue;
				// Retain the last stationary instant with the doors open. On release,
				// the original door-closing and acceleration sequence continues normally.
				double heldElapsed = ElapsedAtPlanTime(station.ClosingDoorsStartTime);
				HeldTime += Math.Max(0, elapsed - heldElapsed);
				WaitingStationPosition = station.Position;
				return Math.Min(elapsed, heldElapsed);
			}
			return elapsed;
		}

		/// <summary>Rewinds seeded traffic to its last actual stop, avoiding queues in open line.</summary>
		public double PrepareAtPreviousStation(double elapsed)
		{
			double time = PlanTime(elapsed);
			var station = Data.OfType<TravelStopData>().LastOrDefault(x => x.ArrivalTime < time - 0.000001);
			double age = station == null ? 0 : ElapsedAtPlanTime(station.ArrivalTime);
			PrepareAtElapsed(age);
			return age;
		}

		/// <summary>Places initial traffic at its actual point along the itinerary before rendering.</summary>
		public void PrepareAtElapsed(double elapsed)
		{
			ResetForRepeat();
			InitialElapsed = elapsed;
			GetNewState(PlanTime(elapsed), out double mileage, out double position, out TravelDirection direction, out _, out _);
			CurrentPosition = position;
			CurrentMileage = mileage;
			CurrentDirection = direction;
			Train.PlaceCars(position);
			foreach (var car in Train.Cars)
			{
				SetRailIndex(mileage, direction, car.FrontAxle.Follower);
				SetRailIndex(mileage, direction, car.RearAxle.Follower);
				SetRailIndex(mileage, direction, car.FrontBogie.FrontAxle.Follower);
				SetRailIndex(mileage, direction, car.FrontBogie.RearAxle.Follower);
				SetRailIndex(mileage, direction, car.RearBogie.FrontAxle.Follower);
				SetRailIndex(mileage, direction, car.RearBogie.RearAxle.Follower);
			}
		}

		public override void Trigger(double TimeElapsed)
		{
			if (Train.AwaitingHandoff || (Train.State != TrainState.Available && Train.State != TrainState.Bogus))
			{
				return;
			}
			if (TrainManagerBase.currentHost.InGameTime == TimeLastProcessed)
			{
				return;
			}

			// Initialize
			if (double.IsNaN(TimeLastProcessed))
			{
				AppearanceTime = TrainManagerBase.currentHost.InGameTime - InitialElapsed;
				LeaveTime = AppearanceTime + Train.LeaveTime;
				if (InitialElapsed == 0) CurrentPosition = Data[0].Position;
			}

			double frameTime = double.IsNaN(TimeLastProcessed) ? 0 : Math.Max(0, TrainManagerBase.currentHost.InGameTime - TimeLastProcessed);
			TimeLastProcessed = TrainManagerBase.currentHost.InGameTime;

			double elapsed = TrainManagerBase.currentHost.InGameTime - AppearanceTime - HeldTime;
			double plannedArrival = Data[Data.Length - 1].ArrivalTime;
			double journeyDuration = Train.JourneyTime > 0 ? Train.JourneyTime : plannedArrival;
			bool chained = !string.IsNullOrWhiteSpace(Train.NextService);

			// Dispose the train if it is past the leave time of the train
			if (!chained && LeaveTime > AppearanceTime && TrainManagerBase.currentHost.InGameTime >= LeaveTime)
			{
				Train.Dispose();
				return;
			}

			// Calculate the position where the train is at the present time.
			// JourneyTime scales the entire travel timeline, including intermediate waits.
			elapsed = WaitForStationExit(elapsed, frameTime);
			double planTime = PlanTime(elapsed);
			GetNewState(planTime, out double NewMileage, out double NewPosition, out TravelDirection NewDirection, out bool OpenLeftDoors, out bool OpenRightDoors);

			// Calculate the travel distance of the train.
			double DeltaPosition = NewPosition - CurrentPosition;
			if (Train.ProtectedTraffic && Train.TrafficControl != null && !Train.TrafficControl.CanAdvance(Train, DeltaPosition, frameTime > 0 ? Math.Abs(DeltaPosition) / frameTime : 0))
			{
				HeldTime += frameTime;
				Train.CurrentSpeed = 0;
				Train.Specs.CurrentAverageAcceleration = 0;
				foreach (var car in Train.Cars)
				{
					car.CurrentSpeed = 0;
					car.Specs.PerceivedSpeed = 0;
				}
				return;
			}


			// Set the state quantity of the train.
			if (DeltaPosition < 0)
			{
				Train.Handles.Reverser.Driver = ReverserPosition.Reverse;
			}
			else if (DeltaPosition > 0)
			{
				Train.Handles.Reverser.Driver = ReverserPosition.Forwards;
			}

			Train.Handles.Reverser.Actual = Train.Handles.Reverser.Driver;

			Train.OpenDoors(OpenLeftDoors, OpenRightDoors);
			Train.CloseDoors(!OpenLeftDoors, !OpenRightDoors);

			if (TimeElapsed != 0.0)
			{
				double oldSpeed = Train.CurrentSpeed;
				Train.CurrentSpeed = DeltaPosition / TimeElapsed;
				if (Math.Abs(Train.CurrentSpeed - oldSpeed) < 0.0001)
				{
					/*
					 * The TFO model sets the current speed directly
					 * However, the physics engine alters it via resistance
					 * etc. - This causes the sounds to bug out with constant
					 * acceleration / deceleration by a tiny fraction
					 * 
					 * So, if our speed delta is very minimal here, let's assume
					 * we're not actually doing anything....
					 */
					Train.Specs.CurrentAverageAcceleration = 0;
				}
				else
				{
					Train.Specs.CurrentAverageAcceleration = (int)NewDirection * (Train.CurrentSpeed - oldSpeed) / TimeElapsed;
				}
			}
			else
			{
				Train.CurrentSpeed = 0.0;
				Train.Specs.CurrentAverageAcceleration = 0.0;
			}

			foreach (CarBase Car in Train.Cars)
			{
				SetRailIndex(NewMileage, NewDirection, Car.FrontAxle.Follower);
				SetRailIndex(NewMileage, NewDirection, Car.RearAxle.Follower);
				SetRailIndex(NewMileage, NewDirection, Car.FrontBogie.FrontAxle.Follower);
				SetRailIndex(NewMileage, NewDirection, Car.FrontBogie.RearAxle.Follower);
				SetRailIndex(NewMileage, NewDirection, Car.RearBogie.FrontAxle.Follower);
				SetRailIndex(NewMileage, NewDirection, Car.RearBogie.RearAxle.Follower);

				Car.Move(DeltaPosition);

				Car.CurrentSpeed = Train.CurrentSpeed;
				Car.Specs.PerceivedSpeed = Train.CurrentSpeed;
				Car.Specs.Acceleration = Train.Specs.CurrentAverageAcceleration;
				Car.TractionModel.CurrentAcceleration = Train.Specs.CurrentAverageAcceleration;

				// Traction model related AI
				if (Car.TractionModel.Components.TryGetTypedValue(EngineComponent.Pantograph, out Pantograph p) && p.State == PantographState.Lowered)
				{
					p.Raise();
				}

				if (Car.TractionModel.Components.TryGetTypedValue(EngineComponent.CylinderCocks, out CylinderCocks c) && !c.Automatic)
				{
					if (Car.CurrentSpeed == 0 && c.Opened == false)
					{
						c.ControlDown(Translations.Command.CylinderCocks);
					}

					if (Car.CurrentSpeed > 5 && c.Opened)
					{
						c.ControlDown(Translations.Command.CylinderCocks);
					}
				}
			}

			CurrentPosition = NewPosition;
			CurrentMileage = NewMileage;
			CurrentDirection = NewDirection;
			if (chained && elapsed >= journeyDuration + Train.CompletionWait)
			{
				Train.CompleteJourney();
			}
		}

		/// <summary>Create a train operation plan.</summary>
		internal void SetupTravelData()
		{
			// The first point must be TravelStopData.
			TravelStopData FirstData = (TravelStopData)Data[0];

			// Calculate the end point of acceleration and the start point of deceleration.
			{
				TravelDirection LastDirection;
				double DeltaPosition;

				// The start point does not slow down. Acceleration only.
				{
					FirstData.Mileage = 0.0;
					LastDirection = FirstData.Direction;
					DeltaPosition = 0.0;
					if (FirstData.Accelerate != 0.0)
					{
						DeltaPosition = Math.Pow(FirstData.TargetSpeed, 2.0) / (2.0 * FirstData.Accelerate);
					}

					FirstData.AccelerationEndPosition = FirstData.Position + (int)LastDirection * DeltaPosition;
				}

				for (int i = 1; i < Data.Length; i++)
				{
					DeltaPosition = 0.0;
					if (Data[i].Decelerate != 0.0)
					{
						DeltaPosition = (Math.Pow(Data[i].PassingSpeed, 2.0) - Math.Pow(Data[i - 1].TargetSpeed, 2.0)) / (2.0 * Data[i].Decelerate);
					}

					Data[i].DecelerationStartPosition = Data[i].Position - (int)LastDirection * DeltaPosition;

					Data[i].Mileage = Data[i - 1].Mileage + Math.Abs(Data[i].Position - Data[i - 1].Position);
					LastDirection = (Data[i] as TravelStopData)?.Direction ?? LastDirection;
					DeltaPosition = 0.0;
					if (Data[i].Accelerate != 0.0)
					{
						DeltaPosition = (Math.Pow(Data[i].TargetSpeed, 2.0) - Math.Pow(Data[i].PassingSpeed, 2.0)) / (2.0 * Data[i].Accelerate);
					}

					Data[i].AccelerationEndPosition = Data[i].Position + (int)LastDirection * DeltaPosition;
				}
			}

			// Reflect the delay until the TFO becomes effective at the first point.
			{
				if (FirstData.OpenLeftDoors || FirstData.OpenRightDoors)
				{
					FirstData.OpeningDoorsEndTime += OpeningDoorsTime;
				}

				FirstData.ClosingDoorsStartTime = FirstData.OpeningDoorsEndTime + FirstData.StopTime;
				FirstData.DepartureTime = FirstData.ClosingDoorsStartTime;
				if (FirstData.OpenLeftDoors || FirstData.OpenRightDoors)
				{
					FirstData.DepartureTime += ClosingDoorsTime;
				}
			}

			// Calculate the time of each point.
			{
				double DeltaT;

				// The start point does not slow down. Acceleration only.
				{
					DeltaT = 0.0;
					if (FirstData.Accelerate != 0.0)
					{
						DeltaT = FirstData.TargetSpeed / FirstData.Accelerate;
					}

					FirstData.AccelerationEndTime = FirstData.DepartureTime + DeltaT;
				}

				for (int i = 1; i < Data.Length; i++)
				{
					DeltaT = 0.0;
					if (Data[i - 1].TargetSpeed != 0.0)
					{
						DeltaT = Math.Abs(Data[i].DecelerationStartPosition - Data[i - 1].AccelerationEndPosition) / Data[i - 1].TargetSpeed;
					}

					Data[i].DecelerationStartTime = Data[i - 1].AccelerationEndTime + DeltaT;

					DeltaT = 0.0;
					if (Data[i].Decelerate != 0.0)
					{
						DeltaT = (Data[i].PassingSpeed - Data[i - 1].TargetSpeed) / Data[i].Decelerate;
					}

					Data[i].ArrivalTime = Data[i].DecelerationStartTime + DeltaT;

					if (Data[i] is TravelStopData StopData)
					{
						StopData.OpeningDoorsEndTime = StopData.ArrivalTime;
						if (StopData.OpenLeftDoors || StopData.OpenRightDoors)
						{
							StopData.OpeningDoorsEndTime += OpeningDoorsTime;
						}

						StopData.ClosingDoorsStartTime = StopData.OpeningDoorsEndTime + StopData.StopTime;
						StopData.DepartureTime = StopData.ClosingDoorsStartTime;
						if (StopData.OpenLeftDoors || StopData.OpenRightDoors)
						{
							StopData.DepartureTime += ClosingDoorsTime;
						}
					}

					DeltaT = 0.0;
					if (Data[i].Accelerate != 0.0)
					{
						DeltaT = (Data[i].TargetSpeed - Data[i].PassingSpeed) / Data[i].Accelerate;
					}

					Data[i].AccelerationEndTime = Data[i].DepartureTime + DeltaT;
				}
			}
		}

		/// <summary>Check whether the travel plan of the train is valid.</summary>
		private void CheckTravelData()
		{
			bool Recalculation = false;
			TravelDirection LastDirection = ((TravelStopData)Data[0]).Direction;

			for (int i = 1; i < Data.Length; i++)
			{
				// The deceleration start point must appear after the acceleration end point.
				if ((Data[i - 1].AccelerationEndPosition - Data[i].DecelerationStartPosition) * (int)LastDirection > 0)
				{
					// Reset acceleration and deceleration.
					double Delta = Math.Abs(Data[i].Position - Data[i - 1].Position);
					if (Delta != 0.0)
					{
						Data[i - 1].Accelerate = (Math.Pow(Data[i - 1].TargetSpeed, 2.0) - Math.Pow(Data[i - 1].PassingSpeed, 2.0)) / Delta;
						Data[i].Decelerate = (Math.Pow(Data[i].PassingSpeed, 2.0) - Math.Pow(Data[i - 1].TargetSpeed, 2.0)) / Delta;
						Recalculation = true;
					}
				}

				LastDirection = (Data[i] as TravelStopData)?.Direction ?? LastDirection;
			}

			// Recreate the operation plan of the train.
			if (Recalculation)
			{
				SetupTravelData();
			}
		}

		private void GetNewState(double Time, out double NewMileage, out double NewPosition, out TravelDirection NewDirection, out bool OpenLeftDoors, out bool OpenRightDoors)
		{
			double DeltaT;
			double DeltaPosition;
			OpenLeftDoors = false;
			OpenRightDoors = false;

			{
				// The first point must be TravelStopData.
				TravelStopData FirstData = (TravelStopData)Data[0];

				NewMileage = FirstData.Mileage;
				NewPosition = FirstData.Position;
				NewDirection = FirstData.Direction;

				if (Time <= FirstData.ArrivalTime)
				{
					return;
				}

				if (Time <= FirstData.ClosingDoorsStartTime)
				{
					OpenLeftDoors = FirstData.OpenLeftDoors;
					OpenRightDoors = FirstData.OpenRightDoors;
					return;
				}

				if (Time <= FirstData.DepartureTime)
				{
					return;
				}

				// The start point does not slow down. Acceleration only.
				if (Time <= FirstData.AccelerationEndTime)
				{
					DeltaT = Time - FirstData.DepartureTime;
					DeltaPosition = 0.5 * FirstData.Accelerate * Math.Pow(DeltaT, 2.0);
					NewMileage += DeltaPosition;
					NewPosition += (int)NewDirection * DeltaPosition;
					return;
				}

				NewMileage += Math.Abs(FirstData.AccelerationEndPosition - NewPosition);
				NewPosition = FirstData.AccelerationEndPosition;
			}

			for (int i = 1; i < Data.Length; i++)
			{
				if (Time <= Data[i].DecelerationStartTime)
				{
					DeltaT = Time - Data[i - 1].AccelerationEndTime;
					DeltaPosition = Data[i - 1].TargetSpeed * DeltaT;
					NewMileage += DeltaPosition;
					NewPosition += (int)NewDirection * DeltaPosition;
					return;
				}

				NewMileage += Math.Abs(Data[i].DecelerationStartPosition - NewPosition);
				NewPosition = Data[i].DecelerationStartPosition;

				if (Time <= Data[i].ArrivalTime)
				{
					DeltaT = Time - Data[i].DecelerationStartTime;
					DeltaPosition = Data[i - 1].TargetSpeed * DeltaT + 0.5 * Data[i].Decelerate * Math.Pow(DeltaT, 2.0);
					NewMileage += DeltaPosition;
					NewPosition += (int)NewDirection * DeltaPosition;
					return;
				}

				TravelStopData StopData = Data[i] as TravelStopData;

				NewMileage = Data[i].Mileage;
				NewPosition = Data[i].Position;
				NewDirection = StopData?.Direction ?? NewDirection;

				if (Time <= StopData?.ClosingDoorsStartTime)
				{
					OpenLeftDoors = StopData.OpenLeftDoors;
					OpenRightDoors = StopData.OpenRightDoors;
					return;
				}

				// The end point does not accelerate.
				if (Time <= Data[i].DepartureTime || i == Data.Length - 1)
				{
					return;
				}

				if (Time <= Data[i].AccelerationEndTime)
				{
					DeltaT = Time - Data[i].DepartureTime;
					DeltaPosition = Data[i].PassingSpeed * DeltaT + 0.5 * Data[i].Accelerate * Math.Pow(DeltaT, 2.0);
					NewMileage += DeltaPosition;
					NewPosition += (int)NewDirection * DeltaPosition;
					return;
				}

				NewMileage += Math.Abs(Data[i].AccelerationEndPosition - NewPosition);
				NewPosition = Data[i].AccelerationEndPosition;
			}
		}

		private void SetRailIndex(double Mileage, TravelDirection Direction, TrackFollower TrackFollower)
		{
			TrackFollower.TrackIndex = Data.LastOrDefault(x => x.Mileage <= Mileage + (int)Direction * (TrackFollower.TrackPosition - CurrentPosition))?.RailIndex ?? Data[0].RailIndex;
		}
	}
}
