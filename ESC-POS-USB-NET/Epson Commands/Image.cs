using System;
using System.Collections;
using System.IO;
using ESC_POS_USB_NET.Interfaces.Command;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace ESC_POS_USB_NET.EpsonCommands
{
    public class Image : IImage
    {
        private static BitmapData GetBitmapData(Image<Rgba32> image)
        {
            var threshold = 127;
            var index = 0;
            double multiplier = 505; // this depends on your printer model.
            double scale = multiplier / image.Width;
            int xheight = (int)(image.Height * scale);
            int xwidth = (int)(image.Width * scale);
            var dimensions = xwidth * xheight;
            var dots = new BitArray(dimensions);

            image.Mutate(x => x.Resize(xwidth, xheight));

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<Rgba32> pixelRow = accessor.GetRowSpan(y);

                    for (int x = 0; x < pixelRow.Length; x++)
                    {
                        ref Rgba32 pixel = ref pixelRow[x];
                        var luminance = (int)(pixel.R * 0.3 + pixel.G * 0.59 + pixel.B * 0.11);
                        dots[index] = (luminance < threshold);
                        index++;
                    }
                }
            });

            return new BitmapData()
            {
                Dots = dots,
                Height = xheight,
                Width = xwidth
            };
        }

        private static Image<Rgba32> CropImage(Image<Rgba32> image)
        {
            // Detect the area of interest
            int left = image.Width, right = 0, top = image.Height, bottom = 0;

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<Rgba32> pixelRow = accessor.GetRowSpan(y);

                    for (int x = 0; x < pixelRow.Length; x++)
                    {
                        ref Rgba32 pixel = ref pixelRow[x];
                        // Adjust these conditions to detect your label area correctly
                        if (pixel.R < 250 || pixel.G < 250 || pixel.B < 250)
                        {
                            if (x < left) left = x;
                            if (x > right) right = x;
                            if (y < top) top = y;
                            if (y > bottom) bottom = y;
                        }
                    }
                }
            });

            // Ensure the cropping is within bounds
            left = Math.Max(0, left - 10);
            right = Math.Min(image.Width, right + 10);
            top = Math.Max(0, top - 10);
            bottom = Math.Min(image.Height, bottom + 10);

            return image.Clone(ctx => ctx.Crop(new Rectangle(left, top, right - left, bottom - top)));
        }

        byte[] IImage.Print(Image<Rgba32> image)
        {
            // Crop the image to only include the label part
            image = CropImage(image);

            var data = GetBitmapData(image);
            BitArray dots = data.Dots;
            byte[] width = BitConverter.GetBytes(data.Width);

            int offset = 0;
            MemoryStream stream = new MemoryStream();
            BinaryWriter bw = new BinaryWriter(stream);

            bw.Write((char)0x1B);
            bw.Write('@');

            bw.Write((char)0x1B);
            bw.Write('3');
            bw.Write((byte)24);

            while (offset < data.Height)
            {
                bw.Write((char)0x1B);
                bw.Write('*');         // bit-image mode
                bw.Write((byte)33);    // 24-dot double-density
                bw.Write(width[0]);  // width low byte
                bw.Write(width[1]);  // width high byte

                for (int x = 0; x < data.Width; ++x)
                {
                    for (int k = 0; k < 3; ++k)
                    {
                        byte slice = 0;
                        for (int b = 0; b < 8; ++b)
                        {
                            int y = (((offset / 8) + k) * 8) + b;
                            // Calculate the location of the pixel we want in the bit array.
                            // It'll be at (y * width) + x.
                            int i = (y * data.Width) + x;

                            // If the image is shorter than 24 dots, pad with zero.
                            bool v = false;
                            if (i < dots.Length)
                            {
                                v = dots[i];
                            }
                            slice |= (byte)((v ? 1 : 0) << (7 - b));
                        }

                        bw.Write(slice);
                    }
                }
                offset += 24;
                bw.Write((char)0x0A);
            }
            // Restore the line spacing to the default of 30 dots.
            bw.Write((char)0x1B);
            bw.Write('3');
            bw.Write((byte)30);

            bw.Flush();
            byte[] bytes = stream.ToArray();
            bw.Dispose();
            return bytes;
        }
    }

    public class BitmapData
    {
        public BitArray Dots { get; set; }
        public int Height { get; set; }
        public int Width { get; set; }
    }
}
