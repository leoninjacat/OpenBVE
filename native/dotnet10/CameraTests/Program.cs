using System;
using System.Numerics;
using RouteViewer;
static class Tests
{
    static void Check(bool pass,string message) { if(!pass)throw new Exception(message); }
    static bool Near(Vector3 a,Vector3 b)=>Vector3.Distance(a,b)<.002;
    static void DocumentTests()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        string folder=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"route-editor-test-"+Guid.NewGuid());
        System.IO.Directory.CreateDirectory(folder);
        try {
            var encodings=new System.Text.Encoding[]{new System.Text.UTF8Encoding(false),new System.Text.UTF8Encoding(true),System.Text.Encoding.Unicode,System.Text.Encoding.GetEncoding(1252)};
            foreach(var encoding in encodings) {
                string path=System.IO.Path.Combine(folder,"route.csv");
                System.IO.File.WriteAllText(path,"; Estação\r\n0, .Pitch 0\r\n",encoding);
                byte[] original=System.IO.File.ReadAllBytes(path);
                var document=RouteDocument.Open(path,encoding);document.Save(document.Text);
                Check(original.AsSpan().SequenceEqual(System.IO.File.ReadAllBytes(path)),"No-op save preserves source bytes and BOM");
                document.Save(document.Text+"25, .Pitch 1\n");
                Check(!System.IO.File.ReadAllText(path,encoding).Replace("\r\n", "").Contains('\n'),"Save normalizes typed newlines to original CRLF");
                Check(System.IO.File.ReadAllText(path,encoding).Contains("25, .Pitch 1"),"Save writes edits");
                System.IO.File.AppendAllText(path,"external edit",encoding);
                bool conflict=false;try{document.Save("replacement");}catch(System.IO.IOException){conflict=true;}
                Check(conflict && System.IO.File.ReadAllText(path,encoding).Contains("external edit"),"External edits are not overwritten");
            }
            var legacy=System.Text.Encoding.GetEncoding(1252);
            string legacyPath=System.IO.Path.Combine(folder,"legacy.csv");System.IO.File.WriteAllText(legacyPath,"estação",legacy);
            var legacyDocument=RouteDocument.Open(legacyPath,legacy);
            bool unsupported=false;try{legacyDocument.Save("漢字");}catch(System.Text.EncoderFallbackException){unsupported=true;}
            Check(unsupported && System.IO.File.ReadAllText(legacyPath,legacy)=="estação","Unrepresentable text does not corrupt legacy route");
            Console.WriteLine("Document tests passed: UTF-8, BOM, UTF-16, CP1252, CRLF, save, external conflict, encoding failures.");
        } finally {System.IO.Directory.Delete(folder,true);}
    }
    static void Main()
    {
        var pose=new CameraPose();
        pose.LookAt(new Vector3(10,5,-20),new Vector3(10,3,15));
        Vector3 target=pose.Target,eye=pose.Position;float distance=pose.Distance;
        Check(Near(eye,new Vector3(10,5,-20)),"LookAt reconstructs eye");
        pose.Orbit(80,25);
        Check(Near(pose.Target,target)&&Math.Abs(pose.Distance-distance)<.001,"Orbit preserves pivot and distance");
        Check(!Near(eye,pose.Position),"Orbit moves eye");
        eye=pose.Position;target=pose.Target;
        pose.Pan(70,-30,800);
        Check(Near(pose.Position-eye,pose.Target-target),"Pan translates eye and pivot together");
        Check(Math.Abs(Vector3.Dot(pose.Target-target,pose.Forward))<.001,"Pan remains in image plane");
        target=pose.Target;pose.Dolly(2);
        Check(pose.Distance<distance&&Near(target,pose.Target),"Dolly preserves pivot");
        eye=pose.Position;pose.Look(100,50);
        Check(Near(eye,pose.Position),"Look rotates around fixed eye");
        foreach(char axis in new[]{'X','Y','Z'}) {
            pose.Snap(axis);
            Check(float.IsFinite(pose.Up.Length())&&Math.Abs(Vector3.Dot(pose.Up,pose.Forward))<.001,"Axis basis is finite and orthogonal");
            var forward=pose.Forward;pose.Snap(axis,true);
            Check(Vector3.Dot(forward,pose.Forward)<-.999,"Opposite axis view");
        }
        pose.Orbit(0,100000);Check(float.IsFinite(pose.Position.Length()),"Orbit pole clamp");
        pose.Dolly(100000);Check(pose.Distance==.5f,"Dolly minimum");
        pose.Dolly(-100000);Check(pose.Distance==20000,"Dolly maximum");
        var first=new CameraPose();var second=new CameraPose();
        first.Fly(1,0,1,1,20);
        for(int i=0;i<100;i++)second.Fly(1,0,1,.01f,20);
        Check(Near(first.Target,second.Target),"Fly independent of frame rate");
        var cab=new TrackCameraState { Position=100 };
        cab.Move(1,1,1,2,5,0,1000);
        cab.Look(100,50);cab.Reset();
        Check(cab.Position==110 && Near(cab.Offset,new Vector3(0,2.5f,0)) && cab.Yaw==0 && cab.Pitch==0,"Cab reset preserves distance and restores offsets");
        var poseOnCurve=cab.Compose(new Vector3(20,7,30),Vector3.UnitX,Vector3.UnitY,-Vector3.UnitZ);
        Check(Near(poseOnCurve.Eye,new Vector3(20,9.5f,30)) && Near(poseOnCurve.Forward,Vector3.UnitX),"Cab follows curved track basis");
        var slope=Vector3.Normalize(new Vector3(0,.2f,1));var bankUp=Vector3.Normalize(Vector3.Cross(slope,Vector3.UnitX));
        var inclined=cab.Compose(Vector3.Zero,slope,bankUp,Vector3.UnitX);
        Check(Near(inclined.Eye,bankUp*2.5f) && Near(inclined.Forward,slope),"Cab follows grade and local up");
        var beforeOrbit=cab.Compose(Vector3.Zero,Vector3.UnitZ,Vector3.UnitY,Vector3.UnitX);
        var cabOffset=cab.Offset;
        cab.Look(60,30);
        var afterOrbit=cab.Compose(Vector3.Zero,Vector3.UnitZ,Vector3.UnitY,Vector3.UnitX);
        Check(Near(beforeOrbit.Eye,afterOrbit.Eye) && Near(cab.Offset,cabOffset) && Vector3.Dot(beforeOrbit.Forward,afterOrbit.Forward)<.99,"Cab rotation changes direction while preserving eye and local offset");
        Check(cab.Position==110,"Cab rotation keeps track anchor");
        cab.Look(40,20);
        var afterLook=cab.Compose(Vector3.Zero,Vector3.UnitZ,Vector3.UnitY,Vector3.UnitX);
        Check(Near(afterOrbit.Eye,afterLook.Eye),"Successive cab rotations keep eye fixed");
        cab.Reset();
        cab.Move(0,0,1,1000,10,0,120);Check(cab.Position==120,"Track endpoint clamp");
        DocumentTests();
        Console.WriteLine("Track camera tests passed: curve, grade, local offsets, reset, endpoint.");
        Console.WriteLine("Camera tests passed: orbit, pan, dolly, look, axis, poles, bounds, frame rate.");
    }
}
