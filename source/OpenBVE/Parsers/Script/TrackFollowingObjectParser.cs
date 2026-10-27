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

using Formats.OpenBve;
using Formats.OpenBve.XML;
using OpenBveApi.Interface;
using OpenBveApi.Math;
using OpenBveApi.Trains;
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using TrainManager.Car;
using TrainManager.Trains;
using Path = OpenBveApi.Path;

namespace OpenBve
{
	internal static class TrackFollowingObjectParser
	{

		/// <summary>Parses a track following object</summary>
		/// <param name="objectPath">Absolute path to the object folder of route data</param>
		/// <param name="fileName">The XML file to parse</param>
		internal static TrainBase ParseTrackFollowingObject(string objectPath, string fileName, bool expandFleet = true)
		{
			TrainBase Train = null;
			XMLFile<TrackFollowingObjectSection, TrackFollowingObjectKey> xmlFile = new XMLFile<TrackFollowingObjectSection, TrackFollowingObjectKey>(fileName, "/openBVE/TrackFollowingObject", Program.CurrentHost);
			if (xmlFile.GetValue(TrackFollowingObjectKey.RunInterval, out double interval) || xmlFile.GetValue(TrackFollowingObjectKey.PreTrain, out interval))
			{
				Train = new TrainBase(TrainState.Pending, TrainType.PreTrain);
				Train.TimetableDelta = interval;
			}

			xmlFile.ReadBlock(TrackFollowingObjectSection.Train, out Block<TrackFollowingObjectSection, TrackFollowingObjectKey> trainBlock);
			string trainDirectory = string.Empty;
			bool consistReversed = false;
			List<TravelData> travelData = new List<TravelData>();
			while (xmlFile.RemainingSubBlocks > 0)
			{
				Block<TrackFollowingObjectSection, TrackFollowingObjectKey> subBlock = xmlFile.ReadNextBlock();
				switch (subBlock.Key)
				{
					case TrackFollowingObjectSection.Definition:
						Train = new ScriptedTrain(TrainState.Pending);
						ParseDefinitionBlock(subBlock, Train as ScriptedTrain);
						break;
					case TrackFollowingObjectSection.Points:
					case TrackFollowingObjectSection.Stops:
						ParseTravelDataBlock(subBlock, travelData);
						break;
				}
			}

			if (trainBlock != null)
			{
				ParseTrainBlock(objectPath, fileName, trainBlock, ref trainDirectory, ref consistReversed);
			}
			else
			{
				throw new InvalidDataException("No train node specified for scripted train");
			}

			if (Train is ScriptedTrain)
			{
				if (travelData.Count < 2)
				{
					Interface.AddMessage(MessageType.Error, false, $"There must be at least two points to go through in {fileName}");
					return null;
				}

				if (!(travelData.First() is TravelStopData) || !(travelData.Last() is TravelStopData))
				{
					Interface.AddMessage(MessageType.Error, false, $"The first and the last point to go through must be the \"Stop\" node in {fileName}");
					return null;
				}
			}

			if (string.IsNullOrEmpty(trainDirectory))
			{
				Interface.AddMessage(MessageType.Error, false, $"No train has been specified in {fileName}");
				return null;
			}

			string trainData;
			if (!trainDirectory.EndsWith(".con", StringComparison.InvariantCultureIgnoreCase) || !File.Exists(trainDirectory))
			{
				/*
				 * First check for a train.ai file- Functionally identical, but allows for differently configured AI
				 * trains not to show up as drivable
				 */
				trainData = Path.CombineFile(trainDirectory, "train.ai");
				if (!File.Exists(trainData))
				{
					// Check for the standard drivable train.dat
					trainData = Path.CombineFile(trainDirectory, "train.dat");
				}

				string exteriorFile = Path.CombineFile(trainDirectory, "extensions.cfg");
				if (!File.Exists(trainData) || !File.Exists(exteriorFile))
				{
					Interface.AddMessage(MessageType.Error, true,
						$"The supplied train folder in TrackFollowingObject {fileName} does not contain an exterior model.");
				}
			}
			else
			{
				// MSTS consist
				trainData = trainDirectory;
			}


			AbstractTrain currentTrain = Train;
			for (int i = 0; i < Program.CurrentHost.Plugins.Length; i++)
			{
				if (Program.CurrentHost.Plugins[i].Train != null && Program.CurrentHost.Plugins[i].Train.CanLoadTrain(trainData))
				{
					Program.CurrentHost.Plugins[i].Train.LoadTrain(Encoding.UTF8, trainDirectory, ref currentTrain, ref Interface.CurrentControls);
				}
			}

			if (!Train.Cars.Any())
			{
				Interface.AddMessage(MessageType.Error, false, $"Failed to load the specified train in {fileName}");
			}

			if (consistReversed)
			{
				Train.Reverse();
			}

			if (Train is ScriptedTrain st)
			{
				Train.AI = new TrackFollowingObjectAI(st, travelData.ToArray());
				foreach (var car in Train.Cars)
				{
					car.FrontAxle.Follower.TrackIndex = travelData[0].RailIndex;
					car.RearAxle.Follower.TrackIndex = travelData[0].RailIndex;
					car.FrontBogie.FrontAxle.Follower.TrackIndex = travelData[0].RailIndex;
					car.FrontBogie.RearAxle.Follower.TrackIndex = travelData[0].RailIndex;
					car.RearBogie.FrontAxle.Follower.TrackIndex = travelData[0].RailIndex;
					car.RearBogie.RearAxle.Follower.TrackIndex = travelData[0].RailIndex;
					Train.PlaceCars(travelData[0].Position);
				}
			}
			else
			{
				Train.AI = new Game.SimpleHumanDriverAI(Train, Interface.CurrentOptions.PrecedingTrainSpeedLimit);
				Train.Specs.DoorOpenMode = DoorMode.Manual;
				Train.Specs.DoorCloseMode = DoorMode.Manual;
			}

			if (Train is ScriptedTrain protectedService && protectedService.ProtectedTraffic)
			{
				protectedService.TrafficControl = Program.TrainManager.ScriptedTraffic;
				if (!string.IsNullOrWhiteSpace(protectedService.ConflictZones))
				foreach (string zone in protectedService.ConflictZones.Split(';'))
				{
					string[] parts = zone.Split(':');
					if (parts.Length != 3 && parts.Length != 4) throw new InvalidDataException("ConflictZones syntax: [group:]low:high:rail,rail;...");
					int offset = parts.Length == 4 ? 1 : 0;
					protectedService.TrafficControl.AddZone(double.Parse(parts[offset], CultureInfo.InvariantCulture), double.Parse(parts[offset + 1], CultureInfo.InvariantCulture), parts[offset + 2].Split(',').Select(x => int.Parse(x, CultureInfo.InvariantCulture)), offset == 1 ? parts[0] : null);
				}
			}

			if (Train is ScriptedTrain service && service.SpawnInterval > 0)
			{
				service.CanIntroduce = candidate => candidate.IsBirthAreaFree(
					Program.TrainManager.Trains.Cast<AbstractTrain>().Concat(Program.TrainManager.TFOs));
				if (expandFleet)
				{
					var fleet = new ScriptedServiceFleet(service.SpawnInterval);
					fleet.Add(service);
					for (int slot = 1; slot < service.FleetSize; slot++)
					{
						var instance = ParseTrackFollowingObject(objectPath, fileName, false) as ScriptedTrain;
						if (instance == null) throw new InvalidDataException("Failed to preload fleet instance: " + fileName);
						fleet.Add(instance);
						Program.TrainManager.TFOs.Add(instance);
					}
				}
			}

			return Train;
		}

