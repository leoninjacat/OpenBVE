using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
namespace RouteViewer;

internal sealed class RouteEditor : IDisposable
{
    private Process process;
    private System.Threading.Channels.Channel<string> outgoing;
    private System.Threading.Tasks.Task writer;
    private RouteDocument document;
    private readonly ConcurrentQueue<string> routes = new();
    private readonly ConcurrentQueue<string> messages = new();
    internal string Layout = "float";
    internal int CanvasMode=-1,CanvasWidth,CanvasHeight;
    internal bool CanvasMatches(ViewerWindow window,int mode) => CanvasMode==mode && window.Width==CanvasWidth && window.Height==CanvasHeight && Program.Renderer.Screen.Width==CanvasWidth && Program.Renderer.Screen.Height==CanvasHeight;
    internal bool Visible=true;
    internal void ToggleVisible() {Visible=!Visible;Send("VISIBLE",Visible?"1":"0");}
    internal bool CloseApproved;
    private bool replacingRoute;
    private bool graphMovePending;
    internal string TestProperties,TestTransform,TestPivot;
    private long lastGraphMove;
    internal bool Running => process != null && !process.HasExited;
    internal void Open(ViewerWindow window, string path)
    {
        if (Program.SmokeSeconds>0 && !Program.EditorTest && !Program.FeedbackTest && !Program.KeyboardTest) return;
        try {
            // Reloading the preview must not replace the editor's undo history.
            if (path != null && !replacingRoute && document?.Path == (File.ResolveLinkTarget(path,true)?.FullName ?? Path.GetFullPath(path)) && Running) return;
            var encoding=path==null?Encoding.UTF8:OpenBveApi.TextEncoding.GetSystemEncodingFromFile(path);
            // Empty / ASCII-only objects have no encoding marker. UTF-8 keeps all
            // existing bytes unchanged and permits accented comments in new files.
            if(Program.ObjectMode&&encoding.CodePage==Encoding.ASCII.CodePage)encoding=new UTF8Encoding(false,true);
            var next=path == null ? null : RouteDocument.Open(path,encoding);
            if (!Running) {
                var start=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,OperatingSystem.IsWindows()?"RouteViewer.Desktop.exe":"route-editor")) {
                    UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true
                };
                if (Program.ObjectMode) start.Environment["OPENBVE_OBJECT_EDITOR"]="1";
                start.ArgumentList.Add(window.WindowInfo.Handle.ToInt64().ToString());
                process=new Process { StartInfo=start,EnableRaisingEvents=true };
                process.OutputDataReceived+=(_,e)=>{if(e.Data!=null)messages.Enqueue(e.Data);};
                if(Program.EditorTest||Program.FeedbackTest||Program.KeyboardTest)start.ArgumentList.Add("--test");
                process.Start();process.BeginOutputReadLine();
                outgoing=System.Threading.Channels.Channel.CreateUnbounded<string>(new() {SingleReader=true,SingleWriter=true});
                var channel=outgoing;
                var input=process.StandardInput;
                writer=System.Threading.Tasks.Task.Run(async ()=> {
                    try {
                        await foreach(var line in channel.Reader.ReadAllAsync()) {
                            await input.WriteLineAsync(line);
                            await input.FlushAsync();
                        }
                    } catch(Exception error) when(error is IOException || error is ObjectDisposedException) {
                        Console.Error.WriteLine("Editor IPC: "+error.Message);
                    }
                });
            }
            document=next;replacingRoute=false;graphMovePending=false;
            Send("PATH",path ?? "Novo objeto");Send("TEXT",document?.Text ?? "");Resize(window);
        } catch(Exception error) { Program.Status="Editor: "+error.Message;Console.Error.WriteLine(Program.Status); }
    }
    internal void Send(string command,string value="")
    {
        if(!Running)return;
        // A large route must never block the render/UI thread on a pipe write.
        // Windows parenting sends synchronous window messages to that thread.
        outgoing?.Writer.TryWrite(command+"\t"+Convert.ToBase64String(Encoding.UTF8.GetBytes(value)));
    }
    internal void Resize(ViewerWindow window)=>Send("RECT",$"{window.Width} {window.Height}");
    internal void RequestRoute(string path) => routes.Enqueue(path);
    internal void Pump(ViewerWindow window)
    {
        if(Program.ObjectMode && ObjectGraph.PendingMove.LengthSquared()>0 && !graphMovePending && Environment.TickCount64-lastGraphMove>100) {
            graphMovePending=true;lastGraphMove=Environment.TickCount64;
            var delta=ObjectGraph.PendingMove;ObjectGraph.PendingMove=System.Numerics.Vector3.Zero;
            Send("MOVE-REQUEST",FormattableString.Invariant($"{ObjectGraph.Selected} {delta.X:R} {delta.Y:R} {delta.Z:R}"));
        }
        if(Program.ObjectMode && !graphMovePending && Environment.TickCount64-lastGraphMove>100 && ObjectTools.TryTake(out var operation)) {
            graphMovePending=true;lastGraphMove=Environment.TickCount64;
            Send("TOOL-REQUEST",FormattableString.Invariant($"{operation.Mesh} {(int)operation.Tool} {operation.Axis} {operation.Amount:R}"));
        }
        while(routes.TryDequeue(out var route)) {if(Running)Send("ROUTE",route);else Program.PendingRoute=route;}
        while(messages.TryDequeue(out var message)) {
            int tab=message.IndexOf('\t');if(tab<0)continue;
            string command=message[..tab],value;
            try {value=Encoding.UTF8.GetString(Convert.FromBase64String(message[(tab+1)..]));}
            catch(FormatException){continue;}
            switch(command) {
                case "CANVAS":
                    var dimensions=value.Split(' ');
                    if(dimensions.Length==3&&int.TryParse(dimensions[0],out var mode)&&int.TryParse(dimensions[1],out var width)&&int.TryParse(dimensions[2],out var height)) {CanvasMode=mode;CanvasWidth=width;CanvasHeight=height;}
                    break;
                case "HOST":if(Program.EditorTest||Program.FeedbackTest)Console.WriteLine("VIEWER_EDITOR_HOST: "+value);break;
                case "THEME":window.DarkTheme=value=="dark";break;
                case "TOGGLE-EDITOR":ToggleVisible();break;
                case "TOGGLE-UI":window.ToggleInterface();break;
                case "VISIBLE":Visible=value=="1";break;
                case "OPEN":Program.OpenPicker();break;
                case "RELOAD":Program.Reload=true;break;
                case "LAYOUT":Layout=value;window.UpdateCanvas();break;
                case "ROUTE":replacingRoute=true;Program.PendingRoute=value;break;
                case "QUIT":CloseApproved=true;window.Close();break;
                case "TEST-PIVOT":if(Program.EditorTest&&Program.ObjectMode)TestPivot=value;break;
                case "TEST-TRANSFORM":if(Program.EditorTest&&Program.ObjectMode)TestTransform=value;break;
                case "TEST-PROPERTIES":if(Program.EditorTest&&Program.ObjectMode)TestProperties=value;break;
                case "PREVIEW":
                    if(Program.ObjectMode){graphMovePending=false;ObjectPreview.Load(value);}
                    break;
                case "GRAPH-SET":
                case "GRAPH-MOVE":
                case "GRAPH-TOOL":
                case "GRAPH-PIVOT":
                    if(!Program.ObjectMode)break;
                    try {
                        int newline=value.IndexOf('\n');if(newline<0)break;
                        var parts=value[..newline].Split(' ',StringSplitOptions.RemoveEmptyEntries);
                        int id=int.Parse(parts[0],System.Globalization.CultureInfo.InvariantCulture);
                        string text=value[(newline+1)..];
                        double Number(int index)=>double.Parse(parts[index],System.Globalization.CultureInfo.InvariantCulture);
                        string updated;
                        if(command=="GRAPH-PIVOT"){
                            if(parts.Length!=2||parts[1] is not ("origin" or "center"))throw new InvalidOperationException("Comando de pivô inválido.");
                            ObjectPivot.SetWorld(text,id,parts[1]=="origin"?System.Numerics.Vector3.Zero:ObjectPivot.Center(text,id));
                            ObjectPivot.Publish(window);graphMovePending=false;Send("PIVOT-DONE");break;
                        }else if(command=="GRAPH-TOOL"){
                            if(parts.Length!=4)throw new InvalidOperationException("Manipulação incompleta.");
                            var tool=(ObjectTool)int.Parse(parts[1],System.Globalization.CultureInfo.InvariantCulture);
                            int axis=int.Parse(parts[2],System.Globalization.CultureInfo.InvariantCulture);
                            if(tool==ObjectTool.Pivot){
                                if(axis<0||axis>2)throw new InvalidOperationException("Escolha X, Y ou Z para mover o pivô.");
                                var point=ObjectPivot.World(text,id);if(axis==0)point.X+=(float)Number(3);else if(axis==1)point.Y+=(float)Number(3);else point.Z+=(float)Number(3);
                                ObjectPivot.SetWorld(text,id,point);ObjectPivot.Publish(window);graphMovePending=false;Send("PIVOT-DONE");break;
                            }
                            updated=ObjectTools.Apply(text,id,tool,axis,Number(3));
                        }else if(command=="GRAPH-MOVE"){
                            var values=ObjectGraph.Values(text,id);
                            for(int i=0;i<3;i++)values[i]+=Number(i+1);
                            updated=ObjectGraph.Transform(text,id,values);
                        }else{
                            if(parts.Length!=21)throw new InvalidOperationException("Propriedades incompletas.");
                            int edits=int.Parse(parts[1],System.Globalization.CultureInfo.InvariantCulture);
                            updated=text;
                            if((edits&8)!=0){
                                ObjectPivot.SetWorld(text,id,new System.Numerics.Vector3((float)Number(18),(float)Number(19),(float)Number(20)));
                                ObjectPivot.Publish(window);
                                if((edits&7)==0){graphMovePending=false;Send("PIVOT-DONE");break;}
                            }
                            if((edits&1)!=0)updated=ObjectGraph.Transform(updated,id,System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(2,9),Number)));
                            if((edits&2)!=0)updated=ObjectGraph.Resize(updated,id,new[]{Number(11),Number(12),Number(13)});
                            if((edits&4)!=0)updated=ObjectGraph.SetColor(updated,id,new[]{Number(14),Number(15),Number(16),Number(17)});
                        }
                        Send("PATCH",Convert.ToBase64String(Encoding.UTF8.GetBytes(text))+"\n"+updated);
                    } catch(Exception error){graphMovePending=false;Send("GRAPH-ERROR",error.Message);}
                    break;
                case "NEW-EMPTY":
                case "NEW":
                    try {
                        if(!Program.ObjectMode || (Path.GetExtension(value).ToLowerInvariant() is not (".b3d" or ".csv")))throw new IOException("Escolha um arquivo B3D ou CSV.");
                        using(var file=new FileStream(value,FileMode.CreateNew,FileAccess.Write)) {
                            var bytes=Encoding.UTF8.GetBytes(command=="NEW-EMPTY"?"":"; Novo objeto OpenBVE\n"+(Path.GetExtension(value).Equals(".csv",StringComparison.OrdinalIgnoreCase)?"CreateMeshBuilder\n":"[MeshBuilder]\n"));file.Write(bytes);
                        }
                        replacingRoute=true;Program.PendingRoute=value;
                    } catch(Exception error){Send("ERROR",error.Message);}
                    break;
                case "SAVE":
                    try {if(document==null)throw new IOException("Crie ou abra um objeto antes de salvar.");document.Save(value);Send("SAVED");Program.Reload=true;}
                    catch(Exception error){Send("ERROR","Não foi possível salvar: "+error.Message);}
                    break;
            }
        }
        if(process!=null && process.HasExited && Layout!="float") {Layout="float";window.UpdateCanvas();}
    }
    public void Dispose() {Send("EXIT");outgoing?.Writer.TryComplete();if(Running)process.WaitForExit(1500);process?.Dispose();process=null;}
}
