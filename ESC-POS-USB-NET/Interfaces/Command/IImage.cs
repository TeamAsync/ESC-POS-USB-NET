using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ESC_POS_USB_NET.Interfaces.Command
{
    internal interface IImage
    {
        byte[] Print(Image<Rgba32> image);
    }
}
