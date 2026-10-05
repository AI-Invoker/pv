// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AI-Invoker
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PV
{
    internal sealed class VolumeSlider : Control
    {
        private double value=70;
        private bool tracking;
        internal event EventHandler ValueChanged;

        internal VolumeSlider()
        {
            Size=new Size(120,34);DoubleBuffered=true;TabStop=true;Cursor=Cursors.Hand;
            BackColor=Color.FromArgb(28,31,38);AccessibleName="音量";AccessibleRole=AccessibleRole.Slider;
            SetStyle(ControlStyles.Selectable|ControlStyles.ResizeRedraw,true);
        }
        internal double Value
        {
            get{return value;}
            set
            {
                double next=double.IsNaN(value)||double.IsInfinity(value)?0:Math.Max(0,Math.Min(100,value));
                if(Math.Abs(next-this.value)<.000001)return;
                this.value=next;Invalidate();if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);
            }
        }
        private void SetFromPoint(int x)
        {
            double inset=Height*8.0/34.0;
            Value=100*(x-inset)/Math.Max(1,ClientSize.Width-2*inset);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);if(!Enabled||e.Button!=MouseButtons.Left)return;
            Focus();tracking=true;Capture=true;SetFromPoint(e.X);
        }
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(tracking)SetFromPoint(e.X);}
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);if(!tracking||e.Button!=MouseButtons.Left)return;
            SetFromPoint(e.X);tracking=false;Capture=false;
        }
        protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture)tracking=false;}
        protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
        protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
        protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);if(!Enabled){tracking=false;Capture=false;}Invalidate();}
        protected override bool IsInputKey(Keys key)
        {
            Keys k=key&Keys.KeyCode;return k==Keys.Left||k==Keys.Right||k==Keys.Up||k==Keys.Down||k==Keys.Home||k==Keys.End||base.IsInputKey(key);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if(e.KeyCode==Keys.Left||e.KeyCode==Keys.Down)Value-=1;
            else if(e.KeyCode==Keys.Right||e.KeyCode==Keys.Up)Value+=1;
            else if(e.KeyCode==Keys.Home)Value=0;
            else if(e.KeyCode==Keys.End)Value=100;
            else return;
            e.Handled=true;e.SuppressKeyPress=true;
        }
        protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);if(Enabled)Value+=e.Delta/120.0*5;}
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;g.Clear(BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
            float scale=Height/34f,left=8*scale,right=Math.Max(left,Width-left),y=Height/2f,x=left+(float)(value/100)*(right-left);
            using(Pen rail=new Pen(Enabled?Color.FromArgb(137,141,149):Color.FromArgb(68,74,85),3*scale))
            {rail.StartCap=rail.EndCap=LineCap.Round;g.DrawLine(rail,left,y,right,y);}
            if(Enabled&&value>0)using(Pen active=new Pen(Color.FromArgb(62,184,239),3*scale))
            {active.StartCap=active.EndCap=LineCap.Round;g.DrawLine(active,left,y,x,y);}
            using(Brush ring=new SolidBrush(Color.FromArgb(60,67,76)))g.FillEllipse(ring,x-8*scale,y-8*scale,16*scale,16*scale);
            using(Brush dot=new SolidBrush(Enabled?Color.FromArgb(69,192,250):Color.FromArgb(107,114,127)))g.FillEllipse(dot,x-4.5f*scale,y-4.5f*scale,9*scale,9*scale);
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(ClientRectangle,-1,-4),Color.FromArgb(180,186,196),BackColor);
        }
    }
}
