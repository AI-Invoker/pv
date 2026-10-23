// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AI-Invoker
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Xml;
using Svg;

namespace PV
{
    // The bitmap is a bounded preview; ImageCanvas draws the retained vector document
    // at the current zoom, so small icons stay sharp and large viewBoxes do not
    // allocate a full-size bitmap. Parsing happens once on the image loader worker.
    internal sealed class SvgImage
    {
        internal const long MaximumFileBytes=16*1024*1024;
        private readonly SvgDocument document;
        private readonly SizeF originalSize;
        private int quarterTurns;

        static SvgImage()
        {
            SvgDocument.DisableDtdProcessing=true;
            SvgDocument.ResolveExternalXmlEntites=ExternalType.None;
            SvgDocument.ResolveExternalImages=ExternalType.None;
            SvgDocument.ResolveExternalElements=ExternalType.None;
            SvgDocument.PointsPerInch=96;
        }

        private SvgImage(SvgDocument value,SizeF size){document=value;originalSize=size;}
        internal SizeF Size {get{return quarterTurns%2==0?originalSize:new SizeF(originalSize.Height,originalSize.Width);}}
        internal void Rotate(){quarterTurns=(quarterTurns+1)%4;}

        internal static Image Load(string path)
        {
            XmlDocument xml=new XmlDocument{XmlResolver=null};
            // Ignore the common SVG 1.1 DOCTYPE without fetching it. Undefined
            // entities still fail validation; scripts and external resources are inert.
            XmlReaderSettings options=new XmlReaderSettings{DtdProcessing=DtdProcessing.Ignore,XmlResolver=null,MaxCharactersInDocument=MaximumFileBytes};
            using(FileStream stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            {
                if(stream.Length>MaximumFileBytes)throw new InvalidDataException("SVG 文件超过 16 MB。");
                using(XmlReader reader=XmlReader.Create(stream,options))xml.Load(reader);
            }
            XmlElement root=xml.DocumentElement;
            if(root==null||root.LocalName!="svg"||(root.NamespaceURI.Length>0&&root.NamespaceURI!="http://www.w3.org/2000/svg"))
                throw new InvalidDataException("文件不是有效的 SVG 图片。");
            using(XmlNodeReader reader=new XmlNodeReader(xml))
            {
                int elements=0;
                while(reader.Read())if(reader.Depth>128||(reader.NodeType==XmlNodeType.Element&&++elements>100000))
                    throw new InvalidDataException("SVG 内容过于复杂。");
            }
            SvgDocument doc;
            using(XmlNodeReader reader=new XmlNodeReader(xml))doc=SvgDocument.Open<SvgDocument>(reader);
            if(doc==null)throw new InvalidDataException("无法读取 SVG 图片。");
            bool hasViewBox=root.HasAttribute("viewBox");
            if(hasViewBox)
            {
                if(!ValidExtent(doc.ViewBox.Width)||!ValidExtent(doc.ViewBox.Height))throw new InvalidDataException("SVG viewBox 尺寸无效。");
                // SVG.NET's default percentage sizing includes the viewBox origin.
                // A standalone file's viewport depends on its extent, not its offset.
                if(!root.HasAttribute("width")||doc.Width.Type==SvgUnitType.Percentage)doc.Width=new SvgUnit(doc.ViewBox.Width*doc.Width.Value/100);
                if(!root.HasAttribute("height")||doc.Height.Type==SvgUnitType.Percentage)doc.Height=new SvgUnit(doc.ViewBox.Height*doc.Height.Value/100);
            }
            else
            {
                if(!root.HasAttribute("width")||doc.Width.Type==SvgUnitType.Percentage)doc.Width=new SvgUnit(300);
                if(!root.HasAttribute("height")||doc.Height.Type==SvgUnitType.Percentage)doc.Height=new SvgUnit(150);
            }
            SizeF size=doc.GetDimensions();
            if(hasViewBox)
            {
                if(!root.HasAttribute("height")&&root.HasAttribute("width"))size.Height=size.Width*doc.ViewBox.Height/doc.ViewBox.Width;
                if(!root.HasAttribute("width")&&root.HasAttribute("height"))size.Width=size.Height*doc.ViewBox.Width/doc.ViewBox.Height;
            }
            if(!ValidDimension(size.Width)||!ValidDimension(size.Height))throw new InvalidDataException("SVG 尺寸无效或超过 32768 像素。");
            // Resolve percentage root sizes once, independently of zoom and rotation.
            doc.Width=new SvgUnit(size.Width);doc.Height=new SvgUnit(size.Height);
            SvgImage vector=new SvgImage(doc,size);
            double scale=Math.Min(1,1024.0/Math.Max(size.Width,size.Height));
            Bitmap preview=new Bitmap(Math.Max(1,(int)Math.Ceiling(size.Width*scale)),Math.Max(1,(int)Math.Ceiling(size.Height*scale)));
            try
            {
                using(Graphics graphics=Graphics.FromImage(preview))vector.Draw(graphics,new RectangleF(0,0,preview.Width,preview.Height));
                preview.Tag=vector;
                return preview;
            }
            catch{preview.Dispose();throw;}
        }

        private static bool ValidExtent(float value){return !float.IsNaN(value)&&!float.IsInfinity(value)&&value>0;}
        private static bool ValidDimension(float value){return ValidExtent(value)&&value<=32768;}

        internal void Draw(Graphics graphics,RectangleF bounds)
        {
            GraphicsState state=graphics.Save();
            try
            {
                graphics.SetClip(bounds,CombineMode.Intersect);
                graphics.TranslateTransform(bounds.Left+bounds.Width/2,bounds.Top+bounds.Height/2);
                graphics.ScaleTransform(bounds.Width/Size.Width,bounds.Height/Size.Height);
                graphics.RotateTransform(quarterTurns*90);
                graphics.TranslateTransform(-originalSize.Width/2,-originalSize.Height/2);
                graphics.SmoothingMode=SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
                document.Draw(graphics,originalSize);
            }
            finally{graphics.Restore(state);}
        }
    }
}
