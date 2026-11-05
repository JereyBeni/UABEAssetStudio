#!/usr/bin/env python3
"""
Reemplaza Texture2D y posiciones de Mesh dentro de un bundle o archivo serializado
de Unity, y guarda el resultado en un archivo nuevo.

Usa UnityPy para leer y escribir (incluye la serialización de objetos y el
reempaquetado del bundle).

Uso:
    # Reemplazar una textura por nombre
    python replace_assets.py entrada.bundle salida.bundle \
        --texture NombreTextura --png nueva.png

    # Reemplazar las posiciones de una malla por nombre (mismo número de vértices)
    python replace_assets.py entrada.bundle salida.bundle \
        --mesh NombreMalla --obj nueva.obj

    # Ver qué objetos hay (nombres y clases)
    python replace_assets.py entrada.bundle --list

Limitaciones:
- Mesh: solo reemplaza la posición (canal 0, float32, 3 componentes). El .obj debe
  tener el mismo número de vértices que la malla original y en el mismo orden,
  porque el índice de triángulos no se cambia. Si el .obj viene de exportar la misma
  malla con AssetStudio, normalmente mantiene el orden.
- Texture2D: se re-codifica en el mismo formato que tenía la original cuando UnityPy
  lo soporta. Los mipmaps no se regeneran: si la textura los tenía, se pierden.
- No verificado con un juego real: probar primero con una copia del archivo.
"""

import argparse
import os
import struct
import sys

import UnityPy
from PIL import Image


def list_objects(env):
    for obj in env.objects:
        try:
            data = obj.read()
            name = getattr(data, "m_Name", "") or ""
        except Exception:
            name = "?"
        print(f"{obj.type.name:<20} path_id={obj.path_id:<12} name={name}")


def find_by_name(env, type_name, name):
    matches = []
    for obj in env.objects:
        if obj.type.name != type_name:
            continue
        data = obj.read()
        if getattr(data, "m_Name", None) == name:
            matches.append((obj, data))
    if not matches:
        raise SystemExit(f"No se encontró un {type_name} llamado '{name}'.")
    if len(matches) > 1:
        print(f"Aviso: hay {len(matches)} objetos con ese nombre; se usa el primero.", file=sys.stderr)
    return matches[0]


def replace_texture(env, name, png_path):
    obj, tex = find_by_name(env, "Texture2D", name)
    img = Image.open(png_path).convert("RGBA")

    # Si la imagen cambió de tamaño, la textura lo acepta igual en Unity,
    # pero conviene avisar porque puede no coincidir con el uso original.
    if (img.width, img.height) != (tex.m_Width, tex.m_Height):
        print(
            f"Aviso: la imagen es {img.width}x{img.height} y la original es "
            f"{tex.m_Width}x{tex.m_Height}.",
            file=sys.stderr,
        )

    from UnityPy.export.Texture2DConverter import image_to_texture2d

    data, _ = image_to_texture2d(img, tex.m_TextureFormat, flip=True)
    tex.image_data = data
    tex.m_Width = img.width
    tex.m_Height = img.height
    tex.m_CompleteImageSize = len(data)
    if getattr(tex, "m_MipCount", 1) > 1:
        tex.m_MipCount = 1
    tex.save()
    print(f"Textura '{name}' reemplazada ({img.width}x{img.height}).")


def read_obj_positions(obj_path):
    positions = []
    with open(obj_path, "r", encoding="utf-8", errors="replace") as f:
        for line in f:
            parts = line.split()
            if parts and parts[0] == "v" and len(parts) >= 4:
                positions.append((float(parts[1]), float(parts[2]), float(parts[3])))
    return positions


def find_position_channel(vertex_data):
    # Canal 0 = posición (kShaderChannelVertex). Formato 0 = float32.
    channel = vertex_data.m_Channels[0]
    if channel.format != 0 or channel.dimension < 3:
        raise SystemExit(
            "La malla no tiene la posición en float32 de 3 componentes; "
            "este script no lo soporta todavía."
        )
    stream = vertex_data.m_Streams[channel.stream]
    return stream, channel


def replace_mesh_positions(env, name, obj_path):
    obj, mesh = find_by_name(env, "Mesh", name)
    vertex_data = mesh.m_VertexData
    count = vertex_data.m_VertexCount

    positions = read_obj_positions(obj_path)
    if len(positions) != count:
        raise SystemExit(
            f"El .obj tiene {len(positions)} vértices y la malla tiene {count}. "
            "Deben coincidir (este script no cambia la cantidad de vértices)."
        )

    stream, channel = find_position_channel(vertex_data)
    data = bytearray(vertex_data.m_DataSize)
    for i, (x, y, z) in enumerate(positions):
        offset = stream.offset + i * stream.stride + channel.offset
        struct.pack_into("<3f", data, offset, x, y, z)

    vertex_data.m_DataSize = bytes(data)
    mesh.save()
    print(f"Posiciones de la malla '{name}' reemplazadas ({count} vértices).")


def main():
    parser = argparse.ArgumentParser(description="Reemplaza texturas y posiciones de mallas en assets de Unity.")
    parser.add_argument("input", help="Bundle o archivo serializado de entrada")
    parser.add_argument("output", nargs="?", help="Archivo de salida (no hace falta con --list)")
    parser.add_argument("--list", action="store_true", help="Solo lista los objetos")
    parser.add_argument("--texture", help="Nombre del Texture2D a reemplazar")
    parser.add_argument("--png", help="Imagen PNG para la textura")
    parser.add_argument("--mesh", help="Nombre del Mesh a reemplazar")
    parser.add_argument("--obj", help="Archivo .obj con las nuevas posiciones")
    args = parser.parse_args()

    env = UnityPy.load(args.input)

    if args.list:
        list_objects(env)
        return 0

    if not args.output:
        parser.error("Falta el archivo de salida.")
    if not (args.texture or args.mesh):
        parser.error("Indicá --texture o --mesh.")
    if args.texture and not args.png:
        parser.error("--texture necesita --png.")
    if args.mesh and not args.obj:
        parser.error("--mesh necesita --obj.")

    if args.texture:
        replace_texture(env, args.texture, args.png)
    if args.mesh:
        replace_mesh_positions(env, args.mesh, args.obj)

    # env.save escribe solo los archivos marcados como cambiados, con el nombre original.
    # "original" conserva la compresión que traía el bundle.
    out_dir = os.path.dirname(os.path.abspath(args.output))
    os.makedirs(out_dir, exist_ok=True)
    tmp_dir = os.path.join(out_dir, ".replace_tmp")
    os.makedirs(tmp_dir, exist_ok=True)
    env.save(pack="original", out_path=tmp_dir)

    written = [os.path.join(tmp_dir, n) for n in os.listdir(tmp_dir)]
    if len(written) != 1:
        raise SystemExit(f"Se esperaba un archivo de salida y hay {len(written)}. No se guardó nada.")
    os.replace(written[0], args.output)
    os.rmdir(tmp_dir)
    print(f"Guardado en: {args.output}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
