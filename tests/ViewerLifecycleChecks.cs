// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AI-Invoker
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace PV
{
    internal static class ViewerLifecycleChecks
    {
        private static int checks,errors;
        private static string fixtures,temporary;
        private static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
        private static object Field(object owner,string name){return owner.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(owner);}
        private static void Call(object owner,string name,params object[] args){owner.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(owner,args);}
        private static void Pump(Func<bool> ready)
        {
            Stopwatch timeout=Stopwatch.StartNew();
            do{Application.DoEvents();Thread.Sleep(2);if(timeout.ElapsedMilliseconds>8000)throw new Exception("Viewer did not complete loading");}while(!ready());
        }
        private static Viewer Window()
        {
            Viewer window=new Viewer(new[]{"--diagnostics",Path.Combine(temporary,"diagnostics.json")});
            window.GetType().GetField("settings",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(window,Path.Combine(temporary,"settings.ini"));
            window.StartPosition=FormStartPosition.Manual;window.Location=new Point(-3000,-3000);window.ShowInTaskbar=false;window.Show();
            // Force a missing ambient context, including after handle recreation.
            // Asynchronous loading must still update controls on their owner thread.
            SynchronizationContext.SetSynchronizationContext(null);
            return window;
        }
        [STAThread] internal static int Main(string[] args)
        {
            fixtures=args[0];temporary=Path.Combine(fixtures,"lifecycle");Directory.CreateDirectory(temporary);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException+=(s,e)=>{errors++;Console.Error.WriteLine(e.Exception.GetType().Name+": "+e.Exception.Message+"\n"+e.Exception.StackTrace);};
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Control.CheckForIllegalCrossThreadCalls=true;
            try
            {
                string animated=Path.Combine(fixtures,"blender_279_sausage_7400_binary.fbx"),stationary=Path.Combine(fixtures,"blender_272_cube_7400_binary.fbx");
                string image=Path.Combine(temporary,"sample.png");using(Bitmap sample=new Bitmap(1600,900))using(Graphics g=Graphics.FromImage(sample)){g.Clear(Color.CornflowerBlue);sample.Save(image);}
                using(Viewer window=Window())
                {
                    window.Open(animated);Pump(()=>!(bool)Field(window,"loading"));
                    ModelCanvas canvas=(ModelCanvas)Field(window,"modelSurface");
                    Check((string)Field(window,"error")==""&&canvas.Playback.Available,"Animated load failed");
                    Call(window,"ToggleFull");Call(window,"ToggleFull");SynchronizationContext.SetSynchronizationContext(null);
                    for(int i=0;i<5;i++){window.Open(animated);window.Open(stationary);Pump(()=>!(bool)Field(window,"loading"));Check(canvas.Model!=null&&canvas.Model.Triangles==12&&!canvas.Playback.Available,"A stale model overwrote the latest file");}
                    window.Open(image);Pump(()=>!(bool)Field(window,"loading"));Check(((ImageCanvas)Field(window,"canvas")).Picture!=null,"Image load failed without a synchronization context");
                    window.Open(animated);Pump(()=>!(bool)Field(window,"loading"));
                    ComboBox animation=(ComboBox)Field(window,"animation");Check(animation.Items.Count==3&&animation.SelectedIndex==1,"Animation controls missing");
                    animation.SelectedIndex=0;Pump(()=>!canvas.Playback.Evaluating);Check(canvas.Playback.Paused,"Default pose did not stop animation");
                    animation.SelectedIndex=2;Call(window,"TogglePause");Pump(()=>!canvas.Playback.Evaluating);Check(canvas.Playback.Selection==1&&canvas.Playback.Paused,"Animation selection/pause failed");
                    Call(window,"OpenModelViews");Application.DoEvents();Call(window,"OpenMenu");Application.DoEvents();window.Close();
                }
                for(int i=0;i<8;i++)using(Viewer window=Window()){window.Open(i%2==0?animated:image);window.Close();Application.DoEvents();}
                Stopwatch drain=Stopwatch.StartNew();while(drain.ElapsedMilliseconds<200){Application.DoEvents();Thread.Sleep(2);}
                GC.Collect();GC.WaitForPendingFinalizers();Check(errors==0,"UI exceptions occurred during switching or closing");
                Console.WriteLine("PASS: "+checks+" lifecycle checks; 8 closes during loading; no cross-thread access or unhandled UI exceptions");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex.GetType().Name+": "+ex.Message+"\n"+ex.StackTrace);return 1;}
        }
    }
}
