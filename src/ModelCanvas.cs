// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AI-Invoker
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PV
{
    // OpenGL 1.1 is supplied by Windows; no browser or rendering framework is needed.
    internal sealed class ModelCanvas : Control
    {
        private ModelData model;
        private IntPtr dc,context,window;
        private uint[] textures;
        private bool uploaded,dragging,panning,wire,grid=true;
        private Point last;
        private double yaw=-25,pitch=18,distance=3.6,fittedDistance=3.6,panX,panY;
        private static readonly int[,] frustumEdges={{0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},{0,4},{1,5},{2,6},{3,7}};
        private struct Point3
        {
            internal double X,Y,Z;
            internal Point3(double x,double y,double z){X=x;Y=y;Z=z;}
        }
        private struct AxisMark
        {
            internal Point3 Direction;
            internal Color Color;
            internal char Label;
            internal bool Positive;
        }
        internal event Action ViewChanged;
        internal event Action<string> Failed;
        internal ModelData Model { get {return model;} }
        internal bool Wireframe { get {return wire;} }
        internal bool GridVisible { get {return grid;} }
        internal double Yaw { get {return yaw;} }
        internal double Pitch { get {return pitch;} }
        internal double Distance { get {return distance;} }
        internal double ZoomScale { get {return fittedDistance/distance;} }
        internal int TextureCount { get {int count=0;if(textures!=null)foreach(uint id in textures)if(id!=0)count++;return count;} }

        internal ModelCanvas()
        {
            BackColor=Color.FromArgb(19,21,26);TabStop=true;
            AccessibleName="FBX 模型视图";AccessibleDescription="左键拖动旋转，右键拖动平移，滚轮缩放，0 复位";
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.Opaque|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
        }
        protected override CreateParams CreateParams
        {
            get { CreateParams p=base.CreateParams;p.ClassStyle|=0x20;return p; }
        }
        internal void SetModel(ModelData value)
        {
            ClearModel();model=value;
            try { EnsureGraphics();UploadTextures();ResetView(); }
            catch { ClearModel();throw; }
        }
        internal void ClearModel()
        {
            DestroyGraphics();if(model!=null){model.Dispose();model=null;}Invalidate();
        }
        private void Changed(){Invalidate();if(ViewChanged!=null)ViewChanged();}
        internal void ResetView()
        {
            yaw=-25;pitch=18;panX=panY=0;double aspect=(double)Math.Max(1,Width)/Math.Max(1,Height);
            double halfAngle=Math.Atan(Math.Tan(20*Math.PI/180)*Math.Min(1,aspect));
            distance=fittedDistance=1.15/Math.Sin(halfAngle);Changed();
        }
        internal void Preset(int index)
        {
            ResetView();yaw=index==1?90:0;pitch=index==2?89.9:0;Changed();
        }
        internal void Zoom(double factor){SetZoomScale(ZoomScale*factor);}
        internal void SetZoomScale(double value){distance=Math.Max(1.05,Math.Min(80,fittedDistance/ZoomRange.Clamp(value)));Changed();}
        internal void Orbit(double horizontal,double vertical){yaw=(yaw+horizontal)%360;pitch=Math.Max(-89.9,Math.Min(89.9,pitch+vertical));Changed();}
        internal void ToggleWireframe(){wire=!wire;Changed();}
        internal void ToggleGrid(){grid=!grid;Changed();}
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);Focus();
            if(model!=null&&(e.Button==MouseButtons.Left||e.Button==MouseButtons.Right||e.Button==MouseButtons.Middle))
            { dragging=true;panning=e.Button!=MouseButtons.Left||(ModifierKeys&Keys.Shift)!=0;last=e.Location;Capture=true;Cursor=Cursors.SizeAll; }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);if(!dragging)return;
            int dx=e.X-last.X,dy=e.Y-last.Y;last=e.Location;
            if(panning){double unit=2*distance*Math.Tan(20*Math.PI/180)/Math.Max(1,Height);panX+=dx*unit;panY-=dy*unit;Changed();}
            else Orbit(dx*.45,dy*.45);
        }
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);dragging=false;Capture=false;Cursor=Cursors.Default;}
        protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture){dragging=false;Cursor=Cursors.Default;}}
        protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);Zoom(Math.Pow(1.2,e.Delta/120.0));}
        protected override void OnResize(EventArgs e){base.OnResize(e);Invalidate();}
        protected override void OnPaintBackground(PaintEventArgs e){}
        protected override void OnPaint(PaintEventArgs e)
        {
            try { Render(e); }
            catch(Exception ex){DestroyGraphics();if(Failed!=null)Failed("模型视图无法显示："+ex.Message);}
        }
        private void Render(PaintEventArgs e)
        {
            if(model==null){e.Graphics.Clear(BackColor);return;}
            EnsureGraphics();if(!uploaded)UploadTextures();GL.wglMakeCurrent(dc,context);
            int width=Math.Max(1,Width),height=Math.Max(1,Height);double aspect=(double)width/height,near=.01,far=120;
            GL.glViewport(0,0,width,height);GL.glClearColor(19/255f,21/255f,26/255f,1);GL.glClear(GL.COLOR_BUFFER_BIT|GL.DEPTH_BUFFER_BIT);
            GL.glMatrixMode(GL.PROJECTION);GL.glLoadIdentity();double top=near*Math.Tan(20*Math.PI/180);
            GL.glFrustum(-top*aspect,top*aspect,-top,top,near,far);
            GL.glMatrixMode(GL.MODELVIEW);GL.glLoadIdentity();
            GL.glLightfv(GL.LIGHT0,GL.POSITION,new float[]{-3,5,5,0});
            GL.glLightfv(GL.LIGHT1,GL.POSITION,new float[]{4,1,-3,0});
            GL.glTranslated(panX,panY,-distance);GL.glRotated(pitch,1,0,0);GL.glRotated(yaw,0,1,0);
            if(grid)DrawGrid(aspect,near,far);
            GL.glEnableClientState(GL.VERTEX_ARRAY);GL.glEnableClientState(GL.NORMAL_ARRAY);GL.glEnableClientState(GL.TEXTURE_COORD_ARRAY);
            GL.glVertexPointer(3,GL.FLOAT,48,model.Vertices);GL.glNormalPointer(GL.FLOAT,48,IntPtr.Add(model.Vertices,12));
            GL.glTexCoordPointer(2,GL.FLOAT,48,IntPtr.Add(model.Vertices,24));
            if(wire)
            {
                GL.glDisable(GL.LIGHTING);GL.glDisable(GL.TEXTURE_2D);GL.glDisableClientState(GL.COLOR_ARRAY);
                GL.glColor4f(.5f,.75f,1,1);GL.glPolygonMode(GL.FRONT_AND_BACK,GL.LINE);
            }
            else
            {
                GL.glEnable(GL.LIGHTING);GL.glEnableClientState(GL.COLOR_ARRAY);GL.glColorPointer(4,GL.FLOAT,48,IntPtr.Add(model.Vertices,32));
                GL.glPolygonMode(GL.FRONT_AND_BACK,GL.FILL);
            }
            GL.glEnable(GL.ALPHA_TEST);GL.glAlphaFunc(GL.GREATER,.05f);
            foreach(ModelData.Part part in model.Parts)
            {
                int ti=model.Materials[part.Material].Texture;
                if(!wire&&ti>=0&&ti<textures.Length&&textures[ti]!=0){GL.glEnable(GL.TEXTURE_2D);GL.glBindTexture(GL.TEXTURE_2D,textures[ti]);}
                else GL.glDisable(GL.TEXTURE_2D);
                GL.glDrawArrays(GL.TRIANGLES,(int)part.First,(int)part.Count);
            }
            GL.glDisable(GL.ALPHA_TEST);GL.glDisableClientState(GL.VERTEX_ARRAY);GL.glDisableClientState(GL.NORMAL_ARRAY);
            GL.glDisableClientState(GL.TEXTURE_COORD_ARRAY);GL.glDisableClientState(GL.COLOR_ARRAY);GL.glPolygonMode(GL.FRONT_AND_BACK,GL.FILL);
            DrawOrientation(width,height,e.Graphics.DpiX/96.0);
            GL.SwapBuffers(dc);GL.wglMakeCurrent(IntPtr.Zero,IntPtr.Zero);
        }
        private Point3 ViewToWorld(double x,double y,double z)
        {
            x-=panX;y-=panY;z+=distance;
            double a=pitch*Math.PI/180,b=yaw*Math.PI/180;
            double wy=Math.Cos(a)*y+Math.Sin(a)*z,wz=-Math.Sin(a)*y+Math.Cos(a)*z;
            return new Point3(Math.Cos(b)*x-Math.Sin(b)*wz,wy,Math.Sin(b)*x+Math.Cos(b)*wz);
        }
        private Point3 RotateDirection(double x,double y,double z)
        {
            double a=pitch*Math.PI/180,b=yaw*Math.PI/180;
            double wx=Math.Cos(b)*x+Math.Sin(b)*z,wz=-Math.Sin(b)*x+Math.Cos(b)*z;
            return new Point3(wx,Math.Cos(a)*y-Math.Sin(a)*wz,Math.Sin(a)*y+Math.Cos(a)*wz);
        }
        private bool GridBounds(double aspect,double near,double far,double ground,out double minX,out double maxX,out double minZ,out double maxZ)
        {
            // Intersect the ground with all twelve edges of the current camera
            // frustum, so panning and orbiting never expose a fixed grid border.
            Point3[] corners=new Point3[8];double tangent=Math.Tan(20*Math.PI/180);
            for(int i=0;i<8;i++)
            {
                double depth=i<4?near:far;int corner=i%4;
                corners[i]=ViewToWorld((corner==0||corner==3?-1:1)*depth*tangent*aspect,(corner<2?-1:1)*depth*tangent,-depth);
            }
            minX=minZ=double.PositiveInfinity;maxX=maxZ=double.NegativeInfinity;
            for(int i=0;i<12;i++)
            {
                Point3 a=corners[frustumEdges[i,0]],b=corners[frustumEdges[i,1]];double delta=b.Y-a.Y;
                if(Math.Abs(delta)<.000000001)continue;
                double t=(ground-a.Y)/delta;if(t<0||t>1)continue;
                double x=a.X+(b.X-a.X)*t,z=a.Z+(b.Z-a.Z)*t;
                minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minZ=Math.Min(minZ,z);maxZ=Math.Max(maxZ,z);
            }
            return minX<=maxX&&minZ<=maxZ;
        }
        private static double GridStep(double target)
        {
            double power=Math.Pow(10,Math.Floor(Math.Log10(target))),fraction=target/power;
            return power*(fraction<=1?1:fraction<=2?2:fraction<=5?5:10);
        }
        private void DrawGrid(double aspect,double near,double far)
        {
            double y=model.Info.Bottom-.015,minX,maxX,minZ,maxZ;
            if(!GridBounds(aspect,near,far,y,out minX,out maxX,out minZ,out maxZ))return;
            double step=GridStep(Math.Max(.05,Math.Max(distance*.05,((maxX-minX)+(maxZ-minZ))/3000)));
            GL.glDisable(GL.LIGHTING);GL.glDisable(GL.TEXTURE_2D);GL.glLineWidth(1);GL.glBegin(GL.LINES);
            long first=(long)Math.Floor(minX/step),last=(long)Math.Ceiling(maxX/step);
            for(long i=first;i<=last;i++)
            {
                if(i%5==0)GL.glColor4f(.23f,.26f,.31f,1);else GL.glColor4f(.15f,.18f,.22f,1);
                GL.glVertex3d(i*step,y,minZ);GL.glVertex3d(i*step,y,maxZ);
            }
            first=(long)Math.Floor(minZ/step);last=(long)Math.Ceiling(maxZ/step);
            for(long i=first;i<=last;i++)
            {
                if(i%5==0)GL.glColor4f(.23f,.26f,.31f,1);else GL.glColor4f(.15f,.18f,.22f,1);
                GL.glVertex3d(minX,y,i*step);GL.glVertex3d(maxX,y,i*step);
            }
            GL.glEnd();GL.glLineWidth(1.5f);GL.glBegin(GL.LINES);
            GL.glColor4f(.78f,.29f,.26f,1);GL.glVertex3d(minX,y,0);GL.glVertex3d(maxX,y,0);
            GL.glColor4f(.29f,.51f,.88f,1);GL.glVertex3d(0,y,minZ);GL.glVertex3d(0,y,maxZ);GL.glEnd();GL.glLineWidth(1);
        }
        private static void ColorGL(Color value){GL.glColor4f(value.R/255f,value.G/255f,value.B/255f,value.A/255f);}
        private static void Disc(double x,double y,double radius,Color center,Color edge)
        {
            GL.glBegin(GL.TRIANGLE_FAN);ColorGL(center);GL.glVertex3d(x,y,0);ColorGL(edge);
            for(int i=0;i<=40;i++){double a=i*Math.PI/20;GL.glVertex3d(x+Math.Cos(a)*radius,y+Math.Sin(a)*radius,0);}GL.glEnd();
        }
        private static void AxisLabel(char label,double x,double y,double size)
        {
            GL.glBegin(GL.LINES);
            if(label=='X')
            {
                GL.glVertex3d(x-size,y-size,0);GL.glVertex3d(x+size,y+size,0);
                GL.glVertex3d(x-size,y+size,0);GL.glVertex3d(x+size,y-size,0);
            }
            else if(label=='Y')
            {
                GL.glVertex3d(x-size,y+size,0);GL.glVertex3d(x,y,0);GL.glVertex3d(x+size,y+size,0);GL.glVertex3d(x,y,0);
                GL.glVertex3d(x,y,0);GL.glVertex3d(x,y-size,0);
            }
            else
            {
                GL.glVertex3d(x-size,y+size,0);GL.glVertex3d(x+size,y+size,0);GL.glVertex3d(x+size,y+size,0);GL.glVertex3d(x-size,y-size,0);
                GL.glVertex3d(x-size,y-size,0);GL.glVertex3d(x+size,y-size,0);
            }
            GL.glEnd();
        }
        private void DrawOrientation(int width,int height,double dpi)
        {
            double scale=Math.Max(.75,dpi),radius=43*scale,x=width-16*scale-radius,y=16*scale+radius;
            Color[] colors={Color.FromArgb(242,84,72),Color.FromArgb(105,231,123),Color.FromArgb(103,157,255)};
            AxisMark[] axes=new AxisMark[6];
            for(int i=0;i<3;i++)for(int sign=-1;sign<=1;sign+=2)
            {
                bool positive=sign>0;Color color=colors[i];
                axes[i*2+(positive?1:0)]=new AxisMark{Direction=RotateDirection(i==0?sign:0,i==1?sign:0,i==2?sign:0),Color=positive?color:Color.FromArgb(color.R/2,color.G/2,color.B/2),Label="XYZ"[i],Positive=positive};
            }
            Array.Sort(axes,(a,b)=>a.Direction.Z.CompareTo(b.Direction.Z));
            GL.glPushAttrib(GL.CURRENT_BIT|GL.LINE_BIT|GL.ENABLE_BIT);
            GL.glMatrixMode(GL.PROJECTION);GL.glPushMatrix();GL.glLoadIdentity();GL.glOrtho(0,width,0,height,-1,1);
            GL.glMatrixMode(GL.MODELVIEW);GL.glPushMatrix();GL.glLoadIdentity();
            try
            {
                GL.glDisable(GL.DEPTH_TEST);GL.glDisable(GL.LIGHTING);GL.glDisable(GL.TEXTURE_2D);GL.glDisable(GL.ALPHA_TEST);GL.glEnable(GL.BLEND);GL.glEnable(GL.LINE_SMOOTH);
                Disc(x,y,radius,Color.FromArgb(235,42,47,56),Color.FromArgb(235,27,31,38));
                Disc(x,y,2*scale,Color.FromArgb(202,208,218),Color.FromArgb(202,208,218));
                foreach(AxisMark axis in axes)
                {
                    double ax=x+axis.Direction.X*32*scale,ay=y+axis.Direction.Y*32*scale;
                    ColorGL(axis.Color);GL.glLineWidth((float)(1.5*scale));GL.glBegin(GL.LINES);GL.glVertex3d(x,y,0);GL.glVertex3d(ax,ay,0);GL.glEnd();
                    Disc(ax,ay,(axis.Positive?8:5.5)*scale,axis.Color,axis.Color);
                    if(axis.Positive){GL.glColor4f(.07f,.09f,.12f,1);GL.glLineWidth((float)(1.25*scale));AxisLabel(axis.Label,ax,ay,3*scale);}
                }
            }
            finally
            {
                GL.glPopMatrix();GL.glMatrixMode(GL.PROJECTION);GL.glPopMatrix();GL.glMatrixMode(GL.MODELVIEW);GL.glPopAttrib();
            }
        }
        private void EnsureGraphics()
        {
            if(context!=IntPtr.Zero)return;
            window=Handle;dc=GL.GetDC(window);if(dc==IntPtr.Zero)throw new InvalidOperationException("无法创建模型视图");
            try
            {
                GL.PixelFormatDescriptor p=new GL.PixelFormatDescriptor();p.Size=(ushort)Marshal.SizeOf(typeof(GL.PixelFormatDescriptor));p.Version=1;
                p.Flags=0x25;p.ColorBits=32;p.AlphaBits=8;p.DepthBits=24;p.StencilBits=8;
                int format=GL.GetPixelFormat(dc);if(format==0){format=GL.ChoosePixelFormat(dc,ref p);if(format==0||!GL.SetPixelFormat(dc,format,ref p))throw new InvalidOperationException("无法初始化 OpenGL 模型视图");}
                context=GL.wglCreateContext(dc);if(context==IntPtr.Zero||!GL.wglMakeCurrent(dc,context))throw new InvalidOperationException("无法启动 OpenGL 模型视图");
                GL.glEnable(GL.DEPTH_TEST);GL.glDepthFunc(GL.LEQUAL);GL.glEnable(GL.NORMALIZE);
                GL.glEnable(GL.BLEND);GL.glBlendFunc(GL.SRC_ALPHA,GL.ONE_MINUS_SRC_ALPHA);
                GL.glEnable(GL.COLOR_MATERIAL);GL.glColorMaterial(GL.FRONT_AND_BACK,GL.AMBIENT_AND_DIFFUSE);
                GL.glLightModelfv(GL.LIGHT_MODEL_AMBIENT,new float[]{.38f,.38f,.38f,1});GL.glLightModeli(GL.LIGHT_MODEL_TWO_SIDE,1);
                GL.glEnable(GL.LIGHT0);GL.glLightfv(GL.LIGHT0,GL.DIFFUSE,new float[]{.65f,.65f,.65f,1});
                GL.glEnable(GL.LIGHT1);GL.glLightfv(GL.LIGHT1,GL.DIFFUSE,new float[]{.28f,.3f,.34f,1});
                GL.glShadeModel(GL.SMOOTH);GL.glPixelStorei(GL.UNPACK_ALIGNMENT,1);
                GL.wglMakeCurrent(IntPtr.Zero,IntPtr.Zero);
            }
            catch { DestroyGraphics();throw; }
        }
        private void UploadTextures()
        {
            GL.wglMakeCurrent(dc,context);textures=new uint[model.Textures.Length];int maxTexture;GL.glGetIntegerv(GL.MAX_TEXTURE_SIZE,out maxTexture);
            try
            {
                for(int i=0;i<textures.Length;i++)
                {
                    ModelData.Texture t=model.Textures[i];if(t.Pixels==IntPtr.Zero||t.Width>(uint)maxTexture||t.Height>(uint)maxTexture)continue;
                    uint id;GL.glGenTextures(1,out id);textures[i]=id;GL.glBindTexture(GL.TEXTURE_2D,id);
                    GL.glTexParameteri(GL.TEXTURE_2D,GL.TEXTURE_MIN_FILTER,GL.LINEAR);GL.glTexParameteri(GL.TEXTURE_2D,GL.TEXTURE_MAG_FILTER,GL.LINEAR);
                    GL.glTexParameteri(GL.TEXTURE_2D,GL.TEXTURE_WRAP_S,t.WrapU==0?GL.REPEAT:GL.CLAMP_TO_EDGE);
                    GL.glTexParameteri(GL.TEXTURE_2D,GL.TEXTURE_WRAP_T,t.WrapV==0?GL.REPEAT:GL.CLAMP_TO_EDGE);
                    GL.glTexImage2D(GL.TEXTURE_2D,0,GL.RGBA,(int)t.Width,(int)t.Height,0,GL.RGBA,GL.UNSIGNED_BYTE,t.Pixels);
                    uint failure=GL.glGetError();if(failure!=0){GL.glDeleteTextures(1,ref id);textures[i]=0;}
                }
                uploaded=true;
            }
            finally { GL.wglMakeCurrent(IntPtr.Zero,IntPtr.Zero); }
        }
        private void DestroyGraphics()
        {
            if(context!=IntPtr.Zero)
            {
                GL.wglMakeCurrent(dc,context);
                if(textures!=null)foreach(uint tex in textures){if(tex!=0){uint id=tex;GL.glDeleteTextures(1,ref id);}}
                GL.wglMakeCurrent(IntPtr.Zero,IntPtr.Zero);GL.wglDeleteContext(context);context=IntPtr.Zero;
            }
            if(dc!=IntPtr.Zero){GL.ReleaseDC(window,dc);dc=IntPtr.Zero;}textures=null;uploaded=false;
        }
        protected override void OnHandleDestroyed(EventArgs e){DestroyGraphics();base.OnHandleDestroyed(e);}
        protected override void Dispose(bool disposing){if(disposing)ClearModel();base.Dispose(disposing);}
    }

    internal static class GL
    {
        internal const uint COLOR_BUFFER_BIT=0x4000,DEPTH_BUFFER_BIT=0x100,PROJECTION=0x1701,MODELVIEW=0x1700,
            LIGHTING=0xB50,LIGHT0=0x4000,LIGHT1=0x4001,POSITION=0x1203,DIFFUSE=0x1201,LIGHT_MODEL_AMBIENT=0xB53,LIGHT_MODEL_TWO_SIDE=0xB52,
            DEPTH_TEST=0xB71,LEQUAL=0x203,NORMALIZE=0xBA1,BLEND=0xBE2,SRC_ALPHA=0x302,ONE_MINUS_SRC_ALPHA=0x303,
            COLOR_MATERIAL=0xB57,FRONT_AND_BACK=0x408,AMBIENT_AND_DIFFUSE=0x1602,SMOOTH=0x1D01,
            TEXTURE_2D=0xDE1,FLOAT=0x1406,UNSIGNED_BYTE=0x1401,VERTEX_ARRAY=0x8074,NORMAL_ARRAY=0x8075,COLOR_ARRAY=0x8076,TEXTURE_COORD_ARRAY=0x8078,
            LINE=0x1B01,FILL=0x1B02,TRIANGLES=4,LINES=1,TRIANGLE_FAN=6,ALPHA_TEST=0xBC0,GREATER=0x204,
            CURRENT_BIT=1,LINE_BIT=4,ENABLE_BIT=0x2000,LINE_SMOOTH=0xB20;
        internal const int RGBA=0x1908,LINEAR=0x2601,REPEAT=0x2901,CLAMP_TO_EDGE=0x812F,UNPACK_ALIGNMENT=0xCF5,MAX_TEXTURE_SIZE=0xD33;
        internal const uint TEXTURE_MIN_FILTER=0x2801,TEXTURE_MAG_FILTER=0x2800,TEXTURE_WRAP_S=0x2802,TEXTURE_WRAP_T=0x2803;
        [StructLayout(LayoutKind.Sequential)] internal struct PixelFormatDescriptor
        {
            internal ushort Size,Version;internal uint Flags;
            internal byte PixelType,ColorBits,RedBits,RedShift,GreenBits,GreenShift,BlueBits,BlueShift,AlphaBits,AlphaShift,
                AccumBits,AccumRedBits,AccumGreenBits,AccumBlueBits,AccumAlphaBits,DepthBits,StencilBits,AuxBuffers,LayerType,Reserved;
            internal uint LayerMask,VisibleMask,DamageMask;
        }
        [DllImport("user32.dll")]internal static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")]internal static extern int ReleaseDC(IntPtr window,IntPtr dc);
        [DllImport("gdi32.dll")]internal static extern int ChoosePixelFormat(IntPtr dc,ref PixelFormatDescriptor format);
        [DllImport("gdi32.dll")]internal static extern int GetPixelFormat(IntPtr dc);
        [DllImport("gdi32.dll")][return:MarshalAs(UnmanagedType.Bool)]internal static extern bool SetPixelFormat(IntPtr dc,int index,ref PixelFormatDescriptor format);
        [DllImport("gdi32.dll")][return:MarshalAs(UnmanagedType.Bool)]internal static extern bool SwapBuffers(IntPtr dc);
        [DllImport("opengl32.dll")]internal static extern IntPtr wglCreateContext(IntPtr dc);
        [DllImport("opengl32.dll")][return:MarshalAs(UnmanagedType.Bool)]internal static extern bool wglDeleteContext(IntPtr context);
        [DllImport("opengl32.dll")][return:MarshalAs(UnmanagedType.Bool)]internal static extern bool wglMakeCurrent(IntPtr dc,IntPtr context);
        [DllImport("opengl32.dll")]internal static extern void glViewport(int x,int y,int width,int height);
        [DllImport("opengl32.dll")]internal static extern void glClearColor(float r,float g,float b,float a);
        [DllImport("opengl32.dll")]internal static extern void glClear(uint mask);
        [DllImport("opengl32.dll")]internal static extern void glMatrixMode(uint mode);
        [DllImport("opengl32.dll")]internal static extern void glLoadIdentity();
        [DllImport("opengl32.dll")]internal static extern void glPushMatrix();
        [DllImport("opengl32.dll")]internal static extern void glPopMatrix();
        [DllImport("opengl32.dll")]internal static extern void glPushAttrib(uint mask);
        [DllImport("opengl32.dll")]internal static extern void glPopAttrib();
        [DllImport("opengl32.dll")]internal static extern void glOrtho(double left,double right,double bottom,double top,double near,double far);
        [DllImport("opengl32.dll")]internal static extern void glFrustum(double left,double right,double bottom,double top,double near,double far);
        [DllImport("opengl32.dll")]internal static extern void glTranslated(double x,double y,double z);
        [DllImport("opengl32.dll")]internal static extern void glRotated(double angle,double x,double y,double z);
        [DllImport("opengl32.dll")]internal static extern void glEnable(uint capability);
        [DllImport("opengl32.dll")]internal static extern void glDisable(uint capability);
        [DllImport("opengl32.dll")]internal static extern void glEnableClientState(uint array);
        [DllImport("opengl32.dll")]internal static extern void glDisableClientState(uint array);
        [DllImport("opengl32.dll")]internal static extern void glVertexPointer(int size,uint type,int stride,IntPtr pointer);
        [DllImport("opengl32.dll")]internal static extern void glNormalPointer(uint type,int stride,IntPtr pointer);
        [DllImport("opengl32.dll")]internal static extern void glTexCoordPointer(int size,uint type,int stride,IntPtr pointer);
        [DllImport("opengl32.dll")]internal static extern void glColorPointer(int size,uint type,int stride,IntPtr pointer);
        [DllImport("opengl32.dll")]internal static extern void glDrawArrays(uint mode,int first,int count);
        [DllImport("opengl32.dll")]internal static extern void glColor4f(float r,float g,float b,float a);
        [DllImport("opengl32.dll")]internal static extern void glPolygonMode(uint face,uint mode);
        [DllImport("opengl32.dll")]internal static extern void glLineWidth(float width);
        [DllImport("opengl32.dll")]internal static extern void glBegin(uint mode);
        [DllImport("opengl32.dll")]internal static extern void glEnd();
        [DllImport("opengl32.dll")]internal static extern void glVertex3d(double x,double y,double z);
        [DllImport("opengl32.dll")]internal static extern void glDepthFunc(uint function);
        [DllImport("opengl32.dll")]internal static extern void glBlendFunc(uint source,uint destination);
        [DllImport("opengl32.dll")]internal static extern void glAlphaFunc(uint function,float reference);
        [DllImport("opengl32.dll")]internal static extern void glColorMaterial(uint face,uint mode);
        [DllImport("opengl32.dll")]internal static extern void glLightfv(uint light,uint pname,float[] parameters);
        [DllImport("opengl32.dll")]internal static extern void glLightModelfv(uint pname,float[] parameters);
        [DllImport("opengl32.dll")]internal static extern void glLightModeli(uint pname,int parameter);
        [DllImport("opengl32.dll")]internal static extern void glShadeModel(uint mode);
        [DllImport("opengl32.dll")]internal static extern void glPixelStorei(uint pname,int parameter);
        [DllImport("opengl32.dll")]internal static extern void glGetIntegerv(uint pname,out int parameter);
        [DllImport("opengl32.dll")]internal static extern void glGenTextures(int count,out uint texture);
        [DllImport("opengl32.dll")]internal static extern void glDeleteTextures(int count,ref uint texture);
        [DllImport("opengl32.dll")]internal static extern void glBindTexture(uint target,uint texture);
        [DllImport("opengl32.dll")]internal static extern void glTexParameteri(uint target,uint pname,int parameter);
        [DllImport("opengl32.dll")]internal static extern void glTexImage2D(uint target,int level,int internalFormat,int width,int height,int border,uint format,uint type,IntPtr pixels);
        [DllImport("opengl32.dll")]internal static extern uint glGetError();
    }
}
