using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenBve.Native.Desktop;
using LibRender2;
using LibRender2.Objects;
using LibRender2.Screens;
using OpenBveApi;
using OpenBveApi.Colors;
using OpenBveApi.FileSystem;
using OpenBveApi.Graphics;
using OpenBveApi.Hosts;
using OpenBveApi.Interface;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenBveApi.Routes;
using OpenBveApi.Runtime;
using OpenBveApi.Textures;
using OpenTK.Graphics.OpenGL;
using RouteManager2.Events;
using RouteManager2.Tracks;
using Vector2 = OpenBveApi.Math.Vector2;
using Vector3 = OpenBveApi.Math.Vector3;

namespace RouteViewer
{
	internal class NewRenderer : BaseRenderer
	{
		// stats
		internal bool RenderStatsOverlay = true;

		// options
		internal bool OptionInterface = true;
		internal bool OptionEvents = false;
		internal bool OptionPaths = false;

		// textures
		private Texture BrightnessChangeTexture;
		private Texture BackgroundChangeTexture;
		private Texture StationStartTexture;
		private Texture StationEndTexture;
		private Texture LimitTexture;
		private Texture SectionTexture;
		private Texture TransponderTexture;
		private Texture SoundTexture;
		private Texture BufferTexture;
		private Texture StopTexture;
		private Texture PointSoundTexture;
		private Texture RunSoundTexture;
		private Texture LightingEventTexture;
		private Texture WeatherEventTexture;

		public override void Initialize()
		{
			base.Initialize();

			string Folder = Path.CombineDirectory(Program.FileSystem.GetDataFolder(), "RouteViewer");
			TextureManager.RegisterTexture(Path.CombineFile(Folder, "background.png"), out BackgroundChangeTexture);
			TextureManager.RegisterTexture(Path.CombineFile(Folder, "brightness.png"), out BrightnessChangeTexture);
			TextureManager.RegisterTexture(Path.CombineFile(Folder, "transponder.png"), out TransponderTexture);
			TextureManager.RegisterTexture(Path.CombineFile(Folder, "section.png"), out SectionTexture);
			TextureManager.RegisterTexture(Path.CombineFile(Folder, "limit.png"), out LimitTexture);
			TextureManager.RegisterTexture(Path.CombineFile(Folder, "station_start.png"), out StationStartTexture);
			TextureManager.RegisterTexture(Path.CombineFile(Folder, "station_end.png"), out StationEndTexture);
			TextureManager.RegisterTexture(Path.CombineFile(Folder, "stop.png"), out StopTexture);
			TextureManager.RegisterTexture(Path.CombineFile(Folder, "buffer.png"), out BufferTexture);
			TextureManager.RegisterTexture(Path.CombineFile(Folder, "sound.png"), out SoundTexture);
			TextureManager.RegisterTexture(Path.CombineFile(Folder, "switchsound.png"), out PointSoundTexture);
			TextureManager.RegisterTexture(Path.CombineFile(Folder, "runsound.png"), out RunSoundTexture);
			TextureManager.RegisterTexture(Path.CombineFile(Folder, "lighting.png"), out LightingEventTexture);
			TextureManager.RegisterTexture(Path.CombineFile(Folder, "weather.png"), out WeatherEventTexture);
		}

