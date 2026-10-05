// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AI-Invoker
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace PV
{
    // libmpv uses UTF-8 even on Windows; never pass ANSI file paths.
    internal sealed class MpvPlayer : IDisposable
    {
        private IntPtr handle;
        internal event Action Loaded;
        internal event Action<string> Failed;
        internal bool IsLoaded { get; private set; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MpvEvent { public int Id; public int Error; public ulong Reply; public IntPtr Data; }
        [StructLayout(LayoutKind.Sequential)]
        private struct EndFile { public int Reason; public int Error; public long PlaylistId; public long InsertId; public int InsertCount; }
        [DllImport("libmpv-2.dll", CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr mpv_create();
        [DllImport("libmpv-2.dll", CallingConvention=CallingConvention.Cdecl)] private static extern int mpv_initialize(IntPtr ctx);
        [DllImport("libmpv-2.dll", CallingConvention=CallingConvention.Cdecl)] private static extern void mpv_terminate_destroy(IntPtr ctx);
        [DllImport("libmpv-2.dll", CallingConvention=CallingConvention.Cdecl)] private static extern int mpv_set_option_string(IntPtr ctx, IntPtr name, IntPtr value);
        [DllImport("libmpv-2.dll", CallingConvention=CallingConvention.Cdecl)] private static extern int mpv_set_property_string(IntPtr ctx, IntPtr name, IntPtr value);
        [DllImport("libmpv-2.dll", CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr mpv_get_property_string(IntPtr ctx, IntPtr name);
        [DllImport("libmpv-2.dll", CallingConvention=CallingConvention.Cdecl)] private static extern int mpv_command(IntPtr ctx, IntPtr args);
        [DllImport("libmpv-2.dll", CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);
        [DllImport("libmpv-2.dll", CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr mpv_error_string(int code);
        [DllImport("libmpv-2.dll", CallingConvention=CallingConvention.Cdecl)] private static extern void mpv_free(IntPtr data);

        private static IntPtr Utf8(string s) { byte[] b=Encoding.UTF8.GetBytes(s+"\0"); IntPtr p=Marshal.AllocHGlobal(b.Length); Marshal.Copy(b,0,p,b.Length); return p; }
        private static string Read(IntPtr p) { if(p==IntPtr.Zero)return ""; int n=0; while(Marshal.ReadByte(p,n)!=0)n++; byte[] b=new byte[n]; Marshal.Copy(p,b,0,n); return Encoding.UTF8.GetString(b); }
        private void Option(string name,string value) { IntPtr n=Utf8(name),v=Utf8(value); try { int r=mpv_set_option_string(handle,n,v); if(r<0)throw new InvalidOperationException(name+": "+Read(mpv_error_string(r))); } finally { Marshal.FreeHGlobal(n); Marshal.FreeHGlobal(v); } }

        internal MpvPlayer(IntPtr window)
        {
            handle=mpv_create(); if(handle==IntPtr.Zero)throw new InvalidOperationException("无法创建播放内核");
            try {
                Option("config","no"); Option("load-scripts","no"); Option("input-default-bindings","no");
                Option("input-vo-keyboard","no"); Option("input-cursor","no"); Option("osc","no");
                if(window==IntPtr.Zero){Option("vo","null");Option("ao","null");}
                else {Option("wid",window.ToInt64().ToString(CultureInfo.InvariantCulture));Option("vo","gpu-next");Option("gpu-api","d3d11");Option("hwdec","auto-safe");}
                Option("keep-open","yes"); Option("idle","yes"); Option("cursor-autohide","no");
                Option("osd-level","0"); Option("terminal","no"); Option("ytdl","no");
                Option("demuxer-max-bytes","32MiB"); Option("image-display-duration","inf");
                Option("audio-pitch-correction","yes");
                int r=mpv_initialize(handle); if(r<0)throw new InvalidOperationException(Read(mpv_error_string(r)));
            } catch { Dispose(); throw; }
        }
        internal void Set(string name,string value) { if(handle==IntPtr.Zero)return; IntPtr n=Utf8(name),v=Utf8(value); try { mpv_set_property_string(handle,n,v); } finally { Marshal.FreeHGlobal(n); Marshal.FreeHGlobal(v); } }
        internal void Set(string name,double value) { Set(name,value.ToString(CultureInfo.InvariantCulture)); }
        internal string Get(string name) { if(handle==IntPtr.Zero)return ""; IntPtr n=Utf8(name); try { IntPtr p=mpv_get_property_string(handle,n); try { return Read(p); } finally { if(p!=IntPtr.Zero)mpv_free(p); } } finally { Marshal.FreeHGlobal(n); } }
        internal double Number(string name) { double v; return double.TryParse(Get(name),NumberStyles.Float,CultureInfo.InvariantCulture,out v)?v:0; }
        internal bool Flag(string name) { return Get(name)=="yes"; }
        internal void Command(params string[] args)
        {
            if(handle==IntPtr.Zero)return; IntPtr[] strs=new IntPtr[args.Length]; IntPtr array=Marshal.AllocHGlobal((args.Length+1)*IntPtr.Size);
            try { for(int i=0;i<args.Length;i++){strs[i]=Utf8(args[i]);Marshal.WriteIntPtr(array,i*IntPtr.Size,strs[i]);} Marshal.WriteIntPtr(array,args.Length*IntPtr.Size,IntPtr.Zero); int r=mpv_command(handle,array); if(r<0)throw new InvalidOperationException(Read(mpv_error_string(r))); }
            finally { foreach(IntPtr p in strs)if(p!=IntPtr.Zero)Marshal.FreeHGlobal(p); Marshal.FreeHGlobal(array); }
        }
        internal string Metadata(string key){string value=Get("metadata/by-key/"+key);return value.Length>0?value:Get("metadata/by-key/"+key.ToUpperInvariant());}
        internal void Load(string path,bool image,bool audio=false) { IsLoaded=false; Set("vid",audio?"no":"auto");Set("audio-display",audio?"no":"embedded-first");Set("pause","no"); Set("video-zoom",0); Set("video-pan-x",0); Set("video-pan-y",0); Set("video-align-x",0); Set("video-align-y",0); Set("video-recenter",image?"yes":"no"); Set("video-rotate",0); Set("loop-file",image?"inf":"no"); Command("loadfile",path,"replace"); }
        internal void Stop() { if(handle!=IntPtr.Zero){Command("stop");IsLoaded=false;} }
        internal void Poll()
        {
            if(handle==IntPtr.Zero)return;
            for(int i=0;i<40;i++) { IntPtr p=mpv_wait_event(handle,0); MpvEvent e=(MpvEvent)Marshal.PtrToStructure(p,typeof(MpvEvent)); if(e.Id==0)break;
                if(e.Id==8){IsLoaded=true;if(Loaded!=null)Loaded();}
                if(e.Id==7&&e.Data!=IntPtr.Zero){EndFile end=(EndFile)Marshal.PtrToStructure(e.Data,typeof(EndFile));if(end.Reason==4){IsLoaded=false;if(Failed!=null)Failed("无法解码此文件："+Read(mpv_error_string(end.Error)));}}
            }
        }
        public void Dispose() { if(handle!=IntPtr.Zero){mpv_terminate_destroy(handle);handle=IntPtr.Zero;} }
    }
}