		/// <summary>Function to parse TFO definition</summary>
		/// <param name="definitionBlock">The definition to parse</param>
		/// <param name="scriptedTrain">The track following object to parse this block into</param>
		private static void ParseDefinitionBlock(Block<TrackFollowingObjectSection, TrackFollowingObjectKey> definitionBlock, ScriptedTrain scriptedTrain)
		{
			definitionBlock.TryGetTime(TrackFollowingObjectKey.AppearanceTime, ref scriptedTrain.AppearanceTime);
			definitionBlock.TryGetValue(TrackFollowingObjectKey.AppearanceStartPosition, ref scriptedTrain.AppearanceStartPosition);
			definitionBlock.TryGetValue(TrackFollowingObjectKey.AppearanceEndPosition, ref scriptedTrain.AppearanceEndPosition);
			definitionBlock.TryGetTime(TrackFollowingObjectKey.LeaveTime, ref scriptedTrain.LeaveTime);
			// RepeatInterval is measured in seconds between actual appearances.
			definitionBlock.TryGetValue(TrackFollowingObjectKey.RepeatInterval, ref scriptedTrain.RepeatInterval, NumberRange.NonNegative);
			if (double.IsNaN(scriptedTrain.RepeatInterval) || double.IsInfinity(scriptedTrain.RepeatInterval) ||
			    (scriptedTrain.RepeatInterval > 0 && (scriptedTrain.LeaveTime <= 0 || scriptedTrain.LeaveTime >= scriptedTrain.RepeatInterval)))
			{
				Interface.AddMessage(MessageType.Error, false, "RepeatInterval requires a positive LeaveTime shorter than the interval; repetition disabled.");
				scriptedTrain.RepeatInterval = 0;
			}
			definitionBlock.TryGetValue(TrackFollowingObjectKey.SpawnInterval, ref scriptedTrain.SpawnInterval, NumberRange.NonNegative);
			definitionBlock.TryGetValue(TrackFollowingObjectKey.FleetSize, ref scriptedTrain.FleetSize, NumberRange.Positive);
			definitionBlock.TryGetValue(TrackFollowingObjectKey.BirthClearance, ref scriptedTrain.BirthClearance, NumberRange.NonNegative);
			if (double.IsNaN(scriptedTrain.SpawnInterval) || double.IsInfinity(scriptedTrain.SpawnInterval)) scriptedTrain.SpawnInterval = 0;
			if (double.IsNaN(scriptedTrain.BirthClearance) || double.IsInfinity(scriptedTrain.BirthClearance)) scriptedTrain.BirthClearance = 50;
			if (scriptedTrain.FleetSize < 1 || scriptedTrain.FleetSize > 32 || (scriptedTrain.FleetSize > 1 && scriptedTrain.SpawnInterval <= 0))
			{
				Interface.AddMessage(MessageType.Error, false, "FleetSize must be 1..32; multiple instances require SpawnInterval in seconds. Fleet disabled.");
				scriptedTrain.FleetSize = 1;
				scriptedTrain.SpawnInterval = 0;
			}
			if (scriptedTrain.SpawnInterval > 0 && scriptedTrain.RepeatInterval > 0)
			{
				Interface.AddMessage(MessageType.Error, false, "SpawnInterval replaces RepeatInterval; legacy repetition disabled.");
				scriptedTrain.RepeatInterval = 0;
			}
			definitionBlock.TryGetValue(TrackFollowingObjectKey.ContinuousHandoff, ref scriptedTrain.ContinuousHandoff);
			definitionBlock.TryGetValue(TrackFollowingObjectKey.WarmStart, ref scriptedTrain.WarmStart);
			definitionBlock.TryGetValue(TrackFollowingObjectKey.ProtectedTraffic, ref scriptedTrain.ProtectedTraffic);
			definitionBlock.TryGetValue(TrackFollowingObjectKey.PreferStationWaiting, ref scriptedTrain.PreferStationWaiting);
			definitionBlock.TryGetValue(TrackFollowingObjectKey.DisposeAtDestination, ref scriptedTrain.DisposeAtDestination);
			definitionBlock.TryGetValue(TrackFollowingObjectKey.ConflictZones, ref scriptedTrain.ConflictZones);
			definitionBlock.TryGetValue(TrackFollowingObjectKey.ServiceId, ref scriptedTrain.ServiceId);
			definitionBlock.TryGetValue(TrackFollowingObjectKey.NextService, ref scriptedTrain.NextService);
			definitionBlock.TryGetValue(TrackFollowingObjectKey.TriggeredStart, ref scriptedTrain.AwaitingTrigger);
			definitionBlock.TryGetTime(TrackFollowingObjectKey.JourneyTime, ref scriptedTrain.JourneyTime);
			definitionBlock.TryGetTime(TrackFollowingObjectKey.CompletionWait, ref scriptedTrain.CompletionWait);
			if (scriptedTrain.JourneyTime < 0 || double.IsNaN(scriptedTrain.JourneyTime) || double.IsInfinity(scriptedTrain.JourneyTime)) scriptedTrain.JourneyTime = 0;
			if (scriptedTrain.CompletionWait < 0 || double.IsNaN(scriptedTrain.CompletionWait) || double.IsInfinity(scriptedTrain.CompletionWait)) scriptedTrain.CompletionWait = 0;
			if (scriptedTrain.SpawnInterval > 0 && string.IsNullOrWhiteSpace(scriptedTrain.NextService) && scriptedTrain.LeaveTime <= 0)
			{
				Interface.AddMessage(MessageType.Error, false, "Unchained fleets require positive LeaveTime; fleet disabled.");
				scriptedTrain.SpawnInterval = 0;
				scriptedTrain.FleetSize = 1;
			}
			if (!string.IsNullOrWhiteSpace(scriptedTrain.NextService))
			{
				if (scriptedTrain.RepeatInterval > 0)
				{
					Interface.AddMessage(MessageType.Error, false, "NextService cannot be combined with RepeatInterval; interval repetition disabled.");
					scriptedTrain.RepeatInterval = 0;
				}
				scriptedTrain.JourneyCompleted = () =>
				{
					var matches = Program.TrainManager.TFOs.OfType<ScriptedTrain>().Where(x => x.ServiceId == scriptedTrain.NextService && x.FleetSlot == scriptedTrain.FleetSlot).ToArray();
					if (matches.Length != 1 || matches[0] == scriptedTrain || !matches[0].AwaitingTrigger || matches[0].State != TrainState.Pending)
					{
						Interface.AddMessage(MessageType.Error, false, "NextService must identify exactly one inactive service with the same fleet slot: " + scriptedTrain.NextService);
						return;
					}
					matches[0].ActivateService(scriptedTrain.ContinuousHandoff ? scriptedTrain : null);
				};
			}
			definitionBlock.ReportErrors();
		}

