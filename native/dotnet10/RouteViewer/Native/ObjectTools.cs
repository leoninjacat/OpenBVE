using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
namespace RouteViewer;

internal enum ObjectTool { Select, Move, Rotate, Scale, Pivot }
internal static class ObjectTools
{
    internal sealed record Operation(int Mesh, ObjectTool Tool, int Axis, double Amount);
    internal static ObjectTool Tool { get; private set; }
    // -1 = waiting for an axis; 3 = uniform scale (ALL).
    internal static int Axis { get; private set; } = -1;
    static readonly List<Operation> pending = new();
    static Operation drag;
    static float unitsPerPixel;
    internal static string Name => Tool switch { ObjectTool.Move=>"Mover",ObjectTool.Rotate=>"Girar",ObjectTool.Scale=>"Escalar",ObjectTool.Pivot=>"Pivô",_=>"Selecionar" };
    internal static string Hint => ObjectGraph.Selected==0?"Clique em um mesh para selecionar":Tool==ObjectTool.Select?"Escolha uma ferramenta":Axis<0?Name+" · escolha X, Y ou Z"+(Tool==ObjectTool.Scale?" ou ALL":""):Name+" · "+(Axis==3?"ALL":"eixo "+"XYZ"[Axis])+(Tool==ObjectTool.Pivot?" · arraste o marcador ou o mesh":" · arraste o mesh");
    internal static void Reset() {Tool=ObjectTool.Select;Axis=-1;drag=null;pending.Clear();}
    internal static void ChooseTool(ObjectTool tool) { Tool=tool;Axis=-1;Program.Status=Hint; }
    internal static void ChooseAxis(int axis)
    {
        if(Tool==ObjectTool.Select||axis<0||axis>3||axis==3&&Tool!=ObjectTool.Scale)return;
        Axis=axis;Program.Status=Hint;
    }
    internal static bool BeginDrag(ViewerWindow window)
    {
        if(ObjectGraph.Selected==0||Tool==ObjectTool.Select||Axis<0){Program.Status=Hint;return false;}
        drag=new(ObjectGraph.Selected,Tool,Axis,0);
        // Use the selected geometry's depth so translation feels consistent at different zooms.
        var points=Program.Renderer.StaticObjectStates.SelectMany(s=>s.Prototype.Mesh.Faces.Where(f=>f.SourceMesh==drag.Mesh).SelectMany(f=>f.Vertices.Select(v=>CameraRig.ToV(s.Prototype.Mesh.Vertices[v.Index].Coordinates)))).ToArray();
        var center=Tool==ObjectTool.Pivot?ObjectPivot.World(ObjectGraph.Source,drag.Mesh):points.Length==0?Program.Camera.Pose.Target:points.Aggregate(Vector3.Zero,(a,b)=>a+b)/points.Length;
        float depth=Math.Max(.1f,Math.Abs(Vector3.Dot(center-Program.Camera.Pose.Position,Program.Camera.Pose.Forward)));
        unitsPerPixel=2*depth*MathF.Tan((float)Program.Renderer.Camera.VerticalViewingAngle/2)/Math.Max(1,window.ViewHeight);
        return true;
    }
    internal static void EndDrag()=>drag=null;
    internal static void Drag(float dx,float dy)
    {
        if(drag==null||dx==0&&dy==0)return;
        double amount;
        if(drag.Tool is ObjectTool.Move or ObjectTool.Pivot){
            var axis=drag.Axis==0?Vector3.UnitX:drag.Axis==1?Vector3.UnitY:Vector3.UnitZ;
            float sx=Vector3.Dot(axis,Program.Camera.Pose.Right),sy=-Vector3.Dot(axis,Program.Camera.Pose.Up);
            float length=sx*sx+sy*sy;
            // An axis pointing into the camera has almost no screen projection.
            // In that case, dragging up/right still moves along its positive direction.
            float pixels=length>.05f?(dx*sx+dy*sy)/length:(dx-dy)*.7071068f;
            amount=pixels*unitsPerPixel;
        }else amount=(dx-dy)*(drag.Tool==ObjectTool.Rotate?.5:.007);
        if(!double.IsFinite(amount)||amount==0)return;
        var operation=drag with { Amount=amount };
        if(pending.Count>0&&pending[^1].Mesh==operation.Mesh&&pending[^1].Tool==operation.Tool&&pending[^1].Axis==operation.Axis)
            pending[^1]=operation with {Amount=pending[^1].Amount+amount};
        else pending.Add(operation);
    }
    internal static bool TryTake(out Operation operation)
    {
        operation=null;if(pending.Count==0)return false;
        operation=pending[0];pending.RemoveAt(0);return true;
    }
    internal static string Apply(string text,int mesh,ObjectTool tool,int axis,double amount)
    {
        if(tool is not (ObjectTool.Move or ObjectTool.Rotate or ObjectTool.Scale)||axis<0||axis>3||axis==3&&tool!=ObjectTool.Scale||!double.IsFinite(amount))throw new InvalidOperationException("Selecione uma ferramenta e um eixo válido.");
        var values=ObjectGraph.Values(text,mesh);
        if(tool==ObjectTool.Move)values[axis]+=amount;
        else if(tool==ObjectTool.Rotate)values[3+axis]=Math.IEEERemainder(values[3+axis]+amount,360);
        else {
            int first=axis==3?6:6+axis,last=axis==3?8:first;
            double min=double.MaxValue,max=0;
            for(int i=first;i<=last;i++){min=Math.Min(min,values[i]);max=Math.Max(max,values[i]);}
            double factor=Math.Clamp(Math.Exp(Math.Clamp(amount,-50,50)),.000001/min,1000000/max);
            for(int i=first;i<=last;i++)values[i]*=factor;
        }
        return ObjectGraph.Transform(text,mesh,values);
    }
}
