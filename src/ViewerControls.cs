// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AI-Invoker
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;

namespace PV
{
    internal static class ZoomRange
    {
        internal const double Minimum=.02,Maximum=8;
        internal static double Clamp(double value){return double.IsNaN(value)||double.IsInfinity(value)?1:Math.Max(Minimum,Math.Min(Maximum,value));}
    }

    internal sealed class FooterPanel : Panel
    {
        internal FooterPanel(){DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);}
    }

    internal sealed class StatusInfo : Control
    {
        internal StatusInfo(){DoubleBuffered=true;AccessibleRole=AccessibleRole.StaticText;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true);}
        protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Invalidate();}
        protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);Invalidate();}
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            Rectangle area=new Rectangle(Padding.Left,Padding.Top,Math.Max(1,ClientSize.Width-Padding.Horizontal),Math.Max(1,ClientSize.Height-Padding.Vertical));
            TextRenderer.DrawText(e.Graphics,Text,Font,area,ForeColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding);
        }
    }

    // Icons are embedded SVG resources. This renderer handles the small SVG subset
    // used by our own icons, keeping the viewer independent of an image framework.
    internal static class SvgIcons
    {
        private static readonly Dictionary<string,XmlDocument> cache=new Dictionary<string,XmlDocument>();
        private static readonly Regex tokens=new Regex("[MLHVZmlhvz]|[-+]?(?:[0-9]*[.])?[0-9]+(?:[eE][-+]?[0-9]+)?",RegexOptions.Compiled);
        private static float Number(string value){return float.Parse(value,CultureInfo.InvariantCulture);}
        private static float Attr(XmlElement element,string name,float fallback){string value=element.GetAttribute(name);return value.Length==0?fallback:Number(value);}
        internal static GraphicsPath Rounded(RectangleF bounds,float radius)
        {
            GraphicsPath p=new GraphicsPath();float d=Math.Max(0,Math.Min(radius*2,Math.Min(bounds.Width,bounds.Height)));
            if(d<.1f){p.AddRectangle(bounds);return p;}
            p.AddArc(bounds.Left,bounds.Top,d,d,180,90);p.AddArc(bounds.Right-d,bounds.Top,d,d,270,90);
            p.AddArc(bounds.Right-d,bounds.Bottom-d,d,d,0,90);p.AddArc(bounds.Left,bounds.Bottom-d,d,d,90,90);p.CloseFigure();return p;
        }
        private static GraphicsPath PathData(string value)
        {
            MatchCollection t=tokens.Matches(value);GraphicsPath path=new GraphicsPath();float x=0,y=0,sx=0,sy=0;char op=' ';int i=0;
            try
            {
                while(i<t.Count)
                {
                    string word=t[i].Value;if(char.IsLetter(word[0])){op=word[0];i++;}
                    bool relative=char.IsLower(op);char kind=char.ToUpperInvariant(op);
                    if(kind=='Z'){path.CloseFigure();x=sx;y=sy;op=' ';continue;}
                    float nx=x,ny=y;
                    if(kind=='M'||kind=='L'){nx=Number(t[i++].Value);ny=Number(t[i++].Value);if(relative){nx+=x;ny+=y;}}
                    else if(kind=='H'){nx=Number(t[i++].Value);if(relative)nx+=x;}
                    else if(kind=='V'){ny=Number(t[i++].Value);if(relative)ny+=y;}
                    else throw new InvalidDataException("Unsupported SVG path command");
                    if(kind=='M'){path.StartFigure();sx=nx;sy=ny;op=relative?'l':'L';}
                    else path.AddLine(x,y,nx,ny);x=nx;y=ny;
                }
                return path;
            }
            catch { path.Dispose();throw; }
        }
        internal static void Draw(Graphics graphics,string name,RectangleF bounds,Color color)
        {
            XmlDocument doc;
            if(!cache.TryGetValue(name,out doc))
            {
                using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("PV.Icons."+name+".svg"))
                {
                    if(stream==null)throw new InvalidDataException("Missing SVG icon: "+name);
                    XmlReaderSettings options=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null};
                    using(XmlReader reader=XmlReader.Create(stream,options)){doc=new XmlDocument{XmlResolver=null};doc.Load(reader);}
                }
                cache.Add(name,doc);
            }
            XmlElement root=doc.DocumentElement;string[] box=root.GetAttribute("viewBox").Split(new[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries);
            float vw=Number(box[2]),vh=Number(box[3]);float scale=Math.Min(bounds.Width/vw,bounds.Height/vh);
            GraphicsState state=graphics.Save();
            try
            {
                graphics.SmoothingMode=SmoothingMode.AntiAlias;graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
                graphics.TranslateTransform(bounds.X+(bounds.Width-vw*scale)/2,bounds.Y+(bounds.Height-vh*scale)/2);graphics.ScaleTransform(scale,scale);
                float stroke=Attr(root,"stroke-width",1);bool fill=root.GetAttribute("fill")!="none",outline=root.GetAttribute("stroke")!="none";
                foreach(XmlNode child in root.ChildNodes)
                {
                    XmlElement item=child as XmlElement;if(item==null)continue;GraphicsPath path=null;
                    if(item.LocalName=="path")path=PathData(item.GetAttribute("d"));
                    else if(item.LocalName=="circle"){path=new GraphicsPath();float r=Attr(item,"r",0),cx=Attr(item,"cx",0),cy=Attr(item,"cy",0);path.AddEllipse(cx-r,cy-r,2*r,2*r);}
                    else if(item.LocalName=="rect")path=Rounded(new RectangleF(Attr(item,"x",0),Attr(item,"y",0),Attr(item,"width",0),Attr(item,"height",0)),Attr(item,"rx",0));
                    if(path==null)continue;
                    using(path)
                    {
                        string localFill=item.GetAttribute("fill"),localStroke=item.GetAttribute("stroke");
                        if(localFill.Length==0?fill:localFill!="none")using(Brush brush=new SolidBrush(color))graphics.FillPath(brush,path);
                        if(localStroke.Length==0?outline:localStroke!="none")using(Pen pen=new Pen(color,Attr(item,"stroke-width",stroke))){pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;graphics.DrawPath(pen,path);}
                    }
                }
            }
            finally { graphics.Restore(state); }
        }
    }

    internal class SvgButton : Button
    {
        private bool hot,down;
        private string icon;
        internal bool FillBackground;
        internal float IconSize=20;
        internal SvgButton(string name,string label)
        {
            icon=name;AccessibleName=label;Cursor=Cursors.Hand;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;TabStop=true;
            Size=new Size(40,40);BackColor=Color.FromArgb(28,31,38);ForeColor=Color.FromArgb(219,223,231);
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        }
        internal string IconName {get{return icon;}set{if(icon!=value){icon=value;Invalidate();}}}
        protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hot=true;Invalidate();}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hot=false;down=false;Invalidate();}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left){down=true;Invalidate();}}
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);down=false;Invalidate();}
        protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
        protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
        protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Invalidate();}
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;g.Clear(BackColor);float factor=Math.Max(.5f,Height/40f);
            if(FillBackground||hot||down){using(GraphicsPath p=SvgIcons.Rounded(new RectangleF(1,2,Math.Max(1,Width-2),Math.Max(1,Height-4)),4*factor))using(Brush b=new SolidBrush(down?Color.FromArgb(57,60,65):hot?Color.FromArgb(52,55,60):Color.FromArgb(42,45,50)))g.FillPath(b,p);}
            Color color=Enabled?ForeColor:Color.FromArgb(91,96,106);float size=IconSize*factor;
            SvgIcons.Draw(g,icon,new RectangleF((Width-size)/2,(Height-size)/2,size,size),color);
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(ClientRectangle,-3,-3),ForeColor,BackColor);
        }
    }

    internal sealed class PercentButton : Button
    {
        private bool hot;
        internal PercentButton()
        {
            AccessibleName="缩放比例";Text="100%";Cursor=Cursors.Hand;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;
            BackColor=Color.FromArgb(28,31,38);ForeColor=Color.FromArgb(232,234,239);
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        }
        protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hot=true;Invalidate();}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hot=false;Invalidate();}
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;g.Clear(BackColor);float factor=Height/40f;
            using(GraphicsPath p=SvgIcons.Rounded(new RectangleF(1,2,Math.Max(1,Width-2),Math.Max(1,Height-4)),4*factor))
            using(Brush b=new SolidBrush(hot&&Enabled?Color.FromArgb(58,61,66):Color.FromArgb(44,47,52)))g.FillPath(b,p);
            Color color=Enabled?ForeColor:Color.FromArgb(91,96,106);
            TextRenderer.DrawText(g,Text,Font,new Rectangle((int)(12*factor),0,Math.Max(1,Width-(int)(33*factor)),Height),color,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.NoPadding);
            SvgIcons.Draw(g,"chevron-down",new RectangleF(Width-26*factor,12*factor,16*factor,16*factor),color);
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(ClientRectangle,-3,-3),ForeColor,BackColor);
        }
    }

    internal sealed class ZoomSlider : Control
    {
        private double value=1;
        private bool tracking;
        internal event Action<double> ZoomRequested;
        internal ZoomSlider()
        {
            DoubleBuffered=true;TabStop=true;Cursor=Cursors.Hand;BackColor=Color.FromArgb(28,31,38);
            AccessibleName="图片缩放";AccessibleRole=AccessibleRole.Slider;
            SetStyle(ControlStyles.Selectable|ControlStyles.ResizeRedraw,true);
        }
        internal void SetValue(double scale){value=ZoomRange.Clamp(scale);Invalidate();}
        // Give the common 2–100% range the first quarter, then spread larger sizes
        // logarithmically, matching the compact slider's useful low-zoom range.
        private static double Fraction(double scale){return scale<=1?.25*(scale-ZoomRange.Minimum)/(1-ZoomRange.Minimum):.25+.75*Math.Log(scale)/Math.Log(ZoomRange.Maximum);}
        private static double Scale(double fraction){return fraction<=.25?ZoomRange.Minimum+(1-ZoomRange.Minimum)*fraction/.25:Math.Pow(ZoomRange.Maximum,(fraction-.25)/.75);}
        private void SetFromPoint(int x)
        {
            double inset=Height*.2,f=Math.Max(0,Math.Min(1,(x-inset)/Math.Max(1,Width-2*inset)));Request(Scale(f));
        }
        private void Request(double scale){value=ZoomRange.Clamp(scale);Invalidate();if(ZoomRequested!=null)ZoomRequested(value);}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(Enabled&&e.Button==MouseButtons.Left){Focus();tracking=true;Capture=true;SetFromPoint(e.X);}}
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(tracking)SetFromPoint(e.X);}
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(tracking){SetFromPoint(e.X);tracking=false;Capture=false;}}
        protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture)tracking=false;}
        protected override bool IsInputKey(Keys key){Keys k=key&Keys.KeyCode;return k==Keys.Left||k==Keys.Right||k==Keys.Up||k==Keys.Down||k==Keys.Home||k==Keys.End||base.IsInputKey(key);}
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);double f=Fraction(value);
            if(e.KeyCode==Keys.Left||e.KeyCode==Keys.Down){Request(Scale(Math.Max(0,f-.025)));e.Handled=true;}
            else if(e.KeyCode==Keys.Right||e.KeyCode==Keys.Up){Request(Scale(Math.Min(1,f+.025)));e.Handled=true;}
            else if(e.KeyCode==Keys.Home){Request(ZoomRange.Minimum);e.Handled=true;}else if(e.KeyCode==Keys.End){Request(ZoomRange.Maximum);e.Handled=true;}
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;g.Clear(BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
            float scale=Height/40f,left=8*scale,right=Width-left,y=Height/2f,x=left+(float)Fraction(value)*Math.Max(1,right-left);
            using(Pen baseLine=new Pen(Enabled?Color.FromArgb(137,141,149):Color.FromArgb(68,74,85),3*scale)){baseLine.StartCap=baseLine.EndCap=LineCap.Round;g.DrawLine(baseLine,left,y,right,y);}
            if(Enabled)using(Pen active=new Pen(Color.FromArgb(62,184,239),3*scale)){active.StartCap=active.EndCap=LineCap.Round;g.DrawLine(active,left,y,x,y);}
            using(Brush ring=new SolidBrush(Color.FromArgb(60,67,76)))g.FillEllipse(ring,x-9*scale,y-9*scale,18*scale,18*scale);
            using(Brush dot=new SolidBrush(Enabled?Color.FromArgb(69,192,250):Color.FromArgb(107,114,127)))g.FillEllipse(dot,x-4.5f*scale,y-4.5f*scale,9*scale,9*scale);
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(ClientRectangle,-1,-6),Color.FromArgb(180,186,196),BackColor);
        }
    }

    internal sealed class ZoomToolbar : UserControl
    {
        private readonly SvgButton fit,minus,plus,fullscreen;
        private readonly PercentButton percent;
        private readonly ZoomSlider slider;
        private readonly ToolTip tips;
        private readonly Panel separator;
        private ContextMenuStrip percentMenu;
        private bool showZoom=true;
        private double scale=1;
        internal event Action FitRequested,FullscreenRequested;
        internal event Action<double> ZoomRequested;
        internal event Action WidthPreferenceChanged;
        internal bool SliderFocused { get{return slider.Focused;} }
        internal double ScaleValue { get{return scale;} }
        internal ZoomToolbar()
        {
            BackColor=Color.FromArgb(28,31,38);Size=new Size(416,40);AutoScaleMode=AutoScaleMode.None;
            tips=new ToolTip{InitialDelay=450,ReshowDelay=100,AutoPopDelay=3000};
            fit=new SvgButton("fit-window","适应窗口");percent=new PercentButton();minus=new SvgButton("zoom-out","缩小");
            slider=new ZoomSlider();plus=new SvgButton("zoom-in","放大");fullscreen=new SvgButton("fullscreen","全屏");
            separator=new Panel{BackColor=Color.FromArgb(61,65,74)};
            Controls.AddRange(new Control[]{fit,percent,minus,slider,plus,separator,fullscreen});
            tips.SetToolTip(fit,"适应窗口 (0)");tips.SetToolTip(percent,"缩放比例 · 100% 为原始像素");tips.SetToolTip(minus,"缩小");tips.SetToolTip(plus,"放大");tips.SetToolTip(fullscreen,"全屏 (F11)");
            fit.Click+=(s,e)=>{if(FitRequested!=null)FitRequested();};fullscreen.Click+=(s,e)=>{if(FullscreenRequested!=null)FullscreenRequested();};
            minus.Click+=(s,e)=>Request(scale/1.25);plus.Click+=(s,e)=>Request(scale*1.25);slider.ZoomRequested+=Request;
            percent.Click+=(s,e)=>OpenPercentMenu();Resize+=(s,e)=>LayoutButtons();LayoutButtons();
        }
        private void Request(double value){if(ZoomRequested!=null)ZoomRequested(ZoomRange.Clamp(value));}
        internal void SetState(double value,bool ready,bool isFull,bool zoomVisible,bool isModel)
        {
            scale=double.IsNaN(value)||double.IsInfinity(value)?1:Math.Max(.000001,Math.Min(ZoomRange.Maximum,value));percent.Text=(scale*100).ToString(scale<.1?"0.#":"0",CultureInfo.InvariantCulture)+"%";slider.SetValue(scale);
            fit.Enabled=percent.Enabled=slider.Enabled=ready;minus.Enabled=ready&&scale>ZoomRange.Minimum+.000001;plus.Enabled=ready&&scale<ZoomRange.Maximum-.000001;
            fullscreen.IconName=isFull?"exit-fullscreen":"fullscreen";fullscreen.AccessibleName=isFull?"退出全屏":"全屏";
            tips.SetToolTip(fullscreen,isFull?"退出全屏 (Esc)":"全屏 (F11)");fit.AccessibleName=isModel?"复位模型视角":"适应窗口";
            tips.SetToolTip(fit,isModel?"复位模型视角 (0)":"适应窗口 (0)");tips.SetToolTip(percent,isModel?"模型缩放比例":"缩放比例 · 100% 为原始像素");slider.AccessibleName=isModel?"模型缩放":"图片缩放";
            if(showZoom!=zoomVisible){showZoom=zoomVisible;LayoutButtons();if(WidthPreferenceChanged!=null)WidthPreferenceChanged();}
        }
        internal int WantedWidth(int height){return (int)Math.Round((showZoom?416:40)*Math.Max(.5,height/40.0));}
        private void LayoutButtons()
        {
            if(fit==null)return;float f=Math.Max(.1f,Math.Min(Height/40f,Width/(showZoom?416f:40f)));int h=Math.Max(1,(int)Math.Round(40*f)),y=(Height-h)/2;
            fit.Visible=percent.Visible=minus.Visible=slider.Visible=plus.Visible=separator.Visible=showZoom;
            if(showZoom)
            {
                fit.SetBounds(0,y,(int)(40*f),h);percent.SetBounds((int)(48*f),y,(int)(88*f),h);
                minus.SetBounds((int)(144*f),y,(int)(32*f),h);slider.SetBounds((int)(180*f),y,(int)(140*f),h);
                plus.SetBounds((int)(324*f),y,(int)(32*f),h);separator.SetBounds((int)(366*f),y+(int)(5*f),Math.Max(1,(int)f),(int)(30*f));
                fullscreen.SetBounds((int)(376*f),y,(int)(40*f),h);
            }
            else fullscreen.SetBounds((Width-(int)(40*f))/2,y,(int)(40*f),h);
        }
        private void OpenPercentMenu()
        {
            if(percentMenu==null)
            {
                percentMenu=new ContextMenuStrip{ShowImageMargin=false,BackColor=BackColor,ForeColor=ForeColor};
                percentMenu.Items.Add(fit.AccessibleName,null,(s,e)=>{if(FitRequested!=null)FitRequested();});percentMenu.Items.Add(new ToolStripSeparator());
                foreach(double value in new[]{.1,.25,.5,.75,1,1.25,1.5,2,3,4,8})
                {
                    double choice=value;ToolStripMenuItem item=new ToolStripMenuItem((value*100).ToString("0",CultureInfo.InvariantCulture)+"%"){Tag=value};
                    item.Click+=(s,e)=>Request(choice);percentMenu.Items.Add(item);
                }
            }
            percentMenu.Items[0].Text=fit.AccessibleName;
            foreach(ToolStripItem item in percentMenu.Items){ToolStripMenuItem entry=item as ToolStripMenuItem;if(entry!=null&&entry.Tag is double)entry.Checked=Math.Abs(scale-(double)entry.Tag)<.005;}
            percentMenu.Show(percent,new Point(0,0),ToolStripDropDownDirection.AboveRight);
        }
        protected override void Dispose(bool disposing){if(disposing){if(percentMenu!=null)percentMenu.Dispose();tips.Dispose();}base.Dispose(disposing);}
    }
}
