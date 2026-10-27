using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
namespace RouteViewer;

// Runs against GTK and the rendered geometry after the existing editor smoke checks.
internal static class ViewerObjectToolsTest
{
    static int phase,index;
    static Stopwatch timer,step;
    static string baseline,others;
    static byte[] disk;
    static double[] initial;
    static ObjectTool tool;
    static int axis;
    internal static bool Complete=>phase>=7;
    static void Click(ViewerWindow window,string id)
    {
        ViewerOverlay.Build(window);var point=ViewerOverlay.Center(id);ViewerOverlay.Press(window,point.X,point.Y);
    }
    static string OtherGeometry()=>string.Join(";",Program.Renderer.StaticObjectStates.SelectMany(s=>s.Prototype.Mesh.Faces.Where(f=>f.SourceMesh!=ObjectGraph.Selected).SelectMany(f=>f.Vertices.Select(v=>s.Prototype.Mesh.Vertices[v.Index].Coordinates.ToString()))));
    internal static void Rendered(ViewerWindow window)
    {
        if(Complete)return;
        timer??=Stopwatch.StartNew();step??=Stopwatch.StartNew();
        if(timer.Elapsed.TotalSeconds>25)throw new Exception($"Object tools timed out: phase={phase}, tool={tool}, axis={axis}");
        if(step.Elapsed.TotalSeconds<.2)return;
        var editor=window.Editor;
        switch(phase){
            case 0:
                baseline=ObjectGraph.Source;initial=ObjectGraph.Values(baseline,ObjectGraph.Selected);others=OtherGeometry();disk=File.ReadAllBytes(Program.CurrentRouteFile);
                phase=1;goto case 1;
            case 1:
                tool=index<3?ObjectTool.Move:index<6?ObjectTool.Rotate:ObjectTool.Scale;
                axis=index==9?3:index%3;
                Click(window,"tool-"+(tool==ObjectTool.Move?"move":tool==ObjectTool.Rotate?"rotate":"scale"));
                if(ObjectTools.Axis!=-1||ObjectTools.BeginDrag(window))throw new Exception("Tool moved a mesh before an axis was chosen");
                ViewerOverlay.Build(window);
                if(ViewerOverlay.Controls.Any(c=>c.Id=="axis-3")!=(tool==ObjectTool.Scale))throw new Exception("ALL should only be available with Scale");
                ObjectTools.ChooseAxis(3);
                if(tool!=ObjectTool.Scale&&ObjectTools.Axis!=-1)throw new Exception("ALL was accepted by Move or Rotate");
                Click(window,"axis-"+axis);
                if(ObjectTools.Tool!=tool||ObjectTools.Axis!=axis||!ObjectTools.BeginDrag(window))throw new Exception("Toolbar tool/axis selection did not start a drag");
                window.Drag=DragMode.ObjectTransform;window.DragBy(30,-12);window.EndInteraction();
                // Changing the toolbar must not redirect movement already waiting on GTK.
                if(index==0){Click(window,"tool-rotate");Click(window,"axis-2");}
                phase=2;step.Restart();break;
            case 2:
                if(ObjectGraph.Source==baseline)return;
                var values=ObjectGraph.Values(ObjectGraph.Source,ObjectGraph.Selected);
                int first=tool==ObjectTool.Move?axis:tool==ObjectTool.Rotate?3+axis:axis==3?6:6+axis;
                int last=axis==3?8:first;
                for(int i=0;i<12;i++){
                    bool changed=Math.Abs(values[i]-initial[i])>1e-7;
                    if(changed!=(i>=first&&i<=last))throw new Exception($"Tool {tool}/{axis} changed the wrong property: {i}");
                }
                if(tool==ObjectTool.Rotate&&Math.Abs(Math.IEEERemainder(values[first]-initial[first],360)-21)>.001)throw new Exception("Rotation drag did not produce degrees on the chosen axis");
                if(axis==3){double factor=values[6]/initial[6];for(int i=7;i<=8;i++)if(Math.Abs(values[i]/initial[i]-factor)>.00001)throw new Exception("ALL scale changed proportions");}
                if(OtherGeometry()!=others)throw new Exception("Tool changed another mesh");
                if(!File.ReadAllBytes(Program.CurrentRouteFile).SequenceEqual(disk))throw new Exception("Tool unexpectedly saved the document");
                editor.TestTransform=null;editor.Send("TEST-READ-TRANSFORM");phase=3;step.Restart();break;
            case 3:
                if(editor.TestTransform==null)return;
                var actual=ObjectGraph.Values(ObjectGraph.Source,ObjectGraph.Selected);
                var reported=editor.TestTransform.Split(' ').Select(v=>double.Parse(v,CultureInfo.InvariantCulture)).ToArray();
                if(reported.Length!=9||reported.Where((v,i)=>Math.Abs(v-actual[i])>.002).Any())throw new Exception("GTK position/rotation/scale fields did not follow the tool");
                editor.Send("TEST-UNDO");phase=4;step.Restart();break;
            case 4:
                if(ObjectGraph.Source!=baseline)return;
                if(++index<10){phase=1;step.Restart();break;}
                editor.Send("TEST-ROTATION");phase=5;step.Restart();break;
            case 5:
                var rotation=ObjectGraph.Values(ObjectGraph.Source,ObjectGraph.Selected);
                if(Math.Abs(rotation[3]-17)>.001||Math.Abs(rotation[4]+23)>.001||Math.Abs(rotation[5]-41)>.001)return;
                if(OtherGeometry()!=others)throw new Exception("Rotation panel changed another mesh");
                editor.Send("TEST-UNDO");phase=6;step.Restart();break;
            case 6:
                if(ObjectGraph.Source!=baseline)return;
                phase=7;break;
        }
        if(Complete){
            ObjectTools.ChooseTool(ObjectTool.Rotate);ObjectTools.ChooseAxis(1);
            Console.WriteLine("OBJECT_EDITOR_TOOLS_OK: Move/Rotate/Scale XYZ, Scale ALL, explicit axis selection, queued tool capture, panel rotation XYZ, live code, independent meshes and undo");
        }
    }
}
