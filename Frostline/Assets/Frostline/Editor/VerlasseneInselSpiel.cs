using System.Collections.Generic;
using System.IO;
using Frostline.Flight;
using Frostline.Gameplay;
using Frostline.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Frostline.EditorTools
{
    /// <summary>
    /// Macht die „Verlassene Insel“ (gebaut mit Frostline › 9) spielbar, ohne die Welt neu zu bauen:
    /// Drohne am Boden in der Dorfgasse, alle Spielsysteme, Checkpoints aus den Fotostandpunkten,
    /// Kollision für alles Feste. Laub, Gras, Farne und Wasser bleiben durchfliegbar; Bäume bekommen nur
    /// am Stamm eine Kapsel, damit man unter Kronen hindurchfliegen kann. Das Kinolicht der Karte bleibt.
    /// Wiederholbar: Vorherige Spielteile werden zuerst entfernt.
    /// </summary>
    public static class VerlasseneInselSpiel
    {
        public const string ScenePath = "Assets/Frostline/Scenes/Verlassene_Insel.unity";
        const string CollisionFolder = "Assets/Frostline/Generated/Kollision";
        const float MapSize = 8000f;
        static readonly string[] GameplayRoots =
            { "Drohne", "Frostline", "Verfolgerkamera", "Ghost", "Multiplayer", "Netzwerk-Welt", "Startplatz", "Checkpoints (Spiel)", "Bäume (gebündelt)", "Lichtstimmungen" };
        static readonly string[] SoftWords =
        {
            "leaf", "leaves", "laub", "nadel", "needle", "foliage", "grass", "gras", "fern", "farn", "bush", "busch",
            "plant", "pflanze", "twig", "zweig", "crown", "krone", "canopy", "flower", "blume", "ivy", "vegetation",
            "heather", "heide", "reed", "schilf", "water", "wasser", "fluss", "river", "ocean", "meer", "wave",
            "decal", "nebel", "fog", "cloud", "wolke", "spray", "gischt",
        };

        public static string Report = "";

        /// <summary>Menü und Terminal laufen über Frostline.FotoInsel.FotoSpielbar (repariert vorher die Atmosphäre).</summary>
        public static string Build()
        {
            Report = "";
            if (!File.Exists(ScenePath)) return Report = "Die Verlassene Insel fehlt: zuerst Frostline › 9 bauen.";
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            AddGameplay(scene);
            EditorSceneManager.SaveScene(scene);
            GameSystemsBuilder.AddSceneToBuild(ScenePath, true);
            return Report;
        }

        /// <summary>Fügt Drohne und Spielsysteme in die offene Szene ein (auch direkt nach dem Bau der Insel).</summary>
        public static void AddGameplay(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (System.Array.IndexOf(GameplayRoots, root.name) >= 0) Object.DestroyImmediate(root);

            // Die Fotokamera der Karte weicht der FPV-Kamera der Drohne. Das Objekt bleibt aktiv, damit
            // „Fotos rendern“ es weiter findet; Camera.Render() funktioniert auch mit abgeschalteter Kamera.
            foreach (Camera c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                if (c.gameObject.name != "Fotokamera") continue;
                c.gameObject.tag = "Untagged";
                c.gameObject.SetActive(true);
                c.enabled = false;
                var listener = c.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = false;
            }

            // Laub-Streuprofil dauerhaft in HDRP eintragen, sonst rendert Laub im Spiel rot/rosa.
            HdrpAccess.RegisterDiffusionProfiles();

            // Gras-, Baum- und Wolkengrenzen fürs Spiel setzt der QualityTuner zur Laufzeit je Qualitätsstufe;
            // die Szene behält die Foto-Einstellungen (Gras 250 m, Bäume 5 km).
            int colliders = AddColliders(scene);
            int trees = AddTreeColliders();
            Physics.SyncTransforms();

            Transform spawn = CreateSpawn();
            DroneController drone = GameSystemsBuilder.CreateDrone(spawn);

            var env = new EnvironmentRefs { Sun = FindSun(), BaseVolume = FindAtmosphere() };
            var options = new SystemsOptions
            {
                IslandCenter = new Vector2(MapSize * 0.5f, MapSize * 0.5f),
                SignalCleanRadius = MapSize * 0.7f,
                SignalLostRadius = MapSize * 0.85f,
                BenchmarkCenter = new Vector3(MapSize * 0.5f, 0f, MapSize * 0.5f),
                BenchmarkRadius = 1600f,
                // Wolkenmeer der Karte (FotoAtmosphaere): Unterkante 1150 m, 550 m dick.
                CloudBottom = 1150f,
                CloudThickness = 550f,
                Checkpoints = CreateCheckpoints(),
            };
            GameSystemsBuilder.CreateSystems(drone, env, options);

            // Kinolook: Sonne und Licht der Karte behalten, klares Bild ohne Analograuschen.
            foreach (WeatherSystem w in Object.FindObjectsByType<WeatherSystem>(FindObjectsInactive.Include))
            {
                w.controlSun = false;
                w.weather = Weather.Clear;
            }
            foreach (SignalBoundary s in Object.FindObjectsByType<SignalBoundary>(FindObjectsInactive.Include)) s.baseNoise = 0f;

            // Bäume gebündelt zeichnen: Terrain-Bäume mit LOD-Gruppen kosten sonst 60–70 ms CPU pro Bild.
            new GameObject("Bäume (gebündelt)").AddComponent<TerrainTreeInstancer>();

            // Alle Wolken-Volumes der Karte (auch die aufgerissene Decke in Bodennähe) über die Qualitätsstufe steuern.
            var tunedVolumes = new List<Component>();
            foreach (Component v in FindVolumes())
                if (v.gameObject.name.StartsWith("Atmosphäre") || v.gameObject.name.StartsWith("Wolken in Bodennähe")) tunedVolumes.Add(v);
            foreach (QualityTuner tuner in Object.FindObjectsByType<QualityTuner>(FindObjectsInactive.Include))
            {
                foreach (Component v in tuner.volumes ?? new Component[0])
                    if (v != null && !tunedVolumes.Contains(v)) tunedVolumes.Add(v);
                tuner.volumes = tunedVolumes.ToArray();
            }

            Report += "Spielbar gemacht: Start bei " + spawn.position.ToString("F0") + ", "
                + colliders + " Kollider ergänzt, " + trees + " Baumarten mit Stamm-Kollision.\n";
            Debug.Log("Frostline: " + Report.Trim());
        }

        // ------------------------------------------------------------ Kollision

        /// <summary>Mesh-Kollider für feste Geometrie; weiche Teilmeshes (Laub, Gras, Wasser) bleiben ohne.</summary>
        static int AddColliders(Scene scene)
        {
            FrostlineProjectSetup.EnsureFolder(CollisionFolder);
            var solidOnly = new Dictionary<Mesh, Mesh>();
            int added = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.GetComponent<Collider>() != null) continue;
                    var filter = r.GetComponent<MeshFilter>();
                    Mesh mesh = filter != null ? filter.sharedMesh : null;
                    if (mesh == null || mesh.vertexCount < 3) continue;
                    if (IsSoft(r.gameObject.name, null)) continue;

                    Material[] mats = r.sharedMaterials;
                    int subs = mesh.subMeshCount;
                    var solid = new List<int>();
                    for (int i = 0; i < subs; i++)
                        if (!IsSoft(null, i < mats.Length ? mats[i] : null)) solid.Add(i);
                    if (solid.Count == 0) continue;

                    Mesh collision = mesh;
                    if (solid.Count < subs)
                    {
                        if (!solidOnly.TryGetValue(mesh, out collision))
                        {
                            collision = SolidSubmeshes(mesh, solid);
                            solidOnly[mesh] = collision;
                        }
                        if (collision == null) continue;
                    }
                    r.gameObject.AddComponent<MeshCollider>().sharedMesh = collision;
                    added++;
                }
            }
            return added;
        }

        static bool IsSoft(string objectName, Material m)
        {
            string text = (objectName ?? "") + " " + (m != null ? m.name + " " + m.shader.name : "");
            text = text.ToLowerInvariant();
            foreach (string w in SoftWords)
                if (text.Contains(w)) return true;
            if (m == null) return false;
            // Alpha-getestete Karten (Laub, Gitter) sind keine feste Fläche.
            if (m.HasProperty("_AlphaCutoffEnable") && m.GetFloat("_AlphaCutoffEnable") > 0.5f) return true;
            return m.renderQueue >= 2450 && m.renderQueue < 2500;
        }

        /// <summary>Kollisionsmesh nur aus den festen Teilmeshes (z. B. Stamm ohne Laubkarten).</summary>
        static Mesh SolidSubmeshes(Mesh source, List<int> solid)
        {
            var triangles = new List<int>();
            foreach (int i in solid) triangles.AddRange(source.GetTriangles(i));
            if (triangles.Count < 3) return null;
            var mesh = new Mesh { name = source.name + "_Kollision", indexFormat = source.indexFormat };
            mesh.SetVertices(source.vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            string path = AssetDatabase.GenerateUniqueAssetPath(CollisionFolder + "/" + Safe(mesh.name) + ".asset");
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>
        /// Terrain-Bäume: Kapsel nur um den unteren Stamm, damit man unter der Krone durchfliegen kann.
        /// Die Prefabs bekommen sie dauerhaft; das Terrain übernimmt sie als Baum-Kollision.
        /// </summary>
        static int AddTreeColliders()
        {
            var done = new HashSet<GameObject>();
            foreach (Terrain terrain in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
            {
                // Der TerrainCollider übernimmt Kollider der Baum-Prefabs von selbst (Unity 6 hat dafür keinen Schalter).
                foreach (TreePrototype p in terrain.terrainData.treePrototypes)
                {
                    if (p.prefab == null || done.Contains(p.prefab)) continue;
                    done.Add(p.prefab);
                    string path = AssetDatabase.GetAssetPath(p.prefab);
                    if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab")) continue;
                    GameObject contents = PrefabUtility.LoadPrefabContents(path);
                    foreach (Collider c in contents.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                    Bounds b = LocalBounds(contents);
                    float height = Mathf.Max(b.size.y, 1f);
                    float radius = Mathf.Clamp(Mathf.Min(b.size.x, b.size.z) * 0.06f, 0.12f, 0.6f);
                    var capsule = contents.AddComponent<CapsuleCollider>();
                    capsule.direction = 1;
                    capsule.radius = radius;
                    capsule.height = height * 0.45f;
                    capsule.center = new Vector3(b.center.x, b.min.y + capsule.height * 0.5f, b.center.z);
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    PrefabUtility.UnloadPrefabContents(contents);
                }
                terrain.terrainData.RefreshPrototypes();
            }
            return done.Count;
        }

        static Bounds LocalBounds(GameObject go)
        {
            Bounds b;
            if (!PropModels.LocalBounds(go, go.transform, false, out b)) b = new Bounds(Vector3.up * 5f, new Vector3(2f, 10f, 2f));
            return b;
        }

        // ------------------------------------------------------------ Start, Checkpoints, Licht

        static Transform CreateSpawn()
        {
            var spawn = new GameObject("Startplatz").transform;
            Transform view = FindViewpoint("Dorfgasse") ?? FindViewpoint("Verlassenes Wikingerdorf");
            Vector3 probe = view != null ? view.position : new Vector3(MapSize * 0.5f, 0f, MapSize * 0.5f);
            Vector3 forward = view != null ? Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized : Vector3.forward;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            // Von weit oben suchen: die Karte reicht bis über 2000 m (Berg über den Wolken).
            RaycastHit hit;
            Vector3 ground = Physics.Raycast(new Vector3(probe.x, 4000f, probe.z), Vector3.down, out hit, 5000f, ~0, QueryTriggerInteraction.Ignore)
                ? hit.point : probe;
            spawn.SetPositionAndRotation(ground + Vector3.up * 0.15f, Quaternion.LookRotation(forward));
            return spawn;
        }

        static Transform CreateCheckpoints()
        {
            var group = new GameObject("Checkpoints (Spiel)").transform;
            GameObject views = GameObject.Find("Fotostandpunkte");
            if (views == null) return group;
            foreach (Transform v in views.transform)
            {
                Vector3 flat = Vector3.ProjectOnPlane(v.forward, Vector3.up);
                var point = new GameObject(v.name).transform;
                point.SetParent(group, false);
                point.SetPositionAndRotation(v.position, flat.sqrMagnitude > 0.001f ? Quaternion.LookRotation(flat) : Quaternion.identity);
            }
            return group;
        }

        static Transform FindViewpoint(string name)
        {
            // Die Standpunkte heißen „Blick: Dorfgasse“ usw.
            GameObject views = GameObject.Find("Fotostandpunkte");
            if (views == null) return null;
            foreach (Transform v in views.transform)
                if (v.name.Contains(name)) return v;
            return null;
        }

        static Light FindSun()
        {
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (l.type == LightType.Directional) return l;
            return null;
        }

        static Component FindAtmosphere()
        {
            foreach (Component c in FindVolumes())
                if (c.gameObject.name.StartsWith("Atmosphäre")) return c;
            return null;
        }

        static List<Component> FindVolumes()
        {
            var list = new List<Component>();
            System.Type volumeType = HdrpAccess.CoreType("Volume");
            if (volumeType == null) return list;
            foreach (Object o in Object.FindObjectsByType(volumeType, FindObjectsInactive.Exclude))
                if (o is Component c) list.Add(c);
            return list;
        }

        static string Safe(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }
    }
}
