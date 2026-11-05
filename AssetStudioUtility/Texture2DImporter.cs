using System;
using System.IO;

namespace AssetStudio
{
    /// <summary>
    /// Reemplaza el contenido de un Texture2D por píxeles RGBA32 crudos.
    ///
    /// Limitaciones de esta primera versión:
    /// - Solo genera RGBA32 sin mipmaps (m_MipCount = 1).
    /// - No vuelve a escribir el archivo: solo modifica el objeto en memoria.
    ///   La escritura de serialized files y bundles todavía no existe.
    /// - El orden de filas (flipVertical) está por verificar con una textura real.
    /// </summary>
    public static class Texture2DImporter
    {
        /// <param name="texture">Textura a reemplazar.</param>
        /// <param name="rgba">Píxeles en orden R, G, B, A, de izquierda a derecha, fila por fila.
        /// Largo esperado: width * height * 4.</param>
        /// <param name="width">Ancho de la imagen en píxeles.</param>
        /// <param name="height">Alto de la imagen en píxeles.</param>
        /// <param name="flipVertical">Si es true, invierte el orden de las filas (Unity guarda
        /// las texturas sin comprimir de abajo hacia arriba). Verificar con una textura real.</param>
        public static void ReplaceWithRgba32(Texture2D texture, byte[] rgba, int width, int height, bool flipVertical = true)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            if (rgba == null) throw new ArgumentNullException(nameof(rgba));
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Dimensiones inválidas.");

            long expected = (long)width * height * 4;
            if (rgba.LongLength != expected)
                throw new ArgumentException($"Se esperaban {expected} bytes para {width}x{height} RGBA32, llegaron {rgba.LongLength}.", nameof(rgba));

            byte[] data = flipVertical ? FlipRows(rgba, width, height) : rgba;

            texture.m_Width = width;
            texture.m_Height = height;
            texture.m_TextureFormat = TextureFormat.RGBA32;
            texture.m_MipMap = false;
            texture.m_MipCount = 1;

            // Los datos quedan embebidos en el objeto, no en un archivo .resS externo.
            texture.m_StreamData = null;
            texture.image_data = new ResourceReader(new BinaryReader(new MemoryStream(data)), 0, data.Length);
        }

        private static byte[] FlipRows(byte[] src, int width, int height)
        {
            int rowBytes = width * 4;
            var dst = new byte[src.Length];
            for (int y = 0; y < height; y++)
            {
                Buffer.BlockCopy(src, y * rowBytes, dst, (height - 1 - y) * rowBytes, rowBytes);
            }
            return dst;
        }
    }
}
