using System;
using System.Linq;
using OpenBveApi.Interface;
namespace RouteViewer;
internal static class RouteInfo
{
    internal static int Errors,Warnings;
    internal static string Failure,FirstError;
    static int messageCount=-1;
    static string lastFailure;
    internal static void RefreshIfChanged() {
        int count;lock(Interface.LogLock)count=Interface.LogMessages.Count;
        if(count!=messageCount||Failure!=lastFailure)Refresh();
    }
    internal static void Refresh()
    {
        var messages=Interface.GetLogSnapshot();
        messageCount=messages.Count;lastFailure=Failure;
        Errors=messages.Count(m=>m.Type is MessageType.Error or MessageType.Critical);
        Warnings=messages.Count(m=>m.Type==MessageType.Warning);
        FirstError=Failure??messages.FirstOrDefault(m=>m.Type is MessageType.Error or MessageType.Critical)?.Text;
        if(Failure!=null&&Errors==0)Errors=1;
    }
    internal static string Time(double seconds)=>seconds<0?"—":$"{(int)(seconds/3600):00}:{(int)(seconds/60)%60:00}:{(int)seconds%60:00}";
    internal static string[] StationLines()
    {
        double position=Program.Renderer.CameraTrackFollower.TrackPosition;
        var stops=Program.CurrentRoute.Stations.Where(s=>!s.Dummy&&s.Stops!=null).SelectMany(s=>s.Stops.Select(stop=>(Station:s,Position:stop.TrackPosition))).OrderBy(s=>s.Position).ToArray();
        if(stops.Length==0)return new[]{"Sem estações com ponto de parada","Posição na rota: "+position.ToString("0")+" m"};
        var next=stops.FirstOrDefault(s=>s.Position>=position-15);
        if(next.Station==null)next=stops[^1];
        double distance=next.Position-position;
        string relation=Math.Abs(distance)<=15?"na estação":distance>0?$"a {distance:0} m":$"há {-distance:0} m";
        var station=next.Station;
        string doors=station.OpenLeftDoors?(station.OpenRightDoors?"ambos os lados":"esquerda"):station.OpenRightDoors?"direita":"não definida";
        return new[]{station.Name+" · "+relation,$"Chegada {Time(station.ArrivalTime)} · Saída {Time(station.DepartureTime)}","Plataforma: "+doors};
    }
}
