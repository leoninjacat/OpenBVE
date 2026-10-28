using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using OpenTK.Input;
namespace RouteViewer;

internal static class ViewerMeshActionsTest
{
    static int phase;
    static Stopwatch timer,step;
    static string baseline,named,geometry,key;
    static byte[] disk,metadata;
    const string Label="Porta · ação 🚉";
    internal static bool Complete=>phase>=7;
    static string Geometry(string text)=>string.Join(";",ObjectGraph.Parse(text).Mesh.Vertices.Select(v=>v.Coordinates.ToString()));
    static void CheckLink(string report,string expectedName){
        var fields=report.Split(' ');var (a,b)=ObjectGraph.Range(ObjectGraph.Source,1);
        if(int.Parse(fields[0])!=1||int.Parse(fields[1])!=Encoding.UTF8.GetByteCount(ObjectGraph.Source[..a])||int.Parse(fields[2])!=Encoding.UTF8.GetByteCount(ObjectGraph.Source[..b]))throw new Exception("GTK did not highlight the selected mesh code range");
        if(Encoding.UTF8.GetString(Convert.FromBase64String(fields[3]))!=expectedName)throw new Exception("GTK name field differs from the mesh comment");
    }
    internal static void Rendered(ViewerWindow window){
        if(Complete)return;
        timer??=Stopwatch.StartNew();step??=Stopwatch.StartNew();
        if(timer.Elapsed.TotalSeconds>15)throw new Exception("Mesh actions timed out at phase "+phase);
        if(step.Elapsed.TotalSeconds<.3)return;
        var editor=window.Editor;
        switch(phase){
            case 0:
                baseline=ObjectGraph.Source;geometry=Geometry(baseline);key=ObjectGraph.MeshKey(baseline,1);disk=File.ReadAllBytes(Program.CurrentRouteFile);
                metadata=File.Exists(ObjectPivot.Sidecar)?File.ReadAllBytes(ObjectPivot.Sidecar):null;
                // A global transform located in a deleted builder must survive.
                string synthetic="; cabeçalho 🚉\n"+(Path.GetExtension(Program.CurrentRouteFile)==".b3d"?"[MeshBuilder]\nCube 1,1,1\n[MeshBuilder]\nCube 2,2,2\nRotateAll 0,1,0,45\n":"CreateMeshBuilder\nCube,1,1,1\nCreateMeshBuilder\nCube,2,2,2\nRotateAll,0,1,0,45\n");
                string removed=ObjectGraph.Delete(synthetic,2);
                if(!removed.Contains("RotateAll")||ObjectGraph.Parse(removed).Mesh.Faces.Length!=6)throw new Exception("Deleting a builder lost global commands or other geometry");
                ObjectGraph.Selected=1;ObjectGraph.Publish(window);ObjectGraph.PublishCode(window,true);editor.TestMeshLink=null;editor.Send("TEST-READ-MESH-LINK");phase++;step.Restart();break;
            case 1:
                if(editor.TestMeshLink==null)return;CheckLink(editor.TestMeshLink,ObjectGraph.Name(baseline,1));
                editor.Send("TEST-NAME",Label);phase++;step.Restart();break;
            case 2:
                if(ObjectGraph.Name(ObjectGraph.Source,1)!=Label)return;
                named=ObjectGraph.Source;
                if(Geometry(named)!=geometry||ObjectGraph.MeshKey(named,1)!=key)throw new Exception("Renaming changed geometry or pivot identity");
                editor.TestMeshLink=null;editor.Send("TEST-READ-MESH-LINK");phase++;step.Restart();break;
            case 3:
                if(editor.TestMeshLink==null)return;CheckLink(editor.TestMeshLink,Label);
                window.HandleKey(Key.Delete,false);phase++;step.Restart();break;
            case 4:
                if(ObjectGraph.Source==named)return;
                if(ObjectGraph.Source!=ObjectGraph.Delete(named,1)||ObjectGraph.Selected!=0)throw new Exception("Delete did not remove exactly the selected builder and clear selection");
                editor.Send("TEST-UNDO");phase++;step.Restart();break;
            case 5:
                if(ObjectGraph.Source!=named)return;
                if(Geometry(ObjectGraph.Source)!=geometry)throw new Exception("Undo did not restore deleted geometry");
                editor.Send("TEST-UNDO");phase++;step.Restart();break;
            case 6:
                if(ObjectGraph.Source!=baseline)return;
                if(!File.ReadAllBytes(Program.CurrentRouteFile).SequenceEqual(disk))throw new Exception("Mesh actions unexpectedly saved the model");
                if(metadata!=null&&!File.ReadAllBytes(ObjectPivot.Sidecar).SequenceEqual(metadata))throw new Exception("Mesh actions changed pivot metadata");
                ObjectGraph.Selected=1;ObjectGraph.Publish(window);phase++;
                Console.WriteLine("OBJECT_EDITOR_MESH_ACTIONS_OK: GTK code highlight, Unicode names/comments, stable pivots, viewport Delete, globals preserved, geometry and name undo, no disk writes");break;
        }
    }
}
