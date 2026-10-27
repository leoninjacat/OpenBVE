using System;
using System.Drawing;
using LibRender2.Textures;
using OpenBveApi.Colors;
using OpenBveApi.Graphics;
using OpenBveApi.Math;
using OpenBveApi.Routes;
using OpenBveApi.Textures;
using OpenBveApi.Trains;
using TrainManager.Trains;
using Vector2 = OpenBveApi.Math.Vector2;

namespace OpenBve.Graphics.Renderers
{
	internal partial class Overlays
	{
		private Texture ccoMap;
		private int ccoMapVersion = -1;
		private double ccoMinX, ccoMinZ, ccoScale;
		private const int CcoMapWidth = 360, CcoMapHeight = 240;
		private static readonly Color128 CcoStopped = Color128.Red;
		private static readonly Color128 CcoTraffic = new Color128(0.15f, 0.55f, 1, 1);
		private static readonly Color128 CcoDriver = new Color128(0.15f, 1, 0.35f, 1);
		private static readonly Color128 CcoRail0 = new Color128(0.25f, 0.8f, 1, 1);
		private static readonly Color128 CcoRail1 = new Color128(1, 0.65f, 0.2f, 1);

		// Static geometry is rasterized once per session; only traffic and station markers
		// are drawn each frame. This also works with the native renderer's core GL context.
		private void BuildCcoMap()
		{
			if (ccoMap != null) TextureManager.UnloadTexture(ref ccoMap);
			ccoMap = null;
			ccoMapVersion = CcoMode.MapVersion;
			double maxX = double.NegativeInfinity, maxZ = double.NegativeInfinity;
			ccoMinX = ccoMinZ = double.PositiveInfinity;
			foreach (int rail in new[] { 0, 1, 4, 50 })
			{
				if (!Program.CurrentRoute.Tracks.TryGetValue(rail, out var track)) continue;
				foreach (var element in track.Elements)
				{
					if (element.InvalidElement || CcoMode.TrafficRail(rail, element.StartingTrackPosition) < 0) continue;
					ccoMinX = Math.Min(ccoMinX, element.WorldPosition.X);
					ccoMinZ = Math.Min(ccoMinZ, element.WorldPosition.Z);
					maxX = Math.Max(maxX, element.WorldPosition.X);
					maxZ = Math.Max(maxZ, element.WorldPosition.Z);
				}
			}
			if (double.IsInfinity(ccoMinX)) return;
			ccoScale = Math.Min((CcoMapWidth - 32) / Math.Max(1, maxX - ccoMinX),
				(CcoMapHeight - 32) / Math.Max(1, maxZ - ccoMinZ));
			// Center the route while preserving the same projection for trains and stations.
			ccoMinX -= ((CcoMapWidth - 32) / ccoScale - (maxX - ccoMinX)) / 2;
			ccoMinZ -= ((CcoMapHeight - 32) / ccoScale - (maxZ - ccoMinZ)) / 2;
			using (var bitmap = new Bitmap(CcoMapWidth, CcoMapHeight))
			{
				foreach (int rail in new[] { 0, 1, 4, 50 })
				{
					if (!Program.CurrentRoute.Tracks.TryGetValue(rail, out var track)) continue;
					Color color = rail == 0 ? Color.FromArgb(64, 204, 255) : Color.FromArgb(255, 166, 51);
					{
						for (int i = 1; i < track.Elements.Length; i++)
						{
							if (track.Elements[i - 1].InvalidElement || track.Elements[i].InvalidElement) continue;
							int displayRail = CcoMode.TrafficRail(rail, track.Elements[i].StartingTrackPosition);
							if (displayRail < 0 || CcoMode.TrafficRail(rail, track.Elements[i - 1].StartingTrackPosition) < 0) continue;
							var a = CcoProject(track.Elements[i - 1].WorldPosition, displayRail);
							var b = CcoProject(track.Elements[i].WorldPosition, displayRail);
							CcoRasterLine(bitmap, a, b, color);
						}
					}
				}
				ccoMap = new Texture(bitmap);
			}
		}

