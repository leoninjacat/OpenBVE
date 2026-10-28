using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using OpenBveApi.Colors;
using OpenBveApi.Graphics;
using V2=OpenBveApi.Math.Vector2;
using W=OpenBveApi.Math.Vector3;
namespace RouteViewer;

// Pivot positions are editor metadata in the mesh's pre-editor coordinate frame.
// Only a real transform writes compensation into the ordinary B3D/CSV commands.
internal static class ObjectPivot
{
    sealed class Settings
    {
        public int Version {get;set;}=1;
        public Dictionary<string,double[]> Pivots {get;set;}=new();
    }
    static Settings settings=new();
    static byte[] original;
    static string loadError,cachedSource;
    static readonly Dictionary<int,string> keys=new();
    static readonly Dictionary<int,Matrix4x4> frames=new();
    internal static string Sidecar {get;private set;}
    internal static void Open(string path)
    {
        string canonical=File.ResolveLinkTarget(path,true)?.FullName??Path.GetFullPath(path);
        Sidecar=canonical+".objecteditor.json";settings=new();original=null;loadError=null;cachedSource=null;keys.Clear();frames.Clear();
        try {
            if(!File.Exists(Sidecar))return;
            original=File.ReadAllBytes(Sidecar);
            var loaded=JsonSerializer.Deserialize<Settings>(original);
            if(loaded==null||loaded.Version!=1||loaded.Pivots==null||loaded.Pivots.Any(p=>p.Value==null||p.Value.Length!=3||p.Value.Any(v=>!double.IsFinite(v))))throw new InvalidDataException("Formato de pivôs inválido.");
            settings=loaded;
        }catch(Exception e){loadError=e.Message;Console.Error.WriteLine("Pivot metadata: "+e.Message);}
    }
    static void Cache(string text)
    {
        if(ReferenceEquals(text,cachedSource))return;
        cachedSource=text;keys.Clear();frames.Clear();
    }
    static string Key(string text,int mesh)
    {
        Cache(text);if(!keys.TryGetValue(mesh,out string key))keys[mesh]=key=ObjectGraph.MeshKey(text,mesh);return key;
    }
    internal static Vector3 Local(string text,int mesh)
    {
        if(!settings.Pivots.TryGetValue(Key(text,mesh),out var point))return Vector3.Zero;
        return new((float)point[0],(float)point[1],(float)point[2]);
    }
    internal static Matrix4x4 Frame(string text,int mesh)
    {
        Cache(text);if(frames.TryGetValue(mesh,out var cached))return cached;
        bool b3d=Path.GetExtension(Program.CurrentRouteFile).Equals(".b3d",StringComparison.OrdinalIgnoreCase);
        string frame=b3d?"\n[MeshBuilder]\nVertex 0,0,0\nVertex 1,0,0\nVertex 0,1,0\nVertex 0,0,1\nFace 0,1,2,3\n":"\nCreateMeshBuilder\nAddVertex,0,0,0\nAddVertex,1,0,0\nAddVertex,0,1,0\nAddVertex,0,0,1\nAddFace,0,1,2,3\n";
        int insertion=ObjectGraph.FrameInsertion(text,mesh);
        var obj=ObjectGraph.Parse(text.Insert(insertion,frame));
        // A temporary, non-rendered builder inherits the editor commands and all
        // later *All commands, giving us the exact local-to-model affine frame.
        var face=obj.Mesh.Faces.First(f=>f.SourceMesh==mesh+1);
        // Mirrors and negative scales reverse face winding, but preserve vertex IDs.
        var indices=face.Vertices.Select(v=>v.Index).OrderBy(v=>v).ToArray();
        Vector3 Point(int index)=>CameraRig.ToV(obj.Mesh.Vertices[indices[index]].Coordinates);
        var origin=Point(0);var x=Point(1)-origin;var y=Point(2)-origin;var z=Point(3)-origin;
        var matrix=new Matrix4x4(x.X,x.Y,x.Z,0,y.X,y.Y,y.Z,0,z.X,z.Y,z.Z,0,origin.X,origin.Y,origin.Z,1);
        frames[mesh]=matrix;return matrix;
    }
    internal static Vector3 World(string text,int mesh)=>Vector3.Transform(Local(text,mesh),Frame(text,mesh));
    internal static Vector3 Default(string text,int mesh)=>Vector3.Transform(Vector3.Zero,Frame(text,mesh));
    internal static Vector3 Center(string text,int mesh)
    {
        var obj=ObjectGraph.Parse(text);var min=new Vector3(float.MaxValue);var max=new Vector3(float.MinValue);
        foreach(var face in obj.Mesh.Faces)if(face.SourceMesh==mesh)
            foreach(var vertex in face.Vertices){var p=CameraRig.ToV(obj.Mesh.Vertices[vertex.Index].Coordinates);min=Vector3.Min(min,p);max=Vector3.Max(max,p);}
        if(min.X==float.MaxValue)throw new InvalidOperationException("Mesh sem geometria.");
        return (min+max)*.5f;
    }
    internal static void SetWorld(string text,int mesh,Vector3 world)
    {
        if(!float.IsFinite(world.X)||!float.IsFinite(world.Y)||!float.IsFinite(world.Z)||Math.Abs(world.X)>1000000||Math.Abs(world.Y)>1000000||Math.Abs(world.Z)>1000000)throw new InvalidOperationException("Use coordenadas de pivô válidas, em metros.");
        string current=File.ResolveLinkTarget(Program.CurrentRouteFile,true)?.FullName??Path.GetFullPath(Program.CurrentRouteFile);
        if(Sidecar!=current+".objecteditor.json")throw new InvalidOperationException("Carregue o objeto antes de configurar seu pivô.");
        if(!Matrix4x4.Invert(Frame(text,mesh),out var inverse))throw new InvalidOperationException("As transformações atuais não permitem posicionar o pivô.");
        var local=Vector3.Transform(world,inverse);
        if(!float.IsFinite(local.X)||!float.IsFinite(local.Y)||!float.IsFinite(local.Z))throw new InvalidOperationException("Pivô fora dos limites numéricos do modelo.");
        if(Sidecar==null||loadError!=null)throw new IOException("Não é possível salvar os pivôs. Verifique o arquivo auxiliar e reabra o objeto. "+loadError);
        var disk=File.Exists(Sidecar)?File.ReadAllBytes(Sidecar):null;
        if(!(disk==null&&original==null)&&!(disk!=null&&original!=null&&disk.AsSpan().SequenceEqual(original)))throw new IOException("O arquivo auxiliar de pivôs foi alterado externamente. Reabra o objeto antes de editar o pivô.");
        string key=Key(text,mesh);var next=new Settings {Pivots=new(settings.Pivots)};
        next.Pivots[key]=new double[]{local.X,local.Y,local.Z};
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(next,new JsonSerializerOptions {WriteIndented=true});
        string temporary=Sidecar+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {File.WriteAllBytes(temporary,bytes);if(original!=null&&!OperatingSystem.IsWindows())File.SetUnixFileMode(temporary,File.GetUnixFileMode(Sidecar));File.Move(temporary,Sidecar,true);}
        finally {if(File.Exists(temporary))File.Delete(temporary);}
        settings=next;original=bytes;
    }
    static W Linear(double[] values,Vector3 point)
    {
        var p=new W(point.X*values[6]*values[9],point.Y*values[7]*values[10],point.Z*values[8]*values[11]);
        p.Rotate(new W(1,0,0),values[3]*Math.PI/180);p.Rotate(new W(0,1,0),values[4]*Math.PI/180);p.Rotate(new W(0,0,1),values[5]*Math.PI/180);
        return p;
    }
    internal static double[] Compensate(string text,int mesh,double[] values)
    {
        if(values.Length!=12)return values;
        var point=Local(text,mesh);if(point==Vector3.Zero)return values;
        var old=ObjectGraph.Values(text,mesh);
        var before=Linear(old,point);var after=Linear(values,point);
        var result=(double[])values.Clone();result[0]+=before.X-after.X;result[1]+=before.Y-after.Y;result[2]+=before.Z-after.Z;return result;
    }
    internal static void Publish(ViewerWindow window)
    {
        if(ObjectGraph.Selected==0){window.Editor.Send("PIVOT","");return;}
        try {
            var point=World(ObjectGraph.Source,ObjectGraph.Selected);
            string label=Local(ObjectGraph.Source,ObjectGraph.Selected)==Vector3.Zero?"Ancoragem padrão da mesh":"Pivô personalizado · configuração do editor";
            window.Editor.Send("PIVOT",FormattableString.Invariant($"{ObjectGraph.Selected}\t{point.X:R}\t{point.Y:R}\t{point.Z:R}\t{label}"));
            if(loadError!=null)window.Editor.Send("PIVOT-ERROR",loadError);
        }catch(Exception e){window.Editor.Send("PIVOT-ERROR",e.Message);}
    }
    static bool Project(ViewerWindow window,Vector3 world,out Vector2 screen,out bool outside)
    {
        var p=world-Program.Camera.Pose.Position;float depth=Vector3.Dot(p,Program.Camera.Pose.Forward);
        if(depth<=.01f){screen=default;outside=true;return false;}
        float factor=window.ViewHeight/(2*depth*MathF.Tan((float)Program.Renderer.Camera.VerticalViewingAngle/2));
        float x=window.ViewWidth*.5f+Vector3.Dot(p,Program.Camera.Pose.Right)*factor,y=window.ViewHeight*.5f-Vector3.Dot(p,Program.Camera.Pose.Up)*factor;
        screen=new(Math.Clamp(x,18,Math.Max(18,window.ViewWidth-18)),Math.Clamp(y,112,Math.Max(112,window.ViewHeight-42)));
        outside=screen.X!=x||screen.Y!=y;return true;
    }
    internal static bool Hit(ViewerWindow window,float x,float y)
    {
        if(ObjectGraph.Selected==0)return false;
        try{return Project(window,World(ObjectGraph.Source,ObjectGraph.Selected),out var point,out _)&&Vector2.Distance(point,new(x,y))<=16;}
        catch{return false;}
    }
    internal static void Draw(NewRenderer renderer,ViewerWindow window)
    {
        if(!window.InterfaceVisible||ObjectGraph.Selected==0)return;
        try {
            var point=World(ObjectGraph.Source,ObjectGraph.Selected);var origin=Default(ObjectGraph.Source,ObjectGraph.Selected);
            void Marker(Vector3 position,string label,Color128 color){
                if(!Project(window,position,out var screen,out bool outside)){
                    float y=window.ViewHeight-(label.StartsWith("Ancoragem",StringComparison.Ordinal)?130:108);
                    renderer.OpenGlString.Draw(renderer.Fonts.SmallFont,label+" atrás da câmera",new V2(76,y),TextAlignment.TopLeft,color);return;
                }
                if(outside)label+=" (fora da vista)";
                ViewerIcons.Draw(renderer,"pivot",screen.X-11,screen.Y-11,22,22,color);
                renderer.OpenGlString.Draw(renderer.Fonts.SmallFont,label,new V2(Math.Clamp(screen.X+14,4,Math.Max(4,window.ViewWidth-180)),screen.Y+12),TextAlignment.TopLeft,color);
            }
            if(Vector3.Distance(point,origin)>.00001f)Marker(origin,"Ancoragem padrão",new Color128(.75f,.78f,.82f,1));
            Marker(point,Vector3.Distance(point,origin)<.00001f?"Pivô / padrão":"Pivô",new Color128(1,.72f,.18f,1));
        }catch{/* Parser diagnostics remain in the panel; do not interrupt rendering. */}
    }
}
