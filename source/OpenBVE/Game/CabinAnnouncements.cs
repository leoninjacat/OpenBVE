using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using LibRender2.Screens;
using OpenBveApi.Colors;
using OpenBveApi.Interface;
using OpenBveApi.Trains;
using OpenBveApi;
using RouteManager2.MessageManager;
using SoundManager;
using TrainManager.Trains;
using Path = System.IO.Path;

namespace OpenBve
{
	/// <summary>Cab speaker announcements and optional, advisory-only dispatch radio.</summary>
	internal static class CabinAnnouncements
	{
		internal enum Advisory { None, Wait, Clear }

		// Kept separate from playback so transitions can be tested without sound hardware.
		internal sealed class DepartureState
		{
			private bool held, cleared;
			private double freeSince = double.NaN;
			internal Advisory Update(double now, bool blocked, bool known, bool departureDue)
			{
				if (blocked)
				{
					freeSince = double.NaN;
					cleared = false;
					if (held) return Advisory.None;
					held = true;
					return Advisory.Wait;
				}
				if (!known) { freeSince = double.NaN; cleared = false; return Advisory.None; }
				if (double.IsNaN(freeSince)) freeSince = now;
				if (!cleared && now - freeSince >= 1 && (held || departureDue))
				{
					held = false;
					cleared = true;
					return Advisory.Clear;
				}
				return Advisory.None;
			}
		}

		internal static bool RadioEnabled { get; private set; }
		internal static int StationClipsPlayed { get; private set; }
		internal static int RadioClipsPlayed { get; private set; }
		internal static SoundSource CurrentSource => playing;
		internal static bool RadioAudioReady => waitClip != null && clearClip != null && waitClip.Duration > 0 && clearClip.Duration > 0;
		private static readonly Dictionary<string, SoundBuffer> StationClips = new Dictionary<string, SoundBuffer>();
		private static readonly Dictionary<string, string> StationAliases = new Dictionary<string, string>
		{
			{ "centralmanoellacerda", "manoellacerda" },
			{ "cabritasredegatos", "cabritas" }
		};
		private static SoundBuffer waitClip, clearClip, stationPending, radioPending;
		private static SoundSource playing;
		private static bool playingRadio;
		private static Advisory pendingAdvisory, playingAdvisory;
		private static DepartureState departure = new DepartureState();
		private static int station = -1;
		private static bool arrivalAnnounced;
		private static double lastTime = double.NaN, lastPosition;

		internal static void Initialize()
		{
			Stop();
			StationClips.Clear();
			StationClipsPlayed = RadioClipsPlayed = 0;
			station = -1;
			arrivalAnnounced = false;
			lastTime = double.NaN;
			departure = new DepartureState();
			waitClip = clearClip = null;
			RadioEnabled = false;
			string route = Program.CurrentRoute.Information.RouteFile;
			if (string.IsNullOrEmpty(route)) return;
			string railway = Program.FileSystem.GetRailwayFolder(route, AppDomain.CurrentDomain.BaseDirectory);
			string sounds = Path.Combine(railway, "Sound", "Mobitrens");
			waitClip = Register(Path.Combine(sounds, "CCO", "aguarde.wav"));
			clearClip = Register(Path.Combine(sounds, "CCO", "liberado.wav"));
			RadioEnabled = waitClip != null && clearClip != null;
			// Optional content; other routes continue without missing-file warnings.
			string folder = Path.Combine(sounds, "Anuncios");
			if (!Directory.Exists(folder)) return;
			foreach (string file in Directory.EnumerateFiles(folder))
			{
				string name = Path.GetFileNameWithoutExtension(file);
				if (!name.StartsWith("announce_", StringComparison.OrdinalIgnoreCase) ||
					!string.Equals(Path.GetExtension(file), ".wav", StringComparison.OrdinalIgnoreCase)) continue;
				StationClips[Normalize(name.Substring(9))] = Register(file);
			}
			// Onboard departure greeting at Gatos, rather than its arrival announcement.
			var gatosDeparture = Register(Path.Combine(folder, "destino_cachorros.wav"));
			if (gatosDeparture != null) StationClips["gatos"] = gatosDeparture;
		}

