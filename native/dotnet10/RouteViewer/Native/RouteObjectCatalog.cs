using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace RouteViewer;

internal sealed record RouteObjectKind(string Command,string Label,string Group,bool StaticOnly=false,bool Applied=true);
internal sealed record RouteObjectSelection(string Command,string Folder,string InitialFile=null);
internal sealed record RouteObjectEntry(string Command,int Index,string Path,bool Initial);
internal sealed record RouteObjectPlan(string Declarations,string InitialCommands,string BlockCommands,string Examples,string Manifest,bool HasRail,bool HasGround);

internal static class RouteObjectCatalog
{
    internal static readonly RouteObjectKind[] Kinds={
        new("Rail","Trilhos","Via"),new("Ground","Terreno / solo","Via"),
        new("WallL","Muros à esquerda","Via"),new("WallR","Muros à direita","Via"),
        new("DikeL","Taludes à esquerda","Via"),new("DikeR","Taludes à direita","Via"),
        new("Pole","Postes / catenária (uma via)","Via"),
        new("FormL","Plataforma à esquerda","Plataformas"),new("FormR","Plataforma à direita","Plataformas"),
        new("FormCL","Plataforma central: peça esquerda","Plataformas",true,false),new("FormCR","Plataforma central: peça direita","Plataformas",true,false),
        new("RoofL","Cobertura à esquerda","Plataformas"),new("RoofR","Cobertura à direita","Plataformas"),
        new("RoofCL","Cobertura central: peça esquerda","Plataformas",true,false),new("RoofCR","Cobertura central: peça direita","Plataformas",true,false),
        new("FreeObj","Objetos livres: prédios, placas etc.","Cenário"),new("Beacon","Objetos de balizas","Cenário",false,false),
        new("CrackL","Entre vias: peça esquerda","Cenário",true,false),new("CrackR","Entre vias: peça direita","Cenário",true,false)
    };
    internal static string[] Scan(string command,string folder)
    {
        var kind=Kinds.Single(k=>k.Command==command);
        if(!Directory.Exists(folder))throw new IOException("Pasta não encontrada: "+folder);
        return Directory.EnumerateFiles(folder,"*",new EnumerationOptions {RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint,IgnoreInaccessible=false})
            .Where(f=>Supported(kind,f)).Select(Path.GetFullPath)
            .OrderBy(f=>Path.GetRelativePath(folder,f),StringComparer.OrdinalIgnoreCase).ToArray();
    }
    static bool Supported(RouteObjectKind kind,string file)=>Path.GetExtension(file).ToLowerInvariant() switch {
        ".csv" or ".b3d"=>true,".animated"=>!kind.StaticOnly,_=>false
    };
    internal static RouteObjectPlan Build(string objectRoot,int length,IReadOnlyList<RouteObjectSelection> selections)
    {
        selections??=Array.Empty<RouteObjectSelection>();
        if(selections.Select(s=>s.Command).Distinct().Count()!=selections.Count)throw new ArgumentException("Cada categoria deve ter uma única pasta.");
        var entries=new List<RouteObjectEntry>();var chosen=new Dictionary<string,int>();var declarations=new StringBuilder();
        var pathComparer=OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal;
        foreach(var selection in selections) {
            var kind=Kinds.SingleOrDefault(k=>k.Command==selection.Command)??throw new ArgumentException("Categoria de objeto desconhecida: "+selection.Command);
            string folder=Path.GetFullPath(selection.Folder);
            var files=Scan(selection.Command,folder);
            if(files.Length==0)throw new ArgumentException("Não há objetos compatíveis em "+kind.Label+". Use CSV/B3D"+(kind.StaticOnly?".":" ou ANIMATED."));
            string initial=selection.InitialFile==null?null:Path.GetFullPath(selection.InitialFile);
            if(initial!=null&&!files.Contains(initial,pathComparer))throw new ArgumentException("O objeto inicial não pertence à pasta selecionada em "+kind.Label);
            if(initial!=null)files=new[]{initial}.Concat(files.Where(f=>!pathComparer.Equals(f,initial))).ToArray();
            int index=(selection.Command is "Rail" or "Ground")&&initial!=null?0:1;
            declarations.AppendLine("; "+kind.Label+" — índices desta categoria");
            foreach(string file in files) {
                string relative=Path.GetRelativePath(objectRoot,file).Replace('\\','/');
                if(Path.IsPathRooted(relative)||relative.IndexOfAny(new[]{',',';','\r','\n','"'})>=0)
                    throw new ArgumentException("Use uma biblioteca na mesma unidade, sem vírgulas, ponto e vírgula ou aspas no caminho: "+file);
                string key=selection.Command=="Pole"?$"Pole(0;{index})":$"{selection.Command}({index})";
                declarations.AppendLine($".{key} {relative}");
                bool selected=pathComparer.Equals(file,initial);
                entries.Add(new(selection.Command,index,relative,selected));
                if(selected)chosen.Add(selection.Command,index);
                index++;
            }
        }
        foreach(string side in new[]{"L","R"})if(chosen.ContainsKey("Roof"+side)&&!chosen.ContainsKey("Form"+side))throw new ArgumentException("Escolha a plataforma do mesmo lado antes de aplicar a cobertura "+side+".");
        var start=new List<string>();var examples=new StringBuilder();
        foreach(string category in new[]{"Wall","Dike"}) {
            bool left=chosen.ContainsKey(category+"L"),right=chosen.ContainsKey(category+"R");
            if(left||right)start.Add($".{category} 0;{(left&&right?0:left?-1:1)};1");
        }
        if(chosen.ContainsKey("Pole"))start.Add(".Pole 0;0;0;50;1");
        if(chosen.ContainsKey("FreeObj"))start.Add(".FreeObj 0;1;5;0;0");
        var blocks=new StringBuilder();
        for(int position=0;position<Math.Min(length,100);position+=25) {
            var forms=new List<string>();
            foreach(string side in new[]{"L","R"})if(chosen.ContainsKey("Form"+side))forms.Add($".Form 0;{side};{(chosen.ContainsKey("Roof"+side)?1:0)};1");
            if(forms.Count>0)blocks.AppendLine(position+", "+string.Join(", ",forms));
        }
        if(entries.Any(e=>e.Command=="FreeObj"))examples.AppendLine("; Exemplo de objeto pontual: 100, .FreeObj 0;1;5;0;0");
        if(entries.Any(e=>e.Command=="CrackL"||e.Command=="CrackR"))examples.AppendLine("; Track.Crack exige duas vias existentes; consulte os índices no catálogo antes de usar.");
        if(entries.Any(e=>e.Command=="Beacon"))examples.AppendLine("; Structure.Beacon cadastra a geometria; configure tipo e dados do sistema de segurança em Track.Beacon.");
        if(entries.Any(e=>e.Command.StartsWith("FormC")||e.Command.StartsWith("RoofC")))examples.AppendLine("; Peças centrais são usadas entre duas vias. Track.Form associa os conjuntos pelo mesmo índice.");
        examples.AppendLine("; Objetos contínuos devem ter 25 m no eixo Z (Options.BlockLength). Confira a escala dos seus modelos.");
        string manifest=JsonSerializer.Serialize(new {Documentation="https://openbve-project.net/documentation/HTML/route_csv.html",RelativeTo="Railway/Object",BlockLength=25,Objects=entries},new JsonSerializerOptions{WriteIndented=true});
        return new(declarations.ToString(),start.Count==0?"":", "+string.Join(", ",start),blocks.ToString(),examples.ToString(),manifest,chosen.ContainsKey("Rail"),chosen.ContainsKey("Ground"));
    }
}
