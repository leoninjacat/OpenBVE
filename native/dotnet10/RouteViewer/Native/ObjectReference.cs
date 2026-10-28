using System;
using System.Numerics;
using OpenBveApi.Colors;
using OpenBveApi.Graphics;
using V2=OpenBveApi.Math.Vector2;
namespace RouteViewer;

// A camera-facing, upright 1.75 m silhouette, drawn only in the editor overlay.
// It never enters scene geometry, picking, the parser, undo or disk persistence.
internal static class ObjectReference
{
    internal const float Height=1.75f;
    internal static bool Visible=true;
    static string source,path;
    static Vector3 feet;
    internal static void Draw(NewRenderer renderer,ViewerWindow window)
    {
        if(!Visible||!window.InterfaceVisible||!Loading.Complete)return;
        if(!ReferenceEquals(source,ObjectGraph.Source)||path!=Program.CurrentRouteFile){
            source=ObjectGraph.Source;path=Program.CurrentRouteFile;
            var min=new Vector3(float.MaxValue);var max=new Vector3(float.MinValue);
            foreach(var state in renderer.StaticObjectStates)
                foreach(var vertex in state.Prototype.Mesh.Vertices){var p=CameraRig.ToV(vertex.Coordinates);min=Vector3.Min(min,p);max=Vector3.Max(max,p);}
            if(min.X==float.MaxValue)feet=new Vector3(-.8f,0,0);
            else{
                var horizontal=new Vector3(Program.Camera.Pose.Right.X,0,Program.Camera.Pose.Right.Z);
                horizontal=horizontal.LengthSquared()<.001f?Vector3.UnitX:Vector3.Normalize(horizontal);
                var center=(min+max)/2;float edge=float.MaxValue;
                foreach(var state in renderer.StaticObjectStates)
                    foreach(var vertex in state.Prototype.Mesh.Vertices)edge=Math.Min(edge,Vector3.Dot(CameraRig.ToV(vertex.Coordinates)-center,horizontal));
                feet=center+horizontal*(edge-.7f);feet.Y=min.Y;
            }
        }
        var pose=Program.Camera.Pose;
        var side=new Vector3(pose.Right.X,0,pose.Right.Z);
        if(side.LengthSquared()<.001f)return;
        side=Vector3.Normalize(side);
        float focal=window.ViewHeight/(2*MathF.Tan((float)renderer.Camera.VerticalViewingAngle/2));
        bool Project(Vector3 world,out Vector2 screen){
            var delta=world-pose.Position;float depth=Vector3.Dot(delta,pose.Forward);
            screen=new Vector2(window.ViewWidth/2f+Vector3.Dot(delta,pose.Right)*focal/depth,window.ViewHeight/2f-Vector3.Dot(delta,pose.Up)*focal/depth);
            return depth>.02f&&float.IsFinite(screen.X)&&float.IsFinite(screen.Y);
        }
        if(!Project(feet,out var bottom)||!Project(feet+Vector3.UnitY*Height,out var top))return;
        var color=window.DarkTheme?new Color128(.95f,.74f,.36f,.72f):new Color128(.28f,.21f,.12f,.75f);
        void Strip(float y,float left,float right,float step){
            if(!Project(feet+Vector3.UnitY*y+side*left,out var a)||!Project(feet+Vector3.UnitY*Math.Min(Height,y+step)+side*right,out var b))return;
            float x=Math.Min(a.X,b.X),sy=Math.Min(a.Y,b.Y),w=Math.Abs(b.X-a.X),h=Math.Max(1,Math.Abs(b.Y-a.Y));
            if(x+w<0||x>window.ViewWidth||sy+h<0||sy>window.ViewHeight)return;
            renderer.Rectangle.Draw(null,new V2(x,sy),new V2(w,h),color);
        }
        // Horizontal bands preserve world height and perspective at each level.
        const float step=.01f;
        for(float y=0;y<Height;y+=step){
            if(y>=1.50f){float dy=(y-1.625f)/.125f;float half=.105f*MathF.Sqrt(Math.Max(0,1-dy*dy));Strip(y,-half,half,step);}
            else if(y>=1.42f)Strip(y,-.065f,.065f,step);
            else if(y>=.82f){
                float half=y>1.30f?.20f-(y-1.30f)*.4f:.15f+(y-.82f)*.105f;
                Strip(y,-half,half,step);
                if(y<1.34f){float arm=.20f+(1.34f-y)*.20f;Strip(y,-arm-.045f,-arm+.025f,step);Strip(y,arm-.025f,arm+.045f,step);}
            }else{
                float center=.07f+(.82f-y)*.075f,half=y<.08f?.075f:.055f;
                Strip(y,-center-half,-center+half,step);Strip(y,center-half,center+half,step);
            }
        }
        if(bottom.X>0&&bottom.X<window.ViewWidth-140&&bottom.Y>24&&bottom.Y<window.ViewHeight-24)
            renderer.OpenGlString.Draw(renderer.Fonts.SmallFont,"Pessoa · 1,75 m",new V2(bottom.X-40,bottom.Y+8),TextAlignment.TopLeft,color);
    }
}
