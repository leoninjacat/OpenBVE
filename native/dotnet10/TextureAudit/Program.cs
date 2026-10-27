using System.Text.Json;
using System.Reflection;
using System.Security.Cryptography;
using OpenBveApi.Hosts;
using OpenBveApi.Textures;

if(args.Length<3)throw new ArgumentException("TextureAudit ROOT REPORT.json PLUGIN.dll");
string root=Path.GetFullPath(args[0]);
var host=new AuditHost();
string pluginPath=Path.GetFullPath(args[2]);
var assembly=Assembly.LoadFrom(pluginPath);
if(!string.Equals(assembly.Location,pluginPath,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Audit loaded a different texture plugin");
var plugin=(TextureInterface)Activator.CreateInstance(assembly.GetTypes().Single(t=>!t.IsAbstract&&typeof(TextureInterface).IsAssignableFrom(t)));
plugin.Load(host,null,new AuditOptions());
int count=0,failed=0;
var results=new List<object>();
var extensions=new HashSet<string>(new[]{".bmp",".png",".gif",".jpg",".jpeg",".tif",".tiff"},StringComparer.OrdinalIgnoreCase);
foreach(string path in Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Order()) {
    if(!extensions.Contains(Path.GetExtension(path)))continue;
    host.Errors.Clear();count++;
    bool loaded=false;string error=null;int width=0,height=0;
    try {
        if(!plugin.CanLoadTexture(path))throw new InvalidDataException("No recognized signature");
        if(!plugin.QueryTextureDimensions(path,out width,out height))throw new InvalidDataException("Dimension query failed");
        if(!plugin.LoadTexture(path,out var texture))throw new InvalidDataException("Texture decode failed");
        if(texture.Width!=width||texture.Height!=height)throw new InvalidDataException("Queried and decoded dimensions differ");
        loaded=true;
    } catch(Exception ex){error=ex.Message;}
    if(!loaded||host.Errors.Count>0) {
        failed++;
        string signature=Convert.ToHexString(File.ReadAllBytes(path).AsSpan(0,(int)Math.Min(32,new FileInfo(path).Length)));
        string skia;
        try{var image=SkiaDecoder.Decode(path);skia=$"OK {image.Width}x{image.Height}";}catch(Exception ex){skia=ex.Message;}
        var item=new {Path=Path.GetRelativePath(root,path),Loaded=loaded,Width=width,Height=height,Error=error,Diagnostics=host.Errors.ToArray(),Signature=signature,Skia=skia};
        results.Add(item);Console.WriteLine(JsonSerializer.Serialize(item));
    }
}
string pluginHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pluginPath)));
var report=new {Root=root,Plugin=pluginPath,PluginSha256=pluginHash,Checked=count,Failed=failed,Failures=results};
if(args.Length>1)File.WriteAllText(args[1],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"TEXTURE_AUDIT: checked={count}, failed={failed}");
return failed==0?0:1;

sealed class AuditOptions:OpenBveApi.BaseOptions {public override void Save(string file)=>throw new NotSupportedException();}
sealed class AuditHost:HostInterface {
    internal readonly List<string> Errors=new();
    internal AuditHost():base(HostApplication.OpenBve){}
    public override void ReportProblem(ProblemType type,string text)=>Errors.Add(type+": "+text);
    public override OpenBveApi.Trains.AbstractTrain ParseTrackFollowingObject(string file,string folder)=>throw new NotSupportedException();
}