		// render scene
		internal void RenderScene(double timeElapsed)
		{
			lastObjectState = null;
			ReleaseResources();
			// initialize
			ResetOpenGlState();

			if (OptionWireFrame)
			{
				if (Program.CurrentRoute.CurrentFog.Start < Program.CurrentRoute.CurrentFog.End)
				{
					const float fogDistance = 600.0f;
					float n = (fogDistance - Program.CurrentRoute.CurrentFog.Start) / (Program.CurrentRoute.CurrentFog.End - Program.CurrentRoute.CurrentFog.Start);
					float cr = n * inv255 * Program.CurrentRoute.CurrentFog.Color.R;
					float cg = n * inv255 * Program.CurrentRoute.CurrentFog.Color.G;
					float cb = n * inv255 * Program.CurrentRoute.CurrentFog.Color.B;
					GL.ClearColor(cr, cg, cb, 1.0f);
				}
				else
				{
					GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
				}
			}
			else
			{
				GL.ClearColor(Interface.CurrentOptions.ClearColor.R * inv255, Interface.CurrentOptions.ClearColor.G * inv255, Interface.CurrentOptions.ClearColor.B * inv255, 1.0f);
			}

			GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

			// set up camera and lighting
			CurrentViewMatrix = Matrix4D.LookAt(Vector3.Zero, new Vector3(Camera.AbsoluteDirection.X, Camera.AbsoluteDirection.Y, -Camera.AbsoluteDirection.Z), new Vector3(Camera.AbsoluteUp.X, Camera.AbsoluteUp.Y, -Camera.AbsoluteUp.Z));
			if (Lighting.ShouldInitialize)
			{
				Lighting.Initialize();
				Lighting.ShouldInitialize = false;
			}
			TransformedLightPosition = new Vector3(Lighting.OptionLightPosition.X, Lighting.OptionLightPosition.Y, -Lighting.OptionLightPosition.Z);
			TransformedLightPosition.Transform(CurrentViewMatrix);

			Lighting.OptionLightingResultingAmount = (Lighting.OptionAmbientColor.R + Lighting.OptionAmbientColor.G + Lighting.OptionAmbientColor.B) / 480.0f;

			if (Lighting.OptionLightingResultingAmount > 1.0f)
			{
				Lighting.OptionLightingResultingAmount = 1.0f;
			}
			// fog
			double fd = Program.CurrentRoute.NextFog.TrackPosition - Program.CurrentRoute.PreviousFog.TrackPosition;

			if (fd != 0.0)
			{
				float fr = (float)((CameraTrackFollower.TrackPosition - Program.CurrentRoute.PreviousFog.TrackPosition) / fd);
				float frc = 1.0f - fr;
				Program.CurrentRoute.CurrentFog.Start = Program.CurrentRoute.PreviousFog.Start * frc + Program.CurrentRoute.NextFog.Start * fr;
				Program.CurrentRoute.CurrentFog.End = Program.CurrentRoute.PreviousFog.End * frc + Program.CurrentRoute.NextFog.End * fr;
				Program.CurrentRoute.CurrentFog.Color.R = (byte)(Program.CurrentRoute.PreviousFog.Color.R * frc + Program.CurrentRoute.NextFog.Color.R * fr);
				Program.CurrentRoute.CurrentFog.Color.G = (byte)(Program.CurrentRoute.PreviousFog.Color.G * frc + Program.CurrentRoute.NextFog.Color.G * fr);
				Program.CurrentRoute.CurrentFog.Color.B = (byte)(Program.CurrentRoute.PreviousFog.Color.B * frc + Program.CurrentRoute.NextFog.Color.B * fr);
				if (!Program.CurrentRoute.CurrentFog.IsLinear)
				{
					Program.CurrentRoute.CurrentFog.Density = Program.CurrentRoute.PreviousFog.Density * frc + Program.CurrentRoute.NextFog.Density * fr;
				}

			}
			else
			{
				Program.CurrentRoute.CurrentFog = Program.CurrentRoute.PreviousFog;
			}

			PerformCSMShadowPass();
            if (GameWindow is ViewerWindow viewer) GL.Viewport(0,viewer.Height-viewer.ViewHeight,viewer.ViewWidth,viewer.ViewHeight);
			DefaultShader.Activate();
			BindCSMToDefaultShader();

            // render background
            GL.Disable(EnableCap.DepthTest);
			DefaultShader.SetShadowEnabled(false);
			Program.CurrentRoute.UpdateBackground(timeElapsed, false);
			DefaultShader.SetShadowEnabled(ShadowsEnabled);

			// fog

			if (Program.CurrentRoute.CurrentFog.Start < Program.CurrentRoute.CurrentFog.End && Program.CurrentRoute.CurrentFog.Start < Program.CurrentRoute.CurrentBackground.BackgroundImageDistance)
			{
				Fog.Enabled = true;
				Fog.Start = Program.CurrentRoute.CurrentFog.Start;
				Fog.End = Program.CurrentRoute.CurrentFog.End;
				Fog.Color = Program.CurrentRoute.CurrentFog.Color;
				Fog.Density = Program.CurrentRoute.CurrentFog.Density;
				Fog.IsLinear = Program.CurrentRoute.CurrentFog.IsLinear;
				Fog.Set();
			}
			else
			{
				Fog.Enabled = false;
			}

            // world layer
            // opaque face

            //Setup the shader for rendering the scene
            if (OptionLighting)
            {
	            DefaultShader.SetIsLight(true);
	            DefaultShader.SetLightPosition(TransformedLightPosition);
	            DefaultShader.SetLightAmbient(Lighting.OptionAmbientColor);
	            DefaultShader.SetLightDiffuse(Lighting.OptionDiffuseColor);
	            DefaultShader.SetLightSpecular(Lighting.OptionSpecularColor);
	            DefaultShader.SetLightModel(Lighting.LightModel);
            }

            Fog.Set();
            DefaultShader.SetTexture(0);
            DefaultShader.SetCurrentProjectionMatrix(CurrentProjectionMatrix);
            ResetOpenGlState();
			List<FaceState> opaqueFaces, alphaFaces;
			lock (VisibleObjects.LockObject)
			{
				opaqueFaces = VisibleObjects.OpaqueFaces.ToList();
				alphaFaces = VisibleObjects.GetSortedPolygons();
			}

			foreach (FaceState face in opaqueFaces)
			{
				face.Draw();
			}

			// alpha face
			ResetOpenGlState();

			if (Interface.CurrentOptions.TransparencyMode == TransparencyMode.Performance)
			{
				SetBlendFunc();
				SetAlphaFunc(AlphaFunction.Greater, 0.0f);
				GL.DepthMask(false);

				foreach (FaceState face in alphaFaces)
				{
					face.Draw();
				}
			}
			else
			{
				UnsetBlendFunc();
				SetAlphaFunc(AlphaFunction.Equal, 1.0f);
				GL.DepthMask(true);

				foreach (FaceState face in alphaFaces)
				{
					if (face.Object.Prototype.Mesh.Materials[face.Face.Material].BlendMode == MeshMaterialBlendMode.Normal && face.Object.Prototype.Mesh.Materials[face.Face.Material].GlowAttenuationData == 0)
					{
						if (face.Object.Prototype.Mesh.Materials[face.Face.Material].Color.A == 255)
						{
							face.Draw();
						}
					}
				}

				SetBlendFunc();
				SetAlphaFunc(AlphaFunction.Less, 1.0f);
				GL.DepthMask(false);
				bool additive = false;

				foreach (FaceState face in alphaFaces)
				{
					if (face.Object.Prototype.Mesh.Materials[face.Face.Material].BlendMode == MeshMaterialBlendMode.Additive)
					{
						if (!additive)
						{
							UnsetAlphaFunc();
							additive = true;
						}
					}
					else
					{
						if (additive)
						{
							SetAlphaFunc();
							additive = false;
						}
					}
					face.Draw();
				}
			}

			if (OptionPaths)
			{
				// TODO: Write a shader to draw point list....
				ResetOpenGlState();

				DefaultShader.Deactivate();

                unsafe
				{
					GL.MatrixMode(MatrixMode.Projection);
					GL.PushMatrix();
					fixed (double* matrixPointer = &CurrentProjectionMatrix.Row0.X)
					{
						GL.LoadMatrix(matrixPointer);
					}

					GL.MatrixMode(MatrixMode.Modelview);
					GL.PushMatrix();

					fixed (double* matrixPointer = &CurrentViewMatrix.Row0.X)
					{
						GL.LoadMatrix(matrixPointer);
					}

					Matrix4D m = Camera.TranslationMatrix;
					double* matrixPointer2 = &m.Row0.X;
					{
						GL.MultMatrix(matrixPointer2);
					}
				}

				// render track paths
				for (int i = 0; i < Program.CurrentRoute.Tracks.Count; i++)
				{
					int key = Program.CurrentRoute.Tracks.ElementAt(i).Key;
					trackColors[key].Render();
				}
				GL.PopMatrix();
				GL.MatrixMode(MatrixMode.Projection);
				GL.PopMatrix();
			}

			if (OptionEvents)
			{
				if (Math.Abs(CameraTrackFollower.TrackPosition - lastTrackPosition) > 50)
				{
					lastTrackPosition = CameraTrackFollower.TrackPosition;
					CubesToDraw.Clear();
					for (int i = 0; i < Program.CurrentRoute.Tracks.Count; i++)
					{
						int railIndex = Program.CurrentRoute.Tracks.ElementAt(i).Key;
						FindEvents(railIndex);
					}
				}


				for (int i = 0; i < CubesToDraw.Count; i++)
				{
					Texture T = CubesToDraw.ElementAt(i).Key;
					foreach (Vector3 v in CubesToDraw[T])
					{
						Cube.Draw(v, Camera.AbsoluteDirection, Camera.AbsoluteUp, Camera.AbsoluteSide, 0.2, Camera.AbsolutePosition, T);
					}
				}

			}

            // render overlays
            DefaultShader.Deactivate();

            ResetOpenGlState();
			OptionLighting = false;
			Fog.Enabled = false;
			UnsetAlphaFunc();
			GL.Disable(EnableCap.DepthTest);
			SetBlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha); //FIXME: Remove when text switches between two renderer types
			RenderOverlays(timeElapsed);
			OptionLighting = true;
		}

