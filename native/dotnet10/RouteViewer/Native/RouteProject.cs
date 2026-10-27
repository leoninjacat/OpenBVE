using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace RouteViewer;

// Shared generator: no desktop toolkit, route library or installed content required.
internal static class RouteProject
{
    internal static string FolderName(string name)
    {
        var value=new StringBuilder();
        foreach(char c in name.Trim())value.Append(char.IsLetterOrDigit(c)||c=='-'||c=='_'?c:'_');
        string result=value.ToString().Trim('_','.');
        if(string.IsNullOrEmpty(result))throw new ArgumentException("Informe um nome para a rota.");
        // Prefix also avoids Windows reserved device names such as CON and NUL.
        return "Rota_"+result;
    }
    internal static string Create(string name,string projectFolder,string objectsFolder,int length,int gauge,IReadOnlyList<RouteObjectSelection> libraries=null)
    {
        string slug=FolderName(name);
        if(length<25||length>100000||length%25!=0)throw new ArgumentException("O comprimento deve ser múltiplo de 25 m, entre 25 e 100000 m.");
        if(gauge<600||gauge>2000)throw new ArgumentException("A bitola deve ficar entre 600 e 2000 mm.");
        projectFolder=Path.GetFullPath(projectFolder);
        string railway=Path.Combine(projectFolder,"Railway");
        string objectRoot=Path.Combine(railway,"Object");
        objectsFolder=Path.GetFullPath(objectsFolder);
        string assets=Path.Combine(objectsFolder,slug+"_Base");
        string relative=Path.GetRelativePath(objectRoot,assets);
        if(Path.IsPathRooted(relative))throw new ArgumentException("O projeto e os objetos precisam ficar na mesma unidade para usar caminhos relativos.");
        if(relative.IndexOfAny(new[]{',',';','\r','\n','"'})>=0)throw new ArgumentException("A pasta de objetos não pode conter vírgula, ponto e vírgula ou aspas.");
        string railPath=Path.Combine(assets,"trilho_zero.csv"),groundPath=Path.Combine(assets,"terreno.csv");
        string route=Path.Combine(railway,"Route",slug+".csv");
        string reference=relative.Replace('\\','/');
        string title=name.Replace('\r',' ').Replace('\n',' ').Replace(',',' ').Replace(';',' ');
        var catalog=RouteObjectCatalog.Build(objectRoot,length,libraries);
        string defaults=(catalog.HasRail?"":$".Rail(0) {reference}/trilho_zero.csv\n")+(catalog.HasGround?"":$".Ground(0) {reference}/terreno.csv\n");
        var files=new Dictionary<string,string> {
            [railPath]=Rail(gauge),
            [Path.Combine(projectFolder,"catalogo-objetos.json")]=catalog.Manifest,
            [groundPath]="; Terreno básico, sem texturas externas\nCreateMeshBuilder\nAddVertex,-50,-0.20,0\nAddVertex,50,-0.20,0\nAddVertex,50,-0.20,25\nAddVertex,-50,-0.20,25\nAddFace2,0,1,2,3\nSetColor,85,115,75\n",
            [route]=$"; {title} - criado no Route Viewer by LT ARTS\n; Base gerada e bibliotecas escolhidas: consulte catalogo-objetos.json e preserve os caminhos relativos.\n; Os objetos Structure.Rail(0) repetem-se a cada bloco de 25 m.\n\nWith Options\n.BlockLength 25\n\nWith Route\n.Comment {title}\n.Gauge {gauge}\n\nWith Structure\n{defaults}{catalog.Declarations}\nWith Track\n0, .RailType 0;0, .Ground 0, .Pitch 0, .Curve 0;0{catalog.InitialCommands}\n{catalog.BlockCommands}{catalog.Examples}; Comece a escrever sua rota abaixo. Posições em metros.\n; Exemplo: 100, .Curve 500;0\n; Exemplo: 200, .Curve 0;0\n{length}, .Accuracy 2\n",
            [Path.Combine(projectFolder,"LEIA-ME.txt")]="Projeto de rota OpenBVE by LT ARTS\n\nAbra o CSV em Railway/Route no Route Viewer.\nA base gerada inclui trilho zero e terreno sem texturas externas. Se você escolheu bibliotecas, o CSV referencia os modelos selecionados.\nSalve no editor para atualizar a prévia. Os exemplos comentados mostram curvas.\nRailway/Sound fica disponível para os sons que você adicionar.\nObjetos básicos gerados: "+relative+"\nBibliotecas e índices: catalogo-objetos.json\n\nSe escolheu objetos fora do projeto, mantenha a posição relativa dessas pastas\nou atualize as referências Structure ao mover/compartilhar o projeto.\n"
        };
        foreach(string file in files.Keys)if(File.Exists(file)||Directory.Exists(file))throw new IOException("Já existe um arquivo do projeto neste destino: "+file+". Escolha outra pasta ou nome.");
        var created=new List<string>();
        try {
            Directory.CreateDirectory(Path.Combine(railway,"Sound"));
            Directory.CreateDirectory(objectRoot);
            foreach(var file in files) {
                Directory.CreateDirectory(Path.GetDirectoryName(file.Key));
                using var stream=new FileStream(file.Key,FileMode.CreateNew,FileAccess.Write,FileShare.None);
                created.Add(file.Key);
                using var writer=new StreamWriter(stream,new UTF8Encoding(true));
                writer.Write(file.Value.Replace("\r\n","\n").Replace("\n",Environment.NewLine));
            }
            return route;
        }catch {
            foreach(string file in created)try{File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}
            throw;
        }
    }
    static string Rail(int gauge)
    {
        var builder=new StringBuilder("; Trilho zero básico - bloco de 25 m, sem dependências externas\n");
        float half=gauge/2000f;
        Box(builder,-half-.4f,-.18f,0,half+.4f,-.14f,25,125,125,120);
        for(int i=0;i<12;i++)Box(builder,-half-.25f,-.13f,i*2+.5f,half+.25f,-.04f,i*2+.75f,100,75,50);
        Box(builder,-half-.04f,-.04f,0,-half+.04f,.08f,25,190,195,200);
        Box(builder,half-.04f,-.04f,0,half+.04f,.08f,25,190,195,200);
        return builder.ToString();
    }
    static void Box(StringBuilder b,float x,float y,float z,float X,float Y,float Z,int r,int g,int blue)
    {
        b.AppendLine("CreateMeshBuilder");
        foreach(var v in new[]{(x,y,z),(X,y,z),(X,Y,z),(x,Y,z),(x,y,Z),(X,y,Z),(X,Y,Z),(x,Y,Z)})
            b.Append("AddVertex,").Append(v.Item1.ToString(CultureInfo.InvariantCulture)).Append(',').Append(v.Item2.ToString(CultureInfo.InvariantCulture)).Append(',').AppendLine(v.Item3.ToString(CultureInfo.InvariantCulture));
        b.AppendLine("AddFace2,0,1,2,3\nAddFace2,4,7,6,5\nAddFace2,0,4,5,1\nAddFace2,3,2,6,7\nAddFace2,0,3,7,4\nAddFace2,1,5,6,2");
        b.AppendLine($"SetColor,{r},{g},{blue}");
    }
}
