using RouteViewer;

string root=Path.GetFullPath(args[0]);
Directory.CreateDirectory(root);
void Check(bool pass,string message){if(!pass)throw new Exception(message);}
string basic=Path.Combine(root,"Projeto com espaços");
string file=RouteProject.Create("Estação nova",basic,Path.Combine(basic,"Railway","Object"),1000,1600);
byte[] original=File.ReadAllBytes(file);
Check(File.Exists(file)&&Directory.Exists(Path.Combine(basic,"Railway","Sound")),"Missing project structure");
Check(File.ReadAllText(file).Contains(".Rail(0)")&&File.ReadAllText(file).Contains(".Gauge 1600"),"Missing rail zero / gauge");
bool refused=false;
try{RouteProject.Create("Estação nova",basic,Path.Combine(basic,"Railway","Object"),1000,1600);}catch(IOException){refused=true;}
Check(refused&&original.AsSpan().SequenceEqual(File.ReadAllBytes(file)),"Existing route was overwritten");
string external=Path.Combine(root,"Biblioteca externa");
Directory.CreateDirectory(external);
File.WriteAllText(Path.Combine(external,"objeto-existente.csv"),"keep this");
string other=RouteProject.Create("Outra rota",Path.Combine(root,"Outro projeto"),external,500,1000);
Check(File.ReadAllText(other).Contains("../")&&File.ReadAllText(Path.Combine(external,"objeto-existente.csv"))=="keep this","External references / existing object preservation failed");
bool invalid=false;
try{RouteProject.Create("Inválida",Path.Combine(root,"invalid"),external,26,1600);}catch(ArgumentException){invalid=true;}
Check(invalid&&!Directory.Exists(Path.Combine(root,"invalid")),"Invalid length created files");
Console.WriteLine("PROJECT_TESTS_OK: self contained, external objects, Unicode, no overwrite, invalid input");
Console.WriteLine("BASIC_ROUTE="+file);
Console.WriteLine("EXTERNAL_ROUTE="+other);

// Typed folders: definitions and placements must share documented indices.
string library=Path.Combine(root,"Bibliotecas por tipo");
string objectSource=Directory.GetFiles(Path.Combine(basic,"Railway","Object"),"trilho_zero.csv",SearchOption.AllDirectories).Single();
var selections=new List<RouteObjectSelection>();
foreach(var kind in RouteObjectCatalog.Kinds) {
    string folder=Path.Combine(library,kind.Command);Directory.CreateDirectory(folder);
    string first=Path.Combine(folder,"a.csv"),selected=Path.Combine(folder,"z.csv");
    File.Copy(objectSource,first);File.Copy(objectSource,selected);
    File.WriteAllText(Path.Combine(folder,"ignored.png"),"not an object");
    File.WriteAllText(Path.Combine(folder,"state.animated"),"[Object]\nStates = a.csv\n");
    Check(RouteObjectCatalog.Scan(kind.Command,folder).Length==(kind.StaticOnly?2:3),"Object format filtering failed: "+kind.Command);
    selections.Add(new(kind.Command,folder,kind.Applied?selected:null));
}
string catalogProject=Path.Combine(root,"Projeto catalogado");
string catalogRoute=RouteProject.Create("Catálogo",catalogProject,Path.Combine(catalogProject,"Railway","Object"),500,1600,selections);
string csv=File.ReadAllText(catalogRoute);
Check(csv.Split('\n').Count(line=>line.StartsWith(".Rail(0)"))==1&&csv.Contains("Rail/z.csv"),"Chosen rail did not own index zero");
Check(csv.Contains(".Wall 0;0;1")&&csv.Contains(".Dike 0;0;1"),"Paired left/right definitions were not applied together");
Check(csv.Contains(".Pole(0;1)")&&csv.Contains(".Pole 0;0;0;50;1"),"Pole definition/placement syntax mismatch");
Check(csv.Contains(".Form 0;L;1;1")&&csv.Contains(".Form 0;R;1;1")&&csv.Contains("75, .Form"),"Platform/roof indices or block placement failed");
Check(csv.Contains(".FreeObj 0;1;5;0;0")&&File.Exists(Path.Combine(catalogProject,"catalogo-objetos.json")),"Free object/catalog missing");
string catalogOnly=RouteProject.Create("Só catálogo",Path.Combine(root,"Só catálogo"),Path.Combine(root,"Só catálogo","Railway","Object"),25,1000,new[]{new RouteObjectSelection("Rail",Path.Combine(library,"Rail"))});
Check(File.ReadAllText(catalogOnly).Contains(".Rail(0) Rota_Só_catálogo_Base")&&File.ReadAllText(catalogOnly).Contains(".Rail(1)"),"Catalog-only rail displaced generated fallback");
bool invalidRoof=false;
try{RouteProject.Create("Roof sem form",Path.Combine(root,"invalid-roof"),library,500,1600,new[]{selections.Single(s=>s.Command=="RoofL")});}catch(ArgumentException){invalidRoof=true;}
Check(invalidRoof&&!Directory.Exists(Path.Combine(root,"invalid-roof")),"Roof without corresponding platform accepted");
Console.WriteLine("CATALOG_TESTS_OK: 19 categories, format restrictions, independent indices, selected rail zero, paired walls/dikes, poles, platforms/roofs, free objects, safe fallback, roof validation");
Console.WriteLine("CATALOG_ROUTE="+catalogRoute);
