// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AI-Invoker
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace PV
{
    internal static class SkeletonFbxChecks
    {
        private static int checks;
        private static string fixtures;
        private static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        private static float[] Read(ModelData data,IntPtr pointer){float[] values=new float[checked((int)data.Info.Vertices*12)];Marshal.Copy(pointer,values,0,values.Length);return values;}
        private static double Difference(float[] a,float[] b)
        {double maximum=0;for(int i=0;i<a.Length;i++)if(i%12<3)maximum=Math.Max(maximum,Math.Abs(a[i]-b[i]));return maximum;}
        private static double MotionInView(float[] a,float[] b)
        {
            double maximum=0;
            for(int i=0;i<a.Length;i+=12)
            {
                double ar=0,br=0;for(int axis=0;axis<3;axis++){ar+=a[i+axis]*a[i+axis];br+=b[i+axis]*b[i+axis];}
                if(ar>1.15*1.15||br>1.15*1.15)continue;
                for(int axis=0;axis<3;axis++)maximum=Math.Max(maximum,Math.Abs(a[i+axis]-b[i+axis]));
            }
            return maximum;
        }
        private static void Settle(ModelPlayback playback)
        {Stopwatch watch=Stopwatch.StartNew();do{playback.Tick();Thread.Sleep(1);if(watch.ElapsedMilliseconds>5000)throw new Exception("Skeleton worker timed out");}while(playback.Evaluating);}
        private static string Scene(string objects,string connections)
        {
            return "; FBX 7.4.0 project-local generated regression fixture\nFBXHeaderExtension: { FBXVersion: 7400 }\n"+
                "GlobalSettings: { Properties70: { P: \"UpAxis\", \"int\", \"Integer\", \"\", 1\nP: \"UpAxisSign\", \"int\", \"Integer\", \"\", 1\nP: \"UnitScaleFactor\", \"double\", \"Number\", \"\", 100 } }\n"+
                "Objects: {\n"+objects+"\n}\nConnections: {\n"+connections+"\n}\n";
        }
        private static string Model(int id,string name,string kind,double x,double y,double z)
        {
            return "Model: "+id+", \"Model::"+name+"\", \""+kind+"\" {\nProperties70: { P: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A\", "+x.ToString(System.Globalization.CultureInfo.InvariantCulture)+", "+y.ToString(System.Globalization.CultureInfo.InvariantCulture)+", "+z.ToString(System.Globalization.CultureInfo.InvariantCulture)+" }\n}\n";
        }
        private static string Bone(int id,string name,double x,double y,double z)
        {
            return Model(id,name,"LimbNode",x,y,z)+"NodeAttribute: "+(100000+id)+", \"NodeAttribute::"+name+"\", \"LimbNode\" { TypeFlags: \"Skeleton\" }\n";
        }
        private static string Link(int child,int parent){return "C: \"OO\", "+child+", "+parent+"\n";}
        private static string Write(string name,string data){string path=Path.Combine(fixtures,name);File.WriteAllText(path,data,new UTF8Encoding(false));return path;}
        private static void AddChannel(int id,int model,string property,string axis,double first,double last,ref string objects,ref string links)
        {
            objects+="AnimationCurveNode: "+id+", \"AnimCurveNode::Channel\", \"\" { Properties70: { P: \"d|X\", \"Number\", \"\", \"A\", 0\nP: \"d|Y\", \"Number\", \"\", \"A\", 0\nP: \"d|Z\", \"Number\", \"\", \"A\", 0 } }\n"+
                "AnimationCurve: "+(id+1)+", \"AnimCurve::"+axis+"\", \"\" { Default: 0\nKeyVer: 4008\nKeyTime: *2 { a: 0,46186158000 }\nKeyValueFloat: *2 { a: "+first.ToString(System.Globalization.CultureInfo.InvariantCulture)+","+last.ToString(System.Globalization.CultureInfo.InvariantCulture)+" }\nKeyAttrFlags: *1 { a: 4 }\nKeyAttrDataFloat: *4 { a: 0,0,0,0 }\nKeyAttrRefCount: *1 { a: 2 }\n}\n";
            links+=Link(id,11)+"C: \"OP\", "+id+", "+model+", \""+property+"\"\nC: \"OP\", "+(id+1)+", "+id+", \"d|"+axis+"\"\n";
        }
        private static void CheckGeometry(ModelData data)
        {
            Check(data.IsSkeleton&&data.Bones>0,"Skeleton classification");
            Check(data.Info.Meshes==0&&data.Triangles==0,"Preview helpers must not be reported as source meshes or triangles");
            Check(data.Info.Vertices==data.Bones*24,"Fixed geometry per bone");
            Check(data.Parts.Length==1&&data.Parts[0].First==0&&data.Parts[0].Count==data.Info.Vertices,"All preview geometry is drawable");
            Check(data.Materials.Length==1&&data.Materials[0].Texture==-1&&data.Textures.Length==0,"Skeleton does not require textures");
            float[] values=Read(data,data.Vertices);bool finite=true,opaque=true;double extent=0;int litNormals=0;
            for(int i=0;i<values.Length;i++){if(float.IsNaN(values[i])||float.IsInfinity(values[i]))finite=false;if(i%12<3)extent=Math.Max(extent,Math.Abs(values[i]));}
            for(int i=0;i<values.Length;i+=12){double n=Math.Sqrt(values[i+3]*values[i+3]+values[i+4]*values[i+4]+values[i+5]*values[i+5]);if(Math.Abs(n-1)<1e-4)litNormals++;if(values[i+11]!=1)opaque=false;}
            Check(finite&&extent<1.1,"Normalized finite default-pose geometry");Check(litNormals==data.Info.Vertices,"Unit lighting normals for every helper face");Check(opaque,"Opaque skeleton geometry");
        }
        private static double FramedBones(float[] values,out double longestHelper)
        {
            int framed=0;longestHelper=0;
            for(int bone=0;bone<values.Length;bone+=24*12)
            {
                double radius=0;
                for(int vertex=bone;vertex<bone+24*12;vertex+=12)
                    radius=Math.Max(radius,Math.Sqrt(values[vertex]*values[vertex]+values[vertex+1]*values[vertex+1]+values[vertex+2]*values[vertex+2]));
                if(radius<=1.15)framed++;
                double length=0;for(int axis=0;axis<3;axis++){double delta=values[bone+axis]-values[bone+12*12+axis];length+=delta*delta;}
                longestHelper=Math.Max(longestHelper,Math.Sqrt(length));
            }
            return (double)framed/(values.Length/(24*12));
        }
        private static void CheckAnimation(string path,uint expectedBones,double minimumFramed)
        {
            using(ModelData data=ModelData.Load(path,CancellationToken.None))
            {
                CheckGeometry(data);if(expectedBones>0)Check(data.Bones==expectedBones,"Bone count");Check(data.Animations.Length==1,"Animation clip is retained");
                double duration=data.Animations[0].Duration;Check(duration>0,"Positive duration");
                float[] original=Read(data,data.Vertices),first=null;double movement=0,framedFraction=1,longestHelper=0;Stopwatch evaluation=Stopwatch.StartNew();
                for(int i=0;i<=8;i++)
                {
                    ModelData.AnimationFrame frame=data.Evaluate(0,duration*i/8,1);
                    Check(frame.Error==null,"Evaluate skeleton: "+frame.Error);Check(frame.Vertices!=data.Vertices,"Worker uses the inactive buffer");
                    float[] values=Read(data,frame.Vertices);Check(Difference(original,Read(data,data.Vertices))==0,"Visible buffer stays unchanged during evaluation");
                    bool valid=true;for(int v=0;v<values.Length;v++){if(float.IsNaN(values[v])||float.IsInfinity(values[v]))valid=false;}
                    double helper;framedFraction=Math.Min(framedFraction,FramedBones(values,out helper));longestHelper=Math.Max(longestHelper,helper);
                    Check(valid,"Finite animation geometry");if(first==null)first=values;else movement=Math.Max(movement,MotionInView(first,values));
                }
                evaluation.Stop();Check(movement>1e-5,"Animation must visibly move the skeleton");
                Check(framedFraction>=minimumFramed,"Body stays inside the fitted view: "+framedFraction);Check(longestHelper<=2.05,"Global controls must not draw world-length helper bones: "+longestHelper);
                ModelData.AnimationFrame restored=data.Evaluate(-1,0,1);Check(restored.Error==null&&Difference(original,Read(data,restored.Vertices))<1e-6,"Restore default skeleton pose");
                Check(data.Evaluate(99,0,1).Error!=null,"Invalid animation rejected");Check(data.Evaluate(0,double.NaN,1).Error!=null,"Invalid timestamp rejected");
                double now=0;ModelPlayback playback=new ModelPlayback(()=>now);string failure=null;playback.Failed+=error=>failure=error;
                playback.Attach(data);Settle(playback);Check(playback.Available&&!playback.Paused,"Skeleton starts playing");
                now=duration*.25;Settle(playback);Check(Math.Abs(playback.Position-duration*.25)<1e-6,"Playback advances");
                playback.TogglePause();Settle(playback);float[] paused=Read(data,data.Vertices);now+=2;Settle(playback);
                Check(Math.Abs(playback.Position-duration*.25)<1e-6&&Difference(paused,Read(data,data.Vertices))==0,"Pause stops skeleton motion");
                playback.Seek(duration*.5);Settle(playback);Check(Math.Abs(playback.Position-duration*.5)<1e-6,"Seek while paused");
                playback.SetRate(2);playback.TogglePause();now+=duration*.1;Settle(playback);Check(Math.Abs(playback.Position-duration*.7)<1e-6,"Skeleton playback speed");
                now+=duration*.3;Settle(playback);Check(Math.Abs(playback.Position-duration*.3)<1e-6,"Skeleton loops");
                playback.Select(-1);Settle(playback);Check(playback.Paused&&Difference(original,Read(data,data.Vertices))<1e-6,"Default pose selection");
                playback.Select(0);playback.Tick();playback.Seek(duration*.5);playback.Select(-1);Settle(playback);Check(Difference(original,Read(data,data.Vertices))<1e-6,"Stale frame cannot overwrite pose selection");
                playback.Detach();Check(!playback.Available&&!playback.Evaluating&&failure==null,"Playback cleanup");
                Console.WriteLine(Path.GetFileName(path)+": "+data.Bones+" bones, "+duration+" seconds, movement="+movement.ToString("0.####")+", framed="+framedFraction.ToString("P1")+", longest-helper="+longestHelper.ToString("0.####")+", 9 samples="+evaluation.ElapsedMilliseconds+" ms");
            }
        }
        internal static int Main(string[] args)
        {
            try
            {
                fixtures=Path.GetFullPath(args[0]);Directory.CreateDirectory(fixtures);
                string objects=Model(1,"Scene","Null",10,0,0)+Bone(2,"Root",0,0,0)+Model(3,"Intermediate","Null",0,.5,0)+Bone(4,"Tip",0,.5,0)+Bone(5,"Coincident",0,0,0);
                string links=Link(1,0)+Link(2,1)+Link(3,2)+Link(4,3)+Link(5,2)+Link(100002,2)+Link(100004,4)+Link(100005,5);
                string staticFile=Write("静态 骨架.fbx",Scene(objects,links));
                using(ModelData data=ModelData.Load(staticFile,CancellationToken.None))
                {CheckGeometry(data);Check(data.Bones==3&&data.Animations.Length==0,"Static skeleton and coincident joints");ModelPlayback p=new ModelPlayback();p.Attach(data);Check(!p.Available&&p.Paused,"Static skeleton has no playback");p.Detach();}
                string animation="AnimationStack: 10, \"AnimStack::Turn\", \"\" { Properties70: { P: \"LocalStart\", \"KTime\", \"Time\", \"\", 0\nP: \"LocalStop\", \"KTime\", \"Time\", \"\", 46186158000 } }\n"+
                    "AnimationLayer: 11, \"AnimLayer::BaseLayer\", \"\" {}\n";
                objects+=animation;links+=Link(11,10);AddChannel(12,2,"Lcl Rotation","Z",0,90,ref objects,ref links);
                string animatedFile=Write("animated-skeleton.fbx",Scene(objects,links));
                CheckAnimation(animatedFile,3,1);
                string motionObjects=Bone(1,"Reference",0,0,0)+Bone(2,"Motion",0,0,0)+Bone(3,"Body",0,1,0)+Bone(4,"Tip",0,1,0)+Bone(5,"Prop",0,0,0)+animation;
                string motionLinks=Link(1,0)+Link(2,1)+Link(3,2)+Link(4,3)+Link(5,2)+Link(11,10);
                for(int id=1;id<=5;id++)motionLinks+=Link(100000+id,id);
                AddChannel(12,2,"Lcl Translation","Z",33,35,ref motionObjects,ref motionLinks);
                AddChannel(14,5,"Lcl Translation","Z",-33,-35,ref motionObjects,ref motionLinks);
                AddChannel(16,3,"Lcl Rotation","Z",0,45,ref motionObjects,ref motionLinks);
                CheckAnimation(Write("world-offset-skeleton.fbx",Scene(motionObjects,motionLinks)),5,.6);
                using(ModelData data=ModelData.Load(Write("single-joint.fbx",Scene(Bone(1,"Joint",0,0,0),Link(1,0)+Link(100001,1))),CancellationToken.None))
                {CheckGeometry(data);Check(data.Bones==1,"Single joint has a visible marker without zero-size bounds");}
                string empty=Write("empty.fbx",Scene(Model(1,"OnlyGroup","Null",0,0,0),Link(1,0)));bool rejected=false;
                try{using(ModelData data=ModelData.Load(empty,CancellationToken.None)) {}}catch(IOException ex){rejected=ex.Message.Contains("no visible polygon mesh or skeleton");}Check(rejected,"Empty scene has an accurate error");
                CancellationTokenSource cancelled=new CancellationTokenSource();cancelled.Cancel();bool stopped=false;
                try{using(ModelData data=ModelData.Load(staticFile,cancelled.Token)) {}}catch(OperationCanceledException){stopped=true;}finally{cancelled.Dispose();}Check(stopped,"Cancelled skeleton import");
                for(int i=0;i<12;i++){ModelData data=ModelData.Load(animatedFile,CancellationToken.None);ModelPlayback p=new ModelPlayback();p.Attach(data);p.Tick();p.Detach();data.Dispose();}Thread.Sleep(100);Check(true,"Close and reopen during skeleton evaluation");
                if(args.Length>1)CheckAnimation(Path.GetFullPath(args[1]),0,.98);
                Console.WriteLine("PASS: "+checks+" skeleton checks (no windows opened).");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        }
    }
}
