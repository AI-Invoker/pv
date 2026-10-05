// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AI-Invoker
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace PV
{
    internal static class Associations
    {
        internal const string AppName="PV 轻看";
        internal const string RegistrationName="PV";
        private static RegistryKey registrationHive=Registry.CurrentUser;
        [DllImport("shell32.dll")]private static extern void SHChangeNotify(uint e,uint f,IntPtr a,IntPtr b);
        [DllImport("shlwapi.dll",CharSet=CharSet.Unicode)]private static extern int AssocQueryString(uint flags,uint str,string assoc,string extra,StringBuilder output,ref uint size);
        internal static string Json(string s){return "\""+s.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n").Replace("\t","\\t")+"\"";}
        private static void Value(string key,string name,object value){using(RegistryKey k=registrationHive.CreateSubKey(key)){if(value is byte[])k.SetValue(name,value,RegistryValueKind.None);else k.SetValue(name,value);}}
        internal static void RegisterMachine(string exe){registrationHive=Registry.LocalMachine;try{Register(exe);}finally{registrationHive=Registry.CurrentUser;}}
        internal static void Register(string exe)
        {
            const string client="Software\\Clients\\Media\\PV",caps=client+"\\Capabilities";string command="\""+exe+"\" \"%1\"";
            Value(client,"",AppName);Value(client+"\\shell\\open\\command","",command);Value(client+"\\DefaultIcon","",exe+",0");
            Value(caps,"ApplicationName",RegistrationName);Value(caps,"ApplicationDescription","轻便快捷的图片、视频、音乐与 FBX 查看器，支持缩放、旋转、倍速和全屏。");Value(caps,"ApplicationIcon",exe+",0");
            Value("Software\\RegisteredApplications",RegistrationName,caps);
            using(RegistryKey registered=registrationHive.OpenSubKey("Software\\RegisteredApplications",true))registered.DeleteValue(AppName,false);
            Value("Software\\Microsoft\\Windows\\CurrentVersion\\App Paths\\PV.exe","",exe);
            Value("Software\\Classes\\Applications\\PV.exe","FriendlyAppName",AppName);
            Value("Software\\Classes\\Applications\\PV.exe\\shell\\open\\command","",command);
            foreach(string ext in Viewer.AllExtensions) {
                string id="PV."+ext.Substring(1);string type=Viewer.ImageExtensions.Contains(ext)?"图片":Viewer.ModelExtensions.Contains(ext)?"FBX 模型":Viewer.AudioExtensions.Contains(ext)?"音乐":"视频";
                Value("Software\\Classes\\"+id,"",AppName+" "+type);Value("Software\\Classes\\"+id,"FriendlyTypeName",AppName+" "+type);
                Value("Software\\Classes\\"+id+"\\Application","ApplicationName",AppName);
                Value("Software\\Classes\\"+id+"\\Application","ApplicationDescription","轻便快捷的图片、视频、音乐与 FBX 查看器");
                Value("Software\\Classes\\"+id+"\\Application","ApplicationIcon","\""+exe+"\",0");
                using(RegistryKey app=registrationHive.OpenSubKey("Software\\Classes\\"+id+"\\Application",true))app.DeleteValue("AppUserModelID",false);
                Value("Software\\Classes\\"+id+"\\DefaultIcon","",exe+",0");Value("Software\\Classes\\"+id+"\\shell\\open\\command","",command);
                Value("Software\\Classes\\"+ext+"\\OpenWithProgids",id,new byte[0]);
                Value("Software\\Classes\\Applications\\PV.exe\\SupportedTypes",ext,"");Value(caps+"\\FileAssociations",ext,id);
            }
            SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);
        }
        internal static void OpenSettings(){Process.Start("ms-settings:defaultapps?registeredAppUser="+RegistrationName);}
        internal static void SetFallbackDefaults(string exe)
        {
            Register(exe);
            foreach(string ext in Viewer.AllExtensions)Value("Software\\Classes\\"+ext,"","PV."+ext.Substring(1));
            SHChangeNotify(0x08000000,0x1000,IntPtr.Zero,IntPtr.Zero);
        }
        internal static string Query(string ext){uint n=32768;StringBuilder s=new StringBuilder((int)n);int r=AssocQueryString(0,2,ext,"open",s,ref n);return r==0?s.ToString():"";}
        internal static void WriteReport(string output)
        {
            string[] rows=Viewer.AllExtensions.Select(ext=>"  "+Json(ext)+": "+Json(Query(ext))).ToArray();
            File.WriteAllText(output,"{\n"+string.Join(",\n",rows)+"\n}",new UTF8Encoding(false));
        }
    }
}
