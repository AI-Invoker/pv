// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AI-Invoker
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PV
{
    internal sealed class ImageCanvas : Control
    {
        private Image image;
        private bool fit=true, dragging;
        private double zoom=1;
        private PointF pan;
        private Point last;
        internal event Action ViewChanged;
        internal string Message="拖入图片、视频、音乐或 FBX，立即查看";
        internal string Hint="Ctrl + O 打开文件   ·   ← → 切换   ·   F 全屏";
        internal Image Picture { get {return image;} }
        private SizeF RenderedSize { get {return image==null?SizeF.Empty:new SizeF((float)(image.Width*ScaleFactor),(float)(image.Height*ScaleFactor));} }
        internal bool CanPan { get {return image!=null&&ImagePan.CanPan(RenderedSize,ClientSize);} }
        internal double ScaleFactor { get {return image==null?1:(fit?Math.Min(ZoomRange.Maximum,Math.Min((double)Math.Max(1,Width-40)/image.Width,(double)Math.Max(1,Height-40)/image.Height)):zoom);} }
        internal ImageCanvas() { DoubleBuffered=true; BackColor=Color.FromArgb(19,21,26); SetStyle(ControlStyles.ResizeRedraw|ControlStyles.Selectable,true); TabStop=true; }
        internal void SetPicture(Image value) { EndDrag();if(image!=null){if(ImageAnimator.CanAnimate(image))ImageAnimator.StopAnimate(image,OnFrame);Stream stream=image.Tag as Stream;image.Dispose();if(stream!=null)stream.Dispose();} image=value;fit=true;pan=PointF.Empty;if(image!=null&&ImageAnimator.CanAnimate(image))ImageAnimator.Animate(image,OnFrame);Invalidate();Changed(); }
        private void OnFrame(object sender,EventArgs e) { if(!IsDisposed&&IsHandleCreated)try{BeginInvoke((Action)(()=>{if(!IsDisposed)Invalidate();}));}catch(InvalidOperationException){} }
        private void Changed(){if(ViewChanged!=null)ViewChanged();}
        private void EndDrag(){dragging=false;Capture=false;Cursor=Cursors.Default;}
        private void ClampPan(){pan=ImagePan.Clamp(pan,RenderedSize,ClientSize);if(!CanPan)EndDrag();}
        internal void Fit(){EndDrag();fit=true;pan=PointF.Empty;Invalidate();Changed();}
        internal void Actual(){EndDrag();fit=false;zoom=1;pan=PointF.Empty;Invalidate();Changed();}
        internal void SetZoom(double scale){if(image!=null)Zoom(scale/ScaleFactor,new Point(Width/2,Height/2));}
        internal void Zoom(double factor,Point anchor) { if(image==null)return; double old=ScaleFactor; double next=ZoomRange.Clamp(old*factor); double x=anchor.X-ClientSize.Width/2.0-pan.X,y=anchor.Y-ClientSize.Height/2.0-pan.Y;pan=new PointF((float)(pan.X+x*(1-next/old)),(float)(pan.Y+y*(1-next/old)));fit=false;zoom=next;ClampPan();Invalidate();Changed();}
        internal void Rotate() { if(image==null)return;bool animated=ImageAnimator.CanAnimate(image);if(animated)ImageAnimator.StopAnimate(image,OnFrame);image.RotateFlip(RotateFlipType.Rotate90FlipNone);if(animated)ImageAnimator.Animate(image,OnFrame);Fit();}
        protected override void OnResize(EventArgs e){base.OnResize(e);ClampPan();Invalidate();Changed();}
        protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);Zoom(e.Delta>0?1.2:1/1.2,e.Location);}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);Focus();if(e.Button==MouseButtons.Left&&CanPan&&ImagePan.Bounds(RenderedSize,ClientSize,pan).Contains(e.Location)){dragging=true;last=e.Location;Capture=true;Cursor=Cursors.Hand;}}
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging){pan.X+=e.X-last.X;pan.Y+=e.Y-last.Y;last=e.Location;ClampPan();Invalidate();}}
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);EndDrag();}
        protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture){dragging=false;Cursor=Cursors.Default;}}
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);Graphics g=e.Graphics;
            if(image==null){
                int cx=Width/2,cy=Height/2-52;g.SmoothingMode=SmoothingMode.AntiAlias;
                using(Pen p=new Pen(Color.FromArgb(76,164,251),3)){g.DrawRectangle(p,cx-30,cy-23,60,46);g.DrawLines(p,new[]{new Point(cx-23,cy+14),new Point(cx-8,cy-2),new Point(cx+2,cy+9),new Point(cx+14,cy-8),new Point(cx+25,cy+14)});g.DrawEllipse(p,cx-17,cy-15,7,7);}
                using(Font f=new Font("Microsoft YaHei UI",16,FontStyle.Regular))TextRenderer.DrawText(g,Message,f,new Rectangle(20,cy+52,Math.Max(1,Width-40),50),Color.FromArgb(222,226,237),TextFormatFlags.HorizontalCenter|TextFormatFlags.EndEllipsis);
                using(Font f=new Font("Microsoft YaHei UI",9))TextRenderer.DrawText(g,Hint,f,new Rectangle(20,cy+106,Math.Max(1,Width-40),35),Color.FromArgb(124,135,155),TextFormatFlags.HorizontalCenter|TextFormatFlags.EndEllipsis);
                return;
            }
            try {
                ImageAnimator.UpdateFrames(image);double s=ScaleFactor;RectangleF r=ImagePan.Bounds(RenderedSize,ClientSize,pan);
                // Transparency is shown only beneath the image, without painting a screen-sized grid.
                Rectangle clip=Rectangle.Intersect(ClientRectangle,Rectangle.Ceiling(r));g.SetClip(clip);
                using(SolidBrush a=new SolidBrush(Color.FromArgb(38,41,48)))using(SolidBrush b=new SolidBrush(Color.FromArgb(49,52,61))){g.FillRectangle(a,clip);for(int y=clip.Top/16*16;y<clip.Bottom;y+=16)for(int x=clip.Left/16*16;x<clip.Right;x+=16)if(((x/16+y/16)&1)==0)g.FillRectangle(b,x,y,16,16);}
                g.InterpolationMode=s>2?InterpolationMode.NearestNeighbor:InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                using(ImageAttributes ia=new ImageAttributes()){ia.SetWrapMode(WrapMode.TileFlipXY);g.DrawImage(image,Rectangle.Round(r),0,0,image.Width,image.Height,GraphicsUnit.Pixel,ia);}
                g.ResetClip();
            } catch(ExternalException){} catch(ArgumentException){}
        }
        protected override void Dispose(bool disposing){if(disposing)SetPicture(null);base.Dispose(disposing);}
    }
    internal sealed class SeekBar : Control
    {
        internal double Position,Duration;
        private bool tracking;
        internal event Action<double> Seek;
        internal SeekBar(){DoubleBuffered=true;Height=26;Cursor=Cursors.Hand;AccessibleName="播放进度";AccessibleRole=AccessibleRole.Slider;}
        private void Track(int x){if(Duration<=0)return;Position=Math.Max(0,Math.Min(Duration,(double)(x-12)/Math.Max(1,Width-24)*Duration));Invalidate();}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left){tracking=true;Capture=true;Track(e.X);}}
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(tracking)Track(e.X);}
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(tracking){Track(e.X);tracking=false;Capture=false;if(Seek!=null)Seek(Position);}}
        internal void UpdatePosition(double p,double d){Duration=d;if(!tracking)Position=p;Invalidate();}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;float width=Math.Max(1,Width-24),x=12+(float)(Duration>0?Position/Duration:0)*width;using(Pen p=new Pen(Color.FromArgb(63,70,84),4))g.DrawLine(p,12,13,Width-12,13);using(Pen p=new Pen(Color.FromArgb(76,164,251),4))g.DrawLine(p,12,13,x,13);using(Brush b=new SolidBrush(Color.FromArgb(205,229,255)))g.FillEllipse(b,x-5,8,10,10);}
    }
}
