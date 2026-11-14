using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Frostline.FotoInsel
{
    /// <summary>
    /// Geometrie-Baukasten für große Bauten (Hallen, Türme, Kräne, Schiffe). UVs in Metern, damit die
    /// Poly-Haven-Materialien in echter Größe kacheln. Ein Mesh pro Gruppe, ein Submesh pro Material.
    /// </summary>
    public sealed class FotoKit
    {
        sealed class Part
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<int> T = new List<int>();
        }

        readonly Dictionary<Material, Part> parts = new Dictionary<Material, Part>();
        readonly List<Material> order = new List<Material>();
        public Matrix4x4 Frame = Matrix4x4.identity;
        public int VertexCount;

        Part Get(Material m)
        {
            Part p;
            if (!parts.TryGetValue(m, out p))
            {
                p = new Part();
                parts[m] = p;
                order.Add(m);
            }
            return p;
        }

        void Quad(Part p, Matrix4x4 m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            Vector3 wa = m.MultiplyPoint3x4(a), wb = m.MultiplyPoint3x4(b), wc = m.MultiplyPoint3x4(c), wd = m.MultiplyPoint3x4(d);
            Vector3 n = Vector3.Cross(wb - wa, wd - wa).normalized;
            int s = p.V.Count;
            p.V.Add(wa); p.V.Add(wb); p.V.Add(wc); p.V.Add(wd);
            p.N.Add(n); p.N.Add(n); p.N.Add(n); p.N.Add(n);
            p.UV.Add(ua); p.UV.Add(ub); p.UV.Add(uc); p.UV.Add(ud);
            p.T.Add(s); p.T.Add(s + 1); p.T.Add(s + 3);
            p.T.Add(s + 1); p.T.Add(s + 2); p.T.Add(s + 3);
            VertexCount += 4;
        }

        void Tri(Part p, Matrix4x4 m, Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            Vector3 wa = m.MultiplyPoint3x4(a), wb = m.MultiplyPoint3x4(b), wc = m.MultiplyPoint3x4(c);
            Vector3 n = Vector3.Cross(wb - wa, wc - wa).normalized;
            int s = p.V.Count;
            p.V.Add(wa); p.V.Add(wb); p.V.Add(wc);
            p.N.Add(n); p.N.Add(n); p.N.Add(n);
            p.UV.Add(ua); p.UV.Add(ub); p.UV.Add(uc);
            p.T.Add(s); p.T.Add(s + 1); p.T.Add(s + 2);
            VertexCount += 3;
        }

        /// <summary>Quader; <paramref name="bottom"/> ist die Mitte der Unterseite.</summary>
        public void Box(Material mat, Vector3 bottom, Vector3 size, float yaw = 0f, bool skipBottom = true)
        {
            BoxRot(mat, bottom, size, Quaternion.Euler(0f, yaw, 0f), skipBottom);
        }

        public void BoxRot(Material mat, Vector3 bottom, Vector3 size, Quaternion rotation, bool skipBottom = true)
        {
            Part p = Get(mat);
            Matrix4x4 m = Frame * Matrix4x4.TRS(bottom, rotation, Vector3.one);
            float x = size.x * 0.5f, z = size.z * 0.5f, y = size.y;
            // Vorder-/Rückseite (z), Seiten (x), Deckel, Boden. UV in Metern.
            Quad(p, m, new Vector3(-x, 0, z), new Vector3(x, 0, z), new Vector3(x, y, z), new Vector3(-x, y, z), new Vector2(0, 0), new Vector2(size.x, 0), new Vector2(size.x, y), new Vector2(0, y));
            Quad(p, m, new Vector3(x, 0, -z), new Vector3(-x, 0, -z), new Vector3(-x, y, -z), new Vector3(x, y, -z), new Vector2(0, 0), new Vector2(size.x, 0), new Vector2(size.x, y), new Vector2(0, y));
            Quad(p, m, new Vector3(x, 0, z), new Vector3(x, 0, -z), new Vector3(x, y, -z), new Vector3(x, y, z), new Vector2(0, 0), new Vector2(size.z, 0), new Vector2(size.z, y), new Vector2(0, y));
            Quad(p, m, new Vector3(-x, 0, -z), new Vector3(-x, 0, z), new Vector3(-x, y, z), new Vector3(-x, y, -z), new Vector2(0, 0), new Vector2(size.z, 0), new Vector2(size.z, y), new Vector2(0, y));
            Quad(p, m, new Vector3(-x, y, z), new Vector3(x, y, z), new Vector3(x, y, -z), new Vector3(-x, y, -z), new Vector2(0, size.z), new Vector2(size.x, size.z), new Vector2(size.x, 0), new Vector2(0, 0));
            if (!skipBottom)
                Quad(p, m, new Vector3(-x, 0, -z), new Vector3(x, 0, -z), new Vector3(x, 0, z), new Vector3(-x, 0, z), new Vector2(0, 0), new Vector2(size.x, 0), new Vector2(size.x, size.z), new Vector2(0, size.z));
        }

        /// <summary>Balken mit quadratischem Querschnitt von a nach b.</summary>
        public void Beam(Material mat, Vector3 a, Vector3 b, float thickness)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return;
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, d / len);
            BoxRot(mat, a, new Vector3(thickness, len, thickness), rot, false);
        }

        /// <summary>Rotationskörper: Profil aus Höhen und Radien (stehend), optional mit Deckel.</summary>
        public void Revolve(Material mat, Vector3 baseCenter, float[] heights, float[] radii, int segments, bool capTop, float uvScale = 1f)
        {
            Part p = Get(mat);
            Matrix4x4 m = Frame * Matrix4x4.Translate(baseCenter);
            for (int r = 0; r < heights.Length - 1; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    float a0 = s / (float)segments * Mathf.PI * 2f, a1 = (s + 1) / (float)segments * Mathf.PI * 2f;
                    Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    Vector3 b0 = d0 * radii[r] + Vector3.up * heights[r], b1 = d1 * radii[r] + Vector3.up * heights[r];
                    Vector3 t0 = d0 * radii[r + 1] + Vector3.up * heights[r + 1], t1 = d1 * radii[r + 1] + Vector3.up * heights[r + 1];
                    float u0 = a0 * radii[r] * uvScale, u1 = a1 * radii[r] * uvScale;
                    int start = p.V.Count;
                    Vector3 wb0 = m.MultiplyPoint3x4(b0), wb1 = m.MultiplyPoint3x4(b1), wt0 = m.MultiplyPoint3x4(t0), wt1 = m.MultiplyPoint3x4(t1);
                    float slope = (radii[r] - radii[r + 1]) / Mathf.Max(0.01f, heights[r + 1] - heights[r]);
                    Vector3 n0 = Frame.MultiplyVector((d0 + Vector3.up * slope).normalized).normalized;
                    Vector3 n1 = Frame.MultiplyVector((d1 + Vector3.up * slope).normalized).normalized;
                    p.V.Add(wb0); p.V.Add(wb1); p.V.Add(wt1); p.V.Add(wt0);
                    p.N.Add(n0); p.N.Add(n1); p.N.Add(n1); p.N.Add(n0);
                    p.UV.Add(new Vector2(u0, heights[r] * uvScale)); p.UV.Add(new Vector2(u1, heights[r] * uvScale));
                    p.UV.Add(new Vector2(u1, heights[r + 1] * uvScale)); p.UV.Add(new Vector2(u0, heights[r + 1] * uvScale));
                    p.T.Add(start); p.T.Add(start + 2); p.T.Add(start + 1);
                    p.T.Add(start); p.T.Add(start + 3); p.T.Add(start + 2);
                    VertexCount += 4;
                }
            }
            if (capTop && radii[radii.Length - 1] > 0.01f)
            {
                float h = heights[heights.Length - 1], r = radii[radii.Length - 1];
                for (int s = 0; s < segments; s++)
                {
                    float a0 = s / (float)segments * Mathf.PI * 2f, a1 = (s + 1) / (float)segments * Mathf.PI * 2f;
                    Vector3 c = Vector3.up * h;
                    Vector3 e0 = new Vector3(Mathf.Cos(a0) * r, h, Mathf.Sin(a0) * r), e1 = new Vector3(Mathf.Cos(a1) * r, h, Mathf.Sin(a1) * r);
                    Tri(p, m, c, e1, e0, new Vector2(0, 0), new Vector2(e1.x, e1.z), new Vector2(e0.x, e0.z));
                }
            }
        }

        public void Cylinder(Material mat, Vector3 baseCenter, float radius, float height, int segments, bool cap = true)
        {
            Revolve(mat, baseCenter, new[] { 0f, height }, new[] { radius, radius }, segments, cap);
        }

        public void Sphere(Material mat, Vector3 center, float radius, int segments)
        {
            int rings = Mathf.Max(6, segments / 2);
            var h = new float[rings + 1];
            var r = new float[rings + 1];
            for (int i = 0; i <= rings; i++)
            {
                float a = -Mathf.PI / 2f + Mathf.PI * i / rings;
                h[i] = Mathf.Sin(a) * radius + radius;
                r[i] = Mathf.Max(0.001f, Mathf.Cos(a) * radius);
            }
            Revolve(mat, center - Vector3.up * radius, h, r, segments, false);
        }

        /// <summary>Halde: langgezogener Wall mit dreieckigem Querschnitt und abgeschrägten Enden.</summary>
        public void Heap(Material mat, Vector3 bottom, float length, float width, float height, float yaw)
        {
            Part p = Get(mat);
            Matrix4x4 m = Frame * Matrix4x4.TRS(bottom, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
            float l = length * 0.5f, w = width * 0.5f, r = l - height * 1.4f;
            Vector3 a = new Vector3(-l, 0, -w), b = new Vector3(l, 0, -w), c = new Vector3(l, 0, w), d = new Vector3(-l, 0, w);
            Vector3 t0 = new Vector3(-r, height, 0), t1 = new Vector3(r, height, 0);
            float s = Mathf.Sqrt(w * w + height * height);
            Quad(p, m, b, a, t0, t1, new Vector2(0, 0), new Vector2(length, 0), new Vector2(length - height * 1.4f, s), new Vector2(height * 1.4f, s));
            Quad(p, m, d, c, t1, t0, new Vector2(0, 0), new Vector2(length, 0), new Vector2(length - height * 1.4f, s), new Vector2(height * 1.4f, s));
            Tri(p, m, c, b, t1, new Vector2(0, 0), new Vector2(width, 0), new Vector2(w, s));
            Tri(p, m, a, d, t0, new Vector2(0, 0), new Vector2(width, 0), new Vector2(w, s));
        }

        /// <summary>Satteldach über einer Grundfläche (First entlang lokal x).</summary>
        public void GableRoof(Material roof, Material wall, Vector3 eaveCenter, float length, float width, float rise, float overhang, float yaw)
        {
            Part p = Get(roof);
            Matrix4x4 m = Frame * Matrix4x4.TRS(eaveCenter, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
            float l = length * 0.5f + overhang, w = width * 0.5f + overhang;
            float drop = overhang * rise / (width * 0.5f);
            Vector3 r0 = new Vector3(-l, rise, 0), r1 = new Vector3(l, rise, 0);
            Vector3 e0 = new Vector3(-l, -drop, -w), e1 = new Vector3(l, -drop, -w), e2 = new Vector3(l, -drop, w), e3 = new Vector3(-l, -drop, w);
            float s = Mathf.Sqrt(w * w + (rise + drop) * (rise + drop));
            Quad(p, m, e1, e0, r0, r1, new Vector2(0, 0), new Vector2(length, 0), new Vector2(length, s), new Vector2(0, s));
            Quad(p, m, e3, e2, r1, r0, new Vector2(0, 0), new Vector2(length, 0), new Vector2(length, s), new Vector2(0, s));
            Part g = Get(wall);
            float wi = width * 0.5f;
            Tri(g, m, new Vector3(l - overhang, 0, wi), new Vector3(l - overhang, 0, -wi), new Vector3(l - overhang, rise, 0), new Vector2(0, 0), new Vector2(width, 0), new Vector2(wi, rise));
            Tri(g, m, new Vector3(-l + overhang, 0, -wi), new Vector3(-l + overhang, 0, wi), new Vector3(-l + overhang, rise, 0), new Vector2(0, 0), new Vector2(width, 0), new Vector2(wi, rise));
        }

        /// <summary>Sheddach (Sägezahn) über einer Halle, Zähne quer zu lokal x.</summary>
        public void SawtoothRoof(Material roof, Material glass, Vector3 eaveCenter, float length, float width, float tooth, float rise, float yaw)
        {
            Part pr = Get(roof), pg = Get(glass);
            Matrix4x4 m = Frame * Matrix4x4.TRS(eaveCenter, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
            int count = Mathf.Max(1, Mathf.RoundToInt(length / tooth));
            float step = length / count, w = width * 0.5f;
            for (int i = 0; i < count; i++)
            {
                float x0 = -length * 0.5f + i * step, x1 = x0 + step;
                float slope = Mathf.Sqrt(step * step + rise * rise);
                Quad(pr, m, new Vector3(x0, 0, -w), new Vector3(x0, 0, w), new Vector3(x1, rise, w), new Vector3(x1, rise, -w),
                    new Vector2(0, 0), new Vector2(width, 0), new Vector2(width, slope), new Vector2(0, slope));
                Quad(pg, m, new Vector3(x1, 0, w), new Vector3(x1, 0, -w), new Vector3(x1, rise, -w), new Vector3(x1, rise, w),
                    new Vector2(0, 0), new Vector2(width, 0), new Vector2(width, rise), new Vector2(0, rise));
                Tri(pr, m, new Vector3(x0, 0, w), new Vector3(x1, 0, w), new Vector3(x1, rise, w), new Vector2(0, 0), new Vector2(step, 0), new Vector2(step, rise));
                Tri(pr, m, new Vector3(x0, 0, -w), new Vector3(x1, rise, -w), new Vector3(x1, 0, -w), new Vector2(0, 0), new Vector2(step, rise), new Vector2(step, 0));
            }
        }

        /// <summary>Durchhängendes Seil (Stromleitung) als dünner Balkenzug.</summary>
        public void Cable(Material mat, Vector3 a, Vector3 b, float sag, float thickness, int segments)
        {
            Vector3 prev = a;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector3 p = Vector3.Lerp(a, b, t) + Vector3.down * sag * 4f * t * (1f - t);
                Beam(mat, prev, p, thickness);
                prev = p;
            }
        }

        public bool IsEmpty { get { return order.Count == 0; } }

        public GameObject Build(string name, Transform parent, string folder, bool castShadows = true)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var subs = new List<int[]>();
            foreach (Material mat in order)
            {
                Part p = parts[mat];
                int offset = v.Count;
                v.AddRange(p.V); n.AddRange(p.N); uv.AddRange(p.UV);
                var t = new int[p.T.Count];
                for (int i = 0; i < t.Length; i++) t[i] = p.T[i] + offset;
                subs.Add(t);
            }
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) mesh.SetTriangles(subs[i], i);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            string path = folder + "/" + name + ".asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = order.ToArray();
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
            parts.Clear();
            order.Clear();
            VertexCount = 0;
            return go;
        }
    }
}
