// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AI-Invoker
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PV
{
    internal sealed class AudioCanvas : Control
    {
        private string title="",details="",status="正在打开…";
        internal AudioCanvas()
        {
            DoubleBuffered=true;BackColor=Color.FromArgb(19,21,26);ForeColor=Color.FromArgb(220,225,235);
            TabStop=true;AccessibleName="音乐播放";
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
        }
        internal void SetTrack(string name,string artist,string album)
        {
            title=name;details=string.Join("   ·   ",Array.FindAll(new[]{artist,album},x=>!string.IsNullOrWhiteSpace(x)));
            AccessibleDescription=title+(details.Length>0?" · "+details:"");Invalidate();
        }
        internal void SetStatus(string value){if(status==value)return;status=value;Invalidate();}
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);Graphics g=e.Graphics;g.Clear(BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
            float scale=g.DpiX/96f;int center=ClientSize.Width/2;
            int discSize=(int)(116*scale),top=Math.Max((int)(16*scale),ClientSize.Height/2-(int)(106*scale));
            Rectangle disc=new Rectangle(center-discSize/2,top,discSize,discSize);
            using(Brush b=new LinearGradientBrush(disc,Color.FromArgb(45,68,92),Color.FromArgb(26,37,51),45f))g.FillEllipse(b,disc);
            using(Pen ring=new Pen(Color.FromArgb(51,74,95),scale))g.DrawEllipse(ring,Rectangle.Inflate(disc,-(int)(9*scale),-(int)(9*scale)));
            SvgIcons.Draw(g,"music-note",new RectangleF(center-30*scale,top+28*scale,60*scale,60*scale),Color.FromArgb(81,193,245));
            int y=top+discSize+(int)(20*scale),margin=(int)(48*scale),width=Math.Max(1,ClientSize.Width-2*margin);
            TextFormatFlags flags=TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix;
            using(Font f=new Font(Font.FontFamily,17,FontStyle.Regular))TextRenderer.DrawText(g,title,f,new Rectangle(margin,y,width,(int)(38*scale)),ForeColor,flags);
            using(Font f=new Font(Font.FontFamily,10,FontStyle.Regular))TextRenderer.DrawText(g,details,f,new Rectangle(margin,y+(int)(42*scale),width,(int)(27*scale)),Color.FromArgb(149,163,185),flags);
            using(Font f=new Font(Font.FontFamily,9,FontStyle.Regular))TextRenderer.DrawText(g,status,f,new Rectangle(margin,y+(int)(76*scale),width,(int)(25*scale)),Color.FromArgb(113,129,151),flags);
        }
    }
}
