using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Frostline.EditorTools
{
    /// <summary>
    /// Baukasten für Rohbau-Geometrie (Graybox): Quader, Röhren, Zylinder, Satteldächer,
    /// Bögen. Alles landet in einem Mesh pro Material, damit ein Gebäude nur wenige
    /// Draw Calls kostet. Platzhalter, bis die echten Assets eingesetzt werden.
    /// </summary>
    public sealed class MeshKit
    {
        sealed class Part
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Triangles = new List<int>();
        }

        readonly Dictionary<Material, Part> parts = new Dictionary<Material, Part>();
        readonly List<Material> order = new List<Material>();

        /// <summary>Zusätzliche Verschiebung und Drehung für alle folgenden Teile.</summary>
        public Matrix4x4 Frame = Matrix4x4.identity;

        Part Get(Material material)
        {
            Part part;
            if (!parts.TryGetValue(material, out part))
            {
                part = new Part();
                parts[material] = part;
                order.Add(material);
            }
            return part;
        }

        // ------------------------------------------------------------ Grundformen

        /// <summary>Quader um <paramref name="center"/> mit Kantenlängen <paramref name="size"/>.</summary>
        public void Box(Material material, Vector3 center, Vector3 size, Quaternion rotation)
        {
            Part part = Get(material);
            Matrix4x4 m = Frame * Matrix4x4.TRS(center, rotation, Vector3.one);
            Vector3 h = size * 0.5f;
            // Sechs Seiten, je vier eigene Eckpunkte für harte Kanten.
            Face(part, m, new Vector3(0, 0, 1), new Vector3(-h.x, -h.y, h.z), new Vector3(h.x, -h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z), size.x, size.y);
            Face(part, m, new Vector3(0, 0, -1), new Vector3(h.x, -h.y, -h.z), new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, h.y, -h.z), new Vector3(h.x, h.y, -h.z), size.x, size.y);
            Face(part, m, new Vector3(1, 0, 0), new Vector3(h.x, -h.y, h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(h.x, h.y, h.z), size.z, size.y);
            Face(part, m, new Vector3(-1, 0, 0), new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, -h.y, h.z), new Vector3(-h.x, h.y, h.z), new Vector3(-h.x, h.y, -h.z), size.z, size.y);
            Face(part, m, new Vector3(0, 1, 0), new Vector3(-h.x, h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(h.x, h.y, -h.z), new Vector3(-h.x, h.y, -h.z), size.x, size.z);
            Face(part, m, new Vector3(0, -1, 0), new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, -h.y, h.z), new Vector3(-h.x, -h.y, h.z), size.x, size.z);
        }

        public void Box(Material material, Vector3 center, Vector3 size)
        {
            Box(material, center, size, Quaternion.identity);
        }

        /// <summary>Balken von a nach b mit quadratischem Querschnitt.</summary>
        public void Beam(Material material, Vector3 a, Vector3 b, float thickness)
        {
            Vector3 d = b - a;
            if (d.sqrMagnitude < 1e-6f) return;
            Quaternion rotation = Quaternion.LookRotation(d.normalized, Mathf.Abs(Vector3.Dot(d.normalized, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up);
            Box(material, (a + b) * 0.5f, new Vector3(thickness, thickness, d.magnitude), rotation);
        }

        static void Face(Part part, Matrix4x4 m, Vector3 normal, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float uSize, float vSize)
        {
            int start = part.Vertices.Count;
            Vector3 n = m.MultiplyVector(normal).normalized;
            part.Vertices.Add(m.MultiplyPoint3x4(a));
            part.Vertices.Add(m.MultiplyPoint3x4(b));
            part.Vertices.Add(m.MultiplyPoint3x4(c));
            part.Vertices.Add(m.MultiplyPoint3x4(d));
            for (int i = 0; i < 4; i++) part.Normals.Add(n);
            // UV in Metern, damit Texturen auf allen Größen gleich groß wirken.
            part.Uvs.Add(new Vector2(0f, 0f));
            part.Uvs.Add(new Vector2(uSize, 0f));
            part.Uvs.Add(new Vector2(uSize, vSize));
            part.Uvs.Add(new Vector2(0f, vSize));
            // Unity zeigt Dreiecke, deren Ecken von vorn gesehen im Uhrzeigersinn liegen.
            part.Triangles.Add(start); part.Triangles.Add(start + 1); part.Triangles.Add(start + 2);
            part.Triangles.Add(start); part.Triangles.Add(start + 2); part.Triangles.Add(start + 3);
        }

        /// <summary>
        /// Röhre entlang der lokalen z-Achse: Ellipsen-Querschnitt mit Wandstärke, offen an beiden
        /// Enden. <paramref name="arcDegrees"/> &lt; 360 ergibt eine offene Schale (z. B. Eistunnel).
        /// </summary>
        public void Tube(Material material, Vector3 center, Quaternion rotation, float radiusX, float radiusY, float length,
            float wall, int segments = 24, float arcDegrees = 360f, float startDegrees = 0f)
        {
            Part part = Get(material);
            Matrix4x4 m = Frame * Matrix4x4.TRS(center, rotation, Vector3.one);
            bool closed = arcDegrees >= 359.9f;
            int rings = closed ? segments : segments + 1;
            float step = arcDegrees / segments * Mathf.Deg2Rad;
            float start = startDegrees * Mathf.Deg2Rad;
            float halfLength = length * 0.5f;

            for (int side = 0; side < 2; side++)
            {
                // side 0 = außen, 1 = innen
                float rx = side == 0 ? radiusX : Mathf.Max(radiusX - wall, 0.05f);
                float ry = side == 0 ? radiusY : Mathf.Max(radiusY - wall, 0.05f);
                int baseIndex = part.Vertices.Count;
                for (int i = 0; i < rings; i++)
                {
                    float a = start + i * step;
                    var local = new Vector3(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry, 0f);
                    var normal = new Vector3(Mathf.Cos(a) / rx, Mathf.Sin(a) / ry, 0f).normalized;
                    if (side == 1) normal = -normal;
                    float u = i * step * (rx + ry) * 0.5f;
                    for (int end = 0; end < 2; end++)
                    {
                        Vector3 p = local + new Vector3(0f, 0f, end == 0 ? -halfLength : halfLength);
                        part.Vertices.Add(m.MultiplyPoint3x4(p));
                        part.Normals.Add(m.MultiplyVector(normal).normalized);
                        part.Uvs.Add(new Vector2(u, end * length));
                    }
                }
                for (int i = 0; i < segments; i++)
                {
                    int i0 = baseIndex + (i % rings) * 2;
                    int i1 = baseIndex + ((i + 1) % rings) * 2;
                    if (side == 0) Quad(part, i0, i1, i1 + 1, i0 + 1);
                    else Quad(part, i0, i0 + 1, i1 + 1, i1);
                }
            }

            // Stirnflächen (Ringe) an beiden Enden, damit die Wand Dicke zeigt.
            for (int end = 0; end < 2; end++)
            {
                float z = end == 0 ? -halfLength : halfLength;
                Vector3 normal = m.MultiplyVector(end == 0 ? Vector3.back : Vector3.forward).normalized;
                for (int i = 0; i < segments; i++)
                {
                    float a0 = start + i * step;
                    float a1 = start + (i + 1) * step;
                    Vector3 o0 = new Vector3(Mathf.Cos(a0) * radiusX, Mathf.Sin(a0) * radiusY, z);
                    Vector3 o1 = new Vector3(Mathf.Cos(a1) * radiusX, Mathf.Sin(a1) * radiusY, z);
                    float irx = Mathf.Max(radiusX - wall, 0.05f);
                    float iry = Mathf.Max(radiusY - wall, 0.05f);
                    Vector3 n0 = new Vector3(Mathf.Cos(a0) * irx, Mathf.Sin(a0) * iry, z);
                    Vector3 n1 = new Vector3(Mathf.Cos(a1) * irx, Mathf.Sin(a1) * iry, z);
                    int s = part.Vertices.Count;
                    part.Vertices.Add(m.MultiplyPoint3x4(o0));
                    part.Vertices.Add(m.MultiplyPoint3x4(o1));
                    part.Vertices.Add(m.MultiplyPoint3x4(n1));
                    part.Vertices.Add(m.MultiplyPoint3x4(n0));
                    for (int k = 0; k < 4; k++)
                    {
                        part.Normals.Add(normal);
                        part.Uvs.Add(Vector2.zero);
                    }
                    if (end == 1) Quad(part, s, s + 1, s + 2, s + 3);
                    else Quad(part, s, s + 3, s + 2, s + 1);
                }
            }

            // Bei offenen Schalen auch die Längskanten schließen.
            if (!closed)
            {
                for (int edge = 0; edge < 2; edge++)
                {
                    float a = start + (edge == 0 ? 0f : arcDegrees * Mathf.Deg2Rad);
                    Vector3 outer = new Vector3(Mathf.Cos(a) * radiusX, Mathf.Sin(a) * radiusY, 0f);
                    Vector3 inner = new Vector3(Mathf.Cos(a) * Mathf.Max(radiusX - wall, 0.05f), Mathf.Sin(a) * Mathf.Max(radiusY - wall, 0.05f), 0f);
                    Vector3 tangent = new Vector3(-Mathf.Sin(a), Mathf.Cos(a), 0f) * (edge == 0 ? -1f : 1f);
                    int s = part.Vertices.Count;
                    part.Vertices.Add(m.MultiplyPoint3x4(outer + Vector3.back * halfLength));
                    part.Vertices.Add(m.MultiplyPoint3x4(outer + Vector3.forward * halfLength));
                    part.Vertices.Add(m.MultiplyPoint3x4(inner + Vector3.forward * halfLength));
                    part.Vertices.Add(m.MultiplyPoint3x4(inner + Vector3.back * halfLength));
                    Vector3 n = m.MultiplyVector(tangent).normalized;
                    for (int k = 0; k < 4; k++)
                    {
                        part.Normals.Add(n);
                        part.Uvs.Add(Vector2.zero);
                    }
                    if (edge == 0) Quad(part, s, s + 1, s + 2, s + 3);
                    else Quad(part, s, s + 3, s + 2, s + 1);
                }
            }
        }

        /// <summary>Voller Zylinder (stehend) mit Deckel, z. B. Silo, Leuchtturm, Pfahl.</summary>
        public void Cylinder(Material material, Vector3 bottomCenter, float radius, float height, int segments = 20)
        {
            Part part = Get(material);
            Matrix4x4 m = Frame * Matrix4x4.Translate(bottomCenter);
            int baseIndex = part.Vertices.Count;
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int k = 0; k < 2; k++)
                {
                    part.Vertices.Add(m.MultiplyPoint3x4(dir * radius + Vector3.up * (k * height)));
                    part.Normals.Add(m.MultiplyVector(dir).normalized);
                    part.Uvs.Add(new Vector2(a * radius, k * height));
                }
            }
            for (int i = 0; i < segments; i++)
            {
                int i0 = baseIndex + i * 2;
                int i1 = i0 + 2;
                Quad(part, i0, i0 + 1, i1 + 1, i1);
            }
            // Deckel
            int center = part.Vertices.Count;
            part.Vertices.Add(m.MultiplyPoint3x4(Vector3.up * height));
            part.Normals.Add(m.MultiplyVector(Vector3.up).normalized);
            part.Uvs.Add(Vector2.zero);
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                part.Vertices.Add(m.MultiplyPoint3x4(new Vector3(Mathf.Cos(a) * radius, height, Mathf.Sin(a) * radius)));
                part.Normals.Add(m.MultiplyVector(Vector3.up).normalized);
                part.Uvs.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            }
            for (int i = 0; i < segments; i++)
            {
                part.Triangles.Add(center);
                part.Triangles.Add(center + 2 + i);
                part.Triangles.Add(center + 1 + i);
            }
        }

        /// <summary>Kegelstumpf (stehend), z. B. Baumkrone oder Turmspitze. topRadius 0 = Kegel.</summary>
        public void Frustum(Material material, Vector3 bottomCenter, float bottomRadius, float topRadius, float height, int segments = 12)
        {
            Part part = Get(material);
            Matrix4x4 m = Frame * Matrix4x4.Translate(bottomCenter);
            float slope = Mathf.Atan2(bottomRadius - topRadius, height);
            int baseIndex = part.Vertices.Count;
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 normal = (dir * Mathf.Cos(slope) + Vector3.up * Mathf.Sin(slope)).normalized;
                part.Vertices.Add(m.MultiplyPoint3x4(dir * bottomRadius));
                part.Vertices.Add(m.MultiplyPoint3x4(dir * topRadius + Vector3.up * height));
                part.Normals.Add(m.MultiplyVector(normal).normalized);
                part.Normals.Add(m.MultiplyVector(normal).normalized);
                part.Uvs.Add(new Vector2(a * bottomRadius, 0f));
                part.Uvs.Add(new Vector2(a * bottomRadius, height));
            }
            for (int i = 0; i < segments; i++)
            {
                int i0 = baseIndex + i * 2;
                int i1 = i0 + 2;
                Quad(part, i0, i0 + 1, i1 + 1, i1);
            }
        }

        /// <summary>Liegender, (fast) massiver Zylinder von a nach b, z. B. Baumstamm oder Rohr.</summary>
        public void Log(Material material, Vector3 a, Vector3 b, float radius, int segments = 10)
        {
            Vector3 d = b - a;
            if (d.sqrMagnitude < 1e-6f) return;
            Tube(material, (a + b) * 0.5f, Quaternion.LookRotation(d.normalized), radius, radius, d.magnitude, radius * 0.95f, segments);
        }

        /// <summary>Hohler, stehender Zylinder (Schornstein, Turm zum Durchfliegen).</summary>
        public void HollowCylinder(Material material, Vector3 bottomCenter, float radius, float height, float wall, int segments = 24)
        {
            Tube(material, bottomCenter + Vector3.up * height * 0.5f, Quaternion.Euler(-90f, 0f, 0f), radius, radius, height, wall, segments);
        }

        /// <summary>Satteldach: First entlang der lokalen z-Achse.</summary>
        public void GableRoof(Material material, Vector3 eaveCenter, Quaternion rotation, float width, float length, float rise, float thickness = 0.25f)
        {
            float half = width * 0.5f;
            float slope = Mathf.Sqrt(half * half + rise * rise);
            float angle = Mathf.Atan2(rise, half) * Mathf.Rad2Deg;
            Quaternion left = rotation * Quaternion.Euler(0f, 0f, angle);
            Quaternion right = rotation * Quaternion.Euler(0f, 0f, -angle);
            Vector3 leftCenter = eaveCenter + rotation * new Vector3(-half * 0.5f, rise * 0.5f, 0f);
            Vector3 rightCenter = eaveCenter + rotation * new Vector3(half * 0.5f, rise * 0.5f, 0f);
            Box(material, leftCenter, new Vector3(slope + 0.4f, thickness, length + 0.6f), left);
            Box(material, rightCenter, new Vector3(slope + 0.4f, thickness, length + 0.6f), right);
        }

        /// <summary>Giebeldreieck als flaches Prisma (Stirnseite eines Hauses).</summary>
        public void Gable(Material material, Vector3 baseCenter, Quaternion rotation, float width, float rise, float thickness)
        {
            Part part = Get(material);
            Matrix4x4 m = Frame * Matrix4x4.TRS(baseCenter, rotation, Vector3.one);
            float h = width * 0.5f;
            float t = thickness * 0.5f;
            Vector3[] front = { new Vector3(-h, 0f, t), new Vector3(h, 0f, t), new Vector3(0f, rise, t) };
            Vector3[] back = { new Vector3(h, 0f, -t), new Vector3(-h, 0f, -t), new Vector3(0f, rise, -t) };
            Triangle(part, m, front, Vector3.forward);
            Triangle(part, m, back, Vector3.back);
        }

        static void Triangle(Part part, Matrix4x4 m, Vector3[] points, Vector3 normal)
        {
            int s = part.Vertices.Count;
            Vector3 n = m.MultiplyVector(normal).normalized;
            for (int i = 0; i < 3; i++)
            {
                part.Vertices.Add(m.MultiplyPoint3x4(points[i]));
                part.Normals.Add(n);
                part.Uvs.Add(new Vector2(points[i].x, points[i].y));
            }
            part.Triangles.Add(s);
            part.Triangles.Add(s + 1);
            part.Triangles.Add(s + 2);
        }

        /// <summary>Halbkreis-Bogen (Felsbogen, Torbogen) aus Quadersegmenten.</summary>
        public void Arch(Material material, Vector3 baseCenter, Quaternion rotation, float span, float thickness, float depth, int segments = 14)
        {
            float radius = span * 0.5f;
            Vector3 previous = baseCenter + rotation * new Vector3(-radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = Mathf.PI - i * Mathf.PI / segments;
                Vector3 p = baseCenter + rotation * new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
                Vector3 d = p - previous;
                // Lokales x zeigt entlang der Sehne, z in die Tiefe des Bogens.
                Quaternion along = Quaternion.LookRotation(rotation * Vector3.forward, Vector3.Cross(d, rotation * Vector3.forward));
                Box(material, (p + previous) * 0.5f, new Vector3(d.magnitude + thickness * 0.3f, thickness, depth), along);
                previous = p;
            }
        }

        static void Quad(Part part, int a, int b, int c, int d)
        {
            part.Triangles.Add(a); part.Triangles.Add(b); part.Triangles.Add(c);
            part.Triangles.Add(a); part.Triangles.Add(c); part.Triangles.Add(d);
        }

        // ------------------------------------------------------------ Ausgabe

        public bool IsEmpty { get { return parts.Count == 0; } }

        /// <summary>
        /// Erzeugt ein GameObject mit einem Mesh (ein Submesh pro Material), MeshCollider und
        /// speichert das Mesh als Asset, damit die Szene klein bleibt.
        /// </summary>
        public GameObject Build(string name, Transform parent, string assetFolder, bool collider = true, bool isStatic = true)
        {
            var mesh = new Mesh { name = name };
            int total = 0;
            foreach (Material material in order) total += parts[material].Vertices.Count;
            mesh.indexFormat = total > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;

            var vertices = new List<Vector3>(total);
            var normals = new List<Vector3>(total);
            var uvs = new List<Vector2>(total);
            var submeshes = new List<int[]>();
            foreach (Material material in order)
            {
                Part part = parts[material];
                int offset = vertices.Count;
                vertices.AddRange(part.Vertices);
                normals.AddRange(part.Normals);
                uvs.AddRange(part.Uvs);
                var triangles = new int[part.Triangles.Count];
                for (int i = 0; i < triangles.Length; i++) triangles[i] = part.Triangles[i] + offset;
                submeshes.Add(triangles);
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = submeshes.Count;
            for (int i = 0; i < submeshes.Count; i++) mesh.SetTriangles(submeshes[i], i);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            if (!string.IsNullOrEmpty(assetFolder))
            {
                string path = assetFolder + "/" + name + ".asset";
                if (UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) UnityEditor.AssetDatabase.DeleteAsset(path);
                UnityEditor.AssetDatabase.CreateAsset(mesh, path);
            }

            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = order.ToArray();
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            if (isStatic)
            {
                UnityEditor.GameObjectUtility.SetStaticEditorFlags(go,
                    UnityEditor.StaticEditorFlags.BatchingStatic | UnityEditor.StaticEditorFlags.OccluderStatic
                    | UnityEditor.StaticEditorFlags.OccludeeStatic | UnityEditor.StaticEditorFlags.ContributeGI
                    | UnityEditor.StaticEditorFlags.ReflectionProbeStatic);
            }
            return go;
        }
    }
}