		/// <summary>Function to parse train definition</summary>
		/// <param name="objectPath">Absolute path to the object folder of route data</param>
		/// <param name="fileName">The filename of the containing XML file</param>
		/// <param name="definitionBlock">The definition to parse</param>
		/// <param name="trainDirectory">Absolute path to the train directory</param>
		/// <param name="consistReversed">Whether to reverse the train composition.</param>
		private static void ParseTrainBlock(string objectPath, string fileName, Block<TrackFollowingObjectSection, TrackFollowingObjectKey> definitionBlock, ref string trainDirectory, ref bool consistReversed)
		{
			if (definitionBlock.GetValue(TrackFollowingObjectKey.Directory, out string value))
			{
				string tmpPath = string.Empty;
				try
				{
					tmpPath = Path.CombineDirectory(Path.GetDirectoryName(fileName), value);

					if (!Directory.Exists(tmpPath) && value.EndsWith(".con", StringComparison.InvariantCultureIgnoreCase))
					{
						// potential MSTS consist
						string consistDirectory = Path.CombineDirectory(Program.FileSystem.MSTSDirectory, "TRAINS\\Consists");
						string consistFile = Path.CombineFile(consistDirectory, value);
						if (File.Exists(consistFile))
						{
							trainDirectory = consistFile;
						}

					}

					if (string.IsNullOrEmpty(trainDirectory) && !Directory.Exists(tmpPath))
					{
						tmpPath = Path.CombineFile(Program.FileSystem.InitialTrainFolder, value);
					}

					if (string.IsNullOrEmpty(trainDirectory) && !Directory.Exists(tmpPath))
					{
						tmpPath = Path.CombineFile(Program.FileSystem.TrainInstallationDirectory, value);
					}

					if (string.IsNullOrEmpty(trainDirectory) && !Directory.Exists(tmpPath))
					{
						tmpPath = Path.CombineFile(objectPath, value);
					}

					if (string.IsNullOrEmpty(trainDirectory) && !Directory.Exists(tmpPath))
					{
						// very fuzzy match attempt- step backwards one level from the current train folder
						tmpPath = Path.CombineDirectory(Loading.CurrentTrainFolder, "..");
						tmpPath = Path.CombineFile(tmpPath, value);
					}
				}
				catch
				{
					Interface.AddMessage(MessageType.Error, false, $"Directory was invalid in in {definitionBlock.Key} in {fileName}");
				}

				if (string.IsNullOrEmpty(trainDirectory))
				{
					if (!Directory.Exists(tmpPath))
					{
						Interface.AddMessage(MessageType.Error, false, $"Directory was not found in in  {definitionBlock.Key} in {fileName}");
					}
					else
					{
						trainDirectory = tmpPath;
					}
				}
			}

			definitionBlock.TryGetValue(TrackFollowingObjectKey.Reversed, ref consistReversed);
		}

