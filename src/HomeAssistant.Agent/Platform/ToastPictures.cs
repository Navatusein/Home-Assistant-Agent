#if WINDOWS
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace HomeAssistant.Agent.Platform;

// The square a toast shows its logo in (appLogoOverride) is filled, so a wide
// or tall picture loses its sides. Padded to a square with transparency
// first, all of it shows.
[SupportedOSPlatform("windows10.0.17763.0")]
internal static class ToastPictures
{
    // The square version next to the file, or the file itself when it is
    // square already. Throws when the file is no picture Windows can read.
    public static async Task<string> SquareAsync(string file)
    {
        byte[] pixels;
        int width, height;

        await using (var input = File.OpenRead(file))
        {
            var decoder = await BitmapDecoder.CreateAsync(input.AsRandomAccessStream());

            width = (int)decoder.OrientedPixelWidth;
            height = (int)decoder.OrientedPixelHeight;

            if (width == height)
                return file;

            var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                new BitmapTransform(), ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);

            pixels = data.DetachPixelData();
        }

        var size = Math.Max(width, height);
        var square = new byte[size * size * 4];
        var left = (size - width) / 2;
        var top = (size - height) / 2;

        for (var row = 0; row < height; row++)
            System.Buffer.BlockCopy(pixels, row * width * 4, square, ((top + row) * size + left) * 4, width * 4);

        var output = Path.Combine(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file) + "-square.png");

        using (var stream = new InMemoryRandomAccessStream())
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)size, (uint)size, 96, 96, square);
            await encoder.FlushAsync();

            var bytes = new byte[stream.Size];
            stream.Seek(0);
            await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
            await File.WriteAllBytesAsync(output, bytes);
        }

        return output;
    }
}
#endif
