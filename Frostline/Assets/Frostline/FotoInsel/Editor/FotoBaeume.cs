using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Frostline.FotoInsel
{
    /// <summary>
    /// Bäume für die Foto-Insel: Fichte, Kiefer, Buche, Eiche, Birke als Prefabs mit drei LOD-Stufen.
    /// Zweige und Laub sind Alpha-Karten mit gemalten Texturen (Nadelzweig, Blattbüschel, Rinde),
    /// die Normalen zeigen von der Stammachse weg, damit die Krone weich schattiert wie echtes Laub.
    /// Dazu Gras, Farn, Heide und Büsche aus dem Terrain Sample Asset Pack mit HDRP-Materialien.
    /// </summary>
    public static class FotoBaeume
    {
        public const string Folder = FotoInselBuilder.GeneratedFolder + "/Baeume";

        public sealed class Species
        {
            public string Name;
            public GameObject Prefab;
            public float MinHeight, MaxHeight;   // Skalierung relativ zur Prefab-Höhe
        }

        static Material needles, needlesDark, needlesPine, leavesBeech, leavesOak, leavesBirch, bark, barkPine, barkBirch;
        static DiffusionProfileSettings foliageProfile;

        public static DiffusionProfileSettings FoliageProfile
        {
            get
            {
                if (foliageProfile == null)
                    foliageProfile = AssetDatabase.LoadAssetAtPath<DiffusionProfileSettings>("Assets/TerrainSampleAssets/Settings/FoliageDiffusionProfile.asset");
                return foliageProfile;
            }
        }

        // ------------------------------------------------------------ Texturen

        sealed class Canvas
        {
            public readonly int W, H;
            public readonly Color32[] Px;
            public Canvas(int w, int h) { W = w; H = h; Px = new Color32[w * h]; }

            public void Disc(float cx, float cy, float r, Color32 c)
            {
                int x0 = Mathf.Max(0, (int)(cx - r - 1)), x1 = Mathf.Min(W - 1, (int)(cx + r + 1));
                int y0 = Mathf.Max(0, (int)(cy - r - 1)), y1 = Mathf.Min(H - 1, (int)(cy + r + 1));
                float r2 = r * r;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r2) Px[y * W + x] = c;
            }

            public void Line(Vector2 a, Vector2 b, float r, Color32 c)
            {
                float len = Vector2.Distance(a, b);
                int steps = Mathf.Max(1, Mathf.CeilToInt(len / Mathf.Max(0.5f, r * 0.7f)));
                for (int i = 0; i <= steps; i++)
                {
                    Vector2 p = Vector2.Lerp(a, b, i / (float)steps);
                    Disc(p.x, p.y, r, c);
                }
            }

            public void Ellipse(Vector2 center, Vector2 axis, float length, float width, Color32 c, Color32 rib)
            {
                Vector2 side = new Vector2(-axis.y, axis.x);
                int reach = Mathf.CeilToInt(length);
                for (int y = (int)center.y - reach; y <= center.y + reach; y++)
                {
                    if (y < 0 || y >= H) continue;
                    for (int x = (int)center.x - reach; x <= center.x + reach; x++)
                    {
                        if (x < 0 || x >= W) continue;
                        Vector2 d = new Vector2(x, y) - center;
                        float u = Vector2.Dot(d, axis) / length, v = Vector2.Dot(d, side) / width;
                        // Blattform: vorn spitz, hinten rund.
                        float taper = u > 0f ? 1f - u * u * 0.9f : 1f - u * u;
                        if (u < -1f || u > 1f || v * v > taper * taper) continue;
                        bool onRib = Mathf.Abs(v) < 0.07f && u > -0.9f;
                        float shade = 0.85f + 0.3f * (v * 0.5f + 0.5f);
                        Color32 col = onRib ? rib : new Color32((byte)Mathf.Min(255, c.r * shade), (byte)Mathf.Min(255, c.g * shade), (byte)Mathf.Min(255, c.b * shade), 255);
                        Px[y * W + x] = col;
                    }
                }
            }

            /// <summary>Durchsichtige Pixel bekommen die Durchschnittsfarbe, damit Mipmaps keinen dunklen Rand haben.</summary>
            public void FillTransparent()
            {
                long r = 0, g = 0, b = 0, n = 0;
                foreach (Color32 c in Px) if (c.a > 0) { r += c.r; g += c.g; b += c.b; n++; }
                if (n == 0) return;
                var avg = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 0);
                for (int i = 0; i < Px.Length; i++) if (Px[i].a == 0) Px[i] = avg;
            }
        }

        static Color32 Vary(System.Random rnd, Color baseColor, float amount)
        {
            float k = 1f + ((float)rnd.NextDouble() - 0.5f) * 2f * amount;
            float hue = ((float)rnd.NextDouble() - 0.5f) * amount * 0.6f;
            return new Color(Mathf.Clamp01(baseColor.r * k + hue), Mathf.Clamp01(baseColor.g * k), Mathf.Clamp01(baseColor.b * k - hue * 0.5f), 1f);
        }

        static Texture2D NeedleTexture(string name, int seed, Color baseColor, Color tipColor)
        {
            var canvas = new Canvas(512, 512);
            var rnd = new System.Random(seed);
            var twig = new Color(0.27f, 0.2f, 0.13f);
            // Hauptzweig von unten (Stamm) nach oben (Spitze), leicht gebogen.
            var axis = new List<Vector2>();
            for (int i = 0; i <= 40; i++)
            {
                float t = i / 40f;
                axis.Add(new Vector2(256f + Mathf.Sin(t * 2.4f) * 14f, 8f + t * 490f));
            }
            for (int i = 0; i < axis.Count - 1; i++) canvas.Line(axis[i], axis[i + 1], 3.2f - i * 0.05f, twig);

            void Needles(Vector2 a, Vector2 b, float len, float tipBias)
            {
                Vector2 dir = (b - a).normalized;
                Vector2 side = new Vector2(-dir.y, dir.x);
                float length = Vector2.Distance(a, b);
                for (float s = 0f; s < length; s += 2.2f)
                {
                    Vector2 p = a + dir * s;
                    float t = s / length;
                    Color c = Color.Lerp(baseColor, tipColor, Mathf.Clamp01(t * tipBias + (float)rnd.NextDouble() * 0.25f));
                    for (int sideSign = -1; sideSign <= 1; sideSign += 2)
                    {
                        float angle = (55f + (float)rnd.NextDouble() * 25f) * Mathf.Deg2Rad;
                        Vector2 nd = dir * Mathf.Cos(angle) + side * sideSign * Mathf.Sin(angle);
                        float l = len * (0.75f + (float)rnd.NextDouble() * 0.5f) * (1f - t * 0.35f);
                        canvas.Line(p, p + nd * l, 1.1f, Vary(rnd, c, 0.12f));
                    }
                }
            }

            for (int i = 0; i < axis.Count - 1; i++) Needles(axis[i], axis[i + 1], 13f, 0.4f);
            int sideTwigs = 0;
            for (float y = 40f; y < 470f; y += 21f + (float)rnd.NextDouble() * 8f)
            {
                float t = y / 500f;
                int sign = (sideTwigs++ % 2 == 0) ? 1 : -1;
                Vector2 start = new Vector2(256f + Mathf.Sin(t * 2.4f) * 14f, y);
                float angle = (48f + (float)rnd.NextDouble() * 18f) * Mathf.Deg2Rad;
                float length = (190f * (1f - t) + 40f) * (0.8f + (float)rnd.NextDouble() * 0.35f);
                Vector2 dir = new Vector2(Mathf.Sin(angle) * sign, Mathf.Cos(angle));
                Vector2 end = start + dir * length;
                end.x = Mathf.Clamp(end.x, 14f, 498f);
                end.y = Mathf.Clamp(end.y, 10f, 500f);
                canvas.Line(start, end, 1.8f, twig);
                Needles(start, end, 11f, 1.1f);
                // Kleine Seitenäste zweiter Ordnung.
                for (float s = 0.3f; s < 0.85f; s += 0.28f)
                {
                    Vector2 p = Vector2.Lerp(start, end, s);
                    Vector2 d2 = (dir + new Vector2(0f, 0.6f)).normalized;
                    d2.x *= -1f;
                    Vector2 e2 = p + d2 * length * 0.28f;
                    e2.x = Mathf.Clamp(e2.x, 10f, 502f);
                    e2.y = Mathf.Clamp(e2.y, 10f, 502f);
                    Needles(p, e2, 9f, 1.3f);
                }
            }
            canvas.FillTransparent();
            return SaveTexture(canvas, name, true);
        }

        static Texture2D LeafTexture(string name, int seed, Color baseColor, Color lightColor, float leafLength, float leafWidth, int twigs)
        {
            var canvas = new Canvas(512, 512);
            var rnd = new System.Random(seed);
            var twig = new Color(0.25f, 0.2f, 0.15f);
            var rib = new Color32(70, 85, 40, 255);
            var root = new Vector2(256f, 20f);
            for (int t = 0; t < twigs; t++)
            {
                float angle = Mathf.Lerp(-70f, 70f, (t + (float)rnd.NextDouble() * 0.6f) / twigs) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
                float length = 300f + (float)rnd.NextDouble() * 160f - Mathf.Abs(angle) * 60f;
                Vector2 end = root + dir * length;
                canvas.Line(root, end, 2.2f, twig);
                int leaves = 7 + rnd.Next(6);
                for (int l = 0; l < leaves; l++)
                {
                    float s = 0.25f + 0.75f * l / leaves;
                    Vector2 p = Vector2.Lerp(root, end, s);
                    int sign = (l % 2 == 0) ? 1 : -1;
                    float la = angle + sign * (0.5f + (float)rnd.NextDouble() * 0.5f);
                    Vector2 axis = new Vector2(Mathf.Sin(la), Mathf.Cos(la));
                    float size = leafLength * (0.75f + (float)rnd.NextDouble() * 0.5f);
                    Color c = Color.Lerp(baseColor, lightColor, (float)rnd.NextDouble() * 0.7f + s * 0.2f);
                    canvas.Ellipse(p + axis * size, axis, size, leafWidth * size / leafLength, Vary(rnd, c, 0.1f), rib);
                }
                // Endblatt.
                canvas.Ellipse(end + dir * leafLength, dir, leafLength, leafWidth, Vary(rnd, lightColor, 0.1f), rib);
            }
            canvas.FillTransparent();
            return SaveTexture(canvas, name, true);
        }

        static Texture2D BarkTexture(string name, int seed, Color baseColor, bool birch)
        {
            var canvas = new Canvas(256, 512);
            var rnd = new System.Random(seed);
            for (int y = 0; y < canvas.H; y++)
            {
                for (int x = 0; x < canvas.W; x++)
                {
                    float u = x / (float)canvas.W * Mathf.PI * 2f;
                    // Waagerecht kachelbar: Rauschen auf einem Kreis abtasten.
                    float nx = Mathf.Cos(u) * 3f, nz = Mathf.Sin(u) * 3f;
                    Color c;
                    if (!birch)
                    {
                        float streak = FotoNoise.Fbm(nx * 1.8f + seed, nz * 1.8f + y * 0.012f, 4);
                        float crack = FotoNoise.Ridged(nx * 2.5f + y * 0.002f, nz * 2.5f + y * 0.02f, 3);
                        float v = 0.75f + streak * 0.35f - crack * 0.55f;
                        c = baseColor * Mathf.Clamp(v, 0.25f, 1.3f);
                    }
                    else
                    {
                        float n = FotoNoise.Fbm(nx + seed, nz + y * 0.004f, 3);
                        c = new Color(0.86f, 0.84f, 0.8f) * (0.92f + n * 0.12f);
                        float dash = FotoNoise.Perlin(nx * 6f, y * 0.25f);
                        if (dash > 0.42f) c *= 0.25f;
                        float patch = FotoNoise.Fbm(nx * 1.3f - 7f, nz * 1.3f + y * 0.01f, 3);
                        if (patch > 0.38f) c = Color.Lerp(c, new Color(0.12f, 0.11f, 0.1f), 0.85f);
                    }
                    c.a = 1f;
                    canvas.Px[y * canvas.W + x] = c;
                }
            }
            return SaveTexture(canvas, name, false);
        }

        static Texture2D SaveTexture(Canvas canvas, string name, bool alpha)
        {
            string path = Folder + "/" + name + ".png";
            var texture = new Texture2D(canvas.W, canvas.H, TextureFormat.RGBA32, false);
            texture.SetPixels32(canvas.Px);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = alpha;
            importer.mipMapsPreserveCoverage = alpha;
            importer.alphaTestReferenceValue = 0.45f;
            importer.wrapMode = alpha ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 512;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------ Materialien

        static Material FoliageMaterial(string name, Texture2D texture, Color tint, float thickness)
        {
            var material = NewLit(name);
            material.SetTexture("_BaseColorMap", texture);
            material.SetColor("_BaseColor", tint);
            material.SetFloat("_AlphaCutoffEnable", 1f);
            material.SetFloat("_AlphaCutoff", 0.42f);
            material.SetFloat("_DoubleSidedEnable", 1f);
            material.SetFloat("_DoubleSidedNormalMode", 2f);
            material.SetFloat("_CullMode", 0f);
            material.SetFloat("_CullModeForward", 0f);
            material.SetFloat("_Smoothness", 0.28f);
            material.SetFloat("_Metallic", 0f);
            material.doubleSidedGI = true;
            if (FoliageProfile != null)
            {
                material.SetMaterialType(MaterialId.LitTranslucent);
                HDMaterial.SetDiffusionProfile(material, FoliageProfile);
                material.SetFloat("_Thickness", thickness);
            }
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material BarkMaterial(string name, Texture2D texture, Color tint)
        {
            var material = NewLit(name);
            material.SetTexture("_BaseColorMap", texture);
            material.SetColor("_BaseColor", tint);
            material.SetFloat("_Smoothness", 0.12f);
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        public static Material NewLit(string name)
        {
            string path = Folder + "/M_" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("HDRP/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = Shader.Find("HDRP/Lit");
            material.enableInstancing = true;
            return material;
        }

        static void CreateMaterials()
        {
            Texture2D needleTex = NeedleTexture("T_Nadelzweig", 11, new Color(0.11f, 0.19f, 0.09f), new Color(0.22f, 0.32f, 0.12f));
            Texture2D pineTex = NeedleTexture("T_Kiefernzweig", 23, new Color(0.15f, 0.22f, 0.1f), new Color(0.3f, 0.36f, 0.15f));
            Texture2D beechTex = LeafTexture("T_Buchenlaub", 31, new Color(0.17f, 0.3f, 0.08f), new Color(0.36f, 0.5f, 0.15f), 26f, 15f, 11);
            Texture2D oakTex = LeafTexture("T_Eichenlaub", 37, new Color(0.13f, 0.24f, 0.07f), new Color(0.28f, 0.4f, 0.12f), 30f, 13f, 10);
            Texture2D birchTex = LeafTexture("T_Birkenlaub", 41, new Color(0.24f, 0.38f, 0.1f), new Color(0.45f, 0.58f, 0.2f), 17f, 12f, 13);
            Texture2D barkTex = BarkTexture("T_Rinde", 5, new Color(0.3f, 0.24f, 0.19f), false);
            Texture2D barkPineTex = BarkTexture("T_Kiefernrinde", 9, new Color(0.42f, 0.27f, 0.17f), false);
            Texture2D barkBirchTex = BarkTexture("T_Birkenrinde", 13, Color.white, true);

            needles = FoliageMaterial("Fichte_Nadeln", needleTex, new Color(0.78f, 0.86f, 0.84f), 0.2f);
            needlesDark = FoliageMaterial("Fichte_Nadeln_Dunkel", needleTex, new Color(0.6f, 0.7f, 0.72f), 0.2f);
            needlesPine = FoliageMaterial("Kiefer_Nadeln", pineTex, new Color(0.85f, 0.88f, 0.78f), 0.2f);
            leavesBeech = FoliageMaterial("Buche_Laub", beechTex, new Color(0.82f, 0.86f, 0.72f), 0.3f);
            leavesOak = FoliageMaterial("Eiche_Laub", oakTex, new Color(0.7f, 0.75f, 0.62f), 0.3f);
            leavesBirch = FoliageMaterial("Birke_Laub", birchTex, new Color(0.9f, 0.92f, 0.78f), 0.35f);
            bark = BarkMaterial("Rinde", barkTex, Color.white);
            barkPine = BarkMaterial("Kiefernrinde", barkPineTex, Color.white);
            barkBirch = BarkMaterial("Birkenrinde", barkBirchTex, Color.white);
        }

        // ------------------------------------------------------------ Geometrie

        sealed class TreeMesh
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<int> Bark = new List<int>();
            public readonly List<int> Leaves = new List<int>();

            public void Trunk(Vector3 bottom, Vector3 top, float r0, float r1, int sides, int rings, Vector3 bend)
            {
                int start = V.Count;
                float length = Vector3.Distance(bottom, top);
                Vector3 axis = (top - bottom).normalized;
                Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                Vector3 other = Vector3.Cross(axis, side);
                for (int r = 0; r <= rings; r++)
                {
                    float t = r / (float)rings;
                    Vector3 center = Vector3.Lerp(bottom, top, t) + bend * Mathf.Sin(t * Mathf.PI);
                    float radius = Mathf.Lerp(r0, r1, Mathf.Pow(t, 0.8f));
                    for (int s = 0; s <= sides; s++)
                    {
                        float a = s / (float)sides * Mathf.PI * 2f;
                        Vector3 n = side * Mathf.Cos(a) + other * Mathf.Sin(a);
                        V.Add(center + n * radius);
                        N.Add(n);
                        UV.Add(new Vector2(s / (float)sides * Mathf.Max(1f, Mathf.Round(r0 * 6f)), t * length / 2.2f));
                    }
                }
                for (int r = 0; r < rings; r++)
                {
                    for (int s = 0; s < sides; s++)
                    {
                        int a = start + r * (sides + 1) + s, b = a + sides + 1;
                        Bark.Add(a); Bark.Add(a + 1); Bark.Add(b);
                        Bark.Add(a + 1); Bark.Add(b + 1); Bark.Add(b);
                    }
                }
            }

            /// <summary>Gebogene Zweig-/Laubkarte; Textur-v läuft vom Ansatz zur Spitze.</summary>
            public void Card(Vector3 basePoint, Vector3 along, Vector3 side, float length, float width, float droop,
                Vector3 crownCenter, int segments, float radialBlend = 0.75f)
            {
                int start = V.Count;
                Vector3 cardNormal = Vector3.Cross(side, along).normalized;
                if (cardNormal.y < 0f) cardNormal = -cardNormal;
                for (int k = 0; k <= segments; k++)
                {
                    float s = k / (float)segments;
                    Vector3 c = basePoint + along * length * s + Vector3.down * droop * length * s * s;
                    Vector3 radial = c - crownCenter;
                    radial.y *= 0.6f;
                    Vector3 n = Vector3.Lerp(cardNormal, (radial.normalized + Vector3.up * 0.35f).normalized, radialBlend).normalized;
                    V.Add(c - side * width * 0.5f); N.Add(n); UV.Add(new Vector2(0f, s));
                    V.Add(c + side * width * 0.5f); N.Add(n); UV.Add(new Vector2(1f, s));
                }
                for (int k = 0; k < segments; k++)
                {
                    int a = start + k * 2;
                    Leaves.Add(a); Leaves.Add(a + 2); Leaves.Add(a + 1);
                    Leaves.Add(a + 1); Leaves.Add(a + 2); Leaves.Add(a + 3);
                }
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                if (V.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(V);
                mesh.SetNormals(N);
                mesh.SetUVs(0, UV);
                mesh.subMeshCount = 2;
                mesh.SetTriangles(Bark, 0);
                mesh.SetTriangles(Leaves, 1);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                return mesh;
            }
        }

        static Vector3 Dir(float azimuth, float elevation)
        {
            return new Vector3(Mathf.Cos(azimuth) * Mathf.Cos(elevation), Mathf.Sin(elevation), Mathf.Sin(azimuth) * Mathf.Cos(elevation));
        }

        static Vector3 SideFor(Vector3 along, float roll)
        {
            Vector3 side = Vector3.Cross(Vector3.up, along);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
            side.Normalize();
            Vector3 up = Vector3.Cross(along, side);
            return (side * Mathf.Cos(roll) + up * Mathf.Sin(roll)).normalized;
        }

        static float R(System.Random rnd, float a, float b) { return a + (float)rnd.NextDouble() * (b - a); }

        /// <summary>Fichte: kegelförmig, Quirle mit hängenden Zweigen. Höhe 1 = 25 m.</summary>
        static TreeMesh Spruce(int seed, int lod)
        {
            var rnd = new System.Random(seed);
            var m = new TreeMesh();
            float height = 25f;
            float radius = 4.4f;
            float trunkR = 0.38f;
            int sides = lod == 0 ? 8 : (lod == 1 ? 5 : 3);
            m.Trunk(Vector3.zero, new Vector3(0f, height, 0f), trunkR, 0.03f, sides, lod == 0 ? 6 : 2, Vector3.zero);
            float start = height * R(rnd, 0.1f, 0.16f);
            float step = lod == 0 ? 0.62f : (lod == 1 ? 1.35f : 2.6f);
            int perWhorl = lod == 0 ? 6 : (lod == 1 ? 4 : 3);
            int segments = lod == 0 ? 2 : 1;
            var crown = new Vector3(0f, height * 0.42f, 0f);
            float az0 = R(rnd, 0f, 6.28f);
            for (float y = start; y < height - 0.6f; y += step * R(rnd, 0.85f, 1.15f))
            {
                float t = (y - start) / (height - start);
                float reach = radius * Mathf.Pow(1f - t, 0.92f) * R(rnd, 0.85f, 1.12f) + 0.35f;
                az0 += 2.39996f;
                for (int b = 0; b < perWhorl; b++)
                {
                    float az = az0 + b * Mathf.PI * 2f / perWhorl + R(rnd, -0.25f, 0.25f);
                    float elevation = Mathf.Lerp(-0.32f, 0.15f, t) + R(rnd, -0.1f, 0.1f);
                    Vector3 along = Dir(az, elevation);
                    float length = reach / Mathf.Cos(Mathf.Abs(elevation));
                    float width = Mathf.Max(0.7f, length * 0.72f) * (lod == 2 ? 1.25f : 1f);
                    Vector3 basePoint = new Vector3(0f, y, 0f) + along * 0.15f;
                    m.Card(basePoint, along, SideFor(along, R(rnd, -0.35f, 0.35f)), length, width, Mathf.Lerp(0.22f, 0.05f, t), crown, segments, 0.55f);
                    if (lod == 0 && t < 0.85f)
                        m.Card(basePoint + along * length * 0.15f, along, SideFor(along, R(rnd, 0.9f, 1.3f)), length * 0.8f, width * 0.75f, 0.12f, crown, 1);
                }
            }
            // Wipfeltrieb.
            Vector3 topBase = new Vector3(0f, height - 1.8f, 0f);
            m.Card(topBase, Vector3.up, Vector3.right, 2.2f, 0.9f, 0f, crown, 1, 0.3f);
            m.Card(topBase, Vector3.up, Vector3.forward, 2.2f, 0.9f, 0f, crown, 1, 0.3f);
            return m;
        }

        /// <summary>Kiefer: hoher, leicht krummer Stamm, schirmartige Krone aus Nadelbüscheln.</summary>
        static TreeMesh Pine(int seed, int lod)
        {
            var rnd = new System.Random(seed);
            var m = new TreeMesh();
            float height = 22f;
            Vector3 lean = new Vector3(R(rnd, -0.8f, 0.8f), 0f, R(rnd, -0.8f, 0.8f));
            Vector3 top = new Vector3(lean.x, height, lean.z);
            m.Trunk(Vector3.zero, top, 0.34f, 0.05f, lod == 0 ? 8 : 4, lod == 0 ? 6 : 2, new Vector3(R(rnd, -0.5f, 0.5f), 0f, R(rnd, -0.5f, 0.5f)));
            var crown = new Vector3(lean.x * 0.8f, height * 0.82f, lean.z * 0.8f);
            int arms = lod == 0 ? 11 : (lod == 1 ? 7 : 5);
            int cardsPerClump = lod == 0 ? 4 : (lod == 1 ? 3 : 2);
            for (int a = 0; a < arms; a++)
            {
                float t = R(rnd, 0.62f, 0.95f);
                Vector3 origin = Vector3.Lerp(Vector3.zero, top, t);
                float az = a * 2.39996f + R(rnd, -0.3f, 0.3f);
                Vector3 dir = Dir(az, R(rnd, 0.25f, 0.6f));
                float length = Mathf.Lerp(4.2f, 1.8f, (t - 0.62f) / 0.33f) * R(rnd, 0.8f, 1.15f);
                Vector3 end = origin + dir * length;
                if (lod == 0) m.Trunk(origin, end, 0.09f, 0.03f, 4, 1, Vector3.zero);
                for (int c = 0; c < cardsPerClump; c++)
                {
                    float caz = az + R(rnd, -1.2f, 1.2f);
                    Vector3 along = Dir(caz, R(rnd, -0.05f, 0.35f));
                    float size = R(rnd, 2.2f, 3.1f) * (lod == 2 ? 1.35f : 1f);
                    m.Card(end - along * size * 0.35f, along, SideFor(along, R(rnd, -0.5f, 0.5f)), size, size * 0.85f, 0.06f, crown, 1, 0.8f);
                }
            }
            for (int c = 0; c < cardsPerClump + 1; c++)
            {
                Vector3 along = Dir(c * 2.1f, R(rnd, 0.2f, 0.7f));
                m.Card(top - along * 1.2f, along, SideFor(along, R(rnd, -0.5f, 0.5f)), 2.6f, 2.2f, 0.05f, crown, 1, 0.8f);
            }
            return m;
        }

        /// <summary>Laubbaum: kurzer Stamm, Hauptäste, ellipsoide Krone aus Blattbüscheln.</summary>
        static TreeMesh Broadleaf(int seed, int lod, float height, float crownRadius, float crownCenter, float trunkR, float cardSize)
        {
            var rnd = new System.Random(seed);
            var m = new TreeMesh();
            float fork = height * R(rnd, 0.35f, 0.45f);
            m.Trunk(Vector3.zero, new Vector3(0f, fork, 0f), trunkR, trunkR * 0.75f, lod == 0 ? 9 : 5, lod == 0 ? 4 : 1, Vector3.zero);
            var center = new Vector3(R(rnd, -0.6f, 0.6f), height * crownCenter, R(rnd, -0.6f, 0.6f));
            var radii = new Vector3(crownRadius * R(rnd, 0.9f, 1.1f), height * (1f - crownCenter) * 0.95f, crownRadius * R(rnd, 0.9f, 1.1f));
            int limbs = lod == 0 ? 5 : (lod == 1 ? 3 : 1);
            for (int l = 0; l < limbs; l++)
            {
                float az = l * 2.39996f + R(rnd, -0.3f, 0.3f);
                Vector3 target = center + new Vector3(Mathf.Cos(az) * radii.x * 0.6f, R(rnd, -0.1f, 0.4f) * radii.y, Mathf.Sin(az) * radii.z * 0.6f);
                m.Trunk(new Vector3(0f, fork - 0.4f, 0f), target, trunkR * 0.55f, trunkR * 0.15f, lod == 0 ? 6 : 4, 2,
                    new Vector3(R(rnd, -0.5f, 0.5f), 0.6f, R(rnd, -0.5f, 0.5f)));
            }
            int cards = lod == 0 ? 95 : (lod == 1 ? 34 : 13);
            float sizeScale = lod == 0 ? 1f : (lod == 1 ? 1.55f : 2.3f);
            for (int c = 0; c < cards; c++)
            {
                var dir = new Vector3((float)(rnd.NextDouble() * 2 - 1), (float)(rnd.NextDouble() * 2 - 1), (float)(rnd.NextDouble() * 2 - 1));
                if (dir.sqrMagnitude < 0.01f) dir = Vector3.up;
                dir.Normalize();
                if (dir.y < -0.55f) dir.y = -0.55f;
                float shell = Mathf.Lerp(0.55f, 1f, Mathf.Sqrt((float)rnd.NextDouble()));
                Vector3 p = center + Vector3.Scale(dir.normalized, radii) * shell;
                Vector3 outward = (p - center).normalized;
                Vector3 along = (outward + new Vector3(R(rnd, -0.6f, 0.6f), R(rnd, 0.1f, 0.7f), R(rnd, -0.6f, 0.6f))).normalized;
                float size = cardSize * R(rnd, 0.8f, 1.2f) * sizeScale;
                m.Card(p - along * size * 0.5f, along, SideFor(along, R(rnd, -0.9f, 0.9f)), size, size * 0.95f, 0.08f, center, 1, 0.62f);
            }
            return m;
        }

        static GameObject SaveTree(string name, System.Func<int, TreeMesh> build, Material barkMaterial, Material leafMaterial, float[] screenHeights)
        {
            var root = new GameObject(name);
            var lods = new LOD[3];
            for (int lod = 0; lod < 3; lod++)
            {
                Mesh mesh = build(lod).Build(name + "_LOD" + lod);
                string meshPath = Folder + "/" + name + "_LOD" + lod + ".asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null) AssetDatabase.DeleteAsset(meshPath);
                AssetDatabase.CreateAsset(mesh, meshPath);

                var child = new GameObject("LOD" + lod);
                child.transform.SetParent(root.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = new[] { barkMaterial, leafMaterial };
                renderer.shadowCastingMode = ShadowCastingMode.On;
                lods[lod] = new LOD(screenHeights[lod], new Renderer[] { renderer });
            }
            var group = root.AddComponent<LODGroup>();
            group.SetLODs(lods);
            group.fadeMode = LODFadeMode.None;
            group.RecalculateBounds();
            string path = Folder + "/" + name + ".prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>Alle Baumarten. Höhen in Metern über die Skalierung der Terrain-Bäume.</summary>
        public static List<Species> CreateAll()
        {
            FotoInselBuilder.EnsureFolder(Folder);
            CreateMaterials();
            float[] lodHeights = { 0.09f, 0.022f, 0.0015f };
            var list = new List<Species>
            {
                new Species { Name = "Fichte_A", Prefab = SaveTree("Fichte_A", lod => Spruce(101, lod), bark, needles, lodHeights), MinHeight = 0.72f, MaxHeight = 1.25f },
                new Species { Name = "Fichte_B", Prefab = SaveTree("Fichte_B", lod => Spruce(202, lod), bark, needlesDark, lodHeights), MinHeight = 0.7f, MaxHeight = 1.2f },
                new Species { Name = "Fichte_C", Prefab = SaveTree("Fichte_C", lod => Spruce(303, lod), bark, needles, lodHeights), MinHeight = 0.55f, MaxHeight = 0.9f },
                new Species { Name = "Kiefer_A", Prefab = SaveTree("Kiefer_A", lod => Pine(404, lod), barkPine, needlesPine, lodHeights), MinHeight = 0.75f, MaxHeight = 1.15f },
                new Species { Name = "Kiefer_B", Prefab = SaveTree("Kiefer_B", lod => Pine(505, lod), barkPine, needlesPine, lodHeights), MinHeight = 0.7f, MaxHeight = 1.1f },
                new Species { Name = "Buche_A", Prefab = SaveTree("Buche_A", lod => Broadleaf(606, lod, 22f, 6.8f, 0.6f, 0.42f, 2.7f), bark, leavesBeech, lodHeights), MinHeight = 0.75f, MaxHeight = 1.2f },
                new Species { Name = "Eiche_A", Prefab = SaveTree("Eiche_A", lod => Broadleaf(707, lod, 19f, 8f, 0.56f, 0.55f, 3f), bark, leavesOak, lodHeights), MinHeight = 0.75f, MaxHeight = 1.15f },
                new Species { Name = "Birke_A", Prefab = SaveTree("Birke_A", lod => Broadleaf(808, lod, 17f, 3.6f, 0.64f, 0.2f, 2f), barkBirch, leavesBirch, lodHeights), MinHeight = 0.8f, MaxHeight = 1.15f },
            };
            AssetDatabase.SaveAssets();
            return list;
        }

        // ------------------------------------------------------------ Bodenbewuchs

        static readonly Regex TexEnv = new Regex(@"- (Base_Map|Normal_Map|Mask_Map|_Thickness_Map):\s*\n\s*m_Texture: \{fileID: \d+, guid: ([0-9a-f]{32})");

        /// <summary>Prefab aus dem Terrain Sample Asset Pack mit HDRP/Lit-Material (die mitgelieferte Shader-Graph-Datei baut hier nicht).</summary>
        public static GameObject DetailPrefab(string name)
        {
            string source = "Assets/TerrainSampleAssets/Prefabs/" + name + ".prefab";
            var original = AssetDatabase.LoadAssetAtPath<GameObject>(source);
            if (original == null) return null;
            var filter = original.GetComponentInChildren<MeshFilter>();
            var renderer = original.GetComponentInChildren<MeshRenderer>();
            if (filter == null || renderer == null || renderer.sharedMaterial == null) return null;

            string materialPath = AssetDatabase.GetAssetPath(renderer.sharedMaterial);
            string yaml = File.ReadAllText(materialPath);
            Texture baseMap = null, normalMap = null, maskMap = null;
            foreach (Match match in TexEnv.Matches(yaml))
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(match.Groups[2].Value));
                if (match.Groups[1].Value == "Base_Map") baseMap = texture;
                else if (match.Groups[1].Value == "Normal_Map") normalMap = texture;
                else if (match.Groups[1].Value == "Mask_Map") maskMap = texture;
            }

            var material = NewLit("Detail_" + name);
            if (baseMap != null) material.SetTexture("_BaseColorMap", baseMap);
            if (normalMap != null) material.SetTexture("_NormalMap", normalMap);
            if (maskMap != null) material.SetTexture("_MaskMap", maskMap);
            material.SetColor("_BaseColor", name.StartsWith("GrassDry") ? new Color(1f, 0.95f, 0.85f) : Color.white);
            material.SetFloat("_AlphaCutoffEnable", 1f);
            material.SetFloat("_AlphaCutoff", 0.5f);
            material.SetFloat("_DoubleSidedEnable", 1f);
            material.SetFloat("_DoubleSidedNormalMode", 2f);
            material.SetFloat("_CullMode", 0f);
            material.SetFloat("_CullModeForward", 0f);
            material.doubleSidedGI = true;
            if (FoliageProfile != null)
            {
                material.SetMaterialType(MaterialId.LitTranslucent);
                HDMaterial.SetDiffusionProfile(material, FoliageProfile);
                material.SetFloat("_Thickness", 0.5f);
            }
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);

            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, Folder + "/Detail_" + name + ".prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }
    }
}