		/// <summary>Parses a train travel data block</summary>
		/// <param name="travelDataBlock">The travel data to parse</param>
		/// <param name="travelData">The list of travel data to add this to</param>
		private static void ParseTravelDataBlock(Block<TrackFollowingObjectSection, TrackFollowingObjectKey> travelDataBlock, ICollection<TravelData> travelData)
		{
			while (travelDataBlock.RemainingSubBlocks > 0)
			{
				Block<TrackFollowingObjectSection, TrackFollowingObjectKey> subBlock = travelDataBlock.ReadNextBlock();
				switch (subBlock.Key)
				{
					case TrackFollowingObjectSection.Stop:
						travelData.Add(ParseTravelStopBlock(subBlock));
						break;
					case TrackFollowingObjectSection.Point:
						travelData.Add(ParseTravelPointBlock(subBlock));
						break;
				}
			}
			travelDataBlock.ReportErrors();
		}

		/// <summary>Function to parse the contents of TravelData class</summary>
		/// <param name="travelDataBlock">The travel data to parse</param>
		/// <param name="travelData">Travel data to which the parse results apply</param>
		private static void ParseTravelDataBlock(Block<TrackFollowingObjectSection, TrackFollowingObjectKey> travelDataBlock, TravelData travelData)
		{
			double decelerate = 0.0;
			double accelerate = 0.0;
			double targetSpeed = 0.0;
			
			travelDataBlock.TryGetValue(TrackFollowingObjectKey.Decelerate, ref decelerate, NumberRange.NonNegative);
			travelDataBlock.TryGetValue(TrackFollowingObjectKey.Position, ref travelData.Position);
			travelDataBlock.TryGetValue(TrackFollowingObjectKey.StopPosition, ref travelData.Position);
			travelDataBlock.TryGetValue(TrackFollowingObjectKey.Accelerate, ref accelerate, NumberRange.NonNegative);
			bool targetSpeedSet = travelDataBlock.TryGetValue(TrackFollowingObjectKey.TargetSpeed, ref targetSpeed, NumberRange.NonNegative);
			travelDataBlock.TryGetValue(TrackFollowingObjectKey.Rail, ref travelData.RailIndex, NumberRange.NonNegative);
			if (!Program.CurrentRoute.Tracks.ContainsKey(travelData.RailIndex) || Program.CurrentRoute.Tracks[travelData.RailIndex].Elements.Length == 0)
			{
				Interface.AddMessage(MessageType.Error, false, $"RailIndex {travelData.RailIndex} is invalid in {travelDataBlock.Key} in {travelDataBlock.FileName}");
				travelData.RailIndex = 0;
			}
			
			if (!targetSpeedSet)
			{
				Interface.AddMessage(MessageType.Warning, false, $"A TargetSpeed was not set in {travelDataBlock.Key}. This may cause unexpected results.");
			}
			travelData.Decelerate = -decelerate / 3.6;
			travelData.Accelerate = accelerate / 3.6;
			travelData.TargetSpeed = targetSpeed / 3.6;
		}

