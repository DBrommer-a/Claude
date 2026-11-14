using System.Collections.Generic;
using Frostline.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace Frostline.FotoInsel
{
    /// <summary>
    /// Zusätze für die Verlassene Insel, ohne die Welt neu zu bauen:
    /// Wasserfälle an den steilsten Flussstufen (Wasservorhang mit fließendem Schaum, Gischt,
    /// volumetrischer Sprühnebel, Rauschen, Sprungpunkt) und Vogelschwärme (Möwen an der Küste,
    /// Krähen über dem Dorf, Schwalben am Fluss, ein Adlerpaar am Gipfel).
    /// </summary>
    public static class FotoZusaetze
    {
        public const string RootName = "Zusätze (Wasserfälle, Vögel)";
        static string Folder { get { return FotoInselBuilder.GeneratedFolder + "/Verlassen/Zusaetze"; } }

        const float MinDrop = 4f;           // m Gefälle …
        const float Window = 22f;           // … auf so viel waagerechter Strecke
        const float MinSpacing = 220f;      // Abstand zwischen zwei Wasserfällen
        const int MaxWaterfalls = 6;

        public static string Add(Scene scene)
        {
            foreach (GameObject r in scene.GetRootGameObjects())
                if (r.name == RootName) Object.DestroyImmediate(r);
            Transform checkpoints = null;
            foreach (GameObject r in scene.GetRootGameObjects())
                if (r.name == "Checkpoints (Spiel)") checkpoints = r.transform;

            FotoInselBuilder.EnsureFolder(Folder);
            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);

            Material water = CurtainMaterial();
            Material spray = SprayMaterial();
            var falls = new List<Fall>();
            foreach (Fall f in FindFalls()) falls.Add(f);
            falls.Sort((a, b) => b.Drop.CompareTo(a.Drop));
            var chosen = new List<Fall>();
            foreach (Fall f in falls)
            {
                bool near = false;
                foreach (Fall c in chosen) if (Vector3.Distance(c.Lip, f.Lip) < MinSpacing) near = true;
                if (!near) chosen.Add(f);
                if (chosen.Count >= MaxWaterfalls) break;
            }
            for (int i = 0; i < chosen.Count; i++)
                BuildWaterfall(root.transform, checkpoints, chosen[i], i + 1, water, spray);

            int flocks = AddBirds(root.transform);
            AssetDatabase.SaveAssets();

            var drops = new List<string>();
            foreach (Fall f in chosen) drops.Add(f.Drop.ToString("0") + " m");
            return "Zusätze: " + chosen.Count + " Wasserfälle (" + string.Join(", ", drops) + "), " + flocks + " Vogelschwärme.\n";
        }

        // ------------------------------------------------------------ Flussstufen finden

        struct Fall
        {
            public Vector3 Lip, Base;
            public float Drop, Width;
            /// <summary>Mittellinie und Breite des Flusses von der Kante bis etwas hinter das Becken.</summary>
            public List<Vector3> Path;
            public List<float> Widths;
        }

        /// <summary>Liest die Fluss-Meshes (je Stützstelle zwei Uferpunkte) und sucht steile Stufen.</summary>
        static IEnumerable<Fall> FindFalls()
        {
            GameObject rivers = GameObject.Find("Flüsse");
            if (rivers == null) yield break;
            foreach (MeshFilter filter in rivers.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || mesh.vertexCount < 8) continue;
                Vector3[] v = mesh.vertices;
                int n = v.Length / 2;
                var center = new Vector3[n];
                var half = new float[n];
                for (int i = 0; i < n; i++)
                {
                    Vector3 l = filter.transform.TransformPoint(v[2 * i]), r = filter.transform.TransformPoint(v[2 * i + 1]);
                    center[i] = (l + r) * 0.5f;
                    half[i] = Vector3.Distance(l, r) * 0.5f;
                }
                // Von der Quelle zur Mündung.
                if (center[0].y < center[n - 1].y) { System.Array.Reverse(center); System.Array.Reverse(half); }

                for (int i = 0; i < n; i++)
                {
                    int j = i + 1;
                    while (j < n && Flat(center[j] - center[i]) < Window) j++;
                    if (j >= n) break;
                    float drop = center[i].y - center[j].y;
                    if (drop < MinDrop || center[j].y < 1f) continue;
                    var path = new List<Vector3>();
                    var widths = new List<float>();
                    for (int k = i; k <= Mathf.Min(n - 1, j + 3); k++)
                    {
                        path.Add(center[k]);
                        widths.Add(Mathf.Max(4f, half[k] * 2f));
                    }
                    yield return new Fall { Lip = center[i], Base = center[j], Drop = drop, Width = Mathf.Max(4f, half[i] * 2f), Path = path, Widths = widths };
                }
            }
        }

        static float Flat(Vector3 d) { d.y = 0f; return d.magnitude; }

        // ------------------------------------------------------------ Wasserfall bauen

        static void BuildWaterfall(Transform parent, Transform checkpoints, Fall f, int number, Material water, Material spray)
        {
            var go = new GameObject("Wasserfall " + number + " (" + f.Drop.ToString("0") + " m)");
            go.transform.SetParent(parent, false);
            go.transform.position = f.Base;

            Vector3 flow = f.Base - f.Lip;
            flow.y = 0f;
            flow = flow.sqrMagnitude > 0.01f ? flow.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, flow).normalized;

            // Wasservorhang: Wurfparabel von der Kante zum Becken, unten etwas breiter.
            var curtain = new GameObject("Wasservorhang");
            curtain.transform.SetParent(go.transform, false);
            curtain.transform.position = Vector3.zero;   // Mesh liegt in Weltkoordinaten
            Mesh mesh = Curtain(f, flow, side, number);
            curtain.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = curtain.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = water;
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            // Schaumteller im Becken.
            var pool = new GameObject("Schaum im Becken");
            pool.transform.SetParent(go.transform, false);
            pool.transform.position = f.Base + Vector3.up * 0.25f;
            pool.AddComponent<MeshFilter>().sharedMesh = Pool(f.Width * 0.9f + 2f, number);
            var poolRenderer = pool.AddComponent<MeshRenderer>();
            poolRenderer.sharedMaterial = water;
            poolRenderer.shadowCastingMode = ShadowCastingMode.Off;

            // Gischt: feine Tropfen, die hochspritzen und kleiner werden (HDRP/Unlit kennt kein Ausblenden über Partikelfarbe).
            var sprayGo = new GameObject("Gischt");
            sprayGo.transform.SetParent(go.transform, false);
            sprayGo.transform.position = f.Base + Vector3.up * 0.5f;
            sprayGo.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            ParticleSystem ps = sprayGo.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 4f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 5f + f.Drop * 0.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.9f);
            main.gravityModifier = 0.35f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1500;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 110f + f.Width * 14f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = f.Width * 0.35f;
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0f)));
            var psr = sprayGo.GetComponent<ParticleSystemRenderer>();
            psr.sharedMaterial = spray;
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            psr.shadowCastingMode = ShadowCastingMode.Off;

            // Sprühnebel: volumetrisch, von der Sonne beleuchtet (Lichtstrahlen im Gegenlicht).
            Mist(go.transform, "Sprühnebel am Becken", f.Base + Vector3.up * (f.Drop * 0.2f + 2f),
                new Vector3(f.Width * 2.6f + 14f, f.Drop * 0.5f + 10f, f.Width * 2.6f + 14f), 12f);
            Mist(go.transform, "Sprühnebel am Fall", Vector3.Lerp(f.Lip, f.Base, 0.55f) + Vector3.up * 1.5f,
                new Vector3(f.Width * 1.8f + 6f, f.Drop * 0.8f + 6f, Flat(f.Base - f.Lip) + 8f), 30f);

            WaterfallFlow flowComponent = go.AddComponent<WaterfallFlow>();
            flowComponent.curtain = renderer;
            flowComponent.height = f.Drop;

            // Sprungpunkt (F1 › Checkpoints) und Bildstandpunkt: schräg vor dem Fall, Blick hinein.
            if (checkpoints != null)
            {
                Vector3 look = -flow;
                Vector3 eye = f.Base + flow * 45f + Vector3.up * (f.Drop * 0.5f + 8f) + side * 12f;
                Vector3 target = Vector3.Lerp(f.Lip, f.Base, 0.5f);
                Vector3 dir = Vector3.ProjectOnPlane(target - eye, Vector3.up).normalized;
                if (dir.sqrMagnitude < 0.01f) dir = look;
                // Der Bildlauf setzt die Kamera auf Punkt + 25 m hoch − 40 m zurück und schaut 80 m voraus.
                var point = new GameObject("Wasserfall " + number).transform;
                point.SetParent(checkpoints, false);
                point.SetPositionAndRotation(eye - Vector3.up * 25f + dir * 40f, Quaternion.LookRotation(dir));
            }
        }

        /// <summary>
        /// Wildwasser-Vorhang, der dem Flussbett folgt (knapp über der Wasserfläche), damit er in der
        /// Schlucht liegt statt darüber zu schweben. Etwas breiter als der Fluss, damit die Ufer nass wirken.
        /// </summary>
        static Mesh Curtain(Fall f, Vector3 flow, Vector3 side, int number)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float length = 0f;
            int count = f.Path.Count;
            for (int k = 0; k < count; k++)
            {
                Vector3 c = f.Path[k] + Vector3.up * 0.3f;
                Vector3 ahead = f.Path[Mathf.Min(count - 1, k + 1)] - f.Path[Mathf.Max(0, k - 1)];
                ahead.y = 0f;
                Vector3 across = ahead.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, ahead.normalized) : side;
                if (k > 0) length += Vector3.Distance(f.Path[k - 1], f.Path[k]);
                float w = f.Widths[k] * 0.55f;
                vertices.Add(c - across * w);
                vertices.Add(c + across * w);
                uvs.Add(new Vector2(0f, -length / 3.5f));
                uvs.Add(new Vector2(f.Widths[k] / 3f, -length / 3.5f));
                if (k > 0)
                {
                    int a = (k - 1) * 2;
                    triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
                }
            }
            var mesh = new Mesh { name = "Wasservorhang_" + number };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return Save(mesh, "Wasservorhang_" + number);
        }

        static Mesh Pool(float radius, int number)
        {
            const int segments = 24;
            var vertices = new List<Vector3> { Vector3.zero };
            var uvs = new List<Vector2> { new Vector2(0.5f, 0f) };
            var triangles = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                vertices.Add(new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
                // v wächst nach außen: die fließende Textur lässt Schaumringe nach außen laufen.
                uvs.Add(new Vector2((float)i / segments * 4f, -radius / 6f));
                if (i > 0) triangles.AddRange(new[] { 0, i + 1, i });
            }
            var mesh = new Mesh { name = "Beckenschaum_" + number };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return Save(mesh, "Beckenschaum_" + number);
        }

        static void Mist(Transform parent, string name, Vector3 position, Vector3 size, float meanFreePath)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var fog = go.AddComponent<LocalVolumetricFog>();
            LocalVolumetricFogArtistParameters a = fog.parameters;
            a.size = size;
            a.meanFreePath = meanFreePath;
            a.albedo = new Color(0.96f, 0.98f, 1f);
            a.anisotropy = 0.6f;
            a.positiveFade = new Vector3(0.35f, 0.5f, 0.35f);
            a.negativeFade = new Vector3(0.35f, 0.15f, 0.35f);
            fog.parameters = a;
        }

        // ------------------------------------------------------------ Materialien und Texturen

        static Material CurtainMaterial()
        {
            Texture2D streaks = SaveTexture(StreakTexture(), "T_Wasserfall_Schaum");
            Material m = LitMaterial("M_Wasserfall", new Color(0.82f, 0.9f, 0.93f, 0.7f), 0.9f);
            m.SetTexture("_BaseColorMap", streaks);
            HDMaterial.SetSurfaceType(m, true);
            m.SetFloat("_DoubleSidedEnable", 1f);
            m.SetFloat("_CullMode", 0f);
            m.SetFloat("_CullModeForward", 0f);
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material SprayMaterial()
        {
            Texture2D dot = SaveTexture(DotTexture(), "T_Gischt_Tropfen");
            string path = Folder + "/M_Gischt.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("HDRP/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("HDRP/Unlit");
            m.SetTexture("_UnlitColorMap", dot);
            m.SetColor("_UnlitColor", new Color(0.92f, 0.95f, 1f, 0.75f));
            HDMaterial.SetSurfaceType(m, true);
            m.enableInstancing = true;
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material LitMaterial(string name, Color color, float smoothness)
        {
            string path = Folder + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("HDRP/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("HDRP/Lit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Senkrechte Schaumstreifen, oben und unten nahtlos (die Textur läuft nach unten).</summary>
        static Texture2D StreakTexture()
        {
            const int w = 128, h = 256;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "Wasserfall_Schaum" };
            var pixels = new Color[w * h];
            var random = new System.Random(11);
            var columnPhase = new float[w];
            var columnStrength = new float[w];
            for (int x = 0; x < w; x++)
            {
                columnPhase[x] = (float)random.NextDouble() * Mathf.PI * 2f;
                columnStrength[x] = 0.35f + 0.65f * (float)random.NextDouble();
            }
            for (int y = 0; y < h; y++)
            {
                float v = (float)y / h * Mathf.PI * 2f;
                for (int x = 0; x < w; x++)
                {
                    // Benachbarte Spalten mischen: weiche Bänder statt einzelner Pixelstreifen.
                    float band = 0f;
                    for (int k = -2; k <= 2; k++)
                    {
                        int xx = (x + k + w) % w;
                        band += columnStrength[xx] * (0.6f + 0.4f * Mathf.Sin(v * 3f + columnPhase[xx]) * Mathf.Sin(v * 7f + columnPhase[xx] * 1.7f));
                    }
                    band /= 5f;
                    // Kräftiger Kontrast: klare, dunkle Wasserbahnen zwischen weißem Schaum.
                    float foam = Mathf.Clamp01((band - 0.25f) / 0.5f);
                    foam = foam * foam * (3f - 2f * foam);
                    float a = Mathf.Lerp(0.18f, 0.97f, foam);
                    float white = Mathf.Lerp(0.55f, 1f, foam);
                    pixels[y * w + x] = new Color(white, white, white, a);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply(true);
            return tex;
        }

        static Texture2D DotTexture()
        {
            const int s = 64;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "Gischt_Tropfen" };
            var pixels = new Color[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f) / s * 2f - 1f, dy = (y + 0.5f) / s * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * s + x] = new Color(1f, 1f, 1f, a * a);
                }
            tex.SetPixels(pixels);
            tex.Apply(true);
            return tex;
        }

        static Texture2D SaveTexture(Texture2D tex, string name)
        {
            string path = Folder + "/" + name + ".asset";
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        static Mesh Save(Mesh mesh, string name)
        {
            string path = Folder + "/" + name + ".asset";
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // ------------------------------------------------------------ Vögel

        static int AddBirds(Transform parent)
        {
            int count = 0;
            Transform village = Viewpoint("Dorfgasse");
            Transform beach = Viewpoint("Traumstrand");
            Transform river = Viewpoint("Dorf am Fluss");
            if (beach != null && Flock(parent, "Möwen über der Küste", Ground(beach.position), LitMaterial("M_Moewe", new Color(0.86f, 0.87f, 0.88f), 0.3f),
                    12, 1.3f, 110f, new Vector2(15f, 45f), 10f, 2.5f, 0.65f, 3)) count++;
            if (village != null && Flock(parent, "Krähen über dem Dorf", Ground(village.position), LitMaterial("M_Kraehe", new Color(0.04f, 0.04f, 0.045f), 0.35f),
                    9, 0.9f, 70f, new Vector2(25f, 55f), 9f, 4f, 0.35f, 5)) count++;
            if (river != null && Flock(parent, "Schwalben am Fluss", Ground(river.position), LitMaterial("M_Schwalbe", new Color(0.07f, 0.09f, 0.14f), 0.4f),
                    10, 0.35f, 45f, new Vector2(4f, 16f), 14f, 6f, 0.2f, 7)) count++;
            Vector3 peak;
            if (Peak(out peak) && Flock(parent, "Adlerpaar am Gipfel", peak - Vector3.up * 150f, LitMaterial("M_Adler", new Color(0.13f, 0.1f, 0.075f), 0.3f),
                    2, 2.2f, 170f, new Vector2(60f, 140f), 13f, 1.5f, 0.85f, 9)) count++;
            return count;
        }

        static bool Flock(Transform parent, string name, Vector3 center, Material material, int count, float wingspan,
                          float radius, Vector2 heights, float speed, float flapRate, float glide, int seed)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            BirdFlock flock = go.AddComponent<BirdFlock>();
            flock.material = material;
            flock.count = count;
            flock.wingspan = wingspan;
            flock.radius = radius;
            flock.heightRange = heights;
            flock.speed = speed;
            flock.flapRate = flapRate;
            flock.glide = glide;
            flock.seed = seed;
            return true;
        }

        static Transform Viewpoint(string name)
        {
            GameObject views = GameObject.Find("Fotostandpunkte");
            if (views == null) return null;
            foreach (Transform v in views.transform)
                if (v.name.Contains(name)) return v;
            return null;
        }

        static Vector3 Ground(Vector3 p)
        {
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(p.x, 4000f, p.z), Vector3.down, out hit, 5000f, ~0, QueryTriggerInteraction.Ignore))
                return new Vector3(p.x, Mathf.Max(hit.point.y, 0f), p.z);
            return new Vector3(p.x, 0f, p.z);
        }

        static bool Peak(out Vector3 peak)
        {
            peak = Vector3.zero;
            float best = float.MinValue;
            foreach (Terrain t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude))
            {
                Vector3 o = t.transform.position, s = t.terrainData.size;
                for (int z = 0; z <= 48; z++)
                    for (int x = 0; x <= 48; x++)
                    {
                        var p = new Vector3(o.x + s.x * x / 48f, 0f, o.z + s.z * z / 48f);
                        float h = t.SampleHeight(p) + o.y;
                        if (h > best) { best = h; peak = new Vector3(p.x, h, p.z); }
                    }
            }
            return best > float.MinValue;
        }
    }
}
