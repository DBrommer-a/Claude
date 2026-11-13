using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Frostline.EditorTools
{
    /// <summary>
    /// Freie Vorlagen von Poly Haven (CC0), geladen mit „tools/mac/frostline.sh vorlagen“ nach
    /// Assets/Frostline/Vorlagen. Liefert HDRP-Materialien und Terrain-Schichten aus den Texturen
    /// und stellt Modelle mit Kollision und Fern-Ausblendung auf. Fehlt eine Vorlage, geben die
    /// Methoden null zurück und der Inselbau nimmt wie bisher die gebauten Platzhalter.
    /// </summary>
    public static class Vorlagen
    {
        public const string Root = "Assets/Frostline/Vorlagen";
        const string TextureRoot = Root + "/Texturen";
        const string InfoPath = Root + "/vorlagen_info.json";
        public const string ModelChild = "Vorlage";

        [System.Serializable]
        class Entry
        {
            public string id;
            public float breite_m;
            public float hoehe_m;
            public string datei;
            public int polygone;
        }

        [System.Serializable]
        class Info
        {
            public List<Entry> texturen = new List<Entry>();
            public List<Entry> modelle = new List<Entry>();
        }

        static Info info;

        public static bool Available { get { return File.Exists(InfoPath); } }

        static Info Data
        {
            get
            {
                if (info == null) info = Available ? JsonUtility.FromJson<Info>(File.ReadAllText(InfoPath)) : new Info();
                return info;
            }
        }

        static Entry Find(List<Entry> list, string id)
        {
            foreach (Entry e in list)
                if (e.id == id) return e;
            return null;
        }

        /// <summary>Legt fehlende Materialien, Mask-Maps und Terrain-Layer an (nur beim ersten Mal nötig).</summary>
        public static void EnsureImported()
        {
            info = null;
            if (!AssetDatabase.IsValidFolder(TextureRoot)) return;
            foreach (string folder in AssetDatabase.GetSubFolders(TextureRoot))
            {
                string id = Path.GetFileName(folder);
                if (AssetDatabase.LoadAssetAtPath<Material>(folder + "/M_" + id + ".mat") != null) continue;
                foreach (string line in MegascansImporter.ImportFolder(folder, 2048, true, false))
                    Debug.Log("Frostline: Vorlage " + line);
            }
        }

        static float TileSize(string id, float fallback)
        {
            Entry e = Find(Data.texturen, id);
            return e != null && e.breite_m > 0.05f ? e.breite_m : fallback;
        }

        /// <summary>
        /// HDRP-Lit-Material aus einer Textur-Vorlage, in echter Größe gekachelt (UVs der Insel sind in
        /// Metern) und mit <paramref name="tint"/> eingefärbt (HDRP multipliziert Farbe und Textur).
        /// </summary>
        public static Material TextureMaterial(string id, Color tint, bool doubleSided = true)
        {
            string folder = TextureRoot + "/" + id;
            var source = AssetDatabase.LoadAssetAtPath<Material>(folder + "/M_" + id + ".mat");
            if (source == null) return null;

            string path = folder + "/M_" + id + "_" + ColorUtility.ToHtmlStringRGB(tint) + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(source);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.CopyPropertiesFromMaterial(source);
            }
            material.SetColor("_BaseColor", tint);
            material.SetTextureScale("_BaseColorMap", Vector2.one / TileSize(id, 2f));
            if (doubleSided)
            {
                material.SetFloat("_DoubleSidedEnable", 1f);
                material.SetFloat("_CullMode", 0f);
                material.SetFloat("_CullModeForward", 0f);
                material.doubleSidedGI = true;
            }
            HdrpAccess.InvokeStatic(HdrpAccess.HdType("HDMaterial"), "ValidateMaterial", material);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Terrain-Schicht aus einer Textur-Vorlage, eingefärbt. Gekachelt in echter Größe oder, für
        /// große Flächen wie Berghänge, in <paramref name="tileOverride"/> m (sonst verschwimmt das Muster).
        /// </summary>
        public static TerrainLayer Layer(string id, Color tint, float tileOverride = 0f)
        {
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(TextureRoot + "/" + id + "/TL_" + id + ".terrainlayer");
            if (layer == null) return null;
            layer.tileSize = Vector2.one * (tileOverride > 0f ? tileOverride : TileSize(id, 4f));
            layer.diffuseRemapMax = new Vector4(tint.r, tint.g, tint.b, 1f);
            EditorUtility.SetDirty(layer);
            return layer;
        }

        public static GameObject Model(string id)
        {
            Entry e = Find(Data.modelle, id);
            if (e == null || string.IsNullOrEmpty(e.datei)) return null;
            return AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/" + e.datei);
        }

        /// <summary>
        /// Stellt ein Modell auf: Unterkante auf <paramref name="ground"/> (minus <paramref name="sink"/>),
        /// mittig, gedreht um <paramref name="yaw"/>. Mit Mesh-Kollidern und einer LOD-Gruppe, die das Modell
        /// ausblendet, sobald es kleiner als <paramref name="cullBelow"/> der Bildhöhe ist.
        /// </summary>
        public static GameObject Place(string id, Transform parent, Vector3 ground, float yaw, float scale, bool collider,
            float cullBelow, float sink = 0f, float tilt = 0f)
        {
            GameObject asset = Model(id);
            if (asset == null) return null;

            var root = new GameObject(id);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(ground, Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(tilt, 0f, 0f));

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.transform);
            if (instance == null) instance = Object.Instantiate(asset, root.transform);
            instance.name = ModelChild;
            foreach (Collider c in instance.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * scale;

            Bounds bounds;
            if (!PropModels.LocalBounds(instance, root.transform, false, out bounds))
            {
                Object.DestroyImmediate(root);
                return null;
            }
            instance.transform.localPosition += new Vector3(-bounds.center.x, -bounds.min.y - sink, -bounds.center.z);

            if (collider)
            {
                foreach (MeshFilter f in instance.GetComponentsInChildren<MeshFilter>())
                    if (f.sharedMesh != null) f.gameObject.AddComponent<MeshCollider>().sharedMesh = f.sharedMesh;
            }

            var lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(cullBelow, root.GetComponentsInChildren<Renderer>()) });
            lod.RecalculateBounds();

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(t.gameObject,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic
                    | StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic);
            }
            return root;
        }
    }
}
