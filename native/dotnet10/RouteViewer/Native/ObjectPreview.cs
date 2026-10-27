using System;
using System.IO;
using System.Numerics;
using OpenBveApi.Objects;
namespace RouteViewer;

// The object editor shares the desktop shell and renderer, but never parses a route.
internal static class ObjectPreview
{
    static string loaded;
    static Vector3 center;
    static float radius=2;
    internal static void Frame()
    {
        Program.Camera.Mode=NavigationMode.Free;
        Program.Camera.Speed=2;
        Program.Camera.Pose.LookAt(center+Vector3.Normalize(new Vector3(1,.6f,-1))*radius*3,center);
        Program.Camera.Apply();
    }
    internal static bool Load()
    {
        Program.CurrentlyLoading=true;
        RouteInfo.Failure=null;
        var window=(ViewerWindow)Program.Renderer.GameWindow;
        try {
            string path=Program.CurrentRouteFile;
            if(!string.Equals(Path.GetExtension(path),".b3d",StringComparison.OrdinalIgnoreCase)&&
               !string.Equals(Path.GetExtension(path),".csv",StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Selecione um objeto B3D ou CSV.");
            window.Editor.Open(window,path);
            Game.Reset();
            Interface.CurrentOptions.ObjectDisposalMode=OpenBveApi.Routes.ObjectDisposalMode.Legacy;
            Program.CurrentRoute.AccurateObjectDisposal=OpenBveApi.Routes.ObjectDisposalMode.Legacy;
            Loading.Complete=false;
            Program.CurrentHost.ClearObjectCaches();
            if(!Program.CurrentHost.LoadObject(path,OpenBveApi.TextEncoding.GetSystemEncodingFromFile(path),out var obj)||obj==null)
                throw new InvalidOperationException("Falha ao interpretar objeto. Consulte as mensagens (F9).");
            obj.CreateObject(OpenBveApi.Math.Vector3.Zero,new ObjectCreationParameters(0,0,-100000,100000,1));
            Program.Renderer.Camera.QuadTreeLeaf=null;
            // Standalone objects remain visible independently of route disposal ranges.
            foreach(var state in Program.Renderer.StaticObjectStates){state.StartingDistance=-100000;state.EndingDistance=100000;}
            Program.Renderer.InitializeVisibility();
            foreach(var state in Program.Renderer.StaticObjectStates)Program.Renderer.VisibleObjects.ShowObject(state,ObjectType.Static);
            var min=new Vector3(float.MaxValue);var max=new Vector3(float.MinValue);
            foreach(var state in Program.Renderer.StaticObjectStates)
                foreach(var vertex in state.Prototype.Mesh.Vertices){var v=CameraRig.ToV(vertex.Coordinates);min=Vector3.Min(min,v);max=Vector3.Max(max,v);}
            center=min.X==float.MaxValue?Vector3.Zero:(min+max)*.5f;
            radius=min.X==float.MaxValue?2:Math.Max(.5f,Vector3.Distance(min,max)*.5f);
            Loading.Complete=true;
            if(loaded!=path)Frame();else Program.Camera.Apply();
            Program.Renderer.UpdateVisibility(true);
            Program.Renderer.CurrentInterface=LibRender2.Screens.InterfaceType.Normal;
            loaded=path;Program.LoadCount++;
            window.Title=Path.GetFileName(path)+" — "+Program.AppTitle;
            Program.Status=Path.GetFileName(path);
            Console.WriteLine($"OBJECT_EDITOR_READY: objects={Program.Renderer.StaticObjectStates.Count}, faces={Program.Renderer.VisibleObjects.OpaqueFaces.Count}");
            return true;
        } catch(Exception error) {
            RouteInfo.Failure="Objeto: "+error.Message;Program.Status=RouteInfo.Failure;
            Console.Error.WriteLine(error);
            if(Program.SmokeSeconds>0){Environment.ExitCode=1;Program.CurrentlyLoading=false;window.Close();}
            return false;
        } finally {Program.CurrentlyLoading=false;RouteInfo.Refresh();}
    }
}
