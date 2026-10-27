using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Linq;
using System.Globalization;
using OpenBveApi.Colors;
namespace RouteViewer;
internal static class ViewerEditorTest
{
    static int phase, initialLoads;
    internal static bool Complete => Program.ObjectMode?phase>=15&&ViewerObjectToolsTest.Complete&&ViewerPivotTest.Complete:phase>=5;
    static Stopwatch timer,step;

    static Vector3 eye,graphMove,initialDimensions,wantedDimensions;
    static Color32 initialColor;
    static Color32 CurrentColor()=>Program.Renderer.StaticObjectStates.SelectMany(s=>s.Prototype.Mesh.Faces.Where(f=>f.SourceMesh==ObjectGraph.Selected).Select(f=>s.Prototype.Mesh.Materials[f.Material].Color)).First();
    static string OtherColors()=>string.Join(";",Program.Renderer.StaticObjectStates.SelectMany(s=>s.Prototype.Mesh.Faces.Where(f=>f.SourceMesh!=ObjectGraph.Selected).Select(f=>s.Prototype.Mesh.Materials[f.Material].Color.ToString())));
    static string otherColors;
    static void CheckProperties(string report,Color32 color,Vector3 size) {
        if(report==null)throw new Exception("Properties were not reported by GTK");
        var fields=report.Split(' ').Select(v=>double.Parse(v,CultureInfo.InvariantCulture)).ToArray();
        if(fields.Length!=7||fields[0]!=color.R||fields[1]!=color.G||fields[2]!=color.B||fields[3]!=color.A)throw new Exception("GTK color picker did not load material RGBA: "+report);
        if(Vector3.Distance(new Vector3((float)fields[4],(float)fields[5],(float)fields[6]),size)>.002f)throw new Exception("GTK dimension fields did not load actual mesh dimensions: "+report);
    }
    static void CheckResizingTransforms() {
        int id=ObjectGraph.Selected;var values=ObjectGraph.Values(ObjectGraph.Source,id);
        values[3]=20;values[4]=25;values[5]=10;
        string sep=Path.GetExtension(Program.CurrentRouteFile).Equals(".b3d",StringComparison.OrdinalIgnoreCase)?" ":",";
        string rotated=ObjectGraph.Transform(ObjectGraph.Source,id,values)+"\nRotateAll"+sep+"0,1,0,37\n";
        values[9]=1.2;values[10]=.8;values[11]=1.1;
        var target=ObjectGraph.Dimensions(ObjectGraph.Transform(rotated,id,values),id);
        string resized=ObjectGraph.Resize(rotated,id,new double[]{target.X,target.Y,target.Z});
        if(Vector3.Distance(ObjectGraph.Dimensions(resized,id),target)>.002f)throw new Exception("Resizing ignored local or later global rotation");
        string plane=sep==" "?"[MeshBuilder]\nVertex 0,0,0\nVertex 1,0,0\nVertex 1,1,0\nVertex 0,1,0\nFace 0,1,2,3\n":"CreateMeshBuilder\nAddVertex,0,0,0\nAddVertex,1,0,0\nAddVertex,1,1,0\nAddVertex,0,1,0\nAddFace,0,1,2,3\n";
        if(Vector3.Distance(ObjectGraph.Dimensions(ObjectGraph.Resize(plane,1,new double[]{3,2,0}),1),new Vector3(3,2,0))>.002f)throw new Exception("Planar mesh dimension edit failed");
    }
    static double extent,graphExtent,otherExtent;
    static double MeshExtent(bool selected) {
        double result=0;
        foreach(var state in Program.Renderer.StaticObjectStates)
            foreach(var face in state.Prototype.Mesh.Faces)if((face.SourceMesh==ObjectGraph.Selected)==selected)
                foreach(var vertex in face.Vertices)result=Math.Max(result,state.Prototype.Mesh.Vertices[vertex.Index].Coordinates.NormSquared());
        return result;
    }
    static double Extent() {
        double result=0;
        foreach(var state in Program.Renderer.StaticObjectStates)
            foreach(var vertex in state.Prototype.Mesh.Vertices)result=Math.Max(result,vertex.Coordinates.NormSquared());
        return result;
    }
    internal static void Rendered(ViewerWindow window)
    {
        if(!Program.EditorTest || !Loading.Complete)return;
        if(Program.ObjectMode&&phase>=15){if(!ViewerObjectToolsTest.Complete)ViewerObjectToolsTest.Rendered(window);else ViewerPivotTest.Rendered(window);return;}
        timer??=Stopwatch.StartNew();
        if(timer.Elapsed.TotalSeconds>25)throw new Exception("Editor integration timed out at phase "+phase);
        var editor=window.Editor;
        step??=Stopwatch.StartNew();
        if(step.Elapsed.TotalSeconds<.45)return;
        switch(phase) {
            case 0:
                if(!editor.Running || editor.Layout!="side" || !editor.CanvasMatches(window,0))return;
                editor.Send("TEST-LAYOUT","1");step.Restart();phase++;break;
            case 1:
                if(editor.Layout!="bottom" || !editor.CanvasMatches(window,1))return;
                editor.Send("TEST-LAYOUT","2");step.Restart();phase++;break;
            case 2:
                if(editor.Layout!="float" || !editor.CanvasMatches(window,2))return;
                editor.Send("TEST-LAYOUT","0");step.Restart();phase++;break;
            case 3:
                if(editor.Layout!="side" || !editor.CanvasMatches(window,0))return;
                initialLoads=Program.LoadCount;eye=Program.Camera.Pose.Position;
                // Only use --editor-test with a disposable route fixture.
                extent=Extent();
                string edit="\n; Route Editor integration test — estação\n";
                if(Program.ObjectMode)edit+=Path.GetExtension(Program.CurrentRouteFile).Equals(".b3d",StringComparison.OrdinalIgnoreCase)?"ScaleAll 2,2,2\n":"ScaleAll,2,2,2\n";
                editor.Send("TEST-EDIT",edit);phase++;break;
            case 4:
                if(Program.LoadCount<=initialLoads)return;
                if(!File.ReadAllText(Program.CurrentRouteFile,OpenBveApi.TextEncoding.GetSystemEncodingFromFile(Program.CurrentRouteFile)).Contains("Route Editor integration test"))throw new Exception("Editor did not save route");
                if(Vector3.Distance(eye,Program.Camera.Pose.Position)>.01)throw new Exception("Editor reload moved camera");
                if(Program.ObjectMode && (extent<=0 || Extent()<extent*3.9))throw new Exception("Saved geometry was not updated in preview");
                if(Program.ObjectMode)Console.WriteLine("OBJECT_EDITOR_GEOMETRY_OK: saved scale applied to mesh");
                Console.WriteLine("VIEWER_EDITOR_OK: side, bottom, detached, reattached, save from desktop editor, preview reload, camera preserved");phase++;break;
            case 5:
                if(!Program.ObjectMode)return;
                var target=Vector3.Zero;int vertexCount=0;
                foreach(var state in Program.Renderer.StaticObjectStates)
                    foreach(var face in state.Prototype.Mesh.Faces)if(face.SourceMesh==1)
                        foreach(var vertex in face.Vertices){target+=CameraRig.ToV(state.Prototype.Mesh.Vertices[vertex.Index].Coordinates);vertexCount++;}
                if(vertexCount==0)throw new Exception("Source mesh identity was lost");
                target=target/vertexCount-Program.Camera.Pose.Position;
                var pose=Program.Camera.Pose;float depth=Vector3.Dot(target,pose.Forward);
                float projection=window.ViewHeight/(2*MathF.Tan((float)Program.Renderer.Camera.VerticalViewingAngle/2)*depth);
                int px=(int)(window.ViewWidth/2+Vector3.Dot(target,pose.Right)*projection);
                int py=(int)(window.ViewHeight/2-Vector3.Dot(target,pose.Up)*projection);
                if(!ObjectGraph.Pick(window,px,py)||ObjectGraph.Selected!=1)throw new Exception("Graph picking missed mesh 1");
                initialColor=CurrentColor();initialDimensions=ObjectGraph.Dimensions(ObjectGraph.Source,ObjectGraph.Selected);
                editor.Send("TEST-READ-PROPERTIES");
                graphExtent=MeshExtent(true);otherExtent=MeshExtent(false);editor.Send("TEST-PROPERTY");phase++;step.Restart();break;
            case 6:
                if(!ObjectGraph.Source.Contains("@openbve-editor"))return;
                CheckProperties(editor.TestProperties,initialColor,initialDimensions);
                if(MeshExtent(true)<graphExtent*8.9)throw new Exception($"Property edit did not scale geometry: before={graphExtent}, after={Extent()}, source={ObjectGraph.Source}");
                if(File.ReadAllText(Program.CurrentRouteFile).Contains("@openbve-editor"))throw new Exception("Graph edit unexpectedly saved the document");
                if(Math.Abs(MeshExtent(false)-otherExtent)>.001)throw new Exception("Property edit affected another mesh");
                editor.Send("TEST-UNDO");phase++;step.Restart();break;
            case 7:
                if(ObjectGraph.Source.Contains("@openbve-editor"))return;
                if(Math.Abs(MeshExtent(true)-graphExtent)>.001)throw new Exception("Undo did not restore mesh");
                window.Drag=DragMode.ObjectMove;window.DragBy(25,0);window.Drag=DragMode.None;
                graphMove=ObjectGraph.PendingMove;phase++;step.Restart();break;
            case 8:
                if(!ObjectGraph.Source.Contains("@openbve-editor"))return;
                var values=ObjectGraph.Values(ObjectGraph.Source,ObjectGraph.Selected);
                if(Vector3.Distance(new Vector3((float)values[0],(float)values[1],(float)values[2]),graphMove)>.001)throw new Exception("Drag did not update source translation");
                editor.Send("SAVE-REQUEST");initialLoads=Program.LoadCount;phase++;step.Restart();break;
            case 9:
                if(Program.LoadCount<=initialLoads)return;
                if(!File.ReadAllText(Program.CurrentRouteFile).Contains("@openbve-editor"))throw new Exception("Graph edit was not saved");
                Console.WriteLine("OBJECT_EDITOR_GRAPH_OK: picking, properties, unsaved preview, undo, drag, save and reload");
                initialColor=CurrentColor();otherColors=OtherColors();editor.Send("TEST-COLOR");phase++;step.Restart();break;
            case 10:
                if(!ObjectGraph.Source.Contains("@openbve-color"))return;
                var rgba=CurrentColor();
                if(rgba.R!=12||rgba.G!=100||rgba.B!=210||rgba.A!=180)throw new Exception("Color picker did not update RGBA");
                if(OtherColors()!=otherColors||Math.Abs(MeshExtent(false)-otherExtent)>.001)throw new Exception("Color edit affected another mesh");
                if(File.ReadAllText(Program.CurrentRouteFile).Contains("@openbve-color"))throw new Exception("Color edit unexpectedly saved the document");
                editor.Send("TEST-UNDO");phase++;step.Restart();break;
            case 11:
                if(ObjectGraph.Source.Contains("@openbve-color"))return;
                if(CurrentColor()!=initialColor)throw new Exception("Undo failed to restore original material color");
                CheckResizingTransforms();
                wantedDimensions=ObjectGraph.Dimensions(ObjectGraph.Source,ObjectGraph.Selected)*new Vector3(1.5f,.75f,1.25f);
                editor.Send("TEST-SIZE",FormattableString.Invariant($"{wantedDimensions.X:R} {wantedDimensions.Y:R} {wantedDimensions.Z:R}"));phase++;step.Restart();break;
            case 12:
                if(Vector3.Distance(ObjectGraph.Dimensions(ObjectGraph.Source,ObjectGraph.Selected),wantedDimensions)>.002f)return;
                if(Math.Abs(MeshExtent(false)-otherExtent)>.001)throw new Exception("Dimension edit affected another mesh");
                editor.TestProperties=null;editor.Send("TEST-READ-PROPERTIES");phase++;step.Restart();break;
            case 13:
                if(editor.TestProperties==null)return;
                CheckProperties(editor.TestProperties,initialColor,wantedDimensions);
                // Exercise the actual globe control; MMB keeps its previous direction.
                var camera=Program.Camera.Pose;float yaw=camera.Yaw,pitch=camera.Pitch;
                var pivot=camera.Target;float distance=camera.Distance;
                ViewerOverlay.Build(window);var globe=ViewerOverlay.Center("orbit");ViewerOverlay.Press(window,globe.X,globe.Y);window.DragBy(30,20);window.EndInteraction();
                if(Math.Abs(MathF.IEEERemainder(camera.Yaw-yaw,2*MathF.PI)-.18f)>.001||Math.Abs(camera.Pitch-Math.Clamp(pitch-.12f,-MathF.PI/2+.001f,MathF.PI/2-.001f))>.001||camera.Target!=pivot||camera.Distance!=distance)throw new Exception("Globe direction or orbit pivot is incorrect");
                camera.Yaw=yaw;camera.Pitch=pitch;window.Drag=DragMode.Orbit;window.DragBy(30,20);window.EndInteraction();
                if(Math.Abs(MathF.IEEERemainder(camera.Yaw-yaw,2*MathF.PI)+.18f)>.001)throw new Exception("Globe fix changed MMB orbit");
                camera.Yaw=yaw;camera.Pitch=pitch;Program.Camera.Apply();
                editor.Send("TEST-COLOR","solid");editor.Send("SAVE-REQUEST");initialLoads=Program.LoadCount;phase++;step.Restart();break;
            case 14:
                if(Program.LoadCount<=initialLoads||!File.ReadAllText(Program.CurrentRouteFile).Contains("@openbve-color"))return;
                var savedColor=CurrentColor();
                if(savedColor.R!=12||savedColor.G!=100||savedColor.B!=210||savedColor.A!=255||Vector3.Distance(ObjectGraph.Dimensions(ObjectGraph.Source,ObjectGraph.Selected),wantedDimensions)>.002f)throw new Exception("Saved color or dimensions did not survive reload");
                Console.WriteLine("OBJECT_EDITOR_PROPERTIES_OK: loaded material color, color picker RGBA, undo, dimensions, rotated and planar meshes, independent meshes, save/reload, globe direction and MMB preserved");phase++;break;
        }
    }
}