		/// <summary>Function to parse the contents of TravelStopData class</summary>
		/// <param name="sectionElement">The XElement to parse</param>
		/// <returns>An instance of the new TravelStopData class with the parse result applied</returns>
		private static TravelStopData ParseTravelStopBlock(Block<TrackFollowingObjectSection, TrackFollowingObjectKey> sectionElement)
		{
			TravelStopData travelStopData = new TravelStopData();

			ParseTravelDataBlock(sectionElement, travelStopData);
			sectionElement.TryGetTime(TrackFollowingObjectKey.StopTime, ref travelStopData.StopTime);
			if (sectionElement.GetValue(TrackFollowingObjectKey.Doors, out string doorDirection))
			{
				int doorSide = 0;
				bool doorBoth = false;

				switch (doorDirection.ToLowerInvariant())
				{
					case "l":
					case "left":
						doorSide = -1;
						break;
					case "r":
					case "right":
						doorSide = 1;
						break;
					case "n":
					case "none":
					case "neither":
						doorSide = 0;
						break;
					case "b":
					case "both":
						doorBoth = true;
						break;
					default:
						if (!NumberFormats.TryParseIntVb6(doorDirection, out doorSide))
						{
							Interface.AddMessage(MessageType.Error, false,
								$"Door direction is invalid in in {sectionElement.Key} in {sectionElement.FileName}");
						}
						break;
				}
				travelStopData.OpenLeftDoors = doorSide < 0.0 | doorBoth;
				travelStopData.OpenRightDoors = doorSide > 0.0 | doorBoth;
			}

			if (sectionElement.GetValue(TrackFollowingObjectKey.Direction, out string travelDirection))
			{
				int d;
				switch (travelDirection.ToLowerInvariant())
				{
					case "f":
						d = 1;
						break;
					case "r":
						d = -1;
						break;
					default:
						if (!NumberFormats.TryParseIntVb6(travelDirection, out d) ||
						    !Enum.IsDefined(typeof(TravelDirection), d))
						{
							Interface.AddMessage(MessageType.Error, false,
								$"TravelDirection is invalid in {sectionElement.Key} in {sectionElement.FileName}");
							d = 1;
						}

						break;
				}

				travelStopData.Direction = (TravelDirection)d;
			}

			
			
			return travelStopData;
		}

		/// <summary>Function to parse the contents of TravelPointData class</summary>
		/// <param name="sectionElement">The XElement to parse</param>
		/// <returns>An instance of the new TravelPointData class with the parse result applied</returns>
		private static TravelPointData ParseTravelPointBlock(Block<TrackFollowingObjectSection, TrackFollowingObjectKey> sectionElement)
		{
			TravelPointData travelPointData = new TravelPointData();

			ParseTravelDataBlock(sectionElement, travelPointData);

			double passingSpeed = 0.0;

			sectionElement.TryGetValue(TrackFollowingObjectKey.PassingSpeed, ref passingSpeed);
			
			travelPointData.PassingSpeed = passingSpeed / 3.6;

			return travelPointData;
		}
	}
}