		private double lastTrackPosition;

		/// <summary>Finds visible events on a rail index</summary>
		/// <param name="railIndex">The rail index</param>
		internal void FindEvents(int railIndex)
		{
			if (Program.CurrentRoute.Tracks[railIndex].Elements == null)
			{
				return;
			}

			GL.Enable(EnableCap.CullFace);
			GL.Enable(EnableCap.DepthTest);
			GL.DepthMask(true);
			OptionLighting = false;

			double da = -Camera.BackwardViewingDistance - Camera.ExtraViewingDistance;
			double db = Camera.ForwardViewingDistance + Camera.ExtraViewingDistance;
			bool[] sta = new bool[Program.CurrentRoute.Stations.Length];

			// events
			for (int i = 0; i < Program.CurrentRoute.Tracks[railIndex].Elements.Length; i++)
			{
				double p = Program.CurrentRoute.Tracks[railIndex].Elements[i].StartingTrackPosition;
				double d = p -CameraTrackFollower.TrackPosition;

				if (d >= da && d <= db)
				{
					foreach (GeneralEvent e in Program.CurrentRoute.Tracks[railIndex].Elements[i].Events)
					{
						double dy, dx = 0.0, dz = 0.0;
						Texture t;

						if (e is BrightnessChangeEvent)
						{
							dy = 4.0;
							t = BrightnessChangeTexture;
						}
						else if (e is BackgroundChangeEvent)
						{
							dy = 3.5;
							t = BackgroundChangeTexture;
						}
						else if (e is StationStartEvent startEvent)
						{
							dy = 1.6;
							t = StationStartTexture;
							sta[startEvent.StationIndex] = true;
						}
						else if (e is StationEndEvent endEvent)
						{
							dy = 1.6;
							t = StationEndTexture;
							sta[endEvent.StationIndex] = true;
						}
						else if (e is LimitChangeEvent)
						{
							dy = 1.1;
							t = LimitTexture;
						}
						else if (e is SectionChangeEvent)
						{
							dy = 0.8;
							t = SectionTexture;
						}
						else if (e is TransponderEvent transponderEvent)
						{
							dy = 0.4;
							// beacon type 21 is reserved for legacy weather events
							t = transponderEvent.Type == 21 ? WeatherEventTexture : TransponderTexture;

						}
						else if (e is SoundEvent soundEvent)
						{
							dx = soundEvent.Position.X;
							dy = soundEvent.Position.Y < 0.1 ? 0.1 : soundEvent.Position.Y;
							dz = soundEvent.Position.Z;
							t = SoundTexture;
						}
						else if (e is PointSoundEvent)
						{
							dx = 0;
							dy = 0.2;
							dz = 0;
							t = PointSoundTexture;
						}
						else if (e is RailSoundsChangeEvent)
						{
							dy = 0.8;
							t = RunSoundTexture;
						}
						else if (e is LightingChangeEvent)
						{
							dy = 1.5;
							t = LightingEventTexture;
						}
						else
						{
							dy = 1.0;
							t = null;
						}

						if (t != null)
						{
							TrackFollower f = new TrackFollower(Program.CurrentHost)
							{
								TriggerType = EventTriggerType.None,
								TrackPosition = p,
								TrackIndex = railIndex
							};

							f.UpdateAbsolute(p + e.TrackPositionDelta, true, false);
							Vector3 cubePos = new Vector3(f.WorldPosition);
							cubePos.X += dx * f.WorldSide.X + dy * f.WorldUp.X + dz * f.WorldDirection.X;
							cubePos.Y += dx * f.WorldSide.Y + dy * f.WorldUp.Y + dz * f.WorldDirection.Y;
							cubePos.Z += dx * f.WorldSide.Z + dy * f.WorldUp.Z + dz * f.WorldDirection.Z;

							if (!CubesToDraw.ContainsKey(t))
							{
								CubesToDraw.Add(t, new HashSet<Vector3>());
							}
							CubesToDraw[t].Add(cubePos);
						}
					}
				}

				if (d > db)
				{
					break;
				}
			}

			if (railIndex != 0)
			{
				return;
			}

			// stops
			for (int i = 0; i < sta.Length; i++)
			{
				if (sta[i])
				{
					for (int j = 0; j < Program.CurrentRoute.Stations[i].Stops.Length; j++)
					{
						const double dy = 1.4;
						const double s = 0.2;
						double p = Program.CurrentRoute.Stations[i].Stops[j].TrackPosition;
						TrackFollower f = new TrackFollower(Program.CurrentHost)
						{
							TriggerType = EventTriggerType.None,
							TrackPosition = p
						};
						f.UpdateAbsolute(p, true, false);
						f.WorldPosition.X += dy * f.WorldUp.X;
						f.WorldPosition.Y += dy * f.WorldUp.Y;
						f.WorldPosition.Z += dy * f.WorldUp.Z;

						Cube.Draw(f.WorldPosition, f.WorldDirection, f.WorldUp, f.WorldSide, s, Camera.AbsolutePosition, StopTexture);
					}
				}
			}

			// buffers
			foreach (BufferStop stop in Program.CurrentRoute.BufferTrackPositions)
			{
				double d = stop.TrackPosition - Program.Renderer.CameraTrackFollower.TrackPosition;

				if (d >= da && d <= db)
				{
					const double dy = 2.5;
					const double s = 0.25;
					TrackFollower f = new TrackFollower(Program.CurrentHost)
					{
						TriggerType = EventTriggerType.None,
						TrackPosition = stop.TrackPosition,
						TrackIndex = stop.TrackIndex
					};
					f.UpdateAbsolute(stop.TrackPosition, true, false);
					f.WorldPosition.X += dy * f.WorldUp.X;
					f.WorldPosition.Y += dy * f.WorldUp.Y;
					f.WorldPosition.Z += dy * f.WorldUp.Z;

					Cube.Draw(f.WorldPosition, f.WorldDirection, f.WorldUp, f.WorldSide, s, Camera.AbsolutePosition, BufferTexture);
				}
			}
		}


        private void RenderOverlays(double timeElapsed)
        {
            PushMatrix(MatrixMode.Projection);
            Matrix4D.CreateOrthographicOffCenter(0, Screen.Width, Screen.Height, 0, -1, 1, out CurrentProjectionMatrix);
            PushMatrix(MatrixMode.Modelview);
            CurrentViewMatrix = Matrix4D.Identity;
            bool culling = GL.IsEnabled(EnableCap.CullFace);
            GL.Disable(EnableCap.CullFace);
            ViewerOverlay.Draw(this);
            if (culling) GL.Enable(EnableCap.CullFace);
            PopMatrix(MatrixMode.Modelview);
            PopMatrix(MatrixMode.Projection);
        }
        public NewRenderer(HostInterface host, BaseOptions options, FileSystem fileSystem) : base(host, options, fileSystem)
        {
            Screen.Width = 1280;
            Screen.Height = 800;
        }
    }
}
