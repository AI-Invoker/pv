// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AI-Invoker
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PV
{
    internal sealed class ModelPlayback
    {
        private readonly Func<double> clock;
        private ModelData model,frameModel;
        private Task<ModelData.AnimationFrame> frame;
        private int selection=-1,revision,frameRevision,frontBuffer;
        private double position,lastTime,rate=1;
        private bool paused=true,loop=true,dirty;
        internal event Action FrameReady;
        internal event Action<string> Failed;
        internal bool Available {get{return model!=null&&model.Animations.Length>0;}}
        internal int Selection {get{return selection;}}
        internal double Position {get{return position;}}
        internal double Duration {get{return Available&&selection>=0?model.Animations[selection].Duration:0;}}
        internal double Rate {get{return rate;}}
        internal bool Paused {get{return paused;}}
        internal bool Loop {get{return loop;}}
        internal string Name {get{return Available&&selection>=0?model.Animations[selection].Name:"默认姿态";}}
        internal bool Evaluating {get{return frame!=null||dirty;}}

        internal ModelPlayback(Func<double> timeSource=null)
        {
            Stopwatch watch=Stopwatch.StartNew();clock=timeSource??(()=>watch.Elapsed.TotalSeconds);lastTime=clock();
        }
        internal void Attach(ModelData value)
        {
            Detach();model=value;frontBuffer=0;Select(Available?0:-1);
        }
        internal void Detach()
        {
            // In-flight P/Invoke retains the native SafeHandle until it returns.
            // Its result is discarded after switching or closing a model.
            model=null;frameModel=null;frame=null;revision++;selection=-1;
            position=0;paused=true;dirty=false;lastTime=clock();
        }
        internal void Select(int index)
        {
            if(model==null)return;
            if(index < -1 || index >= model.Animations.Length)throw new ArgumentOutOfRangeException("index");
            selection=index;position=0;paused=index<0;lastTime=clock();revision++;dirty=Available;
        }
        private void Advance()
        {
            double now=clock(),elapsed=Math.Max(0,now-lastTime);lastTime=now;
            if(!Available||paused||selection<0||elapsed<=0)return;
            position+=elapsed*rate;
            if(position>=Duration)
            {
                if(loop)position%=Duration;
                else {position=Duration;paused=true;}
            }
            dirty=true;
        }
        internal void TogglePause()
        {
            if(!Available)return;
            if(selection<0){Select(0);return;}
            Advance();paused=!paused;
            if(!paused&&position>=Duration){position=0;revision++;dirty=true;}
            // A pending frame from before pausing must not overwrite the final
            // paused position. The same rule applies to seeking and switching.
            if(paused){revision++;dirty=true;}
        }
        internal void Seek(double value)
        {
            if(!Available||selection<0||double.IsNaN(value)||double.IsInfinity(value))return;
            Advance();position=Math.Max(0,Math.Min(Duration,value));revision++;dirty=true;
        }
        internal void SetRate(double value)
        {
            if(double.IsNaN(value)||double.IsInfinity(value))return;
            Advance();rate=Math.Max(.25,Math.Min(4,value));
        }
        internal void SetLoop(bool value){Advance();loop=value;}
        internal void Tick()
        {
            Advance();
            if(frame!=null&&frame.IsCompleted)
            {
                ModelData.AnimationFrame ready=frame.Result;frame=null;
                if(ReferenceEquals(frameModel,model)&&frameRevision==revision)
                {
                    if(ready.Error!=null)
                    {
                        paused=true;dirty=false;
                        if(Failed!=null)Failed("动画播放失败："+ready.Error);
                        return;
                    }
                    model.Publish(ready);frontBuffer=ready.Buffer;
                    if(FrameReady!=null)FrameReady();
                }
            }
            if(!Available||frame!=null||!dirty)return;
            ModelData owner=model;int animation=selection,buffer=1-frontBuffer;double time=position;
            frameModel=owner;frameRevision=revision;dirty=false;
            frame=Task.Run(()=>owner.Evaluate(animation,time,buffer));
        }
    }
}
