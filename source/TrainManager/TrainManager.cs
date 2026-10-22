using System;
using System.Linq;
using System.Collections.Generic;
using LibRender2;
using OpenBveApi;
using OpenBveApi.FileSystem;
using OpenBveApi.Hosts;
using OpenBveApi.Trains;
using RouteManager2;
using TrainManager.Trains;
using ScriptedTrain = TrainManager.Trains.ScriptedTrain;

namespace TrainManager
{
	/// <summary>The base train manager class</summary>
	public abstract class TrainManagerBase
	{
		public readonly ScriptedTrafficController ScriptedTraffic;
		internal static HostInterface currentHost;
		internal static BaseRenderer Renderer;
		public static CurrentRoute CurrentRoute;
		internal static FileSystem FileSystem;
		public static bool Toppling;
		public static bool Derailments;

		/// <summary>The list of trains available in the simulation.</summary>
		public List<TrainBase> Trains = new List<TrainBase>();
		/// <summary>A reference to the train of the Trains element that corresponds to the player's train.</summary>
		public static TrainBase PlayerTrain = null;
		/// <summary>The list of TrackFollowingObject available on other tracks in the simulation.</summary>
		public List<AbstractTrain> TFOs = new List<AbstractTrain>();
		/// <summary>Stores a reference to the current options</summary>
		internal static BaseOptions CurrentOptions;
		/// <summary>Stores the plugin error message string, or a null reference if no error encountered</summary>
		public static string PluginError;

		protected TrainManagerBase(HostInterface host, BaseRenderer renderer, BaseOptions Options, FileSystem fileSystem)
		{
			ScriptedTraffic = new ScriptedTrafficController(() => Trains.Cast<AbstractTrain>().Concat(TFOs));
			currentHost = host;
			Renderer = renderer;
			CurrentOptions = Options;
			FileSystem = fileSystem;
		}

		/// <summary>Seeds a complete circulation before the initial scene is displayed.</summary>
		public void InitializeScriptedTraffic()
		{
			foreach (var leader in TFOs.OfType<ScriptedTrain>().Where(x => x.WarmStart && x.FleetSlot == 0 && x.Fleet != null))
			{
				double youngestOut = double.PositiveInfinity, youngestBack = double.PositiveInfinity;
				ScriptedServiceFleet returnFleet = null;
				foreach (var outgoing in leader.Fleet.Members)
				{
					var target = TFOs.OfType<ScriptedTrain>().SingleOrDefault(x => x.ServiceId == outgoing.NextService && x.FleetSlot == outgoing.FleetSlot);
					if (target == null || !(outgoing.AI is TrackFollowingObjectAI outAI) || !(target.AI is TrackFollowingObjectAI backAI)) throw new InvalidOperationException("WarmStart requires paired scripted services");
					double outDuration = outAI.JourneyDuration + outgoing.CompletionWait;
					double backDuration = backAI.JourneyDuration + target.CompletionWait;
					double cycle = outDuration + backDuration;
					if (cycle <= 0 || double.IsInfinity(cycle) || double.IsNaN(cycle) || leader.Fleet.Members.Count * leader.Fleet.Interval < cycle) throw new InvalidOperationException("WarmStart fleet must cover the complete circulation");
					// Space the whole circulation evenly, including the last-to-first gap.
					double phase = outgoing.FleetSlot * cycle / leader.Fleet.Members.Count;
					var active = phase < outDuration ? outgoing : target;
					var hidden = phase < outDuration ? target : outgoing;
					double age = phase < outDuration ? phase : phase - outDuration;
					hidden.AwaitingTrigger = true;
					active.AwaitingTrigger = false;
					active.WarmStartPending = true;
					active.AppearanceTime = currentHost.InGameTime;
					(active.AI as TrackFollowingObjectAI).PrepareAtElapsed(age);
					// Seed approaching trains upstream of interlocked throats. In particular,
					// do not start an arrival inside CAC's throat against the initial departure.
					while (age > 0 && active.ProtectedTraffic && ScriptedTraffic.IsNearConflictZone(active, 250))
					{
						age = (active.AI as TrackFollowingObjectAI).PrepareAtPreviousStation(age);
					}

					if (phase < outDuration) youngestOut = Math.Min(youngestOut, age);
					else youngestBack = Math.Min(youngestBack, age);
					returnFleet = target.Fleet;
				}
				leader.Fleet.SeedHeadway(currentHost.InGameTime, youngestOut);
				returnFleet?.SeedHeadway(currentHost.InGameTime, youngestBack);
			}
			// Let the initial terminal departure reserve its exit before incoming traffic.
			TFOs = TFOs.OrderBy(x => x is ScriptedTrain st && st.WarmStart && st.FleetSlot == 0 ? 0 : 1).ToList();
		}

		/// <summary>Un-derails all trains within the simulation</summary>
		public void UnderailTrains()
		{
			System.Threading.Tasks.Parallel.For(0, Trains.Count, i =>
			{
				UnderailTrain(Trains[i]);
			});
		}

		
		/// <summary>Un-derails a train</summary>
		/// <param name="Train">The train</param>
		public static void UnderailTrain(TrainBase Train)
		{
			Train.Derailed = false;
			for (int i = 0; i < Train.Cars.Length; i++)
			{
				Train.Cars[i].Specs.RollDueToTopplingAngle = 0.0;
				Train.Cars[i].Derailed = false;
				if (Train.Cars[i].TractionModel.ProvidesPower && Train.Cars[i].Sounds.Loop != null)
				{
					Train.Cars[i].Sounds.Loop.Play(Train.Cars[i], true);
				}
			}
		}

		/// <summary>Updates the objects for all trains within the simulation world</summary>
		/// <param name="TimeElapsed">The time elapsed</param>
		/// <param name="ForceUpdate">Whether this is a forced update</param>
		public void UpdateTrainObjects(double TimeElapsed, bool ForceUpdate)
		{
			for (int i = 0; i < Trains.Count; i++)
			{
				Trains[i].UpdateObjects(TimeElapsed, ForceUpdate);
			}

			// ReSharper disable once PossibleInvalidCastExceptionInForeachLoop
			foreach (ScriptedTrain Train in TFOs) //Must not use var, as otherwise the wrong inferred type
			{
				Train.UpdateObjects(TimeElapsed, ForceUpdate);
			}
		}
		
		/// <summary>Performs the jump for all TFOs</summary>
		public void JumpTFO()
		{
			// ReSharper disable once PossibleInvalidCastExceptionInForeachLoop
			foreach (ScriptedTrain Train in TFOs) //Must not use var, as otherwise the wrong inferred type
			{
				Train.Jump(-1, 0);
			}
		}
	}
}
