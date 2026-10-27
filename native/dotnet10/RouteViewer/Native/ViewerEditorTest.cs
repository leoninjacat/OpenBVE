using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
namespace RouteViewer;
internal static class ViewerEditorTest
{
    static int phase, initialLoads;
    internal static bool Complete => phase>=5;
    static Stopwatch timer,step;

    static Vector3 eye;
    internal static void Rendered(ViewerWindow window)
    {
        if(!Program.EditorTest || !Loading.Complete)return;
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
                editor.Send("TEST-EDIT","\n; Route Editor integration test — estação\n");phase++;break;
            case 4:
                if(Program.LoadCount<=initialLoads)return;
                if(!File.ReadAllText(Program.CurrentRouteFile,OpenBveApi.TextEncoding.GetSystemEncodingFromFile(Program.CurrentRouteFile)).Contains("Route Editor integration test"))throw new Exception("Editor did not save route");
                if(Vector3.Distance(eye,Program.Camera.Pose.Position)>.01)throw new Exception("Editor reload moved camera");
                Console.WriteLine("VIEWER_EDITOR_OK: side, bottom, detached, reattached, save from desktop editor, preview reload, camera preserved");phase++;break;
        }
    }
}
