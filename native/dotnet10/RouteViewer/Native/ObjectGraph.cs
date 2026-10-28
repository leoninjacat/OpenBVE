using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using OpenBveApi.Objects;
namespace RouteViewer;

// Source block IDs survive parsing, texture/material grouping and object cloning.
internal static class ObjectGraph
{
    internal static int Selected;
    internal static string Source = "";
    internal static Vector3 PendingMove;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly Regex Blocks = new(@"(?im)^[ \t]*(?:\[\s*(?:CreateMeshBuilder|MeshBuilder)\s*\]|(?:CreateMeshBuilder|MeshBuilder)(?=[,;\s]|$))[^\r\n]*");
    static readonly Regex ColorEdit = new(@"(?m)^; @openbve-color\r?\n[\s\S]*?^; @openbve-color-end[^\r\n]*(?:\r?\n)?");
    static readonly Regex Edit = new(@"(?m)^; @openbve-editor ([^\r\n]+)\r?\n[\s\S]*?^; @openbve-editor-end[^\r\n]*(?:\r?\n)?");
    internal static void Reset(string source) { Source=source;Selected=0;PendingMove=Vector3.Zero;ObjectTools.Reset(); }
    internal static (int Start,int End) Range(string text,int id)
    {
        var matches=Blocks.Matches(text);
        if(id<1||id>matches.Count)throw new InvalidOperationException("Selecione um bloco MeshBuilder explícito para editar.");
        return (matches[id-1].Index,id<matches.Count?matches[id].Index:text.Length);
    }
    internal static double[] Values(string text,int id)
    {
        var (start,end)=Range(text,id);
        var match=Edit.Match(text.Substring(start,end-start));
        var values=match.Success?match.Groups[1].Value.Split(' ').Select(v=>double.Parse(v,Inv)).ToArray():new double[]{0,0,0,0,0,0,1,1,1};
        if(values.Length==9)values=values.Concat(new double[]{1,1,1}).ToArray();
        if(values.Length!=12||values.Any(v=>!double.IsFinite(v)))throw new InvalidOperationException("Transformação inválida no código.");
        return values;
    }
    internal static string Transform(string text,int id,double[] values)
    {
        if(values.Length==9)values=values.Concat(Values(text,id).Skip(9)).ToArray();
        return TransformRaw(text,id,ObjectPivot.Compensate(text,id,values));
    }
    internal static string MeshKey(string text,int id)
    {
        string Base(int mesh) {var (a,b)=Range(text,mesh);return ColorEdit.Replace(Edit.Replace(text.Substring(a,b-a),""),"").Replace("\r\n","\n").Trim();}
        string block=Base(id);int duplicate=0;
        for(int i=1;i<id;i++)if(Base(i)==block)duplicate++;
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(block)))+":"+duplicate;
    }
    internal static int FrameInsertion(string text,int id)
    {
        var (a,b)=Range(text,id);var match=Edit.Match(text.Substring(a,b-a));return match.Success?a+match.Index:b;
    }
    internal static string TransformRaw(string text,int id,double[] values)
    {
        if(values.Length==9)values=values.Concat(Values(text,id).Skip(9)).ToArray();
        if(values.Length!=12||values.Any(v=>!double.IsFinite(v)||Math.Abs(v)>1000000)||values.Skip(6).Any(v=>v<=0))
            throw new InvalidOperationException("Use valores finitos e escala maior que zero.");
        var (start,end)=Range(text,id);
        string block=Edit.Replace(text.Substring(start,end-start),"");
        string nl=text.Contains("\r\n")?"\r\n":"\n";
        string sep=System.IO.Path.GetExtension(Program.CurrentRouteFile).Equals(".b3d",StringComparison.OrdinalIgnoreCase)?" ":",";
        string F(double v)=>v.ToString("G17",Inv);
        string Cmd(string command,params double[] args)=>command+sep+string.Join(",",args.Select(F))+nl;
        string addition="; @openbve-editor "+string.Join(" ",values.Select(F))+nl;
        addition+=Cmd("Scale",values[6],values[7],values[8]);
        // Both scale factors describe the unrotated shape. Scaling in world axes
        // after rotation would shear a nonuniformly resized cube or cylinder.
        addition+=Cmd("Scale",values[9],values[10],values[11]);
        addition+=Cmd("Rotate",1,0,0,values[3])+Cmd("Rotate",0,1,0,values[4])+Cmd("Rotate",0,0,1,values[5]);
        addition+=Cmd("Translate",values[0],values[1],values[2])+"; @openbve-editor-end"+nl;
        return text[..start]+block.TrimEnd('\r','\n')+nl+addition+text[end..];
    }
    internal static string SetColor(string text,int id,double[] rgba)
    {
        if(rgba.Length!=4||rgba.Any(v=>!double.IsFinite(v)||v<0||v>255))throw new InvalidOperationException("Cor RGBA inválida.");
        var (start,end)=Range(text,id);
        string block=ColorEdit.Replace(text.Substring(start,end-start),"");
        string nl=text.Contains("\r\n")?"\r\n":"\n";
        bool b3d=System.IO.Path.GetExtension(Program.CurrentRouteFile).Equals(".b3d",StringComparison.OrdinalIgnoreCase);
        string command=b3d?"Color ":"SetColor,";
        return text[..start]+block.TrimEnd('\r','\n')+nl+"; @openbve-color"+nl+command+string.Join(",",rgba.Select(v=>Math.Round(v).ToString(Inv)))+nl+"; @openbve-color-end"+nl+text[end..];
    }
    internal static StaticObject Parse(string text)
    {
        foreach(var plugin in Program.CurrentHost.Plugins)
            if(plugin.Object!=null&&plugin.Object.CanLoadObject(Program.CurrentRouteFile)&&plugin.Object.LoadObjectFromText(Program.CurrentRouteFile,text,out var obj)&&obj is StaticObject mesh)return mesh;
        throw new InvalidOperationException("Não foi possível medir o mesh.");
    }
    internal static Vector3 Dimensions(string text,int id)=>Dimensions(Parse(text),id);
    internal static Vector3 Dimensions(StaticObject obj,int id)
    {
        var min=new Vector3(float.MaxValue);var max=new Vector3(float.MinValue);
        foreach(var face in obj.Mesh.Faces)if(face.SourceMesh==id)
            foreach(var vertex in face.Vertices){var v=CameraRig.ToV(obj.Mesh.Vertices[vertex.Index].Coordinates);min=Vector3.Min(min,v);max=Vector3.Max(max,v);}
        if(min.X==float.MaxValue)throw new InvalidOperationException("Mesh sem geometria para medir.");
        return max-min;
    }
    internal static string Resize(string text,int id,double[] dimensions)
    {
        if(dimensions.Length!=3||dimensions.Any(v=>!double.IsFinite(v)||v<0||v>1000000))throw new InvalidOperationException("Use dimensões válidas em metros.");
        var desired=new Vector3((float)dimensions[0],(float)dimensions[1],(float)dimensions[2]);
        var values=Values(text,id);
        string candidate=text;
        float tolerance=Math.Max(.0001f,desired.Length()*1e-5f);
        // Solve local shape scales against world bounding dimensions. Measure through
        // the actual parser so rotation and later *All
        // commands are included, rather than assuming dimensions equal scale factors.
        for(int iteration=0;iteration<10;iteration++){
            var size=Dimensions(Parse(candidate),id);var error=desired-size;
            if(error.Length()<=tolerance)return candidate;
            if((desired.X==0&&size.X>tolerance)||(desired.Y==0&&size.Y>tolerance)||(desired.Z==0&&size.Z>tolerance))break;
            var columns=new Vector3[3];
            for(int axis=0;axis<3;axis++){
                var probe=(double[])values.Clone();probe[9+axis]*=Math.Exp(.001);
                columns[axis]=(Dimensions(Parse(Transform(text,id,probe)),id)-size)/.001f;
            }
            // Damped least squares also handles flat meshes with a zero-size axis.
            var system=new double[3,4];
            for(int row=0;row<3;row++){
                for(int col=0;col<3;col++)system[row,col]=Vector3.Dot(columns[row],columns[col])+(row==col?1e-9:0);
                system[row,3]=Vector3.Dot(columns[row],error);
            }
            for(int pivot=0;pivot<3;pivot++){
                int best=pivot;for(int row=pivot+1;row<3;row++)if(Math.Abs(system[row,pivot])>Math.Abs(system[best,pivot]))best=row;
                for(int col=0;col<4;col++)(system[pivot,col],system[best,col])=(system[best,col],system[pivot,col]);
                double divisor=system[pivot,pivot];if(Math.Abs(divisor)<1e-20)break;
                for(int col=pivot;col<4;col++)system[pivot,col]/=divisor;
                for(int row=0;row<3;row++)if(row!=pivot){double factor=system[row,pivot];for(int col=pivot;col<4;col++)system[row,col]-=factor*system[pivot,col];}
            }
            bool improved=false;
            for(double damping=1;damping>=1.0/32;damping/=2){
                var next=(double[])values.Clone();
                for(int axis=0;axis<3;axis++)next[9+axis]*=Math.Exp(Math.Clamp(system[axis,3]*damping,-2,2));
                if(next.Skip(9).Any(v=>!double.IsFinite(v)||v<=0||v>1000000))continue;
                string nextText=Transform(text,id,next);
                if(Vector3.Distance(Dimensions(Parse(nextText),id),desired)>=error.Length())continue;
                values=next;candidate=nextText;improved=true;break;
            }
            if(!improved)break;
        }
        throw new InvalidOperationException("Não foi possível obter essas dimensões com as transformações atuais. Verifique eixos planos ou comandos *All posteriores.");
    }
    internal static void Publish(ViewerWindow window)
    {
        if(!Program.ObjectMode)return;
        try {
            if(Selected==0){window.Editor.Send("MESH","");window.Editor.Send("PIVOT","");return;}
            var values=Values(Source,Selected);
            int faces=0;var vertices=new HashSet<Vector3>();
            var min=new Vector3(float.MaxValue);var max=new Vector3(float.MinValue);
            foreach(var state in Program.Renderer.StaticObjectStates)
                foreach(var face in state.Prototype.Mesh.Faces)if(face.SourceMesh==Selected){
                    faces++;
                    foreach(var vertex in face.Vertices){var v=CameraRig.ToV(state.Prototype.Mesh.Vertices[vertex.Index].Coordinates);vertices.Add(v);min=Vector3.Min(min,v);max=Vector3.Max(max,v);}
                }
            if(faces==0){Selected=0;window.Editor.Send("MESH","");return;}
            var size=max-min;
            var colors=Program.Renderer.StaticObjectStates.SelectMany(state=>state.Prototype.Mesh.Faces.Where(f=>f.SourceMesh==Selected).Select(f=>state.Prototype.Mesh.Materials[f.Material].Color)).GroupBy(c=>(c.R,c.G,c.B,c.A)).Select(g=>g.First()).ToArray();
            var color=colors[0];
            window.Editor.Send("MESH",$"{Selected}\t{vertices.Count}\t{faces}\t"+string.Join("\t",values.Take(9).Select(v=>v.ToString("G17",Inv)))+$"\t{size.X.ToString("R",Inv)}\t{size.Y.ToString("R",Inv)}\t{size.Z.ToString("R",Inv)}\t{color.R}\t{color.G}\t{color.B}\t{color.A}\t{(colors.Length>1?1:0)}");
            ObjectPivot.Publish(window);
            Program.Status=$"Mesh {Selected} · "+ObjectTools.Hint;
        }catch(Exception error){window.Editor.Send("GRAPH-ERROR",error.Message);}
    }
    internal static void DrawSelection(NewRenderer renderer,ViewerWindow window)
    {
        if(Selected==0||!window.InterfaceVisible)return;
        var pose=Program.Camera.Pose;float k=MathF.Tan((float)renderer.Camera.VerticalViewingAngle/2);
        float left=float.MaxValue,top=float.MaxValue,right=float.MinValue,bottom=float.MinValue;
        foreach(var state in renderer.StaticObjectStates)
            foreach(var face in state.Prototype.Mesh.Faces)if(face.SourceMesh==Selected)
                foreach(var vertex in face.Vertices){
                    var v=CameraRig.ToV(state.Prototype.Mesh.Vertices[vertex.Index].Coordinates)-pose.Position;
                    float depth=Vector3.Dot(v,pose.Forward);if(depth<=.01f)return;
                    float x=window.ViewWidth*.5f+Vector3.Dot(v,pose.Right)*window.ViewHeight/(2*k*depth);
                    float y=window.ViewHeight*.5f-Vector3.Dot(v,pose.Up)*window.ViewHeight/(2*k*depth);
                    left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);
                }
        if(left==float.MaxValue)return;
        left=Math.Clamp(left-5,0,window.ViewWidth);right=Math.Clamp(right+5,0,window.ViewWidth);
        top=Math.Clamp(top-5,0,window.ViewHeight);bottom=Math.Clamp(bottom+5,0,window.ViewHeight);
        var color=new OpenBveApi.Colors.Color128(.2f,.7f,1,1);
        void Line(float x,float y,float w,float h)=>renderer.Rectangle.Draw(null,new OpenBveApi.Math.Vector2(x,y),new OpenBveApi.Math.Vector2(w,h),color);
        Line(left,top,right-left,2);Line(left,bottom,right-left,2);Line(left,top,2,bottom-top);Line(right,top,2,bottom-top);
    }
    internal static bool Pick(ViewerWindow window,int x,int y)
    {
        var pose=Program.Camera.Pose;
        float k=MathF.Tan((float)Program.Renderer.Camera.VerticalViewingAngle/2);
        var ray=Vector3.Normalize(pose.Forward+pose.Right*((2f*x/window.ViewWidth-1)*k*window.ViewWidth/window.ViewHeight)+pose.Up*((1-2f*y/window.ViewHeight)*k));
        float nearest=float.MaxValue;int selected=0;
        foreach(var state in Program.Renderer.StaticObjectStates){
            var mesh=state.Prototype.Mesh;
            foreach(var face in mesh.Faces){
                if(face.SourceMesh<=0||face.Vertices.Length<3)continue;
                // Match the renderer topology, including cylinders triangulated by the parser.
                int count=face.Vertices.Length;
                var type=face.Flags&FaceFlags.FaceTypeMask;
                int step=type==FaceFlags.Triangles?3:type==FaceFlags.Quads?4:type==FaceFlags.QuadStrip?2:1;
                int triangles=type==FaceFlags.Triangles?count/3:type==FaceFlags.Quads?count/4*2:count-2;
                if(type==FaceFlags.QuadStrip)triangles=(count/2-1)*2;
                for(int i=0;i<triangles;i++){
                    int aIndex,bIndex,cIndex;
                    if(type==FaceFlags.Triangles){aIndex=i*step;bIndex=aIndex+1;cIndex=aIndex+2;}
                    else if(type==FaceFlags.Quads){int j=i/2*step;aIndex=j;bIndex=j+(i%2==0?1:2);cIndex=j+(i%2==0?2:3);}
                    else if(type==FaceFlags.TriangleStrip){aIndex=i;bIndex=i+1;cIndex=i+2;}
                    else if(type==FaceFlags.QuadStrip){int j=i/2*step;aIndex=j;bIndex=j+(i%2==0?1:3);cIndex=j+(i%2==0?3:2);}
                    else {aIndex=0;bIndex=i+1;cIndex=i+2;}
                    var a=CameraRig.ToV(mesh.Vertices[face.Vertices[aIndex].Index].Coordinates);
                    var b=CameraRig.ToV(mesh.Vertices[face.Vertices[bIndex].Index].Coordinates);
                    var c=CameraRig.ToV(mesh.Vertices[face.Vertices[cIndex].Index].Coordinates);
                    var edge=b-a;var edge2=c-a;var p=Vector3.Cross(ray,edge2);float det=Vector3.Dot(edge,p);
                    if(Math.Abs(det)<1e-8)continue;
                    float inverse=1/det;var t=pose.Position-a;float u=Vector3.Dot(t,p)*inverse;
                    if(u<0||u>1)continue;
                    var q=Vector3.Cross(t,edge);float v=Vector3.Dot(ray,q)*inverse;
                    if(v<0||u+v>1)continue;
                    float distance=Vector3.Dot(edge2,q)*inverse;
                    if(distance>0&&distance<nearest){nearest=distance;selected=face.SourceMesh;}
                }
            }
        }
        Selected=selected;Publish(window);return selected!=0;
    }
    internal static void Move(float dx,float dy,int height)
    {
        if(Selected==0)return;
        var pose=Program.Camera.Pose;
        float units=2*pose.Distance*MathF.Tan((float)Program.Renderer.Camera.VerticalViewingAngle/2)/Math.Max(1,height);
        PendingMove+=(pose.Right*dx-pose.Up*dy)*units;
    }
}
