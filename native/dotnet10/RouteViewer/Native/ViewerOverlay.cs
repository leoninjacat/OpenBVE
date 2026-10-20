using System;
using System.Collections.Generic;
using System.Linq;
using OpenBveApi.Colors;
using OpenBveApi.Graphics;
using OpenBveApi.Math;
namespace RouteViewer;
internal static class ViewerOverlay
{
    internal sealed record Control(string Id,string Icon,string Tip,float X,float Y,float W=40,float H=40,bool Enabled=true,bool Active=false);
    internal static readonly List<Control> Controls=new();
    static Color128 White=new(.92f,.93f,.94f,1),Muted=new(.55f,.57f,.6f,1),Plate=new(.08f,.09f,.10f,.82f),Active=new(.32f,.35f,.37f,.94f);
    static float scale=1;
    sealed record LogRow(int Index, string Text, bool Header);
    static readonly List<LogRow> logRows=new();
    const int VisibleLogRows=15;
    static void BuildLog(ViewerWindow window,float w)
    {
        logRows.Clear();
        var messages=Interface.GetLogSnapshot().AsEnumerable().Reverse().ToArray();
        float available=650*scale;
        string Fit(string text) {
            while(text.Length>0&&Program.Renderer.Fonts.SmallFont.MeasureString(text+"…").X>available)text=text[..^1];
            return text+"…";
        }
        for(int i=0;i<messages.Length;i++) {
            string text=messages[i].Text??"";
            string summary=text.Replace('\r',' ').Replace('\n',' ');
            if(Program.Renderer.Fonts.SmallFont.MeasureString(summary).X>available)summary=Fit(summary);
            logRows.Add(new(i,summary,true));
            if(window.ExpandedLogMessage!=i)continue;
            foreach(string paragraph in text.Replace("\r\n","\n").Replace('\r','\n').Split('\n')) {
                string line="";
                foreach(char ch in paragraph) {
                    if(line.Length>0&&Program.Renderer.Fonts.SmallFont.MeasureString(line+ch).X>available) {
                        logRows.Add(new(i,line,false));line="";
                    }
                    line+=ch;
                }
                logRows.Add(new(i,line,false));
            }
        }
        window.LogScroll=Math.Clamp(window.LogScroll,0,Math.Max(0,logRows.Count-VisibleLogRows));
        for(int row=0;row<VisibleLogRows&&row+window.LogScroll<logRows.Count;row++) {
            var item=logRows[row+window.LogScroll];
            if(item.Header)Controls.Add(new("log-message-"+item.Index,"","Expandir / recolher mensagem",w/2-344,182+row*24,688,24));
        }
    }
    internal static void Build(ViewerWindow window)
    {
        White=window.DarkTheme?new(.92f,.93f,.94f,1):new(.12f,.14f,.17f,1);
        Muted=window.DarkTheme?new(.6f,.63f,.67f,1):new(.34f,.37f,.41f,1);
        Plate=window.DarkTheme?new(.08f,.09f,.10f,.9f):new(.96f,.97f,.98f,.94f);
        Active=window.DarkTheme?new(.32f,.35f,.37f,.94f):new(.73f,.79f,.85f,.96f);
        Controls.Clear();scale=Math.Min(1,Math.Min(window.ViewWidth/1000f,window.ViewHeight/700f));
        float w=window.ViewWidth/scale,h=window.ViewHeight/scale;
        bool ready=Loading.Complete&&Program.CurrentRouteFile!=null&&!Program.CurrentlyLoading;
        bool free=Program.Camera.Mode==NavigationMode.Free;
        void Add(string id,string icon,string tip,float x,float y,bool enabled=true,bool active=false,float size=40)=>Controls.Add(new(id,icon,tip,x,y,size,size,enabled,active));
        Add("toggle-ui","eye","Exibir / ocultar interface · F10",w-60,16,true,!window.InterfaceVisible);
        Add("toggle-editor","document","Exibir / ocultar editor · F8",w-60,60,window.Editor.Running,window.Editor.Visible);
        if(!window.InterfaceVisible)return;
        Controls.Add(new("route-info","","Informações da rota · clique para ver mensagens",16,68,340,140,ready||RouteInfo.Errors>0));
        string[] ids={"open","reload","previous","next","home","wire","events","log","help"};
        string[] icons={"folder","reload","previous","next","home","wire","events","log","help"};
        string[] tips={"Abrir rota · Ctrl+O","Recarregar · F5","Estação anterior · Page Up","Próxima estação · Page Down","Restaurar visão · Num 5","Aramado · F","Eventos da rota","Mensagens · F9","Ajuda · F1"};
        for(int i=0;i<ids.Length;i++)Add(ids[i],icons[i],tips[i],16+i*44,16,i==0?!Program.Picking:i>=7||ready,ids[i]=="wire"&&Program.Renderer.OptionWireFrame||ids[i]=="events"&&Program.Renderer.OptionEvents);
        Add("mode-track","track","Cabine invisível · trilho zero · Tab",w-148,16,ready,!free);
        Add("mode-free","camera","Câmera livre · Tab",w-104,16,ready,free);
        Add("zoom-in","plus","Aproximar",16,h/2-110,ready);
        Add("zoom-out","minus","Afastar",16,h/2+70,ready);
        Controls.Add(new("zoom-slider","","Zoom · arraste",24,h/2-62,24,126,ready));
        // The right-hand controls always move the invisible cab along track zero.
        Add("rail-forward","up","Avançar no trilho zero · W / Num 9",w-60,h/2-90,ready&&!free,window.Held=="rail-forward");
        Add("rail-play",Program.Camera.Cruise==0?"play":"pause","Percorrer / pausar o trilho zero · Espaço",w-60,h/2-46,ready&&!free,Program.Camera.Cruise!=0);
        Add("rail-back","down","Recuar no trilho zero · S / Num 3",w-60,h/2-2,ready&&!free,window.Held=="rail-back");
        Controls.Add(new("rail-slider","","Posição no trilho zero · arraste",w-51,h/2+50,22,110,ready&&!free));
        Add("up","up","Subir · E / Num 8",16,h-108,ready,window.Held=="up");
        Add("down","down","Descer · Q / Num 2",16,h-64,ready,window.Held=="down");
        Controls.Add(new("speed-slider","","Velocidade de navegação · km/h",74,h-58,144,26,ready));
        // Globe = orbit; surrounding arrows = translation, independent of track 0.
        float cx=w-108,cy=h-112;
        Add("orbit","globe",free?"Girar na posição livre · arraste":"Girar na cabine · arraste",cx-50,cy-50,ready,window.Drag==DragMode.Orbit,100);
        Add("forward","up","Voo livre para frente · W",cx-18,cy-91,ready&&free,window.Held=="forward",36);
        Add("back","down","Voo livre para trás · S",cx-18,cy+55,ready&&free,window.Held=="back",36);
        Add("left","left","Voo livre à esquerda · A",cx-91,cy-18,ready&&free,window.Held=="left",36);
        Add("right","right","Voo livre à direita · D",cx+55,cy-18,ready&&free,window.Held=="right",36);
        if(window.Help||window.ShowLog)Add("close","close","Fechar · Esc",w/2+310,123);
        if(window.ShowLog&&!window.Help)BuildLog(window,w);
    }
    static void Rect(NewRenderer r,float x,float y,float w,float h,Color128 color)=>r.Rectangle.Draw(null,new Vector2(x*scale,y*scale),new Vector2(w*scale,h*scale),color);
    static void Text(NewRenderer r,string text,float x,float y,Color128 color)=>r.OpenGlString.Draw(r.Fonts.SmallFont,text,new Vector2(x*scale,y*scale),TextAlignment.TopLeft,color);
    static void Icon(NewRenderer r,string icon,float x,float y,float w,float h,Color128 color)=>ViewerIcons.Draw(r,icon,x*scale,y*scale,w*scale,h*scale,color);
    static void PlateAt(NewRenderer renderer,float x,float y,float w,float h,Color128 color)
    {
        // Draw corners in screen pixels, never by stretching a square mask.
        float px=x*scale,py=y*scale,width=w*scale,height=h*scale;
        float radius=Math.Min(6*scale,Math.Min(width,height)/2);
        void Band(float bx,float by,float bw,float bh)=>renderer.Rectangle.Draw(null,new Vector2(bx,by),new Vector2(bw,bh),color);
        Band(px,py+radius,width,height-2*radius);
        int rows=Math.Max(1,(int)Math.Ceiling(radius));
        float step=radius/rows;
        for(int i=0;i<rows;i++) {
            float dy=radius-(i+.5f)*step;
            float inset=radius-MathF.Sqrt(Math.Max(0,radius*radius-dy*dy));
            Band(px+inset,py+i*step,width-2*inset,step);
            Band(px+inset,py+height-(i+1)*step,width-2*inset,step);
        }
    }
    internal static void Draw(NewRenderer r)
    {
        var window=(ViewerWindow)r.GameWindow;Build(window);
        float w=window.ViewWidth/scale,h=window.ViewHeight/scale;
        foreach(var c in Controls) {
            if(c.Id is "close" or "route-info" || c.Id.StartsWith("log-message-"))continue;
            bool hover=window.PointerX>=c.X*scale&&window.PointerY>=c.Y*scale&&window.PointerX<(c.X+c.W)*scale&&window.PointerY<(c.Y+c.H)*scale;
            if(c.Id.EndsWith("slider")) {
                PlateAt(r,c.X-7,c.Y-7,c.W+14,c.H+14,Plate);
                float fraction;
                if(c.Id=="speed-slider") {
                    fraction=Math.Clamp((Program.Camera.Speed*3.6f-1)/179,0,1);
                    Rect(r,c.X,c.Y+c.H/2,c.W,2,Muted);Icon(r,"plate",c.X+fraction*c.W-5,c.Y+c.H/2-4,10,10,White);
                    Text(r,$"{Program.Camera.Speed*3.6f:0} km/h",c.X+40,c.Y-24,White);
                } else {
                    if(c.Id=="zoom-slider")fraction=Program.Camera.Mode==NavigationMode.Free?Math.Clamp(MathF.Log(Program.Camera.Pose.Distance/.5f)/MathF.Log(40000),0,1):(Program.Camera.FieldOfView-.15f)/1.25f;
                    else {var limits=Program.Camera.Limits;fraction=(float)((Program.Camera.Cab.Position-limits.Min)/Math.Max(1,limits.Max-limits.Min));}
                    Rect(r,c.X+c.W/2,c.Y,2,c.H,c.Enabled?Muted:new Color128(.3f,.32f,.34f,.6f));
                    Icon(r,"plate",c.X+c.W/2-4,c.Y+fraction*c.H-4,10,10,c.Enabled?White:Muted);
                }
                continue;
            }
            var background=c.Active?Active:hover&&c.Enabled?(window.DarkTheme?new Color128(.23f,.25f,.27f,.92f):new Color128(.82f,.85f,.88f,.96f)):Plate;
            if(c.Id is "orbit" or "forward" or "back" or "left" or "right")Icon(r,"disc",c.X,c.Y,c.W,c.H,background);
            else PlateAt(r,c.X,c.Y,c.W,c.H,background);
            float pad=c.Id=="orbit"?8:5;
            Icon(r,c.Icon,c.X+pad,c.Y+pad,c.W-2*pad,c.H-2*pad,c.Enabled?White:new Color128(.4f,.42f,.44f,.65f));
            if(c.Active)Rect(r,c.X+12,c.Y+c.H-4,c.W-24,2,White);
        }
        if(!window.InterfaceVisible)return;
        DrawRouteInfo(r);
        bool loaded=Loading.Complete&&Program.CurrentRouteFile!=null;
        string state=Program.Camera.Mode==NavigationMode.Track?"Trilho 0":"Livre";
        string status=loaded?$"{state}   ·   {r.CameraTrackFollower.TrackPosition:0} m":Program.Status;
        Text(r,status,w/2-Math.Min(status.Length*3,230),h-24,White);
        if(!loaded && RouteInfo.Failure==null) {PlateAt(r,w/2-210,h/2-48,420,96,Plate);Text(r,"Abra uma rota CSV/RW ou arraste o arquivo para a janela.",w/2-190,h/2-15,White);}
        var hovered=Controls.LastOrDefault(c=>window.PointerX>=c.X*scale&&window.PointerY>=c.Y*scale&&window.PointerX<(c.X+c.W)*scale&&window.PointerY<(c.Y+c.H)*scale);
        if(hovered!=null&&!window.Help&&!window.ShowLog) {
            float tw=hovered.Tip.Length*7+20,tx=Math.Clamp(window.PointerX/scale+14,8,Math.Max(8,w-tw-8)),ty=window.PointerY/scale+48;
            if(ty+30>h)ty=hovered.Y-35;
            PlateAt(r,tx,ty,tw,28,Plate);Text(r,hovered.Tip,tx+10,ty+7,White);
        }
        if(window.Help||window.ShowLog) {
            float x=w/2-360;PlateAt(r,x,110,720,470,window.DarkTheme?new Color128(.07f,.08f,.09f,.98f):new Color128(.97f,.98f,.99f,.98f));
            Text(r,window.Help?"Câmera e navegação":"Mensagens da rota",x+24,137,White);
            var close=Controls.Last(c=>c.Id=="close");Icon(r,"close",close.X,close.Y,40,40,White);
            if(window.ShowLog&&!window.Help) {
                for(int row=0;row<VisibleLogRows&&row+window.LogScroll<logRows.Count;row++) {
                    var item=logRows[row+window.LogScroll];float y=186+row*24;
                    if(item.Header) {
                        Icon(r,window.ExpandedLogMessage==item.Index?"down":"right",x+18,y,16,16,Muted);
                        Text(r,item.Text,x+40,y,White);
                    } else Text(r,item.Text,x+40,y,Muted);
                }
                Text(r,logRows.Count==0?"Nenhuma mensagem.":"Clique na seta para expandir ou recolher · role para ver mais",x+24,554,Muted);
                return;
            }
            var lines=new[]{
                "Tab ou ícones no alto à direita: alternar Trilho 0 / Livre.",
                "Trilho 0: cabine invisível acompanha curvas, rampas e superelevação.",
                "W/S ou Num 9/3: avançar/recuar. Espaço: percorrer ou pausar.",
                "Num 4/6: deslocar para os lados. Num 8/2: subir/descer.",
                "Num 5: restaurar visão, mantendo a posição na via.",
                "Setas do teclado ou arraste: olhar ao redor da cabine.",
                "Globo: girar a visão sem deslocar a câmera.",
                "Setas em volta do globo: voo livre; WASD + Q/E também movem.",
                "MMB / botão direito: girar a visão; Shift+MMB: pan.",
                "Roda / controle à esquerda: zoom. Slider inferior: velocidade.",
                "Page Up/Down: estações. Ctrl+O: abrir. F5: recarregar."
            };
            for(int i=0;i<lines.Length;i++){string line=lines[i];if(line.Length>86)line=line[..85]+"…";Text(r,line,x+24,190+i*32,Muted);}
        }
    }
    static void DrawRouteInfo(NewRenderer r)
    {
        RouteInfo.RefreshIfChanged();
        if(Program.CurrentRouteFile==null&&RouteInfo.Failure==null)return;
        const float x=16,y=68,width=340;
        string Clip(string value) {int max=Math.Clamp((int)((width-24)*scale/8),12,45);value=(value??"").Replace('\n',' ').Replace('\r',' ');return value.Length>max?value[..(max-1)]+"…":value;}
        PlateAt(r,x,y,width,140,Plate);
        var lines=RouteInfo.StationLines();
        Text(r,Clip(lines[0]),x+12,y+12,White);
        for(int i=1;i<lines.Length;i++)Text(r,Clip(lines[i]),x+12,y+12+i*23,Muted);
        string diagnostics=RouteInfo.Errors>0||RouteInfo.Warnings>0?$"{RouteInfo.Errors} erro(s) · {RouteInfo.Warnings} aviso(s) · detalhes":"Carregamento sem erros · detalhes";
        var color=RouteInfo.Errors>0?new Color128(.94f,.31f,.25f,1):RouteInfo.Warnings>0?new Color128(.85f,.58f,.18f,1):Muted;
        Text(r,diagnostics,x+12,y+88,color);
        if(RouteInfo.FirstError!=null)Text(r,Clip(RouteInfo.FirstError),x+12,y+112,color);
    }
    internal static (float X,float Y) Center(string id) {var c=Controls.First(c=>c.Id==id);return((c.X+c.W/2)*scale,(c.Y+c.H/2)*scale);}
    internal static void Slide(ViewerWindow window,float x,float y)
    {
        Build(window);var c=Controls.FirstOrDefault(c=>c.Id==window.Held);if(c==null)return;
        if(c.Id=="speed-slider")Program.Camera.Speed=(1+Math.Clamp((x/scale-c.X)/c.W,0,1)*179)/3.6f;
        else if(c.Id=="rail-slider") {var limits=Program.Camera.Limits;Program.Camera.Cab.Position=limits.Min+Math.Clamp((y/scale-c.Y)/c.H,0,1)*(limits.Max-limits.Min);Program.Camera.Cruise=0;}
        else if(c.Id=="zoom-slider") {
            float fraction=Math.Clamp((y/scale-c.Y)/c.H,0,1);
            if(Program.Camera.Mode==NavigationMode.Free)Program.Camera.Pose.Distance=.5f*MathF.Pow(40000,fraction);
            else {float target=.15f+fraction*1.25f;Program.Camera.Zoom(-MathF.Log(target/Program.Camera.FieldOfView)/.08f);}
        }
    }
    internal static bool Press(ViewerWindow window,float x,float y)
    {
        Build(window);var c=Controls.LastOrDefault(c=>x>=c.X*scale&&y>=c.Y*scale&&x<(c.X+c.W)*scale&&y<(c.Y+c.H)*scale);
        if(window.Help||window.ShowLog){
            if(c?.Id=="close"){window.Help=false;window.ShowLog=false;}
            else if(c?.Id.StartsWith("log-message-")==true) {
                int index=int.Parse(c.Id[12..]);
                window.ExpandedLogMessage=window.ExpandedLogMessage==index?-1:index;
            }
            return true;
        }
        if(Program.FeedbackTest)Console.WriteLine($"FEEDBACK_HIT: {c?.Id??"none"} xy={x},{y} scale={scale}");
        if(c==null)return false;if(!c.Enabled)return true;
        switch(c.Id) {
            case "toggle-ui":window.ToggleInterface();break;
            case "toggle-editor":window.Editor.ToggleVisible();break;
            case "route-info":window.ShowLog=true;break;
            case "open":Program.OpenPicker();break;
            case "reload":Program.Reload=true;break;
            case "previous":Program.Camera.Station(-1);break;
            case "next":Program.Camera.Station(1);break;
            case "home":Program.Camera.ResetView();break;
            case "wire":Program.Renderer.OptionWireFrame=!Program.Renderer.OptionWireFrame;break;
            case "events":Program.Renderer.OptionEvents=!Program.Renderer.OptionEvents;break;
            case "log":window.ShowLog=true;break;
            case "help":window.Help=true;break;
            case "mode-track":Program.Camera.SetMode(NavigationMode.Track);window.Held=null;window.Drag=DragMode.None;break;
            case "mode-free":Program.Camera.SetMode(NavigationMode.Free);window.Held=null;window.Drag=DragMode.None;break;
            case "orbit":window.Drag=DragMode.Orbit;break;
            case "zoom-in":Program.Camera.Zoom(1);break;
            case "zoom-out":Program.Camera.Zoom(-1);break;
            case "rail-play":Program.Camera.Cruise=Program.Camera.Cruise==0?1:0;break;
            default:window.Held=c.Id;if(c.Id.EndsWith("slider"))Slide(window,x,y);break;
        }
        return true;
    }
}
