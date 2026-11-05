using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AssetStudioGUI
{
    /// <summary>
    /// Lee un PNG (u otra imagen soportada por System.Drawing) y devuelve píxeles RGBA32.
    /// Solo se usa desde AssetStudioGUI, porque System.Drawing no está en AssetStudioUtility.
    /// </summary>
    internal static class ImageLoader
    {
        public static byte[] LoadRgba32(string path, out int width, out int height)
        {
            using (var bitmap = new Bitmap(path))
            {
                width = bitmap.Width;
                height = bitmap.Height;

                // Copia a 32bppArgb para tener un formato conocido, y después lockea los bits.
                using (var converted = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(converted))
                    {
                        g.DrawImage(bitmap, 0, 0, width, height);
                    }

                    var rect = new Rectangle(0, 0, width, height);
                    var data = converted.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        int byteCount = width * height * 4;
                        var bgra = new byte[byteCount];
                        int rowBytes = width * 4;
                        for (int y = 0; y < height; y++)
                        {
                            Marshal.Copy(data.Scan0 + y * data.Stride, bgra, y * rowBytes, rowBytes);
                        }

                        // System.Drawing usa BGRA; Texture2DImporter espera RGBA.
                        for (int i = 0; i < byteCount; i += 4)
                        {
                            byte b = bgra[i];
                            bgra[i] = bgra[i + 2];
                            bgra[i + 2] = b;
                        }
                        return bgra;
                    }
                    finally
                    {
                        converted.UnlockBits(data);
                    }
                }
            }
        }
    }
}
