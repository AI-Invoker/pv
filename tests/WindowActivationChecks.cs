// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AI-Invoker
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class WindowActivationChecks
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window,uint command);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    private static int checks;
    private static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    private static void Pump(Func<bool> ready,string message)
    {
        Stopwatch timeout=Stopwatch.StartNew();
        while(!ready())
        {
            Application.DoEvents();Thread.Sleep(10);
            if(timeout.ElapsedMilliseconds>6000)throw new Exception(message+"; foreground="+GetForegroundWindow());
        }
        Application.DoEvents();
    }
    private static Process Start(string executable,string argument,bool shell=false)
    {
        return Process.Start(new ProcessStartInfo(executable,argument){UseShellExecute=shell,WorkingDirectory=Path.GetDirectoryName(executable)});
    }
    private static bool Above(IntPtr window,IntPtr other)
    {
        for(IntPtr previous=GetWindow(window,3);previous!=IntPtr.Zero;previous=GetWindow(previous,3))
            if(previous==other)return false;
        return true;
    }
    private static void Wave(string path)
    {
        const int dataLength=96000;
        using(BinaryWriter writer=new BinaryWriter(File.Create(path)))
        {
            writer.Write(new[]{'R','I','F','F'});writer.Write(36+dataLength);writer.Write(new[]{'W','A','V','E'});
            writer.Write(new[]{'f','m','t',' '});writer.Write(16);writer.Write((short)1);writer.Write((short)1);
            writer.Write(48000);writer.Write(96000);writer.Write((short)2);writer.Write((short)16);
            writer.Write(new[]{'d','a','t','a'});writer.Write(dataLength);writer.Write(new byte[dataLength]);
        }
    }
    [STAThread] private static int Main(string[] args)
    {
        string executable=Path.GetFullPath(args[0]),directory=Path.GetFullPath(args[1]);
        Directory.CreateDirectory(directory);
        string first=Path.Combine(directory,"01-current.wav"),second=Path.Combine(directory,"02-next.wav"),report=Path.Combine(directory,"activation.json");
        Wave(first);Wave(second);Process viewer=null;IntPtr originalForeground=GetForegroundWindow();
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            using(Form launcher=new Form{Text="PV window activation checks",ClientSize=new Size(420,100),StartPosition=FormStartPosition.CenterScreen})
            {
                launcher.Controls.Add(new Label{Dock=DockStyle.Fill,Text="Checking file-open activation and minimized-window restoration.",TextAlign=ContentAlignment.MiddleCenter});
                launcher.Show();Application.DoEvents();
                viewer=Start(executable,"\""+first+"\"");
                Pump(()=>{viewer.Refresh();return !viewer.HasExited&&viewer.MainWindowHandle!=IntPtr.Zero;},"Viewer did not create its window");
                IntPtr window=viewer.MainWindowHandle;
                for(int mode=0;mode<4;mode++)
                {
                    ShowWindow(window,mode>=2?3:9);
                    if(mode==1||mode==3)ShowWindow(window,6);
                    SetWindowPos(window,new IntPtr(1),0,0,0,0,0x0013);
                    string file=mode%2==0?second:first;
                    using(Process request=Start(executable,"\""+file+"\""))
                    {
                        Pump(()=>{viewer.Refresh();return viewer.MainWindowTitle.StartsWith(Path.GetFileName(file),StringComparison.Ordinal)&&Above(window,launcher.Handle)&&!IsIconic(window);},"File-open request did not reveal the requested file in mode "+mode);
                        Pump(()=>request.HasExited,"Secondary file-open process did not exit");
                        Check(request.ExitCode==0,"Secondary launch failed");
                    }
                    viewer.Refresh();Check(!viewer.HasExited&&viewer.MainWindowHandle==window,"File-open request replaced the viewer process");
                    Check(viewer.MainWindowTitle.StartsWith(Path.GetFileName(file),StringComparison.Ordinal),"The requested file was not opened");
                    if(mode>=2)Check(IsZoomed(window),"Restoring the viewer lost its maximized state");
                    Check((GetWindowLong(window,-20)&8)==0,"File-open request left the viewer permanently topmost");
                    Console.WriteLine("PASS: file-open activation mode "+mode);
                }
                SetWindowPos(window,new IntPtr(1),0,0,0,0,0x0013);
                using(Process request=Start(executable,"\""+second+"\"",true))
                {
                    Pump(()=>{viewer.Refresh();return request.HasExited&&viewer.MainWindowTitle.StartsWith(Path.GetFileName(second),StringComparison.Ordinal)&&Above(window,launcher.Handle);},"A shell-broker open left the requested file behind the launcher");
                    Check(request.ExitCode==0&&!IsIconic(window),"Shell-broker launch did not restore the viewer");
                    Check((GetWindowLong(window,-20)&8)==0,"Shell-broker launch left the viewer permanently topmost");
                }
                Console.WriteLine("PASS: shell-broker file-open visibility");
                ShowWindow(window,6);
                using(Process request=Start(executable,"--diagnostics \""+report+"\""))
                {
                    Pump(()=>request.HasExited&&File.Exists(report),"Diagnostic request did not complete");
                    Check(request.ExitCode==0,"Diagnostic request failed");
                }
                Check(GetForegroundWindow()!=window,"Diagnostics took foreground focus");
                Check(IsIconic(window),"Diagnostics restored a minimized window");
                using(Process request=Start(executable,""))
                {
                    Pump(()=>request.HasExited&&Above(window,launcher.Handle)&&!IsIconic(window),"Launching without a file did not restore the viewer");
                    Check(request.ExitCode==0&&IsZoomed(window),"Empty launch lost the previous window state");
                }
                Console.WriteLine("PASS: "+checks+" activation checks; diagnostics remain in the background");
            }
            return 0;
        }
        catch(Exception ex){if(viewer!=null){viewer.Refresh();Console.Error.WriteLine("viewer="+viewer.Id+", window="+viewer.MainWindowHandle+", title="+viewer.MainWindowTitle+", minimized="+IsIconic(viewer.MainWindowHandle));}Console.Error.WriteLine(ex.Message);return 1;}
        finally
        {
            if(viewer!=null)
            {
                viewer.Refresh();
                if(!viewer.HasExited)
                {
                    PostMessage(viewer.MainWindowHandle,0x0010,IntPtr.Zero,IntPtr.Zero);
                    if(!viewer.WaitForExit(3000)){viewer.Kill();viewer.WaitForExit();}
                }
                viewer.Dispose();
            }
            if(originalForeground!=IntPtr.Zero)SetForegroundWindow(originalForeground);
        }
    }
}