		private static void CcoRasterLine(Bitmap bitmap, Vector2 a, Vector2 b, Color color)
		{
			int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y))));
			for (int i = 0; i <= steps; i++)
			{
				int x = (int)Math.Round(a.X + (b.X - a.X) * i / steps);
				int y = (int)Math.Round(a.Y + (b.Y - a.Y) * i / steps);
				for (int dx = 0; dx < 2; dx++)
					if (x + dx >= 0 && x + dx < bitmap.Width && y >= 0 && y < bitmap.Height)
						bitmap.SetPixel(x + dx, y, color);
			}
		}

		private Vector2 CcoProject(Vector3 world, int rail)
		{
			// Parallel rails can be just meters apart over an entire route. A small
			// display offset keeps both colors visible at this overview scale.
			return new Vector2(16 + (world.X - ccoMinX) * ccoScale + (rail == 1 ? 4 : 0),
				CcoMapHeight - 16 - (world.Z - ccoMinZ) * ccoScale);
		}

		private bool CcoStationPoint(int index, out Vector2 point)
		{
			point = Vector2.Null;
			if (!Program.CurrentRoute.Tracks.TryGetValue(0, out var track) || track.Elements.Length == 0) return false;
			double position = Program.CurrentRoute.Stations[index].GetStopPosition(TrainManager.PlayerTrain);
			var elements = track.Elements;
			int low = 0, high = elements.Length - 1;
			while (low < high)
			{
				int mid = (low + high + 1) / 2;
				if (elements[mid].StartingTrackPosition <= position) low = mid;
				else high = mid - 1;
			}
			if (elements[low].InvalidElement) return false;
			Vector3 world = elements[low].WorldPosition;
			if (low + 1 < elements.Length && !elements[low + 1].InvalidElement)
			{
				double length = elements[low + 1].StartingTrackPosition - elements[low].StartingTrackPosition;
				double t = length > 0 ? Math.Max(0, Math.Min(1, (position - elements[low].StartingTrackPosition) / length)) : 0;
				world += (elements[low + 1].WorldPosition - world) * t;
			}
			point = CcoProject(world, 0);
			return true;
		}

		private void RenderCcoTraffic()
		{
			if (CcoMode.View == CcoMode.TrafficView.Hidden) return;
			if (ccoMapVersion != CcoMode.MapVersion) BuildCcoMap();
			if (ccoMap == null) return;
			double width = Math.Min(440, renderer.Screen.Width - 24);
			double height = renderer.Screen.Height - 84;
			if (width < 260 || height < 160) return;
			double left = renderer.Screen.Width - width - 12, top = 60;
			if (CcoMode.View == CcoMode.TrafficView.Curves)
			{
				RenderCcoCurves(left, top, width, Math.Min(420, height));
				return;
			}
			CcoPanel(left, top, width, height);
			CcoText("Diagrama de tráfego • F7: ocultar", left + 12, top + 8, Color128.White);
			CcoText("Via 0", left + 22, top + 31, CcoRail0);
			bool secondRail = Program.CurrentRoute.Tracks.ContainsKey(1);
			CcoText(secondRail ? "Via 1" : "Via 1 ausente", left + 76, top + 31, CcoRail1);
			double bottom = top + height - 55, diagramTop = top + 67;
			renderer.Rectangle.Draw(null, new Vector2(left + 38, diagramTop - 12), new Vector2(2, bottom - diagramTop + 24), CcoRail0);
			if (secondRail)
				renderer.Rectangle.Draw(null, new Vector2(left + 86, diagramTop - 12), new Vector2(2, bottom - diagramTop + 24), CcoRail1);
			int count = Program.CurrentRoute.Stations.Length;
			for (int i = 0; i < count; i++)
			{
				double y = CcoDiagramY(Program.CurrentRoute.Stations[i].GetStopPosition(TrainManager.PlayerTrain), diagramTop, bottom);
				Color128 color = i == CcoMode.SelectedStation ? Color128.Yellow : Color128.White;
				renderer.Rectangle.Draw(null, new Vector2(left + 24, y - 9), new Vector2(78, 18), new Color128(0.08f, 0.12f, 0.17f, 0.95f));
				renderer.Rectangle.Draw(null, new Vector2(left + 24, y - 9), new Vector2(78, 1), color);
				renderer.Rectangle.Draw(null, new Vector2(left + 24, y + 9), new Vector2(78, 1), color);
				renderer.OpenGlString.Draw(renderer.Fonts.SmallFont, (i + 1).ToString(), new Vector2(left + 63, y), TextAlignment.CenterMiddle, color, true);
				CcoText(CcoFitLabel(Program.CurrentRoute.Stations[i].Name, width - 126), left + 114, y - 7, color);
			}
			foreach (var train in Program.TrainManager.Trains) CcoDiagramTrain(train, left, diagramTop, bottom);
			foreach (var train in Program.TrainManager.TFOs)
				if (train is TrainBase traffic) CcoDiagramTrain(traffic, left, diagramTop, bottom);
			CcoArrow(new Vector2(left + 18, top + height - 20), 1, CcoDriver);
			CcoText("Condutor", left + 31, top + height - 27, CcoDriver);
			CcoArrow(new Vector2(left + width * 0.36, top + height - 20), -1, CcoTraffic);
			CcoText("IA", left + width * 0.36 + 13, top + height - 27, CcoTraffic);
			CcoArrow(new Vector2(left + width * 0.69, top + height - 20), 0, CcoStopped);
			CcoText("Parado", left + width * 0.69 + 13, top + height - 27, CcoStopped);

		}

		private void RenderCcoCurves(double left, double top, double width, double height)
		{
			CcoPanel(left, top, width, height);
			CcoText("Traçado • F7: diagrama", left + 12, top + 8, Color128.White);
			double plotScale = Math.Min((width - 32) / CcoMapWidth, (height - 64) / CcoMapHeight);
			double plotWidth = CcoMapWidth * plotScale, plotHeight = CcoMapHeight * plotScale;
			Vector2 origin = new Vector2(left + (width - plotWidth) / 2, top + 30 + (height - 64 - plotHeight) / 2);
			renderer.Rectangle.Draw(ccoMap, origin, new Vector2(plotWidth, plotHeight), Color128.White);
			for (int i = 0; i < Program.CurrentRoute.Stations.Length; i++)
			{
				if (!CcoStationPoint(i, out var point)) continue;
				point = CcoScreenPoint(point, origin, plotWidth, plotHeight);
				Color128 color = i == CcoMode.SelectedStation ? Color128.Yellow : Color128.White;
				CcoDot(point, 2, color);
				CcoText((i + 1).ToString(), point.X + 4, point.Y - 7, color);
			}
			foreach (var train in Program.TrainManager.Trains) CcoTrain(train, origin, plotWidth, plotHeight);
			foreach (var train in Program.TrainManager.TFOs)
				if (train is TrainBase traffic) CcoTrain(traffic, origin, plotWidth, plotHeight);
			CcoDot(new Vector2(left + 16, top + height - 18), 4, CcoDriver);
			CcoText("Condutor", left + 28, top + height - 25, CcoDriver);
			CcoDot(new Vector2(left + width * 0.36, top + height - 18), 4, CcoTraffic);
			CcoText("IA", left + width * 0.36 + 12, top + height - 25, CcoTraffic);
			CcoDot(new Vector2(left + width * 0.69, top + height - 18), 4, CcoStopped);
			CcoText("Parado", left + width * 0.69 + 12, top + height - 25, CcoStopped);
		}

		private void CcoPanel(double x, double y, double width, double height)
		{
			renderer.Rectangle.Draw(null, new Vector2(x, y), new Vector2(width, height), new Color128(0.02f, 0.04f, 0.07f, 0.76f));
		}

		private string CcoFitLabel(string label, double available)
		{
			while (label.Length > 2 && renderer.Fonts.SmallFont.MeasureString(label).X > available)
				label = label.Substring(0, label.Length - 2) + "…";
			return label;
		}

		// Equal spacing between stations keeps the diagram legible. Interpolate
		// train mileage within each interval, so stops and traffic remain aligned.
		private double CcoDiagramY(double position, double top, double bottom)
		{
			var stations = Program.CurrentRoute.Stations;
			if (stations.Length < 2) return (top + bottom) / 2;
			double step = (bottom - top) / (stations.Length - 1);
			for (int i = 0; i < stations.Length - 1; i++)
			{
				double a = stations[i].GetStopPosition(TrainManager.PlayerTrain);
				double b = stations[i + 1].GetStopPosition(TrainManager.PlayerTrain);
				if (position <= b && b > a)
					return bottom - (i + Math.Max(-0.25, (position - a) / (b - a))) * step;
			}
			return top - Math.Min(12, Math.Max(0, position - stations[stations.Length - 1].GetStopPosition(TrainManager.PlayerTrain)) * 0.01);
		}

		private static bool CcoVisibleTrain(TrainBase train)
		{
			if (train.State != TrainState.Available || train.Cars.Length == 0 ||
				(train is ScriptedTrain scripted && scripted.AwaitingHandoff)) return false;
			int rail = train.Cars[0].FrontAxle.Follower.TrackIndex;
			return CcoMode.TrafficRail(rail, train.Cars[0].FrontAxle.Follower.TrackPosition) >= 0;
		}

		internal static Color128 CcoTrainColor(TrainBase train)
		{
			return Math.Abs(train.CurrentSpeed) < 0.01 ? CcoStopped : train.IsPlayerTrain ? CcoDriver : CcoTraffic;
		}

		private void CcoDiagramTrain(TrainBase train, double left, double top, double bottom)
		{
			if (!CcoVisibleTrain(train)) return;
			var follower = train.Cars[0].FrontAxle.Follower;
			int direction;
			if (train.AI is TrackFollowingObjectAI ai) direction = (int)ai.Direction;
			else
			{
				// Train speed is relative to its cab; account for reversed consists.
				double orientation = follower.TrackPosition - train.Cars[train.Cars.Length - 1].RearAxle.Follower.TrackPosition;
				direction = Math.Sign(Math.Abs(train.CurrentSpeed) > 0.01 ? train.CurrentSpeed : (double)train.Handles.Reverser.Actual);
				if (orientation < 0) direction = -direction;
			}
			var point = new Vector2(left + (CcoMode.TrafficRail(follower.TrackIndex, follower.TrackPosition) == 0 ? 39 : 87), CcoDiagramY(follower.TrackPosition, top, bottom));
			CcoArrow(point, direction, CcoTrainColor(train));
		}

		private void CcoArrow(Vector2 point, int direction, Color128 color)
		{
			// A rectangular body and a pointed nose, like the dispatch diagram.
			renderer.Rectangle.Draw(null, new Vector2(point.X - 7, point.Y - 10), new Vector2(14, 20), Color128.Black);
			renderer.Rectangle.Draw(null, new Vector2(point.X - 5, point.Y - 8), new Vector2(10, 16), color);
			if (direction == 0) return;
			for (int i = 0; i < 8; i++)
			{
				double y = point.Y - direction * (8 + i);
				renderer.Rectangle.Draw(null, new Vector2(point.X - (8 - i), y), new Vector2(2 * (8 - i) + 1, 1), color);
			}
		}

		private static Vector2 CcoScreenPoint(Vector2 point, Vector2 origin, double width, double height)
		{
			return new Vector2(origin.X + point.X * width / CcoMapWidth, origin.Y + point.Y * height / CcoMapHeight);
		}

		private void CcoTrain(TrainBase train, Vector2 origin, double width, double height)
		{
			if (!CcoVisibleTrain(train)) return;
			var follower = train.Cars[0].FrontAxle.Follower;
			Vector2 point = CcoScreenPoint(CcoProject(follower.WorldPosition, CcoMode.TrafficRail(follower.TrackIndex, follower.TrackPosition)), origin, width, height);
			if (point.X < origin.X || point.X > origin.X + width || point.Y < origin.Y || point.Y > origin.Y + height) return;
			CcoDot(point, 5, Color128.Black);
			CcoDot(point, 4, CcoTrainColor(train));
		}

		private void CcoDot(Vector2 point, int radius, Color128 color)
		{
			for (int y = -radius; y <= radius; y++)
			{
				double halfWidth = Math.Sqrt(radius * radius - y * y);
				renderer.Rectangle.Draw(null, new Vector2(point.X - halfWidth, point.Y + y), new Vector2(2 * halfWidth + 1, 1), color);
			}
		}

		private void CcoText(string text, double x, double y, Color128 color)
		{
			renderer.OpenGlString.Draw(renderer.Fonts.SmallFont, text, new Vector2(x, y), TextAlignment.TopLeft, color, true);
		}
	}
}