		private static SoundBuffer Register(string file)
		{
			return File.Exists(file) ? Program.Sounds.RegisterBuffer(file, 10) : null;
		}

		private static string Normalize(string name)
		{
			var result = new StringBuilder();
			foreach (char c in (name ?? string.Empty).Normalize(NormalizationForm.FormD))
				if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))
					result.Append(char.ToLowerInvariant(c));
			return result.ToString();
		}

		internal static void ResetVisit()
		{
			Stop();
			station = -1;
			arrivalAnnounced = false;
			lastTime = double.NaN;
			departure = new DepartureState();
		}

		internal static void ToggleRadio()
		{
			if (waitClip == null || clearClip == null)
			{
				Notify("CCO: faltam aguarde.wav / liberado.wav em Railway/Sound/Mobitrens/CCO.", MessageColor.Orange);
				return;
			}
			RadioEnabled = !RadioEnabled;
			departure = new DepartureState();
			radioPending = null;
			if (playingRadio) { Program.Sounds.StopSound(playing); playing = null; }
			Notify("CCO de cabine: " + (RadioEnabled ? "ativado" : "desativado") + " (Ctrl+F7)", MessageColor.White);
		}

		private static void Stop()
		{
			if (playing != null) Program.Sounds.StopSound(playing);
			playing = null;
			stationPending = radioPending = null;
			pendingAdvisory = playingAdvisory = Advisory.None;
		}

		internal static void Update()
		{
			if (!Loading.Complete || Game.MinimalisticSimulation || Program.Renderer.CurrentInterface != InterfaceType.Normal) return;
			var train = TrainManager.PlayerTrain;
			if (train == null || train.State != TrainState.Available || train.Cars.Length == 0) return;
			double now = Program.CurrentRoute.SecondsSinceMidnight;
			double position = train.Cars[0].FrontAxle.Follower.TrackPosition;
			bool jumped = !double.IsNaN(lastTime) && (now < lastTime ||
				(Math.Abs(position - lastPosition) > 100 && Math.Abs(train.CurrentSpeed) < 0.1));
			lastTime = now;
			lastPosition = position;
			if (jumped || station != train.Station || train.StationState == TrainStopState.Jumping)
			{
				Stop();
				station = train.Station;
				arrivalAnnounced = false;
				departure = new DepartureState();
			}
			bool stopped = Math.Abs(train.CurrentSpeed) < 0.1;
			bool atStation = station >= 0 && station < Program.CurrentRoute.Stations.Length && stopped &&
				(train.StationState == TrainStopState.Boarding || train.StationState == TrainStopState.Completed);
			if (atStation && !arrivalAnnounced)
			{
				arrivalAnnounced = true;
				string key = Normalize(Program.CurrentRoute.Stations[station].Name);
				if (StationAliases.TryGetValue(key, out var alias)) key = alias;
				StationClips.TryGetValue(key, out stationPending);
			}
			if (RadioEnabled && atStation && now >= train.StationDepartureTime)
			{
				bool blocked, known;
				if (ObserveDeparture(train, out blocked, out known))
				{
					// Never leave a queued or currently spoken clearance after a new obstruction.
					if (blocked || !known)
					{
						if (pendingAdvisory == Advisory.Clear) radioPending = null;
						if (playingRadio && playingAdvisory == Advisory.Clear) { Program.Sounds.StopSound(playing); playing = null; }
					}
					if (!blocked && pendingAdvisory == Advisory.Wait) radioPending = null;
					bool due = now >= train.StationDepartureTime;
					Advisory cue = departure.Update(now, blocked, known, due);
					if (cue != Advisory.None)
					{
						pendingAdvisory = cue;
						radioPending = cue == Advisory.Wait ? waitClip : clearClip;
						if (playingRadio && playingAdvisory != cue) { Program.Sounds.StopSound(playing); playing = null; }
					}
				}
			}
			else
			{
				radioPending = null;
				departure = new DepartureState();
				if (playingRadio) { Program.Sounds.StopSound(playing); playing = null; }
			}
			if (playing != null && (playing.IsPlaying() || playing.IsPaused())) return;
			SoundBuffer clip = stationPending ?? radioPending;
			if (clip == null) return;
			playingRadio = stationPending == null;
			playingAdvisory = playingRadio ? pendingAdvisory : Advisory.None;
			if (playingRadio) radioPending = null; else stationPending = null;
			var car = train.Cars[train.DriverCar];
			playing = Program.Sounds.PlaySound(clip, 1, 1, car.Driver, car, false);
			if (playingRadio)
			{
				RadioClipsPlayed++;
				Notify(playingAdvisory == Advisory.Wait ? "CCO: aguarde a liberação do trem à frente." :
					"CCO: trecho liberado. Observe a sinalização antes de partir.", MessageColor.White);
			}
			else StationClipsPlayed++;
		}

		// Read-only observation: no signal changes, reservations, handle input or AI activation.
		private static bool ObserveDeparture(TrainBase train, out bool blocked, out bool known)
		{
			blocked = false;
			known = false;
			int direction = (int)train.CurrentDirection;
			if (direction == 0) return false;
			int next = train.Station + direction;
			if (next < 0 || next >= Program.CurrentRoute.Stations.Length) return false;
			double from = direction > 0 ? Math.Max(train.FrontCarTrackPosition, train.RearCarTrackPosition) :
				Math.Min(train.FrontCarTrackPosition, train.RearCarTrackPosition);
			double destination = Program.CurrentRoute.Stations[next].GetStopPosition(train);
			if ((destination - from) * direction <= 0) return false;
			int rail = train.Cars[0].FrontAxle.Follower.TrackIndex;
			foreach (var other in Program.TrainManager.Trains)
				if (Obstructs(other, train, rail, from, destination, direction)) blocked = true;
			foreach (var other in Program.TrainManager.TFOs)
				if (other is TrainBase traffic && Obstructs(traffic, train, rail, from, destination, direction)) blocked = true;
			known = true;
			// The upcoming departure signal remains authoritative, even when no nearby train is visible.
			int section = train.CurrentSectionIndex;
			if (section >= 0 && section < Program.CurrentRoute.Sections.Length)
			{
				var signal = direction > 0 ? Program.CurrentRoute.Sections[section].NextSection : Program.CurrentRoute.Sections[section];
				if (signal != null && (signal.TrackPosition - from) * direction >= 0 &&
					(destination - signal.TrackPosition) * direction >= 0)
				{
					known = signal.CurrentAspect >= 0 && signal.CurrentAspect < signal.Aspects.Length;
					// A closed signal can be timetable-dependent. Do not attribute it to a train
					// unless one was observed; still forbid a misleading "trecho liberado".
					if (known && signal.Aspects[signal.CurrentAspect].Speed <= 0) known = false;
				}
			}
			if (train.CurrentSectionLimit <= 0) known = false;
			return true;
		}

		internal static bool Obstructs(TrainBase other, TrainBase player, int rail, double from, double destination, int direction)
		{
			if (ReferenceEquals(other, player) || other.State != TrainState.Available ||
				(other is ScriptedTrain scripted && scripted.AwaitingHandoff)) return false;
			foreach (var car in other.Cars)
			{
				int frontRail = car.FrontAxle.Follower.TrackIndex, rearRail = car.RearAxle.Follower.TrackIndex;
				if (frontRail != rail && rearRail != rail) continue;
				double center = car.FrontAxle.Follower.TrackPosition - car.FrontAxle.Position;
				double low = center - car.Length / 2, high = center + car.Length / 2;
				if (direction > 0 ? high > from && low <= destination : low < from && high >= destination) return true;
			}
			return false;
		}

		private static void Notify(string text, MessageColor color)
		{
			MessageManager.AddMessage(text, MessageDependency.None, GameMode.Expert, color, 8, null);
		}
	}
}
