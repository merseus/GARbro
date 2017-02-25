//! \file       ImageMG2.cs
//! \date       Sat Feb 25 03:08:01 2017
//! \brief      Valkyria image format.
//
// Copyright (C) 2017 by morkt
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to
// deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
// sell copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
// IN THE SOFTWARE.
//

using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Valkyria
{
    internal class Mg2MetaData : ImageMetaData
    {
        public int ImageLength;
        public int AlphaLength;
    }

    [Export(typeof(ImageFormat))]
    public class Mg2Format : ImageFormat
    {
        public override string         Tag { get { return "MG2"; } }
        public override string Description { get { return "Valkyria image format"; } }
        public override uint     Signature { get { return 0x4F43494D; } } // 'MICO'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            if (!header.AsciiEqual (4, "CG01"))
                return null;
            int length = header.ToInt32 (8);
            using (var input = new Mg2EncryptedStream (file.AsStream, 0x10, length))
            using (var png = new BinaryStream (input, file.Name))
            {
                var info = Png.ReadMetaData (png);
                if (null == info)
                    return null;
                return new Mg2MetaData
                {
                    Width = info.Width,
                    Height = info.Height,
                    OffsetX = info.OffsetX,
                    OffsetY = info.OffsetY,
                    BPP = info.BPP,
                    ImageLength = length,
                    AlphaLength = header.ToInt32 (12)
                };
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (Mg2MetaData)info;
            BitmapSource frame;
            using (var input = new Mg2EncryptedStream (file.AsStream, 0x10, meta.ImageLength))
            {
                var decoder = new PngBitmapDecoder (input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                frame = decoder.Frames[0];
                if (0 == meta.AlphaLength)
                {
                    frame.Freeze();
                    return new ImageData (frame, info);
                }
            }
            if (frame.Format.BitsPerPixel != 32)
                frame = new FormatConvertedBitmap (frame, PixelFormats.Bgr32, null, 0);
            int stride = frame.PixelWidth * 4;
            var pixels = new byte[stride * (int)meta.Height];
            frame.CopyPixels (pixels, stride, 0);

            using (var input = new Mg2EncryptedStream (file.AsStream, 0x10+meta.ImageLength, meta.AlphaLength))
            {
                var decoder = new PngBitmapDecoder (input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                BitmapSource alpha_frame = decoder.Frames[0];
                if (alpha_frame.PixelWidth != frame.PixelWidth || alpha_frame.PixelHeight != frame.PixelHeight)
                    return ImageData.Create (info, PixelFormats.Bgr32, null, pixels, stride);

                alpha_frame = new FormatConvertedBitmap (alpha_frame, PixelFormats.Gray8, null, 0);
                var alpha = new byte[alpha_frame.PixelWidth * alpha_frame.PixelHeight];
                alpha_frame.CopyPixels (alpha, alpha_frame.PixelWidth, 0);

                int src = 0;
                for (int dst = 3; dst < pixels.Length; dst += 4)
                {
                    pixels[dst] = alpha[src++];
                }
                return ImageData.Create (info, PixelFormats.Bgra32, null, pixels, stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Mg2Format.Write not implemented");
        }
    }

    internal class Mg2EncryptedStream : StreamRegion
    {
        readonly int    m_threshold;

        public Mg2EncryptedStream (Stream main, int offset, int length)
            : base (main, offset, length, true)
        {
            m_threshold = length / 5;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int pos = (int)Position;
            int read = base.Read (buffer, offset, count);
            for (int i = 0; i < read && pos < m_threshold; ++i)
                buffer[offset+i] ^= (byte)pos++;
            return read;
        }

        public override int ReadByte ()
        {
            long pos = Position;
            int b = base.ReadByte();
            if (b != -1 && pos < m_threshold)
                b ^= (byte)pos;
            return b;
        }
    }
}
