// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AI-Invoker
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace PV
{
    internal sealed class ModelHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal ModelHandle(IntPtr value) : base(true) { SetHandle(value); }
        protected override bool ReleaseHandle() { ModelData.NativeFree(handle); return true; }
    }

    internal sealed class ModelData : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct SceneInfo { internal uint Vertices, Parts, Materials, Meshes, Textures; internal float Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct Part { internal uint First, Count, Material, Reserved; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct Material { internal float R,G,B,A; internal int Texture; internal uint Reserved; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct Texture
        {
            internal IntPtr Relative, Absolute, Filename, Content, Pixels;
            internal ulong ContentSize;
            internal uint Width, Height, WrapU, WrapV;
        }

        [DllImport("pv-fbx.dll",CallingConvention=CallingConvention.Cdecl,EntryPoint="pv_fbx_load")]
        private static extern IntPtr NativeLoad([MarshalAs(UnmanagedType.LPWStr)] string path,IntPtr cancelled,[Out] byte[] error,uint errorSize);
        [DllImport("pv-fbx.dll",CallingConvention=CallingConvention.Cdecl,EntryPoint="pv_fbx_free")]
        internal static extern void NativeFree(IntPtr scene);
        [DllImport("pv-fbx.dll",CallingConvention=CallingConvention.Cdecl,EntryPoint="pv_fbx_info")]
        private static extern void NativeInfo(ModelHandle scene,out SceneInfo info);
        [DllImport("pv-fbx.dll",CallingConvention=CallingConvention.Cdecl,EntryPoint="pv_fbx_vertices")]
        private static extern IntPtr NativeVertices(ModelHandle scene);
        [DllImport("pv-fbx.dll",CallingConvention=CallingConvention.Cdecl,EntryPoint="pv_fbx_parts")]
        private static extern IntPtr NativeParts(ModelHandle scene);
        [DllImport("pv-fbx.dll",CallingConvention=CallingConvention.Cdecl,EntryPoint="pv_fbx_materials")]
        private static extern IntPtr NativeMaterials(ModelHandle scene);
        [DllImport("pv-fbx.dll",CallingConvention=CallingConvention.Cdecl,EntryPoint="pv_fbx_texture_info")]
        private static extern int NativeTexture(ModelHandle scene,uint index,out Texture texture);
        [DllImport("pv-fbx.dll",CallingConvention=CallingConvention.Cdecl,EntryPoint="pv_fbx_decode_texture")]
        private static extern int NativeDecodeTexture(ModelHandle scene,uint index,IntPtr bytes,uint size);

        private static readonly SemaphoreSlim importGate=new SemaphoreSlim(1,1);
        private ModelHandle handle;
        internal SceneInfo Info;
        internal Part[] Parts;
        internal Material[] Materials;
        internal Texture[] Textures;
        internal IntPtr Vertices;
        internal int LoadedTextures, MissingTextures;
        internal uint Triangles { get { return Info.Vertices/3; } }

        private ModelData(ModelHandle owner)
        {
            handle=owner;NativeInfo(handle,out Info);Vertices=NativeVertices(handle);
            Parts=ReadArray<Part>(NativeParts(handle),Info.Parts);
            Materials=ReadArray<Material>(NativeMaterials(handle),Info.Materials);
            Textures=new Texture[Info.Textures];
        }
        private static T[] ReadArray<T>(IntPtr pointer,uint count) where T:struct
        {
            T[] result=new T[count];int stride=Marshal.SizeOf(typeof(T));
            for(int i=0;i<result.Length;i++)result[i]=(T)Marshal.PtrToStructure(IntPtr.Add(pointer,checked(i*stride)),typeof(T));
            return result;
        }
        private static string Utf8(IntPtr pointer)
        {
            if(pointer==IntPtr.Zero)return "";int length=0;while(Marshal.ReadByte(pointer,length)!=0)length++;
            byte[] bytes=new byte[length];Marshal.Copy(pointer,bytes,0,length);return Encoding.UTF8.GetString(bytes);
        }
        internal static ModelData Load(string path,CancellationToken token)
        {
            importGate.Wait(token);
            try
            {
                token.ThrowIfCancellationRequested();int[] cancelFlag={0};GCHandle pin=GCHandle.Alloc(cancelFlag,GCHandleType.Pinned);
                IntPtr raw=IntPtr.Zero;
                try
                {
                    using(token.Register(()=>Interlocked.Exchange(ref cancelFlag[0],1)))
                    {
                        byte[] error=new byte[1024];raw=NativeLoad(path,pin.AddrOfPinnedObject(),error,(uint)error.Length);
                        if(raw==IntPtr.Zero)
                        {
                            token.ThrowIfCancellationRequested();int end=Array.IndexOf(error,(byte)0);if(end<0)end=error.Length;
                            throw new IOException("FBX 读取失败："+Encoding.UTF8.GetString(error,0,end));
                        }
                    }
                }
                finally { pin.Free(); }
                ModelHandle owner=new ModelHandle(raw);ModelData data=null;
                try
                {
                    token.ThrowIfCancellationRequested();data=new ModelData(owner);
                    data.LoadTextures(path,token);return data;
                }
                catch { if(data!=null)data.Dispose();else owner.Dispose();throw; }
            }
            finally { importGate.Release(); }
        }
        private void LoadTextures(string modelPath,CancellationToken token)
        {
            for(uint i=0;i<Info.Textures;i++)
            {
                token.ThrowIfCancellationRequested();Texture texture;NativeTexture(handle,i,out texture);bool decoded=false;
                if(texture.ContentSize>0)decoded=NativeDecodeTexture(handle,i,IntPtr.Zero,0)!=0;
                if(!decoded)
                {
                    string file=FindTexture(modelPath,Utf8(texture.Relative),Utf8(texture.Filename),Utf8(texture.Absolute));
                    if(file!=null)
                    {
                        try
                        {
                            byte[] bytes;
                            using(FileStream stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                            {
                                if(stream.Length>128L*1024*1024)throw new IOException("Texture too large");
                                using(MemoryStream buffer=new MemoryStream()){stream.CopyTo(buffer);bytes=buffer.ToArray();}
                            }
                            token.ThrowIfCancellationRequested();GCHandle pin=GCHandle.Alloc(bytes,GCHandleType.Pinned);
                            try { decoded=NativeDecodeTexture(handle,i,pin.AddrOfPinnedObject(),(uint)bytes.Length)!=0; }
                            finally { pin.Free(); }
                        }
                        catch(IOException){}catch(UnauthorizedAccessException){}
                    }
                }
                NativeTexture(handle,i,out Textures[i]);if(decoded)LoadedTextures++;else MissingTextures++;
            }
        }
        private static string FindTexture(string modelPath,params string[] names)
        {
            string folder=Path.GetDirectoryName(modelPath);List<string> candidates=new List<string>();
            foreach(string name in names)
            {
                if(string.IsNullOrWhiteSpace(name))continue;string value=name.Replace('/','\\');
                try
                {
                    if(Path.IsPathRooted(value))candidates.Add(value);else candidates.Add(Path.Combine(folder,value));
                    string basename=Path.GetFileName(value);
                    candidates.Add(Path.Combine(folder,basename));
                    candidates.Add(Path.Combine(Path.ChangeExtension(modelPath,".fbm"),basename));
                    candidates.Add(Path.Combine(folder,"textures",basename));
                }
                catch(ArgumentException){}catch(NotSupportedException){}
            }
            HashSet<string> seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(string candidate in candidates)
            {
                try
                {
                    string full=Path.GetFullPath(candidate);
                    if(!full.StartsWith("\\\\",StringComparison.Ordinal)&&seen.Add(full)&&File.Exists(full))return full;
                }
                catch(ArgumentException){}catch(NotSupportedException){}catch(PathTooLongException){}
            }
            return null;
        }
        public void Dispose(){if(handle!=null){handle.Dispose();handle=null;}Vertices=IntPtr.Zero;}
    }
}
