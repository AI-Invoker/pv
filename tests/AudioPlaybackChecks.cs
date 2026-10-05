// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AI-Invoker
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using PV;

internal static class AudioPlaybackChecks
{
    private static int assertions;
    private static void Require(bool value,string message){assertions++;if(!value)throw new Exception(message);}
    private static void Pump(MpvPlayer player,int milliseconds)
    {
        Stopwatch clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<milliseconds){player.Poll();Thread.Sleep(10);}
    }
    private static void Until(MpvPlayer player,Func<bool> done,string message)
    {
        Stopwatch clock=Stopwatch.StartNew();bool reached=false;while(clock.ElapsedMilliseconds<6000){player.Poll();if(done()){reached=true;break;}Thread.Sleep(10);}
        if(!reached)Console.WriteLine("state: time="+player.Get("time-pos")+", duration="+player.Get("duration")+", pause="+player.Get("pause")+", eof="+player.Get("eof-reached")+", seeking="+player.Get("seeking")+", speed="+player.Get("speed")+", file="+player.Get("filename"));
        Require(reached,message);
    }
    private static void Main(string[] args)
    {
        try { Run(args); }
        catch(Exception ex){Console.WriteLine("Audio check failed: "+ex.GetType().FullName);Console.WriteLine(ex.Message);Environment.ExitCode=1;}
    }
    private static void Run(string[] args)
    {
        Console.WriteLine("Starting audio decoder checks without a window or sound output.");
        string[] files=Directory.GetFiles(args[0]).Where(x=>Path.GetFileNameWithoutExtension(x)=="playback").OrderBy(x=>x).ToArray();
        Require(files.Length>=8,"Audio fixtures missing");
        using(MpvPlayer player=new MpvPlayer(IntPtr.Zero))
        {
            string failure=null;int loads=0;player.Failed+=message=>failure=message;player.Loaded+=()=>loads++;
            foreach(string file in files)
            {
                failure=null;player.Load(file,false,true);
                Until(player,()=>player.IsLoaded||failure!=null,"Audio load timed out: "+Path.GetExtension(file));
                Require(failure==null,"Audio decode failed: "+failure);Require(player.IsLoaded,"Audio not marked loaded");
                Require(player.Number("duration")>3.8,"Audio duration missing");
                Require(player.Get("vid")=="no","Audio mode selected a video track");
                Require(player.Get("audio-display")=="no","Audio mode enabled cover-art video output");
                if(new[]{".mp3",".flac",".m4a"}.Contains(Path.GetExtension(file)))
                {
                    Require(player.Metadata("title")=="PV 播放检查","Unicode title metadata was lost");
                    Require(player.Metadata("artist")=="PV 轻看","Unicode artist metadata was lost");
                }
                player.Set("pause","yes");Pump(player,70);double pausedAt=player.Number("time-pos");Pump(player,150);
                Require(player.Flag("pause"),"Pause was not applied");Require(Math.Abs(player.Number("time-pos")-pausedAt)<.12,"Paused audio progressed");
                player.Set("speed",2);Require(Math.Abs(player.Number("speed")-2)<.001,"2x speed was not applied");
                player.Set("volume",37);Require(Math.Abs(player.Number("volume")-37)<.001,"Volume was not applied");
                player.Set("mute","yes");Require(player.Flag("mute"),"Mute was not applied");player.Set("mute","no");
                // The reported position includes decoder and output buffering;
                // compressed audio can lag the paused seek target until resumed.
                player.Command("seek","1.5","absolute+exact");Until(player,()=>{double position=player.Number("time-pos");return position>=1.0&&position<=1.8;},"Seek did not reach the requested region: "+Path.GetExtension(file));
                player.Set("pause","no");Until(player,()=>player.Number("time-pos")>1.8,"Audio did not progress after resuming: "+Path.GetExtension(file));
                Console.WriteLine(Path.GetExtension(file)+": load, duration, pause, seek, speed, volume, mute and resume passed");
                player.Stop();
            }
            player.Load(files.First(x=>Path.GetExtension(x)==".wav"),false,true);
            Until(player,()=>player.IsLoaded,"Loop fixture failed to load");player.Set("speed",4);player.Set("loop-file","inf");
            player.Command("seek","3.5","absolute+exact");player.Set("pause","no");Pump(player,750);
            Require(!player.Flag("eof-reached"),"Single-track loop stopped at the end");Require(player.Number("time-pos")<3.5,"Single-track loop did not restart");
            player.Set("loop-file","no");player.Command("seek","3.9","absolute+exact");player.Set("pause","no");
            Until(player,()=>player.Flag("eof-reached"),"End of track was not observable for folder continuation");
            Require(loads>=files.Length+1,"Loaded callbacks were lost");
            player.Stop();string invalid=Path.Combine(args[0],"invalid.mp3");File.WriteAllText(invalid,"invalid audio test");failure=null;player.Load(invalid,false,true);
            Until(player,()=>failure!=null,"Invalid audio did not report a decode error");Require(!player.IsLoaded,"Invalid audio was marked loaded");
        }
        Console.WriteLine("Passed "+assertions.ToString(CultureInfo.InvariantCulture)+" audio assertions across "+files.Length+" formats; no window or sound output was used.");
    }
}
