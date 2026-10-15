using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace ISBSSOFExtra
{
    internal sealed class Scp173Sight
    {
        private sealed class Definition
        {
            public string collider;
            public string mesh;
            public int vertexCount;
            public float[][] vertices;
            public int[][] triangles;
            public int[][] opaqueTriangles;
        }

        private sealed class Triangle
        {
            private readonly Vector3 _origin, _first, _second, _normal;
            private readonly Bounds _bounds;
            private readonly float _firstLength, _secondLength, _product, _inverse, _tolerance;

            internal Triangle(float[] a, float[] b, float[] c, float tolerance)
            {
                _tolerance = tolerance;
                _origin = new Vector3(a[0], a[1], a[2]);
                var first = new Vector3(b[0], b[1], b[2]);
                var second = new Vector3(c[0], c[1], c[2]);
                _first = first - _origin;
                _second = second - _origin;
                _normal = Vector3.Cross(_first, _second).normalized;
                _firstLength = Vector3.Dot(_first, _first);
                _secondLength = Vector3.Dot(_second, _second);
                _product = Vector3.Dot(_first, _second);
                _inverse = 1f / (_firstLength * _secondLength - _product * _product);
                _bounds = new Bounds(_origin, Vector3.zero);
                _bounds.Encapsulate(first);
                _bounds.Encapsulate(second);
                _bounds.Expand(tolerance * 2f);
            }

            internal bool Contains(Vector3 point)
            {
                if (!_bounds.Contains(point))
                    return false;
                var offset = point - _origin;
                if (Math.Abs(Vector3.Dot(offset, _normal)) > _tolerance)
                    return false;
                float a = Vector3.Dot(offset, _first);
                float b = Vector3.Dot(offset, _second);
                float u = (_secondLength * a - _product * b) * _inverse;
                float v = (_firstLength * b - _product * a) * _inverse;
                return u >= -.001f && v >= -.001f && u + v <= 1.001f;
            }
        }

        private sealed class Surfaces
        {
            internal readonly Triangle[] transparent, opaque;
            internal Surfaces(Definition definition)
            {
                transparent = Create(definition.vertices, definition.triangles, .08f);
                opaque = Create(definition.vertices, definition.opaqueTriangles, .025f);
            }
            private static Triangle[] Create(float[][] vertices, int[][] faces, float tolerance)
            {
                var result = new Triangle[faces.Length];
                for (int i = 0; i < result.Length; i++) result[i] = new Triangle(vertices[faces[i][0]], vertices[faces[i][1]], vertices[faces[i][2]], tolerance);
                return result;
            }
        }

        private static readonly Lazy<Dictionary<string, Definition>> Definitions = new Lazy<Dictionary<string, Definition>>(Load);
        private readonly Dictionary<Collider, Surfaces> _surfaces = new Dictionary<Collider, Surfaces>();

        private static Dictionary<string, Definition> Load()
        {
            using (var stream = typeof(Scp173Sight).Assembly.GetManifestResourceStream("ISB.Nightmare.SightSurfaces"))
            {
                if (stream == null)
                    throw new InvalidDataException("Nightmare sight surfaces are missing.");
                using (var reader = new StreamReader(stream))
                {
                    var definitions = JsonConvert.DeserializeObject<Definition[]>(reader.ReadToEnd());
                    var result = new Dictionary<string, Definition>(StringComparer.Ordinal);
                    foreach (var definition in definitions)
                        result.Add(definition.collider, definition);
                    return result;
                }
            }
        }

        private Surfaces GetSurfaces(Collider collider)
        {
            if (_surfaces.TryGetValue(collider, out var cached))
                return cached;
            Surfaces surfaces = null;
            if (collider is MeshCollider meshCollider && Definitions.Value.TryGetValue(collider.name, out var definition))
            {
                var mesh = meshCollider.sharedMesh;
                bool researchRoom = false;
                for (var parent = collider.transform.parent; parent != null; parent = parent.parent)
                    if (parent.name == "SBG_Laboratory_Research_Test_Room")
                    {
                        researchRoom = true;
                        break;
                    }
                if (researchRoom && mesh != null && mesh.name == definition.mesh && mesh.vertexCount == definition.vertexCount)
                {
                    surfaces = new Surfaces(definition);
                }
            }
            _surfaces.Add(collider, surfaces);
            return surfaces;
        }

        private bool Transparent(RaycastHit hit)
        {
            var surfaces = GetSurfaces(hit.collider);
            if (surfaces == null)
                return false;
            var point = hit.collider.transform.InverseTransformPoint(hit.point);
            bool transparent = false;
            foreach (var surface in surfaces.transparent)
                if (surface.Contains(point))
                {
                    transparent = true;
                    break;
                }
            if (!transparent) return false;
            foreach (var surface in surfaces.opaque)
                if (surface.Contains(point)) return false;
            return true;
        }

        internal bool ClearLine(Vector3 origin, Vector3 point, int mask, Transform body)
        {
            var delta = point - origin;
            if (delta.sqrMagnitude < .001f)
                return true;
            float distance = delta.magnitude;
            var direction = delta / distance;
            for (int i = 0; i < 12; i++)
            {
                if (!Physics.Raycast(origin, direction, out var hit, distance, mask, QueryTriggerInteraction.Ignore))
                    return true;
                if (body != null && hit.transform.IsChildOf(body))
                    return true;
                if (!Transparent(hit))
                    return false;
                float step = hit.distance + .005f;
                if (step >= distance)
                    return true;
                origin += direction * step;
                distance -= step;
            }
            return false;
        }

        internal void Reset() => _surfaces.Clear();
    }
}
