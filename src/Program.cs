// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AI-Invoker
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualBasic.ApplicationServices;

namespace PV
{
    internal static class Program
    {
        [DllImport("user32.dll")]private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [STAThread] internal static void Main(string[] args)
        {
            try{SetProcessDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){}
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            if(args.Length>0&&args[0]=="--register"){Associations.Register(Application.ExecutablePath);return;}
            if(args.Length>0&&args[0]=="--register-machine"){Associations.RegisterMachine(Application.ExecutablePath);return;}
            if(args.Length>0&&args[0]=="--set-defaults"){Associations.SetFallbackDefaults(Application.ExecutablePath);return;}
            if(args.Length>0&&args[0]=="--associations"){Associations.WriteReport(args.Length>1?args[1]:Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"associations.json"));return;}
            try{if(args.Length==0||args[0]!="--diagnostics")WindowActivation.GrantToExistingInstance();new SingleViewer().Run(args);}catch(Exception ex){MessageBox.Show(ex.Message,"PV 轻看",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        }
    }
    internal sealed class SingleViewer:WindowsFormsApplicationBase
    {
        internal SingleViewer(){IsSingleInstance=true;EnableVisualStyles=true;ShutdownStyle=ShutdownMode.AfterMainFormCloses;}
        protected override void OnCreateMainForm(){MainForm=new Viewer(CommandLineArgs.ToArray());}
        protected override void OnStartupNextInstance(StartupNextInstanceEventArgs e){e.BringToForeground=false;base.OnStartupNextInstance(e);((Viewer)MainForm).Accept(e.CommandLine.ToArray());}
    }
    internal sealed class NaturalComparer:IComparer<string>
    {
        [DllImport("shlwapi.dll",CharSet=CharSet.Unicode)]private static extern int StrCmpLogicalW(string a,string b);
        public int Compare(string a,string b){return StrCmpLogicalW(Path.GetFileName(a),Path.GetFileName(b));}
    }
    internal sealed class Viewer:Form
    {
        internal static readonly string[] ImageExtensions={".jpg",".jpeg",".jpe",".jfif",".png",".bmp",".gif",".tif",".tiff",".ico",".webp",".avif",".heic",".heif"};
        internal static readonly string[] VideoExtensions={".mp4",".mkv",".mov",".avi",".webm",".wmv",".m4v",".mpg",".mpeg",".ts",".mts",".m2ts",".flv",".3gp",".ogv",".vob"};
        internal static readonly string[] AudioExtensions={".mp3",".wav",".flac",".m4a",".m4b",".aac",".ogg",".oga",".opus",".wma",".aif",".aiff",".ape",".wv",".mka"};
        internal static readonly string[] ModelExtensions={".fbx"};
        internal static readonly string[] AllExtensions=ImageExtensions.Concat(VideoExtensions).Concat(AudioExtensions).Concat(ModelExtensions).ToArray();
        private readonly Color barColor=Color.FromArgb(28,31,38),fg=Color.FromArgb(220,225,235);
        private Panel top,stage,imageBar,videoBar,modelBar,videoSurface;
        private ImageCanvas canvas;
        private AudioCanvas audioSurface;
        private ModelCanvas modelSurface;
        private CancellationTokenSource modelLoadCancellation;
        private readonly object modelCancellationLock=new object();
        private readonly CancellationTokenSource closingCancellation=new CancellationTokenSource();
        private readonly CancellationToken closingToken;
        private readonly int uiThread=Thread.CurrentThread.ManagedThreadId;
        private readonly Control uiDispatcher=new Control{Visible=false};
        private volatile bool shuttingDown;
        private Label filename,time;
        private StatusInfo info;
        private Button menu,wireButton,gridButton;
        private SvgButton pause,mute,previousFile,nextFile;
        private ZoomToolbar zoomTools;
        private ToolTip tips;
        private readonly IContainer menuComponents=new Container();
        private ContextMenuStrip mainMenu,modelMenu;
        private ToolStripMenuItem loopItem,imageRotateItem,imageActualItem,modelResetItem,modelWireItem,modelGridItem;
        private ComboBox speed,animation;
        private FlowLayoutPanel videoFlow;
        private VolumeSlider volume;
        private SeekBar seek;
        private MpvPlayer player;
        private System.Windows.Forms.Timer timer;
        private string path="",folder="",error="";
        private string[] siblings=new string[0];
        private int generation,index=-1,rotation;
        private bool video,audio,model,nativeImage,full,muted,loading,updating,layingFooter,nativeDragging;
        private Point nativeLast;
        private Rectangle restoreBounds;
        private FormWindowState restoreState;
        private double rate=1;
        private double vol=70,lastAudibleVolume=70;
        private readonly string settings=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PV","settings.ini");
        private readonly Stopwatch launch=Stopwatch.StartNew();
        private readonly Stopwatch mediaLoad=new Stopwatch();
        private long firstLoadMs;
        private long loadMs;
        [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(IntPtr h,int attr,ref int value,int size);
        internal Viewer(string[] args)
        {
            closingToken=closingCancellation.Token;
            // Keep the posting handle stable while fullscreen recreates the form.
            if(uiDispatcher.Handle==IntPtr.Zero)throw new InvalidOperationException("无法初始化窗口消息队列");
            Text="PV 轻看";BackColor=barColor;ForeColor=fg;Font=new Font("Microsoft YaHei UI",9F);MinimumSize=new Size(700,440);ClientSize=new Size(1060,720);StartPosition=FormStartPosition.CenterScreen;KeyPreview=true;AllowDrop=true;
            try{Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);}catch{}
            ReadSettings();BuildUI();
            Shown+=(s,e)=>{int dark=1;try{DwmSetWindowAttribute(Handle,20,ref dark,4);}catch{}Accept(args);};
            DragEnter+=(s,e)=>{if(e.Data.GetDataPresent(DataFormats.FileDrop))e.Effect=DragDropEffects.Copy;};
            DragDrop+=(s,e)=>{string[] files=(string[])e.Data.GetData(DataFormats.FileDrop);if(files.Length>0)Open(files[0]);};
            timer=new System.Windows.Forms.Timer{Interval=150};timer.Tick+=(s,e)=>Tick();timer.Start();
            FormClosing+=(s,e)=>{if(!e.Cancel)BeginShutdown();};
        }
        private void CancelModelLoad(){lock(modelCancellationLock){if(modelLoadCancellation!=null)modelLoadCancellation.Cancel();}}
        private void BeginShutdown()
        {
            if(shuttingDown)return;shuttingDown=true;generation++;CancelModelLoad();closingCancellation.Cancel();SaveSettings();
            if(timer!=null){timer.Stop();timer.Dispose();}if(canvas!=null)canvas.SetPicture(null);if(modelSurface!=null)modelSurface.ClearModel();
            if(player!=null){player.Dispose();player=null;}if(tips!=null)tips.Dispose();uiDispatcher.Dispose();
        }
        private async Task OnUi(Action action)
        {
            if(shuttingDown||IsDisposed||Disposing)return;
            if(Thread.CurrentThread.ManagedThreadId==uiThread){action();return;}
            TaskCompletionSource<bool> done=new TaskCompletionSource<bool>();
            CancellationTokenRegistration registration;
            try{registration=closingToken.Register(()=>done.TrySetResult(false));}
            catch(ObjectDisposedException){if(shuttingDown)return;throw;}
            using(registration)
            {
                try
                {
                    uiDispatcher.BeginInvoke((Action)(()=>
                    {
                        if(done.Task.IsCompleted)return;
                        try{if(!shuttingDown&&!IsDisposed&&!Disposing)action();done.TrySetResult(true);}
                        catch(Exception ex){done.TrySetException(ex);}
                    }));
                }
                catch(InvalidOperationException){if(shuttingDown||IsDisposed||Disposing)done.TrySetResult(false);else throw;}
                await done.Task.ConfigureAwait(false);
            }
        }
        private Button Button(string text,int width,Action action)
        {
            Button b=new Button{Text=text,Width=width,Height=34,FlatStyle=FlatStyle.Flat,ForeColor=fg,BackColor=Color.FromArgb(36,40,49),Cursor=Cursors.Hand,TabStop=true,Margin=new Padding(4,0,4,0)};
            b.FlatAppearance.BorderSize=0;b.FlatAppearance.MouseOverBackColor=Color.FromArgb(52,63,79);b.Click+=(s,e)=>action();return b;
        }
        private void BuildUI()
        {
            tips=new ToolTip{InitialDelay=450,ReshowDelay=100,AutoPopDelay=3000};
            top=new Panel{Dock=DockStyle.Top,Height=52,BackColor=barColor,Padding=new Padding(12,9,12,9)};
            Button open=Button("打开文件",88,OpenDialog);open.Dock=DockStyle.Left;
            menu=Button("···",44,OpenMenu);menu.Dock=DockStyle.Right;
            filename=new Label{Text="PV 轻看",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true,Padding=new Padding(16,0,12,0)};
            top.Controls.Add(filename);top.Controls.Add(menu);top.Controls.Add(open);
            imageBar=new FooterPanel{Dock=DockStyle.Bottom,Height=52,BackColor=barColor,Padding=new Padding(12,6,12,6)};
            info=new StatusInfo{BackColor=barColor,ForeColor=Color.FromArgb(137,150,171),Padding=new Padding(4,0,12,0),Text="图片、视频、音乐与 FBX · 拖入文件即可打开"};
            zoomTools=new ZoomToolbar();zoomTools.FitRequested+=Fit;zoomTools.ZoomRequested+=SetZoom;zoomTools.FullscreenRequested+=ToggleFull;zoomTools.WidthPreferenceChanged+=LayoutFooter;
            imageBar.Controls.Add(info);imageBar.Controls.Add(zoomTools);
            videoBar=new Panel{Dock=DockStyle.Bottom,Height=76,BackColor=barColor,Visible=false};
            seek=new SeekBar{Dock=DockStyle.Top};seek.Seek+=p=>{if(model){modelSurface.Playback.Seek(p);Tick();}else if(player!=null)player.Command("seek",p.ToString(CultureInfo.InvariantCulture),"absolute+exact");};
            time=new Label{Text="00:00 / 00:00",AutoSize=false,Width=130,Height=34,TextAlign=ContentAlignment.MiddleLeft,Margin=new Padding(6,0,10,0),ForeColor=Color.FromArgb(149,163,185)};
            videoFlow=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Height=36};
            pause=new SvgButton("pause","暂停"){Size=new Size(40,34),IconSize=28,Margin=new Padding(4,0,4,0)};
            pause.Click+=(s,e)=>TogglePause();videoFlow.Controls.Add(pause);videoFlow.Controls.Add(time);UpdatePauseButton(false);
            speed=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=88,Height=34,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(36,40,49),ForeColor=fg,Margin=new Padding(4,5,14,0),AccessibleName="播放倍速"};
            speed.Items.AddRange(new object[]{"0.25×","0.5×","0.75×","1×","1.25×","1.5×","1.75×","2×","2.5×","3×","4×"});
            speed.SelectedIndexChanged+=(s,e)=>{if(!updating&&speed.SelectedItem!=null){rate=double.Parse(speed.SelectedItem.ToString().Replace("×",""),CultureInfo.InvariantCulture);ApplyRate();UpdateInfo();}};UpdateSpeed();videoFlow.Controls.Add(speed);
            animation=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=210,DropDownWidth=360,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(36,40,49),ForeColor=fg,Margin=new Padding(4,5,8,0),AccessibleName="FBX 动画",Visible=false};
            animation.SelectedIndexChanged+=(s,e)=>{if(!updating&&model&&modelSurface.Model!=null&&animation.SelectedIndex>=0){modelSurface.Playback.Select(animation.SelectedIndex-1);Tick();UpdateInfo();}};
            videoFlow.Controls.Add(animation);tips.SetToolTip(animation,"选择动画；默认姿态用于查看静态模型");
            mute=new SvgButton("volume-on","静音"){Size=new Size(34,34),IconSize=24,Margin=new Padding(4,0,0,0)};
            mute.Click+=(s,e)=>ToggleMute();videoFlow.Controls.Add(mute);
            volume=new VolumeSlider{Value=vol,Margin=new Padding(0,0,8,0)};
            volume.ValueChanged+=(s,e)=>{
                vol=volume.Value;
                if(vol>0){lastAudibleVolume=vol;if(muted){muted=false;if(player!=null)player.Set("mute","no");}}
                if(player!=null)player.Set("volume",vol);UpdateVolumeButton();
            };
            videoFlow.Controls.Add(volume);UpdateVolumeButton();
            videoBar.Controls.Add(videoFlow);videoBar.Controls.Add(seek);
            videoBar.Resize+=(s,e)=>LayoutPlayback();videoFlow.SizeChanged+=(s,e)=>LayoutPlayback();
            modelBar=new Panel{Width=208,BackColor=barColor,Visible=false};
            FlowLayoutPanel modelFlow=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Padding=new Padding(0,3,8,0)};
            modelFlow.Controls.Add(Button("视角",56,OpenModelViews));
            wireButton=Button("线框",60,()=>modelSurface.ToggleWireframe());modelFlow.Controls.Add(wireButton);
            gridButton=Button("网格 ✓",60,()=>modelSurface.ToggleGrid());modelFlow.Controls.Add(gridButton);
            modelBar.Controls.Add(modelFlow);imageBar.Controls.Add(modelBar);
            imageBar.Layout+=(s,e)=>LayoutFooter();imageBar.FontChanged+=(s,e)=>LayoutFooter();
            stage=new Panel{Dock=DockStyle.Fill,BackColor=Color.FromArgb(19,21,26)};canvas=new ImageCanvas{Dock=DockStyle.Fill};canvas.ViewChanged+=UpdateInfo;
            audioSurface=new AudioCanvas{Dock=DockStyle.Fill,Visible=false};audioSurface.MouseClick+=(s,e)=>{if(e.Button==MouseButtons.Left)TogglePause();};audioSurface.DoubleClick+=(s,e)=>ToggleFull();
            audioSurface.MouseWheel+=(s,e)=>{if(audio)volume.Value=Math.Max(0,Math.Min(100,volume.Value+(e.Delta>0?5:-5)));};
            modelSurface=new ModelCanvas{Dock=DockStyle.Fill,Visible=false};modelSurface.ViewChanged+=()=>{wireButton.Text=modelSurface.Wireframe?"实体":"线框";gridButton.Text=modelSurface.GridVisible?"网格 ✓":"网格";UpdateInfo();};
            modelSurface.Failed+=ShowError;
            modelSurface.DoubleClick+=(s,e)=>ToggleFull();
            videoSurface=new Panel{Dock=DockStyle.Fill,BackColor=Color.Black,Visible=false};videoSurface.MouseDoubleClick+=(s,e)=>ToggleFull();videoSurface.MouseClick+=(s,e)=>{if(e.Button==MouseButtons.Left&&video)TogglePause();};
            videoSurface.Resize+=(s,e)=>{if(nativeImage){LimitNativeZoom();ConstrainNativePan();UpdateInfo();}};
            videoSurface.MouseWheel+=(s,e)=>{if(video){volume.Value=Math.Max(0,Math.Min(100,volume.Value+(e.Delta>0?5:-5)));}else if(nativeImage&&player!=null&&player.IsLoaded)SetNativeZoom(ViewScale()*(e.Delta>0?1.2:1/1.2),e.Location);};
            videoSurface.MouseDown+=NativeImageMouseDown;videoSurface.MouseMove+=NativeImageMouseMove;videoSurface.MouseUp+=(s,e)=>StopNativeDrag();
            videoSurface.MouseCaptureChanged+=(s,e)=>{if(!videoSurface.Capture){nativeDragging=false;videoSurface.Cursor=Cursors.Default;}};
            canvas.DoubleClick+=(s,e)=>ToggleFull();stage.Controls.Add(canvas);stage.Controls.Add(videoSurface);stage.Controls.Add(modelSurface);stage.Controls.Add(audioSurface);
            previousFile=new SvgButton("previous","上一个文件"){Visible=false,FillBackground=true,BackColor=stage.BackColor};
            nextFile=new SvgButton("next","下一个文件"){Visible=false,FillBackground=true,BackColor=stage.BackColor};
            previousFile.Click+=(s,e)=>Navigate(-1);nextFile.Click+=(s,e)=>Navigate(1);tips.SetToolTip(previousFile,"上一个");tips.SetToolTip(nextFile,"下一个");
            stage.Controls.Add(previousFile);stage.Controls.Add(nextFile);stage.Resize+=(s,e)=>LayoutNavigation();
            Controls.Add(stage);Controls.Add(videoBar);Controls.Add(imageBar);Controls.Add(top);SyncViewControls();
        }
        private void LayoutPlayback(){if(!shuttingDown&&!IsDisposed&&!Disposing&&videoFlow!=null&&!videoFlow.IsDisposed)videoFlow.Location=new Point(Math.Max(0,(videoBar.Width-videoFlow.PreferredSize.Width)/2),34);}
        private void ConfigurePlayback()
        {
            if(shuttingDown||IsDisposed||Disposing)return;
            bool animated=model&&modelSurface.Playback.Available;
            videoBar.Visible=video||audio||animated;animation.Visible=animated;mute.Visible=volume.Visible=video||audio;
            time.Width=animated?168:130;seek.Enabled=!model||modelSurface.Playback.Selection>=0;
            pause.Enabled=video||audio||animated;
            updating=true;
            try
            {
                animation.Items.Clear();
                if(animated){animation.Items.Add("默认姿态");animation.Items.AddRange(modelSurface.Model.Animations);animation.SelectedIndex=modelSurface.Playback.Selection+1;}
            }
            finally{updating=false;}
            LayoutPlayback();
        }
        private void OpenModelViews()
        {
            if(modelMenu==null)
            {
                modelMenu=new ContextMenuStrip(menuComponents){BackColor=barColor,ForeColor=fg,ShowImageMargin=false};
                modelMenu.Items.Add("复位视角    0",null,(s,e)=>Fit());modelMenu.Items.Add("正面    1",null,(s,e)=>modelSurface.Preset(0));
                modelMenu.Items.Add("侧面    2",null,(s,e)=>modelSurface.Preset(1));modelMenu.Items.Add("顶面    3",null,(s,e)=>modelSurface.Preset(2));
            }
            modelMenu.Show(modelBar,new Point(0,0),ToolStripDropDownDirection.AboveRight);
        }
        private void LayoutFooter()
        {
            if(shuttingDown||layingFooter||IsDisposed||Disposing||imageBar==null||info==null||zoomTools==null||modelBar==null)return;
            layingFooter=true;
            try
            {
                Rectangle area=new Rectangle(imageBar.Padding.Left,imageBar.Padding.Top,Math.Max(1,imageBar.ClientSize.Width-imageBar.Padding.Horizontal),Math.Max(1,imageBar.ClientSize.Height-imageBar.Padding.Vertical));
                double factor=Math.Max(.5,area.Height/40.0);int gap=(int)Math.Round(8*factor);
                int reserve=!full?Math.Min(area.Width/3,(int)Math.Round(120*factor)):0;
                int modelWidth=model?Math.Min((int)Math.Round(208*factor),Math.Max(0,area.Width-reserve-gap-(int)Math.Round(256*factor))):0;
                int remaining=area.Width-modelWidth;
                int toolsWidth=Math.Min(zoomTools.WantedWidth(area.Height),Math.Max(1,remaining-reserve-gap));
                int toolsLeft=area.Right-toolsWidth;
                modelBar.SetBounds(area.Left,area.Top,modelWidth,area.Height);
                zoomTools.SetBounds(toolsLeft,area.Top,toolsWidth,area.Height);
                info.SetBounds(area.Left+modelWidth,area.Top,Math.Max(0,toolsLeft-gap-area.Left-modelWidth),area.Height);
                info.Invalidate();
            }
            finally { layingFooter=false; }
        }
        private void LayoutNavigation()
        {
            if(previousFile==null||nextFile==null)return;float scale=Math.Max(.5f,zoomTools.Height/40f);int width=(int)(24*scale),height=(int)(48*scale),gap=(int)(6*scale);
            int y=Math.Max(0,(stage.Height-height)/2);previousFile.SetBounds(gap,y,width,height);nextFile.SetBounds(Math.Max(gap,stage.Width-gap-width),y,width,height);
            bool visible=path!="";previousFile.Visible=nextFile.Visible=visible;previousFile.Enabled=nextFile.Enabled=siblings.Length>1;
            if(visible){previousFile.BringToFront();nextFile.BringToFront();}
        }
        private double ViewScale()
        {
            if(model)return modelSurface.ZoomScale;
            if(nativeImage&&player!=null&&player.IsLoaded)return NativeImageFit()*Math.Pow(2,player.Number("video-zoom"));
            return canvas.ScaleFactor;
        }
        private double NativeImageFit()
        {
            SizeF size=NativeSourceSize();return Math.Min(videoSurface.ClientSize.Width/size.Width,videoSurface.ClientSize.Height/size.Height);
        }
        private SizeF NativeSourceSize()
        {
            double w=player.Number("width"),h=player.Number("height");if(rotation%180!=0){double swap=w;w=h;h=swap;}
            return new SizeF((float)Math.Max(1,w),(float)Math.Max(1,h));
        }
        private SizeF NativeRenderedSize()
        {
            if(!nativeImage||player==null||!player.IsLoaded)return SizeF.Empty;
            SizeF size=NativeSourceSize();double scale=ViewScale();return new SizeF((float)(size.Width*scale),(float)(size.Height*scale));
        }
        private bool NativeCanPan(){return nativeImage&&player!=null&&player.IsLoaded&&ImagePan.CanPan(NativeRenderedSize(),videoSurface.ClientSize);}
        private PointF NativePanOffset()
        {
            PointF limits=ImagePan.Limits(NativeRenderedSize(),videoSurface.ClientSize);
            return new PointF((float)(-player.Number("video-align-x")*limits.X),(float)(-player.Number("video-align-y")*limits.Y));
        }
        private void ApplyNativePan(PointF offset)
        {
            if(!nativeImage||player==null||!player.IsLoaded)return;
            SizeF size=NativeRenderedSize();PointF limits=ImagePan.Limits(size,videoSurface.ClientSize);offset=ImagePan.Clamp(offset,size,videoSurface.ClientSize);
            // Alignment stays inside mpv's image boundaries, including during
            // renderer resize; raw video-pan values can expose empty borders.
            player.Set("video-align-x",limits.X>.01f?-offset.X/limits.X:0);player.Set("video-align-y",limits.Y>.01f?-offset.Y/limits.Y:0);
            if(!ImagePan.CanPan(size,videoSurface.ClientSize))StopNativeDrag();
        }
        private void ConstrainNativePan(){if(nativeImage&&player!=null&&player.IsLoaded)ApplyNativePan(NativePanOffset());}
        private void StopNativeDrag(){nativeDragging=false;if(videoSurface!=null){videoSurface.Capture=false;videoSurface.Cursor=Cursors.Default;}}
        private void NativeImageMouseDown(object sender,MouseEventArgs e)
        {
            if(e.Button!=MouseButtons.Left||!NativeCanPan())return;
            if(!ImagePan.Bounds(NativeRenderedSize(),videoSurface.ClientSize,NativePanOffset()).Contains(e.Location))return;
            videoSurface.Focus();nativeLast=e.Location;nativeDragging=true;videoSurface.Capture=true;videoSurface.Cursor=Cursors.Hand;
        }
        private void NativeImageMouseMove(object sender,MouseEventArgs e)
        {
            if(!nativeDragging)return;
            if(!NativeCanPan()){StopNativeDrag();return;}
            PointF offset=NativePanOffset();offset.X+=e.X-nativeLast.X;offset.Y+=e.Y-nativeLast.Y;nativeLast=e.Location;ApplyNativePan(offset);
        }
        private void SetNativeZoom(double scale,Point anchor)
        {
            scale=ZoomRange.Clamp(scale);double old=Math.Max(.000001,ViewScale());PointF offset=NativePanOffset();
            double x=anchor.X-videoSurface.ClientSize.Width/2.0-offset.X,y=anchor.Y-videoSurface.ClientSize.Height/2.0-offset.Y;
            offset=new PointF((float)(offset.X+x*(1-scale/old)),(float)(offset.Y+y*(1-scale/old)));
            player.Set("video-zoom",Math.Log(scale/Math.Max(.000001,NativeImageFit()),2));ApplyNativePan(offset);UpdateInfo();
        }
        private void SyncViewControls()
        {
            if(shuttingDown||IsDisposed||Disposing||zoomTools==null||zoomTools.IsDisposed||zoomTools.Disposing||canvas==null||modelSurface==null)return;
            bool ready=model?modelSurface.Model!=null:(canvas.Picture!=null||(nativeImage&&player!=null&&player.IsLoaded));
            zoomTools.SetState(ViewScale(),ready,full,!(video||audio),model);LayoutFooter();LayoutNavigation();
        }
        internal void Accept(string[] args)
        {
            if(args.Length>0&&args[0]=="--diagnostics"){if(args.Length>1)WriteDiagnostics(args[1]);return;}
            WindowActivation.Show(this);
            if(args.Length>0)Open(args[0]);
        }
        private static bool Supported(string file){return AllExtensions.Contains(Path.GetExtension(file).ToLowerInvariant());}
        private void OpenDialog(){using(OpenFileDialog d=new OpenFileDialog{Title="打开图片、视频、音乐或 FBX 模型",Filter="可打开文件|"+string.Join(";",AllExtensions.Select(x=>"*"+x))+"|图片|"+string.Join(";",ImageExtensions.Select(x=>"*"+x))+"|视频|"+string.Join(";",VideoExtensions.Select(x=>"*"+x))+"|音乐|"+string.Join(";",AudioExtensions.Select(x=>"*"+x))+"|FBX 模型|*.fbx|所有文件|*.*",CheckFileExists=true})if(d.ShowDialog(this)==DialogResult.OK)Open(d.FileName);}
        private async void ScanFolder(string dir,int ticket,bool songsOnly)
        {
            string[] list=null;
            try{list=await Task.Run(()=>Directory.EnumerateFiles(dir).Where(x=>Supported(x)&&(!songsOnly||AudioExtensions.Contains(Path.GetExtension(x).ToLowerInvariant()))).OrderBy(x=>x,new NaturalComparer()).ToArray()).ConfigureAwait(false);}catch{}
            await OnUi(()=>{if(ticket!=generation)return;siblings=list??new[]{path};index=Array.FindIndex(siblings,x=>string.Equals(x,path,StringComparison.OrdinalIgnoreCase));UpdateInfo();});
        }
        internal async void Open(string file)
        {
            if(shuttingDown||IsDisposed||Disposing)return;
            try { file=Path.GetFullPath(file);if(Directory.Exists(file)){file=Directory.EnumerateFiles(file).Where(Supported).OrderBy(x=>x,new NaturalComparer()).FirstOrDefault();if(file==null)throw new IOException("此文件夹里没有可打开的图片、视频、音乐或 FBX");}if(!File.Exists(file))throw new FileNotFoundException("文件不存在",file);if(!Supported(file))throw new IOException("暂不支持此文件格式"); }
            catch(Exception ex){ShowError(ex.Message);return;}
            StopNativeDrag();int ticket=++generation;mediaLoad.Restart();loading=true;error="";path=file;Text=Path.GetFileName(path)+" — PV 轻看";filename.Text=Path.GetFileName(path);rotation=0;
            string ext=Path.GetExtension(path).ToLowerInvariant();bool nextAudio=AudioExtensions.Contains(ext);
            string dir=Path.GetDirectoryName(path);if(audio!=nextAudio||!string.Equals(folder,dir,StringComparison.OrdinalIgnoreCase)){folder=dir;siblings=new[]{path};index=0;ScanFolder(dir,ticket,nextAudio);}else{index=Array.FindIndex(siblings,x=>string.Equals(x,path,StringComparison.OrdinalIgnoreCase));if(siblings.Length<=1||index<0)ScanFolder(dir,ticket,nextAudio);}
            CancelModelLoad();modelSurface.ClearModel();modelSurface.Visible=false;
            if(player!=null){player.Stop();if(!VideoExtensions.Contains(ext)&&!nextAudio){player.Dispose();player=null;}}canvas.SetPicture(null);videoSurface.Visible=false;audioSurface.Visible=false;canvas.Visible=true;canvas.Message="正在打开…";canvas.Hint=Path.GetFileName(path);canvas.Invalidate();
            video=VideoExtensions.Contains(ext);audio=nextAudio;model=ModelExtensions.Contains(ext);nativeImage=false;imageBar.Visible=true;ConfigurePlayback();modelBar.Visible=model;seek.UpdatePosition(0,0);time.Text="00:00 / 00:00";UpdatePauseButton(false);SyncViewControls();
            if(audio){audioSurface.SetTrack(Path.GetFileNameWithoutExtension(path),"","");audioSurface.SetStatus("正在打开…");}
            if(model){await LoadModel(file,ticket);return;}
            if(video||audio){LoadNative(false);return;}
            Image result=null;bool failed=false;
            try {
                result=await Task.Run(()=>Decode(file)).ConfigureAwait(false);
                await OnUi(()=>{if(ticket!=generation)return;canvas.SetPicture(result);result=null;Loaded();UpdateInfo();});
            }catch(Exception){failed=true;}
            finally{if(result!=null){Stream unused=result.Tag as Stream;result.Dispose();if(unused!=null)unused.Dispose();}}
            if(failed)await OnUi(()=>{if(ticket!=generation)return;nativeImage=true;LoadNative(true);});
        }
        private async Task LoadModel(string file,int ticket)
        {
            CancellationTokenSource source=new CancellationTokenSource();lock(modelCancellationLock){modelLoadCancellation=source;}ModelData result=null;string failure=null;
            try
            {
                result=await Task.Run(()=>ModelData.Load(file,source.Token)).ConfigureAwait(false);
                await OnUi(()=>
                {
                    if(ticket!=generation)return;
                    modelSurface.Visible=true;modelSurface.BringToFront();modelSurface.SetModel(result);result=null;modelSurface.Playback.SetRate(rate);ConfigurePlayback();canvas.Visible=false;Loaded();Tick();UpdateInfo();
                });
            }
            catch(OperationCanceledException){}
            catch(Exception ex){failure=ex.Message;}
            finally { if(result!=null)result.Dispose();lock(modelCancellationLock){if(ReferenceEquals(modelLoadCancellation,source))modelLoadCancellation=null;source.Dispose();} }
            if(failure!=null)await OnUi(()=>{if(ticket==generation)ShowError("打开模型失败："+failure);});
        }
        private static Image Decode(string file)
        {
            // Keep the compressed stream alive with the image, without locking the original file.
            byte[] data;using(FileStream f=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){using(MemoryStream bytes=new MemoryStream()){f.CopyTo(bytes);data=bytes.ToArray();}}
            MemoryStream stream=new MemoryStream(data,false);Image im=null;
            try {im=Image.FromStream(stream,true,true);im.Tag=stream;
                if(im.PropertyIdList.Contains(0x112)){int o=BitConverter.ToUInt16(im.GetPropertyItem(0x112).Value,0);RotateFlipType[] map={RotateFlipType.RotateNoneFlipNone,RotateFlipType.RotateNoneFlipNone,RotateFlipType.RotateNoneFlipX,RotateFlipType.Rotate180FlipNone,RotateFlipType.Rotate180FlipX,RotateFlipType.Rotate90FlipX,RotateFlipType.Rotate90FlipNone,RotateFlipType.Rotate270FlipX,RotateFlipType.Rotate270FlipNone};if(o>0&&o<map.Length)im.RotateFlip(map[o]);}
                return im;
            }catch {if(im!=null)im.Dispose();stream.Dispose();throw;}
        }
        private void LoadNative(bool image)
        {
            try {
                videoSurface.Visible=!audio;audioSurface.Visible=audio;if(audio)audioSurface.BringToFront();else videoSurface.BringToFront();canvas.Visible=false;
                if(player==null){player=new MpvPlayer(videoSurface.Handle);player.Loaded+=()=>{Loaded();player.Set("speed",rate);player.Set("volume",vol);player.Set("mute",muted?"yes":"no");LimitNativeZoom();if(audio)UpdateAudioTrack();UpdateInfo();};player.Failed+=ShowError;}
                player.Load(path,image,audio);UpdateInfo();
            }catch(Exception ex){ShowError("打开失败："+ex.Message);}
        }
        private void Loaded(){loading=false;loadMs=mediaLoad.ElapsedMilliseconds;if(firstLoadMs==0)firstLoadMs=launch.ElapsedMilliseconds;}
        private void ShowError(string message){if(shuttingDown||IsDisposed||Disposing)return;error=message;loading=false;videoSurface.Visible=false;audioSurface.Visible=false;modelSurface.Visible=false;modelSurface.ClearModel();ConfigurePlayback();canvas.Visible=true;canvas.SetPicture(null);canvas.Message=message;canvas.Hint="可按 Ctrl + O 打开其他文件";canvas.Invalidate();UpdateInfo();}
        private void Navigate(int delta){if(siblings.Length==0)return;int next=((index<0?0:index)+delta+siblings.Length)%siblings.Length;Open(siblings[next]);}
        private void TogglePause(){if(model){modelSurface.Playback.TogglePause();if(animation.Items.Count>0){updating=true;animation.SelectedIndex=modelSurface.Playback.Selection+1;updating=false;}Tick();return;}if(player==null||!(video||audio))return;if(player.Flag("eof-reached")){player.Command("seek","0","absolute+exact");player.Set("pause","no");}else player.Set("pause",player.Flag("pause")?"no":"yes");Tick();}
        private void UpdatePauseButton(bool readyToPlay)
        {
            pause.IconName=readyToPlay?"play":"pause";pause.AccessibleName=readyToPlay?"播放":"暂停";
            string hint=pause.AccessibleName+" (空格)";if(tips.GetToolTip(pause)!=hint)tips.SetToolTip(pause,hint);
        }
        private void UpdateVolumeButton()
        {
            bool silent=muted||vol<=0;mute.IconName=silent?"volume-off":"volume-on";mute.AccessibleName=silent?"取消静音":"静音";
            string hint=mute.AccessibleName+" (M)";if(tips.GetToolTip(mute)!=hint)tips.SetToolTip(mute,hint);
            string level="音量 "+vol.ToString("0.#",CultureInfo.InvariantCulture)+"%"+(muted?" · 已静音":"");
            volume.AccessibleDescription=level;tips.SetToolTip(volume,level);
        }
        private void ToggleMute()
        {
            if(muted||vol<=0){muted=false;if(vol<=0)volume.Value=lastAudibleVolume;}
            else muted=true;
            if(player!=null)player.Set("mute",muted?"yes":"no");UpdateVolumeButton();
        }
        private void Zoom(double factor){SetZoom(ViewScale()*factor);}
        private void SetZoom(double scale)
        {
            scale=ZoomRange.Clamp(scale);
            if(model){modelSurface.SetZoomScale(scale);return;}
            if(nativeImage&&player!=null&&player.IsLoaded)SetNativeZoom(scale,new Point(videoSurface.ClientSize.Width/2,videoSurface.ClientSize.Height/2));
            else canvas.SetZoom(scale);
        }
        private void LimitNativeZoom(){if(nativeImage&&player!=null&&player.IsLoaded&&ViewScale()>ZoomRange.Maximum)SetZoom(ZoomRange.Maximum);}
        private void Fit(){if(model){modelSurface.ResetView();return;}if(nativeImage&&player!=null){StopNativeDrag();double fitted=Math.Max(.000001,NativeImageFit());player.Set("video-zoom",Math.Log(Math.Min(ZoomRange.Maximum,fitted)/fitted,2));player.Set("video-pan-x",0);player.Set("video-pan-y",0);player.Set("video-align-x",0);player.Set("video-align-y",0);UpdateInfo();}else canvas.Fit();}
        private void Actual(){if(nativeImage&&player!=null&&player.IsLoaded)SetZoom(1);else canvas.Actual();}
        private void Rotate(){if(nativeImage&&player!=null){rotation=(rotation+90)%360;player.Set("video-rotate",rotation);LimitNativeZoom();ConstrainNativePan();}else canvas.Rotate();UpdateInfo();}
        private void ApplyRate(){if(model)modelSurface.Playback.SetRate(rate);else if(player!=null)player.Set("speed",rate);}
        private void ChangeRate(int direction){double[] rates={.25,.5,.75,1,1.25,1.5,1.75,2,2.5,3,4};int i=Array.FindIndex(rates,r=>Math.Abs(r-rate)<.01);rate=rates[Math.Max(0,Math.Min(rates.Length-1,i+direction))];ApplyRate();UpdateSpeed();UpdateInfo();}
        private void UpdateSpeed(){updating=true;speed.SelectedItem=rate.ToString("0.##",CultureInfo.InvariantCulture)+"×";if(speed.SelectedIndex<0){rate=1;speed.SelectedItem="1×";}updating=false;}
        private void ToggleFull(){if(full){full=false;FormBorderStyle=FormBorderStyle.Sizable;WindowState=FormWindowState.Normal;Bounds=restoreBounds;WindowState=restoreState;top.Visible=true;info.Visible=true;}else{restoreBounds=WindowState==FormWindowState.Normal?Bounds:RestoreBounds;restoreState=WindowState;full=true;WindowState=FormWindowState.Normal;FormBorderStyle=FormBorderStyle.None;Bounds=Screen.FromControl(this).Bounds;top.Visible=false;info.Visible=false;}SyncViewControls();}
        private void UpdateAudioTrack()
        {
            string title=player.Metadata("title");if(string.IsNullOrWhiteSpace(title))title=Path.GetFileNameWithoutExtension(path);
            audioSurface.SetTrack(title,player.Metadata("artist"),player.Metadata("album"));audioSurface.SetStatus("正在播放 · 空格暂停");
        }
        private void Tick()
        {
            if(shuttingDown||IsDisposed||Disposing)return;
            if(model)
            {
                ModelPlayback playback=modelSurface.Playback;
                if(playback.Available)
                {
                    seek.Enabled=playback.Selection>=0;seek.UpdatePosition(playback.Position,playback.Duration);
                    time.Text=FormatTime(playback.Position,true)+" / "+FormatTime(playback.Duration,true);UpdatePauseButton(playback.Paused);
                }
                return;
            }
            if(player==null)return;
            try
            {
                player.Poll();if(!(video||audio)||!player.IsLoaded)return;
                seek.UpdatePosition(player.Number("time-pos"),player.Number("duration"));time.Text=FormatTime(seek.Position)+" / "+FormatTime(seek.Duration);
                bool ended=player.Flag("eof-reached"),paused=player.Flag("pause");UpdatePauseButton(paused||ended);
                if(audio)
                {
                    audioSurface.SetStatus(ended?"播放完毕 · 空格重新播放":paused?"已暂停 · 空格继续":"正在播放 · 空格暂停");
                }
            }
            catch(Exception ex){ShowError(ex.Message);}
        }
        private static string FormatTime(double n){TimeSpan t=TimeSpan.FromSeconds(Math.Max(0,n));return t.TotalHours>=1?((int)t.TotalHours)+t.ToString(@"\:mm\:ss"):t.ToString(@"mm\:ss");}
        private static string FormatTime(double n,bool precise){return FormatTime(n)+(precise?"."+((int)((Math.Max(0,n)%1)*100)).ToString("00"):"");}
        private void UpdateInfo()
        {
            if(shuttingDown||IsDisposed||Disposing||info==null)return;SyncViewControls();if(error!=""){info.Text=error;return;}if(path=="")return;
            string count=(index>=0?(index+1)+" / "+siblings.Length+"   ·   ":"");string size="";try{long n=new FileInfo(path).Length;size=n>=1048576?(n/1048576.0).ToString("0.#")+" MB":(n/1024.0).ToString("0.#")+" KB";}catch{}
            string meta="";if(audio)meta="音乐   ·   "+Path.GetExtension(path).Substring(1).ToUpperInvariant()+"   ·   "+rate.ToString("0.##")+"×";
            else if(model&&modelSurface.Model!=null){ModelData data=modelSurface.Model;int loaded=modelSurface.TextureCount,missing=(int)data.Info.Textures-loaded;meta="FBX "+(modelSurface.Playback.Selection>=0?"动画 · "+modelSurface.Playback.Name:"默认姿态")+"   ·   "+data.Info.Meshes+" 个网格   ·   "+data.Triangles.ToString("N0")+" 个三角形"+(loaded>0?"   ·   "+loaded+" 张贴图":"")+(missing>0?"   ·   "+missing+" 张贴图未载入":"");}
            else if(canvas.Picture!=null)meta=canvas.Picture.Width+" × "+canvas.Picture.Height+"   ·   "+(canvas.ScaleFactor*100).ToString("0")+"%"+(canvas.CanPan?"   ·   可拖动":"   ·   完整显示");
            else if(player!=null&&player.IsLoaded)meta=player.Get("width")+" × "+player.Get("height")+(video?"   ·   "+rate.ToString("0.##")+"×":"");
            info.Text=count+meta+(meta!=""?"   ·   ":"")+size+(loading?"   ·   正在打开…":"");
        }
        private void OpenMenu()
        {
            if(mainMenu==null)
            {
            ContextMenuStrip m=mainMenu=new ContextMenuStrip(menuComponents){BackColor=barColor,ForeColor=fg,ShowImageMargin=false};
            m.Items.Add("打开文件…    Ctrl+O",null,(s,e)=>OpenDialog());
            m.Items.Add("在文件夹中显示",null,(s,e)=>{if(path!="")Process.Start("explorer.exe","/select,\""+path+"\"");});
            m.Items.Add("复制文件路径",null,(s,e)=>{if(path!="")Clipboard.SetText(path);});m.Items.Add(new ToolStripSeparator());
            loopItem=new ToolStripMenuItem("循环播放"){CheckOnClick=true};loopItem.Click+=(s,e)=>{if(model)modelSurface.Playback.SetLoop(loopItem.Checked);else if(player!=null)player.Set("loop-file",loopItem.Checked?"inf":"no");};m.Items.Add(loopItem);
            imageRotateItem=(ToolStripMenuItem)m.Items.Add("旋转 90°    R",null,(s,e)=>Rotate());imageActualItem=(ToolStripMenuItem)m.Items.Add("原始大小 / 100%    1",null,(s,e)=>Actual());
            modelResetItem=(ToolStripMenuItem)m.Items.Add("复位模型视角    0",null,(s,e)=>Fit());modelWireItem=(ToolStripMenuItem)m.Items.Add("切换线框    W",null,(s,e)=>modelSurface.ToggleWireframe());modelGridItem=(ToolStripMenuItem)m.Items.Add("切换地面网格    G",null,(s,e)=>modelSurface.ToggleGrid());
            m.Items.Add("默认应用设置",null,(s,e)=>{Associations.Register(Application.ExecutablePath);Associations.OpenSettings();});
            m.Items.Add("快捷键",null,(s,e)=>MessageBox.Show(this,"Ctrl+O：打开文件\nCtrl+← / Ctrl+→ / PageUp / PageDown：切换文件\n图片：← / → 切换；滚轮 / + / - 缩放；0 适应窗口；1 原始大小；R 旋转\n图片（包括 PNG）：全图时居中；放大超出窗口后可拖动，到边缘停止\n视频 / 音乐：空格播放 / 暂停；← / → 快退 / 快进 5 秒；[ / ] 倍速；Backspace 恢复 1×；↑ / ↓ 音量；M 静音\nFBX 动画：空格播放/暂停；[ / ] 倍速；Backspace 恢复 1×；Shift+← / → 微调 0.1 秒\nFBX：左键拖动旋转；右键 / 中键 / Shift+左键拖动平移；滚轮缩放；方向键旋转；0 / R 复位；1 / 2 / 3 正面 / 侧面 / 顶面；W 线框；G 网格\nF / F11 / 双击全屏；Esc 退出全屏","PV 轻看 · 快捷键"));
            }
            loopItem.Text=audio?"单曲循环":model?"循环播放动画":"循环播放";loopItem.Checked=model?modelSurface.Playback.Loop:player!=null&&player.Get("loop-file")=="inf";loopItem.Enabled=video||audio||(model&&modelSurface.Playback.Available);
            imageRotateItem.Visible=imageActualItem.Visible=!video&&!audio&&!model;
            modelResetItem.Visible=modelWireItem.Visible=modelGridItem.Visible=model;
            mainMenu.Show(menu,new Point(menu.Width-mainMenu.Width,menu.Height));
        }
        protected override void Dispose(bool disposing){if(disposing){BeginShutdown();menuComponents.Dispose();closingCancellation.Dispose();}base.Dispose(disposing);}
        protected override bool ProcessCmdKey(ref Message msg,Keys key)
        {
            if((zoomTools.SliderFocused||volume.Focused)&&(key==Keys.Left||key==Keys.Right||key==Keys.Up||key==Keys.Down||key==Keys.Home||key==Keys.End))return base.ProcessCmdKey(ref msg,key);
            if((speed.DroppedDown||animation.DroppedDown)&&(key==Keys.Up||key==Keys.Down||key==Keys.Left||key==Keys.Right||key==Keys.Home||key==Keys.End||key==Keys.Escape||key==Keys.Enter))return base.ProcessCmdKey(ref msg,key);
            if(key==(Keys.Control|Keys.O)){OpenDialog();return true;}if(key==Keys.F||key==Keys.F11){ToggleFull();return true;}if(key==Keys.Escape&&full){ToggleFull();return true;}
            if(key==(Keys.Control|Keys.Right)||key==Keys.PageDown){Navigate(1);return true;}if(key==(Keys.Control|Keys.Left)||key==Keys.PageUp){Navigate(-1);return true;}
            if(model){if(key==Keys.Space){TogglePause();return true;}if(key==Keys.OemOpenBrackets){ChangeRate(-1);return true;}if(key==Keys.OemCloseBrackets){ChangeRate(1);return true;}if(key==Keys.Back){rate=1;ApplyRate();UpdateSpeed();return true;}if(key==(Keys.Shift|Keys.Left)||key==(Keys.Shift|Keys.Right)){modelSurface.Playback.Seek(modelSurface.Playback.Position+(key==(Keys.Shift|Keys.Right)?.1:-.1));Tick();return true;}}
            if(model){if(key==Keys.Left||key==Keys.Right||key==Keys.Up||key==Keys.Down){modelSurface.Orbit(key==Keys.Left?-10:key==Keys.Right?10:0,key==Keys.Up?-10:key==Keys.Down?10:0);return true;}if(key==Keys.D0||key==Keys.R){Fit();return true;}if(key==Keys.D1||key==Keys.D2||key==Keys.D3){modelSurface.Preset((int)key-(int)Keys.D1);return true;}if(key==Keys.W){modelSurface.ToggleWireframe();return true;}if(key==Keys.G){modelSurface.ToggleGrid();return true;}}
            if(key==Keys.Right||key==Keys.Left){if((video||audio)&&player!=null)player.Command("seek",key==Keys.Right?"5":"-5","relative");else Navigate(key==Keys.Right?1:-1);return true;}
            if(video||audio){if(key==Keys.Space){TogglePause();return true;}if(key==Keys.OemOpenBrackets){ChangeRate(-1);return true;}if(key==Keys.OemCloseBrackets){ChangeRate(1);return true;}if(key==Keys.Back){rate=1;UpdateSpeed();ApplyRate();UpdateInfo();return true;}if(key==Keys.M){ToggleMute();return true;}if(key==Keys.Up||key==Keys.Down){volume.Value=Math.Max(0,Math.Min(100,volume.Value+(key==Keys.Up?5:-5)));return true;}}
            else {if(key==Keys.Oemplus||key==Keys.Add){Zoom(1.25);return true;}if(key==Keys.OemMinus||key==Keys.Subtract){Zoom(1/1.25);return true;}if(key==Keys.D0){Fit();return true;}if(key==Keys.D1){Actual();return true;}if(key==Keys.R){Rotate();return true;}}
            return base.ProcessCmdKey(ref msg,key);
        }
        private void ReadSettings(){try{foreach(string line in File.ReadAllLines(settings)){string[] p=line.Split('=');if(p.Length!=2)continue;double d;if(p[0]=="speed"&&double.TryParse(p[1],NumberStyles.Float,CultureInfo.InvariantCulture,out d))rate=Math.Max(.25,Math.Min(4,d));if(p[0]=="volume"&&double.TryParse(p[1],NumberStyles.Float,CultureInfo.InvariantCulture,out d)&&!double.IsNaN(d)&&!double.IsInfinity(d))vol=Math.Max(0,Math.Min(100,d));}}catch{}if(vol>0)lastAudibleVolume=vol;}
        private void SaveSettings(){try{Directory.CreateDirectory(Path.GetDirectoryName(settings));File.WriteAllLines(settings,new[]{"speed="+rate.ToString(CultureInfo.InvariantCulture),"volume="+vol.ToString(CultureInfo.InvariantCulture)});}catch{}}
        private void WriteDiagnostics(string output)
        {
            string json="{\n  \"version\": \"1.5.9\",\n  \"executable\": "+Associations.Json(Application.ExecutablePath)+",\n  \"path\": "+Associations.Json(path)+",\n  \"loading\": "+loading.ToString().ToLowerInvariant()+",\n  \"video\": "+video.ToString().ToLowerInvariant()+",\n  \"nativeImage\": "+nativeImage.ToString().ToLowerInvariant()+",\n  \"error\": "+Associations.Json(error)+",\n  \"width\": "+(canvas.Picture!=null?canvas.Picture.Width:(player!=null?player.Number("width"):0))+",\n  \"height\": "+(canvas.Picture!=null?canvas.Picture.Height:(player!=null?player.Number("height"):0))+",\n  \"speed\": "+(player!=null?player.Number("speed"):rate).ToString(CultureInfo.InvariantCulture)+",\n  \"position\": "+(player!=null?player.Number("time-pos"):0).ToString(CultureInfo.InvariantCulture)+",\n  \"duration\": "+(player!=null?player.Number("duration"):0).ToString(CultureInfo.InvariantCulture)+",\n  \"paused\": "+(player!=null&&player.Flag("pause")).ToString().ToLowerInvariant()+",\n  \"fullscreen\": "+full.ToString().ToLowerInvariant()+",\n  \"firstLoadMs\": "+firstLoadMs+",\n  \"workingSetMB\": "+(Process.GetCurrentProcess().WorkingSet64/1048576.0).ToString("0.0",CultureInfo.InvariantCulture)+"\n}";
            ModelData data=modelSurface.Model;
            ModelPlayback playback=modelSurface.Playback;
            string modelJson=",\n  \"audio\": "+audio.ToString().ToLowerInvariant()+",\n  \"model\": "+model.ToString().ToLowerInvariant()+",\n  \"modelMeshes\": "+(data!=null?data.Info.Meshes:0)+",\n  \"modelTriangles\": "+(data!=null?data.Triangles:0)+",\n  \"modelTextures\": "+modelSurface.TextureCount+",\n  \"modelYaw\": "+modelSurface.Yaw.ToString(CultureInfo.InvariantCulture)+",\n  \"modelPitch\": "+modelSurface.Pitch.ToString(CultureInfo.InvariantCulture)+",\n  \"modelDistance\": "+modelSurface.Distance.ToString(CultureInfo.InvariantCulture)+",\n  \"imagePanAllowed\": "+(nativeImage?NativeCanPan():canvas.CanPan).ToString().ToLowerInvariant()+",\n  \"loadMs\": "+loadMs;
            modelJson+=",\n  \"modelAnimationCount\": "+(data!=null?data.Animations.Length:0)+",\n  \"modelAnimation\": "+Associations.Json(playback.Name)+",\n  \"modelAnimationPosition\": "+playback.Position.ToString(CultureInfo.InvariantCulture)+",\n  \"modelAnimationDuration\": "+playback.Duration.ToString(CultureInfo.InvariantCulture)+",\n  \"modelAnimationPaused\": "+playback.Paused.ToString().ToLowerInvariant();
            json=json.Substring(0,json.Length-2)+modelJson+"\n}";
            try{File.WriteAllText(output,json,new UTF8Encoding(false));}catch{}
        }
    }
}
