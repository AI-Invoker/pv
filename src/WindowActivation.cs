// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AI-Invoker
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PV
{
    internal static class WindowActivation
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllowSetForegroundWindow(uint processId);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr window,int command);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);

        internal static void GrantToExistingInstance()
        {
            // A launch initiated by the user can pass its foreground permission
            // to the existing viewer before the single-instance message is sent.
            using(Process current=Process.GetCurrentProcess())
            {
                string executable=Path.GetFullPath(Application.ExecutablePath);
                foreach(Process candidate in Process.GetProcessesByName(current.ProcessName))
                using(candidate)
                {
                    try
                    {
                        if(candidate.Id!=current.Id&&candidate.SessionId==current.SessionId&&
                           string.Equals(candidate.MainModule.FileName,executable,StringComparison.OrdinalIgnoreCase))
                            AllowSetForegroundWindow((uint)candidate.Id);
                    }
                    catch(Win32Exception){}
                    catch(InvalidOperationException){}
                }
            }
        }

        internal static void Show(Form window)
        {
            if(window.IsDisposed||window.Disposing)return;
            IntPtr handle=window.Handle;
            if(IsIconic(handle))ShowWindow(handle,9); // SW_RESTORE preserves a maximized window.
            if(!window.Visible)window.Show();
            if(SetForegroundWindow(handle)){window.Activate();return;}
            // Background shell brokers may not carry foreground permission.
            // Reveal an explicit open request without attaching input queues or
            // retaining a topmost window after the request has been handled.
            const uint flags=0x0013; // NOMOVE | NOSIZE | NOACTIVATE
            IntPtr restore=window.TopMost?new IntPtr(-1):new IntPtr(-2);
            try{SetWindowPos(handle,new IntPtr(-1),0,0,0,0,flags);}
            finally{SetWindowPos(handle,restore,0,0,0,0,flags);}
        }
    }
}
