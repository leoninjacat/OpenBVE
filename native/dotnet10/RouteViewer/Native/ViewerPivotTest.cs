using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
namespace RouteViewer;

internal static class ViewerPivotTest
{
    static int phase,axis,loads;
    static Stopwatch timer,step;
    static string source,geometry,others;
    static byte[] disk,metadata;
    static Vector3 point,beforeDrag,size,beforeMove;
    internal static bool Complete=>phase>=16;
    static string Geometry(bool selected)=>string.Join(";",Program.Renderer.StaticObjectStates.SelectMany(s=>s.Prototype.Mesh.Faces.Where(f=>(f.SourceMesh==ObjectGraph.Selected)==selected).SelectMany(f=>f.Vertices.Select(v=>s.Prototype.Mesh.Vertices[v.Index].Coordinates.ToString()))));
    static Vector3 Pivot=>ObjectPivot.World(ObjectGraph.Source,ObjectGraph.Selected);
    static void Near(Vector3 a,Vector3 b,string message){if(Vector3.Distance(a,b)>.002f)throw new Exception(message+$": {a} != {b}");}
    static void Untouched()
    {
        if(ObjectGraph.Source!=source||Geometry(true)!=geometry||Geometry(false)!=others||!File.ReadAllBytes(Program.CurrentRouteFile).SequenceEqual(disk))throw new Exception("Pivot configuration changed model code, geometry or disk file");
    }
    static void Click(ViewerWindow window,string id){ViewerOverlay.Build(window);var center=ViewerOverlay.Center(id);ViewerOverlay.Press(window,center.X,center.Y);}
    static void Drag(ViewerWindow window,string tool,int selectedAxis)
    {
        Click(window,"tool-"+tool);Click(window,"axis-"+selectedAxis);
        if(!ObjectTools.BeginDrag(window))throw new Exception("Pivot/tool drag was not enabled");
        window.Drag=DragMode.ObjectTransform;window.DragBy(24,-9);window.EndInteraction();
    }
    internal static void Rendered(ViewerWindow window)
    {
        if(Complete)return;
        timer??=Stopwatch.StartNew();step??=Stopwatch.StartNew();
        if(timer.Elapsed.TotalSeconds>30)throw new Exception("Pivot integration timed out at phase "+phase);
        if(step.Elapsed.TotalSeconds<.25)return;
        var editor=window.Editor;
        switch(phase){
            case 0:
                source=ObjectGraph.Source;disk=File.ReadAllBytes(Program.CurrentRouteFile);geometry=Geometry(true);others=Geometry(false);
                if(ObjectPivot.Local(source,ObjectGraph.Selected)!=Vector3.Zero)throw new Exception("Fresh mesh did not default to its local origin");
                var probe=new Vector3(.2f,.4f,-.8f);var expected=Vector3.Transform(probe,ObjectPivot.Frame(source,ObjectGraph.Selected));expected.X=-expected.X;
                string separator=Path.GetExtension(Program.CurrentRouteFile).Equals(".b3d",StringComparison.OrdinalIgnoreCase)?" ":",";
                Near(Vector3.Transform(probe,ObjectPivot.Frame(source+"\nMirrorAll"+separator+"1,0,0\n",ObjectGraph.Selected)),expected,"Mirrored face winding corrupted the pivot frame");
                point=new(.25f,.5f,-.75f);editor.Send("TEST-PIVOT-SET","0.25 0.5 -0.75");phase++;step.Restart();break;
            case 1:
                if(Vector3.Distance(Pivot,point)>.002f)return;
                Untouched();if(!File.Exists(ObjectPivot.Sidecar))throw new Exception("Pivot sidecar was not saved");
                ObjectPivot.Open(Program.CurrentRouteFile);Near(Pivot,point,"Pivot did not survive sidecar reload");
                beforeDrag=Pivot;Drag(window,"pivot",0);phase++;step.Restart();break;
            case 2:
                if(Vector3.Distance(Pivot,beforeDrag)<.00001f)return;
                for(int i=0;i<3;i++)if(i!=axis&&Math.Abs(Pivot[i]-beforeDrag[i])>.002)throw new Exception("Pivot drag changed a different axis");
                Untouched();if(++axis<3){beforeDrag=Pivot;Drag(window,"pivot",axis);step.Restart();break;}
                point=Pivot;editor.Send("TEST-ROTATION");phase++;step.Restart();break;
            case 3:
                if(ObjectGraph.Source==source)return;
                Near(Pivot,point,"Rotation moved the chosen pivot");
                if(Geometry(true)==geometry||Geometry(false)!=others)throw new Exception("Rotation around pivot changed the wrong geometry");
                editor.Send("TEST-UNDO");phase++;step.Restart();break;
            case 4:
                if(ObjectGraph.Source!=source)return;
                Untouched();Near(Pivot,point,"Undo detached pivot");size=ObjectGraph.Dimensions(source,ObjectGraph.Selected);
                Drag(window,"scale",3);phase++;step.Restart();break;
            case 5:
                if(ObjectGraph.Source==source)return;
                Near(Pivot,point,"Uniform scaling moved the pivot");
                Near(ObjectGraph.Dimensions(ObjectGraph.Source,ObjectGraph.Selected),size*(float)Math.Exp(33*.007),"ALL scaling lost proportions around pivot");
                if(Geometry(false)!=others||!File.ReadAllBytes(Program.CurrentRouteFile).SequenceEqual(disk))throw new Exception("Pivot scaling affected another mesh or saved the model");
                editor.Send("TEST-UNDO");phase++;step.Restart();break;
            case 6:
                if(ObjectGraph.Source!=source)return;
                point=ObjectPivot.Center(source,ObjectGraph.Selected);editor.Send("TEST-PIVOT-CENTER");phase++;step.Restart();break;
            case 7:
                if(Vector3.Distance(Pivot,point)>.002f)return;
                Untouched();editor.TestPivot=null;editor.Send("TEST-READ-PIVOT");phase++;step.Restart();break;
            case 8:
                if(editor.TestPivot==null)return;
                var coordinates=editor.TestPivot.Split(' ').Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();
                if(coordinates.Length!=4||coordinates[3]!=0)throw new Exception("Pivot-only edits marked the code buffer as modified");
                Near(new(coordinates[0],coordinates[1],coordinates[2]),point,"Pivot coordinates did not update in GTK");
                editor.Send("TEST-SIZE",FormattableString.Invariant($"{size.X*1.1f:R} {size.Y*1.1f:R} {size.Z*1.1f:R}"));phase++;step.Restart();break;
            case 9:
                if(ObjectGraph.Source==source)return;
                Near(Pivot,point,"Dimension edit moved the pivot");
                editor.Send("TEST-UNDO");phase++;step.Restart();break;
            case 10:
                if(ObjectGraph.Source!=source)return;
                editor.Send("TEST-PIVOT-ORIGIN");phase++;step.Restart();break;
            case 11:
                if(Pivot.Length()>.002f)return;
                Untouched();ObjectPivot.Open(Program.CurrentRouteFile);Near(Pivot,Vector3.Zero,"Origin pivot was not persisted");
                metadata=File.ReadAllBytes(ObjectPivot.Sidecar);beforeMove=ObjectPivot.Center(source,ObjectGraph.Selected);Drag(window,"move",0);phase++;step.Restart();break;
            case 12:
                if(ObjectGraph.Source==source)return;
                Near(Pivot,ObjectPivot.Center(ObjectGraph.Source,ObjectGraph.Selected)-beforeMove,"Pivot did not follow mesh movement");
                if(!File.ReadAllBytes(ObjectPivot.Sidecar).SequenceEqual(metadata))throw new Exception("Moving geometry unexpectedly rewrote pivot metadata");
                editor.Send("TEST-UNDO");phase++;step.Restart();break;
            case 13:
                if(ObjectGraph.Source!=source)return;
                Untouched();Near(Pivot,Vector3.Zero,"Mesh undo did not restore anchor position");
                // Metadata conflicts must never overwrite someone else's edit.
                var external=metadata.Concat(new byte[]{10}).ToArray();File.WriteAllBytes(ObjectPivot.Sidecar,external);
                bool refused=false;try{ObjectPivot.SetWorld(source,ObjectGraph.Selected,Vector3.One);}catch(IOException){refused=true;}
                if(!refused||!File.ReadAllBytes(ObjectPivot.Sidecar).SequenceEqual(external))throw new Exception("External pivot metadata conflict was overwritten");
                loads=Program.LoadCount;Program.Reload=true;phase++;step.Restart();break;
            case 14:
                if(Program.LoadCount<=loads)return;
                Near(Pivot,Vector3.Zero,"Disk reload did not restore pivot metadata");
                using(var json=JsonDocument.Parse(File.ReadAllBytes(ObjectPivot.Sidecar)))if(json.RootElement.GetProperty("Pivots").EnumerateObject().Count()!=1)throw new Exception("Pivot edit leaked into other meshes");
                // Keep a useful centered marker visible after the test finishes.
                editor.Send("TEST-PIVOT-CENTER");phase++;step.Restart();break;
            case 15:
                if(Vector3.Distance(Pivot,ObjectPivot.Center(source,ObjectGraph.Selected))>.002f)return;
                Untouched();Click(window,"tool-pivot");Click(window,"axis-0");phase++;break;
        }
        if(Complete)Console.WriteLine("OBJECT_EDITOR_PIVOT_OK: origin marker, XYZ metadata-only edits, center/origin buttons, persistence, rotation/scale/dimensions around pivot, mesh movement, undo and metadata conflicts");
    }
}
