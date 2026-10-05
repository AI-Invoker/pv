// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AI-Invoker
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace PV
{
    internal static class FbxAnimationChecks
    {
        private static int checks;
        private static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
        private static float[] Read(ModelData data,IntPtr pointer)
        {
            float[] values=new float[checked((int)data.Info.Vertices*12)];Marshal.Copy(pointer,values,0,values.Length);return values;
        }
        private static double Difference(float[] a,float[] b,bool pose)
        {
            double maximum=0;
            for(int i=0;i<a.Length;i++)if(pose?i%12<6:i%12>=6)maximum=Math.Max(maximum,Math.Abs(a[i]-b[i]));
            return maximum;
        }
        private static void Settle(ModelPlayback playback)
        {
            Stopwatch timeout=Stopwatch.StartNew();
            do{playback.Tick();Thread.Sleep(1);if(timeout.ElapsedMilliseconds>5000)throw new Exception("Animation worker timed out");}while(playback.Evaluating);
        }
        private static void CheckFile(string path,bool animated)
        {
            using(ModelData data=ModelData.Load(path,CancellationToken.None))
            {
                Check(data.Info.Vertices>0,"No vertices: "+path);
                Check((data.Animations.Length>0)==animated,"Unexpected animation count: "+path);
                Console.WriteLine(Path.GetFileName(path)+": "+data.Animations.Length+" clips, "+data.Triangles+" triangles");
                if(!animated)return;
                float[] original=Read(data,data.Vertices);
                for(int clip=0;clip<data.Animations.Length;clip++)
                {
                    ModelData.AnimationClip animation=data.Animations[clip];
                    Check(animation.Duration>0&&!double.IsInfinity(animation.Duration),"Invalid clip duration");
                    float[] first=null;double movement=0;
                    for(int sample=0;sample<=4;sample++)
                    {
                        IntPtr active=data.Vertices;float[] before=Read(data,active);
                        ModelData.AnimationFrame frame=data.Evaluate(clip,animation.Duration*sample/4,1);
                        Check(frame.Error==null,"Evaluation failed: "+frame.Error);
                        float[] values=Read(data,frame.Vertices);
                        Check(Difference(before,Read(data,active),true)==0,"Worker modified the displayed buffer");
                        Check(Difference(original,values,false)==0,"Animation changed UV or material colors");
                        bool finite=true;foreach(float value in values)if(float.IsNaN(value)||float.IsInfinity(value)){finite=false;break;}
                        Check(finite,"Non-finite vertex");
                        if(first==null)first=values;else movement=Math.Max(movement,Difference(first,values,true));
                    }
                    Check(movement>1e-5,"Animation did not move geometry: "+animation.Name);
                    ModelData.AnimationFrame restored=data.Evaluate(-1,0,1);
                    Check(restored.Error==null&&Difference(original,Read(data,restored.Vertices),true)<1e-6,"Default pose was not restored");
                    Console.WriteLine("  "+animation.Name+" duration="+animation.Duration.ToString("0.###")+" begin="+animation.Begin.ToString("0.###")+" movement="+movement.ToString("0.###"));
                }
                Check(data.Evaluate(999,0,1).Error!=null,"Invalid clip was accepted");
                Check(data.Evaluate(0,double.NaN,1).Error!=null,"NaN position was accepted");
                Check(data.Evaluate(0,0,2).Error!=null,"Invalid buffer was accepted");

                double now=0;ModelPlayback playback=new ModelPlayback(()=>now);string failure=null;
                playback.Failed+=message=>failure=message;playback.Attach(data);Settle(playback);
                Check(playback.Available&&!playback.Paused&&playback.Selection==0,"Autoplay did not start");
                double duration=playback.Duration;
                now=duration*.2;playback.Tick();Settle(playback);
                Check(Math.Abs(playback.Position-duration*.2)<1e-6,"Clock did not advance playback");
                playback.TogglePause();Settle(playback);float[] paused=Read(data,data.Vertices);
                now+=2;playback.Tick();Settle(playback);
                Check(Math.Abs(playback.Position-duration*.2)<1e-6&&Difference(paused,Read(data,data.Vertices),true)==0,"Paused animation changed");
                playback.Seek(duration*.6);Settle(playback);Check(Math.Abs(playback.Position-duration*.6)<1e-6,"Seek failed");
                playback.SetRate(2);playback.TogglePause();now+=duration*.1;playback.Tick();Settle(playback);
                Check(Math.Abs(playback.Position-duration*.8)<1e-6,"2x playback failed");
                now+=duration*.2;playback.Tick();Settle(playback);
                Check(Math.Abs(playback.Position-duration*.2)<1e-6&&!playback.Paused,"Loop boundary failed");
                playback.SetLoop(false);now+=duration;playback.Tick();Settle(playback);
                Check(playback.Paused&&Math.Abs(playback.Position-duration)<1e-6,"Non-looping playback did not stop at end");
                playback.TogglePause();Settle(playback);Check(!playback.Paused&&playback.Position==0,"Replay did not restart");
                playback.Select(-1);Settle(playback);Check(playback.Paused&&playback.Duration==0&&Difference(original,Read(data,data.Vertices),true)<1e-6,"Default pose selection failed");
                playback.Select(0);playback.Tick();playback.Seek(duration*.3);playback.Select(-1);Settle(playback);
                Check(Difference(original,Read(data,data.Vertices),true)<1e-6,"A stale worker overwrote the selected pose");
                playback.Detach();Check(!playback.Available&&!playback.Evaluating,"Detach left a playback worker");
                Check(failure==null,failure??"Playback failure");
            }
        }
        internal static int Main(string[] args)
        {
            try
            {
                string folder=args[0];
                foreach(string name in new[]{"blender_279_sausage_7400_binary.fbx","blender_279_sausage_6100_ascii.fbx","blender440_shape_weight_anim_7400_binary.fbx","maya_anim_layers_7500_ascii.fbx","maya_game_sausage_7500_binary_combined.fbx","maya_game_sausage_6100_ascii_combined.fbx","maya_blend_shape_cube_7700_binary.fbx","max2009_cube_anim_6100_ascii.fbx","maya_anim_pivot_rotate_7700_ascii.fbx"})CheckFile(Path.Combine(folder,name),true);
                CheckFile(Path.Combine(folder,"blender_272_cube_7400_binary.fbx"),false);
                for(int i=0;i<12;i++)
                {
                    ModelData data=ModelData.Load(Path.Combine(folder,"maya_game_sausage_7500_binary_combined.fbx"),CancellationToken.None);
                    ModelPlayback playback=new ModelPlayback();playback.Attach(data);playback.Tick();playback.Detach();data.Dispose();
                }
                Thread.Sleep(100);Console.WriteLine("PASS: "+checks+" checks; rapid close/reopen completed");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        }
    }
}
