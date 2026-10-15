using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ISBSSOFExtra
{
    internal static class WavefrontMesh
    {
        internal sealed class Geometry
        {
            public float[][] vertices { get; set; }
            public float[][] normals { get; set; }
            public float[][] uv { get; set; }
            public int[][] triangles { get; set; }
        }

        internal static Geometry Read(string path, int maxVertices = 10000, int maxIndices = 21000, int maxSurfaces = 4, int maxBytes = 2 * 1024 * 1024)
        {
            if (new FileInfo(path).Length > maxBytes)
                throw new InvalidDataException("OBJ mesh exceeds the configured file size limit.");
            var vertices = new List<float[]>();
            var normals = new List<float[]>();
            var uv = new List<float[]>();
            var groups = new List<List<int>>();
            int indexCount = 0;
            foreach (string line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var fields = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                switch (fields[0])
                {
                    case "v":
                        vertices.Add(Vector(fields, 3));
                        break;
                    case "vn":
                        normals.Add(Vector(fields, 3));
                        break;
                    case "vt":
                        uv.Add(Vector(fields, 2));
                        break;
                    case "g":
                        if (groups.Count >= maxSurfaces)
                            throw new InvalidDataException("Too many OBJ mesh surfaces.");
                        groups.Add(new List<int>());
                        break;
                    case "f":
                        if (fields.Length != 4 || groups.Count == 0)
                            throw new InvalidDataException("OBJ mesh requires triangulated surface groups.");
                        for (int corner = 1; corner <= 3; corner++)
                        {
                            string[] indices = fields[corner].Split('/');
                            int vertex = int.Parse(indices[0], CultureInfo.InvariantCulture);
                            if (vertex < 1 || vertex > vertices.Count)
                                throw new InvalidDataException("OBJ mesh index outside vertex range.");
                            for (int i = 1; i < indices.Length; i++)
                                if (indices[i].Length > 0 && int.Parse(indices[i], CultureInfo.InvariantCulture) != vertex)
                                    throw new InvalidDataException("Export the OBJ with shared vertex attribute indices.");
                            groups[groups.Count - 1].Add(vertex - 1);
                        }

                        indexCount += 3;
                        break;
                    default:
                        throw new InvalidDataException("Unsupported OBJ record: " + fields[0]);
                }

                if (vertices.Count > maxVertices || normals.Count > maxVertices || uv.Count > maxVertices || indexCount > maxIndices)
                    throw new InvalidDataException("OBJ mesh budget exceeded.");
            }

            return new Geometry
            {
                vertices = vertices.ToArray(),
                normals = normals.ToArray(),
                uv = uv.ToArray(),
                triangles = groups.ConvertAll(group => group.ToArray()).ToArray()
            };
        }

        private static float[] Vector(string[] fields, int count)
        {
            if (fields.Length != count + 1)
                throw new InvalidDataException("Invalid OBJ vertex attribute.");
            var value = new float[count];
            for (int i = 0; i < count; i++)
            {
                value[i] = float.Parse(fields[i + 1], CultureInfo.InvariantCulture);
                if (float.IsNaN(value[i]) || float.IsInfinity(value[i]))
                    throw new InvalidDataException("Invalid OBJ vertex value.");
            }

            return value;
        }
    }
}
