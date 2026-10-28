using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using LibRender2.Shaders;
using OpenBveApi.Colors;
using OpenBveApi.Math;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
namespace RouteViewer;
internal static class ViewerIcons
{
    static readonly Dictionary<string,int> textures=new();
    static Shader shader;
    static System.Numerics.Vector3 globeForward, globeUp;
    static void Globe(SKCanvas canvas, SKPaint pen)
    {
        var camera = Program.Renderer.Camera;
        var right = CameraRig.ToV(camera.AbsoluteSide);
        var up = CameraRig.ToV(camera.AbsoluteUp);
        var forward = CameraRig.ToV(camera.AbsoluteDirection);
        SKPoint Project(System.Numerics.Vector3 v) => new(16 + 12*System.Numerics.Vector3.Dot(v,right), 16 - 12*System.Numerics.Vector3.Dot(v,up));
        void Curve(Func<float,System.Numerics.Vector3> point) {
            for(int i=0;i<96;i++) {
                var a=point(i*MathF.PI/48);var b=point((i+1)*MathF.PI/48);
                pen.Color=new SKColor(220,226,232,(byte)(System.Numerics.Vector3.Dot((a+b)/2,forward)<0?230:65));
                canvas.DrawLine(Project(a),Project(b),pen);
            }
        }
        pen.StrokeWidth=.45f;
        for(int i=0;i<4;i++){float angle=i*MathF.PI/4;Curve(t=>new(MathF.Cos(t)*MathF.Cos(angle),MathF.Sin(t),MathF.Cos(t)*MathF.Sin(angle)));}
        for(int i=-1;i<=1;i++){float y=i*.5f,r=MathF.Sqrt(1-y*y);Curve(t=>new(r*MathF.Cos(t),y,r*MathF.Sin(t)));}
        pen.Color=SKColors.White;canvas.DrawCircle(16,16,12,pen);
        var axes=new[]{System.Numerics.Vector3.UnitX,System.Numerics.Vector3.UnitY,System.Numerics.Vector3.UnitZ};
        var colors=new[]{new SKColor(248,105,105),new SKColor(118,216,139),new SKColor(111,169,255)};
        for(int i=0;i<3;i++){pen.Color=colors[i];pen.StrokeWidth=.8f;canvas.DrawLine(new SKPoint(16,16),Project(axes[i]),pen);pen.Style=SKPaintStyle.Fill;var end=Project(axes[i]);canvas.DrawCircle(end,1.1f,pen);pen.Style=SKPaintStyle.Stroke;}
    }
    internal static void Draw(NewRenderer renderer,string name,float x,float y,float w,float h,Color128 tint)
    {
        if(shader==null)shader=new Shader(renderer,"rectangle","rectangle",true);
        if(name=="globe") {
            var camera=Program.Renderer.Camera;
            var forward=CameraRig.ToV(camera.AbsoluteDirection);var up=CameraRig.ToV(camera.AbsoluteUp);
            if(forward!=globeForward || up!=globeUp) {
                if(textures.Remove(name,out int old))GL.DeleteTexture(old);
                globeForward=forward;globeUp=up;
            }
        }
        if(!textures.TryGetValue(name,out int texture)) {
            using var bitmap=new SKBitmap(new SKImageInfo(128,128,SKColorType.Rgba8888,SKAlphaType.Unpremul));
            using var canvas=new SKCanvas(bitmap);canvas.Clear(SKColors.Transparent);canvas.Scale(4);
            using var pen=new SKPaint {Color=SKColors.White,IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeWidth=1.55f,StrokeCap=SKStrokeCap.Round,StrokeJoin=SKStrokeJoin.Round};
            void Line(float a,float b,float c,float d)=>canvas.DrawLine(a,b,c,d,pen);
            void Path(params float[] points) {using var path=new SKPath();path.MoveTo(points[0],points[1]);for(int i=2;i<points.Length;i+=2)path.LineTo(points[i],points[i+1]);canvas.DrawPath(path,pen);}
            switch(name) {
                case "disc": pen.Style=SKPaintStyle.Fill;canvas.DrawCircle(16,16,15,pen);break;
                case "orbit-ring":pen.StrokeWidth=.35f;canvas.DrawCircle(16,16,15,pen);break;
                case "plate": pen.Style=SKPaintStyle.Fill;canvas.DrawRoundRect(1,1,30,30,6,6,pen);break;
                case "save":Path(5,4,24,4,28,8,28,28,4,28,4,4,5,4);Path(10,4,10,13,23,13,23,4);Path(10,28,10,19,23,19,23,28);break;
                case "human":canvas.DrawCircle(16,6,3,pen);Path(7,20,11,12,21,12,25,20);Path(12,13,12,21,9,29);Path(20,13,20,21,23,29);Line(12,21,20,21);break;
                case "cube":Path(5,10,16,4,27,10,16,16,5,10,5,23,16,29,27,23,27,10);Line(16,16,16,29);break;
                case "plane":Path(4,20,14,9,28,14,18,25,4,20);break;
                case "cylinder":canvas.DrawOval(new SKRect(6,4,26,12),pen);Line(6,8,6,24);Line(26,8,26,24);canvas.DrawArc(new SKRect(6,20,26,28),0,180,false,pen);break;
                case "folder":Path(5,25,5,8,13,8,16,11,27,11,27,14);Path(5,25,9,15,29,15,25,25,5,25);break;
                case "pivot":canvas.DrawCircle(16,16,7,pen);Line(16,2,16,12);Line(16,20,16,30);Line(2,16,12,16);Line(20,16,30,16);canvas.DrawCircle(16,16,1,pen);break;
                case "move":Line(16,4,16,28);Line(4,16,28,16);Path(11,9,16,4,21,9);Path(11,23,16,28,21,23);Path(9,11,4,16,9,21);Path(23,11,28,16,23,21);break;
                case "rotate":canvas.DrawArc(new SKRect(6,6,26,26),-55,295,false,pen);Path(26,4,26,12,18,12);canvas.DrawCircle(16,16,2,pen);break;
                case "scale":canvas.DrawRect(5,18,9,9,pen);Line(13,19,27,5);Path(18,5,27,5,27,14);break;
                case "reload":canvas.DrawArc(new SKRect(7,7,25,25),-55,290,false,pen);Path(25,5,25,12,18,12);break;
                case "previous":case "next":
                    canvas.DrawCircle(16,12,5,pen);Line(8,22,25,22);if(name=="previous")Path(12,18,7,22,12,26);else Path(21,18,26,22,21,26);break;
                case "home":Path(11,6,6,6,6,11);Path(21,6,26,6,26,11);Path(6,21,6,26,11,26);Path(21,26,26,26,26,21);canvas.DrawCircle(16,16,3,pen);break;
                case "wire":Path(4,24,16,6,28,24,4,24,11,14,22,14,16,24,16,6);break;
                case "events":Line(9,5,9,28);Path(9,6,25,6,21,12,25,18,9,18);break;
                case "log":canvas.DrawRoundRect(5,6,22,17,3,3,pen);Path(10,23,10,28,16,23);Line(10,12,22,12);Line(10,17,19,17);break;
                case "help":canvas.DrawCircle(16,16,12,pen);canvas.DrawArc(new SKRect(11,8,21,18),180,235,false,pen);Line(16,18,16,21);canvas.DrawCircle(16,25,.5f,pen);break;
                case "track":Path(10,5,5,28);Path(22,5,27,28);for(int i=9;i<29;i+=5)Line(9-(i-9)*.2f,i,23+(i-9)*.2f,i);break;
                case "camera":canvas.DrawRoundRect(4,9,24,17,3,3,pen);Path(9,9,12,5,20,5,23,9);canvas.DrawCircle(16,17,5,pen);break;
                case "document":Path(8,3,20,3,26,9,26,29,8,29,8,3);Path(20,3,20,9,26,9);break;
                case "eye":using(var p=new SKPath()){p.MoveTo(3,16);p.CubicTo(10,4,22,4,29,16);p.CubicTo(22,28,10,28,3,16);canvas.DrawPath(p,pen);}canvas.DrawCircle(16,16,5,pen);break;
                case "globe":Globe(canvas,pen);break;
                case "up":Path(8,19,16,11,24,19);break;
                case "down":Path(8,13,16,21,24,13);break;
                case "left":Path(19,8,11,16,19,24);break;
                case "right":Path(13,8,21,16,13,24);break;
                case "plus":Line(9,16,23,16);Line(16,9,16,23);break;
                case "minus":Line(9,16,23,16);break;
                case "play":Path(11,7,25,16,11,25,11,7);break;
                case "pause":Line(12,8,12,24);Line(21,8,21,24);break;
                case "close":Line(9,9,23,23);Line(23,9,9,23);break;
            }
            byte[] pixels=new byte[128*128*4];Marshal.Copy(bitmap.GetPixels(),pixels,0,pixels.Length);
            texture=GL.GenTexture();GL.BindTexture(TextureTarget.Texture2D,texture);
            GL.TexImage2D(TextureTarget.Texture2D,0,PixelInternalFormat.Rgba,128,128,0,PixelFormat.Rgba,PixelType.UnsignedByte,pixels);
            GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMinFilter,(int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMagFilter,(int)TextureMagFilter.Linear);
            textures[name]=texture;
        }
        shader.Activate();shader.SetTexture(0);
        Matrix4D.CreateOrthographicOffCenter(0,renderer.Screen.Width,renderer.Screen.Height,0,-1,1,out var projection);
        shader.SetCurrentProjectionMatrix(projection);shader.SetCurrentModelViewMatrix(Matrix4D.Identity);
        shader.SetPoint(new Vector2(x,y));shader.SetSize(new Vector2(w,h));shader.SetCoordinates(Vector2.One);shader.SetColor(tint);
        GL.ActiveTexture(TextureUnit.Texture0);GL.BindTexture(TextureTarget.Texture2D,texture);
        renderer.dummyVao.Bind();GL.DrawArrays(PrimitiveType.Triangles,0,6);
        renderer.LastBoundTexture=null;
    }
    internal static void Dispose() {foreach(int texture in textures.Values)GL.DeleteTexture(texture);textures.Clear();}
}
