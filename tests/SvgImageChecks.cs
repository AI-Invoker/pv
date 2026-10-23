// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AI-Invoker
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace PV
{
    internal static class SvgImageChecks
    {
        private static int checks;
        private static string fixtures;
        private static readonly MethodInfo decode=typeof(Viewer).GetMethod("Decode",BindingFlags.Static|BindingFlags.NonPublic);
        private static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
        private static string Write(string name,string xml)
        {
            string path=Path.Combine(fixtures,name);File.WriteAllText(path,xml,new UTF8Encoding(false));return path;
        }
        private static string Svg(string body,string dimensions="width='100' height='80'")
        {
            return "<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' "+dimensions+">"+body+"</svg>";
        }
        private static Image Load(string name,string xml){return (Image)decode.Invoke(null,new object[]{Write(name,xml)});}
        private static Bitmap Render(Image image,int width,int height)
        {
            Bitmap output=new Bitmap(width,height);
            using(Graphics g=Graphics.FromImage(output))((SvgImage)image.Tag).Draw(g,new RectangleF(0,0,width,height));
            return output;
        }
        private static void Pixel(Bitmap image,int x,int y,Func<Color,bool> expected,string message){Color c=image.GetPixel(x,y);Check(expected(c),message+": "+c);}
        private static bool Red(Color c){return c.A>240&&c.R>230&&c.G<25&&c.B<25;}
        private static bool Clear(Color c){return c.A==0;}
        private static void Invalid(string name,string xml)
        {
            bool failed=false;try{using(Image image=Load(name,xml)) {}}catch(TargetInvocationException ex){failed=ex.InnerException is System.Xml.XmlException||ex.InnerException is InvalidDataException;}
            Check(failed,"Reject invalid SVG: "+name);
        }
        [STAThread] private static int Main(string[] args)
        {
            try
            {
                fixtures=Path.GetFullPath(args[0]);Directory.CreateDirectory(fixtures);
                Check(Viewer.ImageExtensions.Contains(".svg"),"SVG is an image format");
                Check(Viewer.AllExtensions.Contains(".svg"),"SVG is registered and included in open filters");
                MethodInfo supported=typeof(Viewer).GetMethod("Supported",BindingFlags.Static|BindingFlags.NonPublic);
                Check((bool)supported.Invoke(null,new object[]{"中文 图标.SVG"}),"Uppercase SVG is accepted");

                using(Image image=Load("中文 图标.SVG",Svg("<rect x='10' y='10' width='30' height='20' fill='red'/>")))
                {
                    Check(image.Tag is SvgImage,"Viewer decoder retains vector data");
                    Check(((SvgImage)image.Tag).Size==new SizeF(100,80),"Intrinsic dimensions");
                    using(Bitmap bitmap=Render(image,100,80)){Pixel(bitmap,20,20,Red,"Rectangle");Pixel(bitmap,1,1,Clear,"Transparent background");}
                    string original=Path.Combine(fixtures,"中文 图标.SVG"),renamed=original+".moved";
                    if(File.Exists(renamed))File.Delete(renamed);File.Move(original,renamed);File.Delete(renamed);
                    using(Bitmap bitmap=Render(image,200,160))Pixel(bitmap,40,40,Red,"Retained drawing after file deletion");
                }
                using(Image image=Load("curves.svg",Svg("<path d='M10 50 C10 10 50 10 50 50 Q30 70 10 50 Z' fill='red'/><path d='M60 30 A15 15 0 1 0 90 30 A15 15 0 1 0 60 30 Z' fill='#0000ff'/>")))
                using(Bitmap bitmap=Render(image,100,80))
                {Pixel(bitmap,30,35,Red,"Cubic and quadratic curves");Pixel(bitmap,75,30,c=>c.B>240&&c.A==255,"Arc path");Pixel(bitmap,2,2,Clear,"Curve bounds");}
                using(Image image=Load("gradient.svg",Svg("<defs><linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><radialGradient id='r'><stop stop-color='white'/><stop offset='1' stop-color='black'/></radialGradient></defs><rect width='100' height='30' fill='url(#g)'/><circle cx='50' cy='55' r='20' fill='url(#r)'/>")))
                using(Bitmap bitmap=Render(image,100,80))
                {Pixel(bitmap,5,15,c=>c.R>200&&c.B<40,"Linear gradient start");Pixel(bitmap,95,15,c=>c.B>200&&c.R<40,"Linear gradient end");Pixel(bitmap,50,55,c=>c.R>210,"Radial gradient center");Pixel(bitmap,50,72,c=>c.R<70&&c.A>240,"Radial gradient edge");}
                using(Image image=Load("clip-style.svg",Svg("<style>.accent { fill: rgb(0,200,40); }</style><defs><clipPath id='c'><circle cx='30' cy='30' r='20'/></clipPath></defs><g clip-path='url(#c)'><rect width='60' height='60' class='accent'/></g><rect x='70' y='10' width='20' height='20' fill='red' opacity='.5'/>")))
                using(Bitmap bitmap=Render(image,100,80))
                {Pixel(bitmap,30,30,c=>c.G==200&&c.B==40&&c.A==255,"CSS style");Pixel(bitmap,5,5,Clear,"Clipping");Pixel(bitmap,80,20,c=>c.A>=126&&c.A<=129&&c.R>240,"Opacity");}
                using(Image image=Load("transform-use.svg",Svg("<defs><rect id='tile' width='10' height='10' fill='red'/></defs><g transform='translate(30 10) scale(2)'><use xlink:href='#tile'/></g>")))
                using(Bitmap bitmap=Render(image,100,80))
                {Pixel(bitmap,35,15,Red,"Local use and transform");Pixel(bitmap,20,15,Clear,"Transform bounds");}
                using(Image image=Load("text.svg",Svg("<text x='2' y='30' font-size='24' font-family='Microsoft YaHei UI' fill='red'>SVG 图标</text>","width='180' height='50'")))
                using(Bitmap bitmap=Render(image,180,50))
                {
                    int english=0,chinese=0;for(int y=0;y<50;y++)for(int x=0;x<180;x++)if(bitmap.GetPixel(x,y).A>0){if(x<50)english++;else chinese++;}
                    Check(english>80,"Latin text");Check(chinese>80,"Chinese text");
                }
                using(Image image=Load("viewbox.svg",Svg("<rect x='100' y='200' width='20' height='10' fill='red'/>","viewBox='100 200 20 10'")))
                using(Bitmap bitmap=Render(image,200,100))
                {Check(((SvgImage)image.Tag).Size==new SizeF(20,10),"ViewBox-only dimensions");Pixel(bitmap,5,5,Red,"Nonzero viewBox origin");Pixel(bitmap,195,95,Red,"ViewBox extent");}
                using(Image image=Load("percentage.svg",Svg("<rect width='100%' height='100%' fill='red'/>","width='100%' height='100%' viewBox='0 0 64 32'")))
                using(Bitmap bitmap=Render(image,128,64))
                {Check(((SvgImage)image.Tag).Size==new SizeF(64,32),"Percentage root dimensions");Pixel(bitmap,120,60,Red,"Percentage geometry");}
                using(Image image=Load("physical.svg",Svg("<rect width='96' height='96' fill='red'/>","width='1in' height='2.54cm'")))
                {SizeF size=((SvgImage)image.Tag).Size;Check(Math.Abs(size.Width-96)<.01&&Math.Abs(size.Height-96)<.01,"Physical units at 96 DPI");}
                using(Image image=Load("width-only.svg",Svg("","width='200' viewBox='0 0 20 10'")))
                    Check(((SvgImage)image.Tag).Size==new SizeF(200,100),"Missing height follows viewBox ratio");
                using(Image image=Load("height-only.svg",Svg("","height='100' viewBox='0 0 20 10'")))
                    Check(((SvgImage)image.Tag).Size==new SizeF(200,100),"Missing width follows viewBox ratio");
                using(Image image=Load("large-coordinates.svg",Svg("<rect width='100000' height='50000' fill='red'/>","width='100' height='50' viewBox='0 0 100000 50000'")))
                using(Bitmap bitmap=Render(image,100,50))Pixel(bitmap,50,25,Red,"ViewBox coordinates do not limit viewport size");
                using(Image image=Load("default-size.svg",Svg("<rect width='10' height='10' fill='red'/>","")))
                    Check(((SvgImage)image.Tag).Size==new SizeF(300,150),"Default SVG viewport");
                using(Image image=Load("aspect.svg",Svg("<rect width='20' height='20' fill='red'/>","width='120' height='60' viewBox='0 0 20 20'")))
                using(Bitmap bitmap=Render(image,120,60))
                {Pixel(bitmap,60,30,Red,"Preserved aspect ratio");Pixel(bitmap,10,30,Clear,"Aspect ratio letterbox");}
                using(Image image=Load("large.svg",Svg("<rect width='32768' height='16384' fill='red'/>","width='32768' height='16384'")))
                {Check(image.Width<=1024&&image.Height<=1024,"Bounded preview allocation");Check(((SvgImage)image.Tag).Size==new SizeF(32768,16384),"Large vector keeps original size");}
                using(Image image=Load("sharp.svg",Svg("<rect x='4.25' y='4' width='.5' height='8' fill='red'/>","width='16' height='16'")))
                using(Bitmap bitmap=Render(image,128,128))
                {Pixel(bitmap,35,50,Red,"Zoom rerenders subpixel vector geometry");Pixel(bitmap,39,50,Clear,"No bitmap interpolation at vector edge");}
                using(Image image=Load("rotate.svg",Svg("<rect width='10' height='10' fill='red'/>","width='40' height='20'")))
                {
                    SvgImage vector=(SvgImage)image.Tag;vector.Rotate();Check(vector.Size==new SizeF(20,40),"Rotation swaps logical dimensions");
                    using(Bitmap bitmap=Render(image,20,40)){Pixel(bitmap,15,5,Red,"Clockwise rotation");Pixel(bitmap,5,35,Clear,"Rotated empty region");}
                    vector.Rotate();vector.Rotate();vector.Rotate();Check(vector.Size==new SizeF(40,20),"Four rotations restore dimensions");
                    using(Bitmap bitmap=Render(image,40,20))Pixel(bitmap,5,5,Red,"Four rotations restore geometry");
                }
                string embedded;
                using(Bitmap tile=new Bitmap(4,4))using(MemoryStream bytes=new MemoryStream())
                {using(Graphics g=Graphics.FromImage(tile))g.Clear(Color.Red);tile.Save(bytes,ImageFormat.Png);embedded=Convert.ToBase64String(bytes.ToArray());}
                using(Image image=Load("embedded.svg",Svg("<image x='10' y='10' width='40' height='40' xlink:href='data:image/png;base64,"+embedded+"'/>")))
                using(Bitmap bitmap=Render(image,100,80))Pixel(bitmap,25,25,Red,"Embedded PNG");
                string external=Path.Combine(fixtures,"external.png");File.WriteAllBytes(external,Convert.FromBase64String(embedded));
                using(Image image=Load("external.svg",Svg("<image width='80' height='80' xlink:href='"+new Uri(external).AbsoluteUri+"'/><rect x='80' width='20' height='20' fill='red'/><script>throw new Error('must not run');</script>")))
                using(Bitmap bitmap=Render(image,100,80))
                {Pixel(bitmap,20,20,Clear,"External file image is not loaded");Pixel(bitmap,90,10,Red,"Static content still renders with inert script");}
                using(Image image=Load("doctype.svg","<!DOCTYPE svg PUBLIC '-//W3C//DTD SVG 1.1//EN' 'http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd'>"+Svg("<rect width='20' height='20' fill='red'/>")))
                using(Bitmap bitmap=Render(image,100,80))Pixel(bitmap,10,10,Red,"Common doctype works offline");
                using(Image image=Load("no-namespace.svg","<svg width='20' height='10'><rect width='20' height='10' fill='red'/></svg>"))
                using(Bitmap bitmap=Render(image,20,10))Pixel(bitmap,5,5,Red,"Unqualified SVG root");
                Invalid("broken.svg","<svg><path></svg>");Invalid("not-svg.svg","<html/>");Invalid("wrong-namespace.svg","<svg xmlns='urn:wrong'/>");
                Invalid("zero.svg",Svg("","width='0' height='10'"));Invalid("negative.svg",Svg("","width='-1' height='10'"));Invalid("huge.svg",Svg("","width='1000000' height='10'"));
                Invalid("zero-viewbox.svg",Svg("","viewBox='0 0 0 10'"));
                Invalid("entity.svg","<!DOCTYPE svg [<!ENTITY x SYSTEM '"+new Uri(external).AbsoluteUri+"'>]>"+Svg("<text>&x;</text>"));
                Invalid("deep.svg",Svg(string.Concat(Enumerable.Repeat("<g>",130))+string.Concat(Enumerable.Repeat("</g>",130))));
                string oversized=Path.Combine(fixtures,"oversized.svg");using(FileStream stream=File.Create(oversized))stream.SetLength(SvgImage.MaximumFileBytes+1);
                bool rejected=false;try{using(Image image=SvgImage.Load(oversized)) {}}catch(InvalidDataException){rejected=true;}Check(rejected,"Oversized file rejected before parsing");File.Delete(oversized);

                // Controls are never shown, activated or captured; exercise only their
                // geometry/lifetime methods and render generated files into bitmaps.
                using(ImageCanvas canvas=new ImageCanvas{Size=new Size(400,300)})
                {
                    canvas.SetPicture(Load("canvas.svg",Svg("<rect width='2000' height='1000' fill='red'/>","width='2000' height='1000'")));
                    Check(canvas.PictureSize==new SizeF(2000,1000),"Canvas uses vector dimensions, not preview dimensions");
                    Check(Math.Abs(canvas.ScaleFactor-.18)<.00001,"Fit uses vector viewport");Check(!canvas.CanPan,"Fitted vector is centered");
                    canvas.Actual();Check(canvas.ScaleFactor==1&&canvas.CanPan,"Actual size and pan");canvas.SetZoom(8);Check(Math.Abs(canvas.ScaleFactor-8)<.00001,"800 percent zoom");
                    canvas.Rotate();Check(canvas.PictureSize==new SizeF(1000,2000),"Canvas rotation updates size");Check(!canvas.CanPan,"Rotation refits vector");
                    canvas.SetPicture(new Bitmap(40,20));Check(canvas.PictureSize==new SizeF(40,20),"Raster switching clears vector state");canvas.Rotate();Check(canvas.PictureSize==new SizeF(20,40),"Raster rotation unchanged");
                    canvas.SetPicture(null);Check(canvas.PictureSize.IsEmpty&&!canvas.CanPan,"Clearing image resets state");
                }
                string png=Path.Combine(fixtures,"raster.png");using(Bitmap bitmap=new Bitmap(13,7)){bitmap.Save(png,ImageFormat.Png);}
                Image raster=(Image)decode.Invoke(null,new object[]{png});Stream rasterStream=(Stream)raster.Tag;
                try{Check(raster.Width==13&&raster.Height==7,"Existing raster decoder unchanged");}finally{raster.Dispose();rasterStream.Dispose();}
                Assembly packaged=Assembly.LoadFrom(args[1]);Type packagedViewer=packaged.GetType("PV.Viewer");
                Check(((string[])packagedViewer.GetField("ImageExtensions",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null)).Contains(".svg"),"Packaged viewer advertises SVG");
                using(Image image=(Image)packagedViewer.GetMethod("Decode",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{Path.Combine(fixtures,"viewbox.svg")}))
                {Check(image.Size==new Size(20,10),"Packaged viewer resolves viewBox dimensions");Pixel((Bitmap)image,5,5,Red,"Packaged viewer decodes SVG pixels");}
                Console.WriteLine("PASS: "+checks+" SVG checks (no windows opened).");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        }
    }
}
