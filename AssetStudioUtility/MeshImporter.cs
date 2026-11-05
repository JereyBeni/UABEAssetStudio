using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace AssetStudio
{
    /// <summary>
    /// Reemplaza la geometría de un Mesh con los datos de un archivo .obj.
    ///
    /// Limitaciones de esta primera versión:
    /// - Solo modifica el objeto en memoria. Escribir el archivo es responsabilidad del
    ///   escritor de serialized files, que todavía no existe.
    /// - Solo soporta mallas con una submesh (el .obj se convierte en una única submesh).
    /// - Solo usa posición, normal (opcional) y UV0 (opcional). Colores, tangentes, skin,
    ///   blendshapes y UV1+ se descartan: al reemplazar, la malla ya no los tiene.
    /// - Solo triangula caras de 3 o 4 vértices (los quads se separan en dos triángulos).
    /// </summary>
    public static class MeshImporter
    {
        public static void ReplaceWithObj(Mesh mesh, string objPath)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (objPath == null) throw new ArgumentNullException(nameof(objPath));
            if (mesh.m_SubMeshes == null || mesh.m_SubMeshes.Length != 1)
                throw new NotSupportedException("Solo se soportan mallas con una submesh.");

            var positions = new List<float[]>();
            var uvs = new List<float[]>();
            var normals = new List<float[]>();
            var faces = new List<int[][]>(); // cada cara: lista de (v, vt, vn) índices 0-based

            foreach (var rawLine in File.ReadAllLines(objPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                switch (parts[0])
                {
                    case "v":
                        positions.Add(ParseFloats(parts, 3));
                        break;
                    case "vt":
                        uvs.Add(ParseFloats(parts, 2));
                        break;
                    case "vn":
                        normals.Add(ParseFloats(parts, 3));
                        break;
                    case "f":
                        faces.Add(ParseFace(parts, positions.Count, uvs.Count, normals.Count));
                        break;
                    // "o", "g", "usemtl", "s", etc. se ignoran.
                }
            }

            if (positions.Count == 0 || faces.Count == 0)
                throw new InvalidDataException("El .obj no tiene vértices ni caras.");

            // Cada combinación única (v, vt, vn) pasa a ser un vértice de la malla.
            var vertexIndex = new Dictionary<string, uint>();
            var outPositions = new List<float>();
            var outUv = new List<float>();
            var outNormals = new List<float>();
            var outIndices = new List<uint>();
            bool hasUv = uvs.Count > 0;
            bool hasNormals = normals.Count > 0;

            foreach (var face in faces)
            {
                // Triangulación en abanico: 3 vértices -> 1 triángulo, 4 -> 2 triángulos.
                for (int i = 1; i + 1 < face.Length; i++)
                {
                    foreach (var corner in new[] { face[0], face[i], face[i + 1] })
                    {
                        var key = corner[0] + "/" + corner[1] + "/" + corner[2];
                        if (!vertexIndex.TryGetValue(key, out uint idx))
                        {
                            idx = (uint)vertexIndex.Count;
                            vertexIndex.Add(key, idx);

                            outPositions.AddRange(positions[corner[0]]);
                            if (hasUv)
                            {
                                if (corner[1] >= 0) outUv.AddRange(uvs[corner[1]]);
                                else outUv.AddRange(new float[2]);
                            }
                            if (hasNormals)
                            {
                                if (corner[2] >= 0) outNormals.AddRange(normals[corner[2]]);
                                else outNormals.AddRange(new float[] { 0f, 1f, 0f });
                            }
                        }
                        outIndices.Add(idx);
                    }
                }
            }

            int vertexCount = vertexIndex.Count;

            mesh.m_VertexCount = vertexCount;
            mesh.m_Vertices = outPositions.ToArray();
            mesh.m_Normals = hasNormals ? outNormals.ToArray() : null;
            mesh.m_UV0 = hasUv ? outUv.ToArray() : null;
            // Se descartan los canales que el .obj no reemplaza.
            mesh.m_Colors = null;
            mesh.m_Tangents = null;
            mesh.m_UV1 = mesh.m_UV2 = mesh.m_UV3 = mesh.m_UV4 = mesh.m_UV5 = mesh.m_UV6 = mesh.m_UV7 = null;
            mesh.m_Skin = null;

            mesh.m_Indices = new List<uint>(outIndices);

            var sub = mesh.m_SubMeshes[0];
            sub.firstByte = 0;
            sub.indexCount = (uint)outIndices.Count;
            sub.triangleCount = (uint)(outIndices.Count / 3);
            sub.baseVertex = 0;
            sub.firstVertex = 0;
            sub.vertexCount = (uint)vertexCount;
        }

        private static float[] ParseFloats(string[] parts, int count)
        {
            var result = new float[count];
            for (int i = 0; i < count; i++)
            {
                // Si falta un componente (por ejemplo, vt con 2 valores), queda en 0.
                if (i + 1 < parts.Length)
                    result[i] = float.Parse(parts[i + 1], CultureInfo.InvariantCulture);
            }
            return result;
        }

        // Devuelve, por cada vértice de la cara, (v, vt, vn) con índices 0-based.
        // Un índice faltante queda en -1.
        private static int[][] ParseFace(string[] parts, int vCount, int vtCount, int vnCount)
        {
            int n = parts.Length - 1;
            if (n < 3 || n > 4)
                throw new NotSupportedException($"Solo se soportan caras de 3 o 4 vértices (llegó una de {n}).");

            var corners = new int[n][];
            for (int i = 0; i < n; i++)
            {
                var pieces = parts[i + 1].Split('/');
                int v = ResolveIndex(pieces[0], vCount);
                int vt = pieces.Length > 1 && pieces[1].Length > 0 ? ResolveIndex(pieces[1], vtCount) : -1;
                int vn = pieces.Length > 2 && pieces[2].Length > 0 ? ResolveIndex(pieces[2], vnCount) : -1;
                corners[i] = new[] { v, vt, vn };
            }
            return corners;
        }

        // OBJ indexa desde 1; los índices negativos cuentan desde el final.
        private static int ResolveIndex(string text, int count)
        {
            int raw = int.Parse(text, CultureInfo.InvariantCulture);
            return raw > 0 ? raw - 1 : count + raw;
        }
    }
}
