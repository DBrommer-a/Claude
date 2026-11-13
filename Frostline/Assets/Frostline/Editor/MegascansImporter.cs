using System;
using System.Collections.Generic;
using System.IO;
using Frostline.Art;
using UnityEditor;
using UnityEngine;

namespace Frostline.EditorTools
{
    /// <summary>
    /// Menü „Frostline > 5 · Megascans importieren“: Ordner mit heruntergeladenen Megascans/Fab-
    /// Texturen wählen. Pro Material entsteht eine HDRP-Mask-Map (Metallic, AO, Detail,
    /// Smoothness), ein HDRP-Lit-Material und auf Wunsch ein Terrain-Layer. Texturen werden auf
    /// 2K begrenzt (laut Plan; 4K nur für Hero-Felsen nah am Flugweg).
    /// </summary>
    public sealed class MegascansImporter : EditorWindow
    {
        DefaultAsset folder;
        bool hero;
        bool terrainLayers = true;
        bool flipNormalGreen;
        Vector2 scroll;
        readonly List<string> log = new List<string>();

        [MenuItem("Frostline/5 · Megascans importieren", priority = 5)]
        static void Open()
        {
            GetWindow<MegascansImporter>(true, "Megascans importieren", true).minSize = new Vector2(460f, 360f);
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Megascans-Dateien (z. B. von fab.com) in einen Ordner unter Assets kopieren und hier wählen. "
                + "Erkannt werden Dateinamen mit _BaseColor/_Albedo, _Normal, _Roughness/_Gloss, _AO, _Metalness, _Displacement.",
                MessageType.Info);

            folder = (DefaultAsset)EditorGUILayout.ObjectField("Ordner", folder, typeof(DefaultAsset), false);
            hero = EditorGUILayout.Toggle(new GUIContent("Hero-Asset (4K)", "Nur für Felsen direkt am Flugweg; sonst 2K."), hero);
            terrainLayers = EditorGUILayout.Toggle("Auch Terrain-Layer anlegen", terrainLayers);
            flipNormalGreen = EditorGUILayout.Toggle(new GUIContent("Normal Map ist DirectX", "Grünkanal umdrehen (Unity erwartet OpenGL)."), flipNormalGreen);

            string path = folder != null ? AssetDatabase.GetAssetPath(folder) : null;
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path)))
            {
                if (GUILayout.Button("Importieren", GUILayout.Height(30f))) Import(path);
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (string line in log) EditorGUILayout.LabelField(line, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
        }

        void Import(string root)
        {
            log.Clear();
            log.AddRange(ImportFolder(root, hero ? 4096 : 2048, terrainLayers, flipNormalGreen));
        }

        /// <summary>
        /// Importiert alle Textur-Gruppen eines Ordners: Mask-Map, HDRP-Lit-Material „M_&lt;name&gt;“ und auf
        /// Wunsch Terrain-Layer „TL_&lt;name&gt;“. Auch per Code nutzbar (Poly-Haven-Vorlagen).
        /// </summary>
        public static List<string> ImportFolder(string root, int maxSize, bool terrainLayers, bool flipNormalGreen)
        {
            var log = new List<string>();
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { root });
            var paths = new List<string>();
            foreach (string guid in guids) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            Dictionary<string, Dictionary<TextureRole, string>> groups = MegascansNaming.Group(paths);
            if (groups.Count == 0)
            {
                log.Add("Keine passenden Texturen gefunden.");
                return log;
            }

            int done = 0;
            try
            {
                foreach (KeyValuePair<string, Dictionary<TextureRole, string>> group in groups)
                {
                    EditorUtility.DisplayProgressBar("Megascans", group.Key, (float)done / groups.Count);
                    try
                    {
                        ImportGroup(group.Key, group.Value, maxSize, terrainLayers, flipNormalGreen);
                        log.Add("✓ " + group.Key);
                    }
                    catch (Exception e)
                    {
                        log.Add("✗ " + group.Key + ": " + e.Message);
                    }
                    done++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
            }
            return log;
        }

        static void ImportGroup(string name, Dictionary<TextureRole, string> files, int maxSize, bool terrainLayers, bool flipNormalGreen)
        {
            string albedo, normal;
            files.TryGetValue(TextureRole.Albedo, out albedo);
            files.TryGetValue(TextureRole.Normal, out normal);
            if (albedo == null) throw new InvalidOperationException("keine Farbtextur");

            Configure(albedo, maxSize, true, false, flipNormalGreen);
            if (normal != null) Configure(normal, maxSize, false, true, flipNormalGreen);

            string folder = Path.GetDirectoryName(albedo).Replace('\\', '/');
            string maskPath = folder + "/" + name + "_MaskMap.png";
            Texture2D mask = BuildMaskMap(files, maskPath, maxSize);

            // HDRP-Lit-Material
            string materialPath = folder + "/M_" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("HDRP/Lit"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>(albedo));
            if (normal != null) material.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
            if (mask != null)
            {
                material.SetTexture("_MaskMap", mask);
                // Mit Mask-Map steuern Min/Max den Bereich der Smoothness.
                material.SetFloat("_SmoothnessRemapMin", 0f);
                material.SetFloat("_SmoothnessRemapMax", 1f);
                material.SetFloat("_AORemapMin", 0f);
                material.SetFloat("_AORemapMax", 1f);
            }
            HdrpAccess.InvokeStatic(HdrpAccess.HdType("HDMaterial"), "ValidateMaterial", material);
            EditorUtility.SetDirty(material);

            if (terrainLayers)
            {
                string layerPath = folder + "/TL_" + name + ".terrainlayer";
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
                if (layer == null)
                {
                    layer = new TerrainLayer();
                    AssetDatabase.CreateAsset(layer, layerPath);
                }
                layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(albedo);
                if (normal != null) layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(normal);
                layer.maskMapTexture = mask;
                layer.tileSize = new Vector2(4f, 4f);
                EditorUtility.SetDirty(layer);
            }
        }

        static void Configure(string path, int maxSize, bool srgb, bool isNormal, bool flipNormalGreen)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            importer.maxTextureSize = maxSize;
            importer.sRGBTexture = srgb;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = true;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            if (isNormal)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.flipGreenChannel = flipNormalGreen;
            }
            importer.SaveAndReimport();
        }

        /// <summary>Liest die Einzeltexturen und schreibt die Mask-Map als PNG.</summary>
        static Texture2D BuildMaskMap(Dictionary<TextureRole, string> files, string maskPath, int maxSize)
        {
            bool any = files.ContainsKey(TextureRole.Roughness) || files.ContainsKey(TextureRole.Gloss)
                || files.ContainsKey(TextureRole.Occlusion) || files.ContainsKey(TextureRole.Metallic);
            if (!any) return null;

            int width = 0, height = 0;
            byte[] metallic = Channel(files, TextureRole.Metallic, ref width, ref height);
            byte[] occlusion = Channel(files, TextureRole.Occlusion, ref width, ref height);
            byte[] roughness = Channel(files, TextureRole.Roughness, ref width, ref height);
            byte[] gloss = Channel(files, TextureRole.Gloss, ref width, ref height);
            if (width == 0) return null;

            byte[] rgba = MaskMap.Pack(width * height, metallic, occlusion, null, roughness, gloss);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            texture.LoadRawTextureData(rgba);
            texture.Apply();
            File.WriteAllBytes(maskPath, texture.EncodeToPNG());
            DestroyImmediate(texture);

            AssetDatabase.ImportAsset(maskPath);
            Configure(maskPath, maxSize, false, false, false);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
        }

        /// <summary>Ein Kanal (Rot) einer Textur als Bytes, auf die Größe der ersten gelesenen gebracht.</summary>
        static byte[] Channel(Dictionary<TextureRole, string> files, TextureRole role, ref int width, ref int height)
        {
            string path;
            if (!files.TryGetValue(role, out path)) return null;

            // Direkt aus der Datei lesen; die importierte Textur ist komprimiert und nicht lesbar.
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!source.LoadImage(File.ReadAllBytes(path)))
            {
                DestroyImmediate(source);
                throw new InvalidOperationException(Path.GetFileName(path) + " ist kein PNG/JPG (EXR bitte vorher umwandeln)");
            }

            if (width == 0)
            {
                width = source.width;
                height = source.height;
            }
            Color32[] pixels = source.width == width && source.height == height ? source.GetPixels32() : Resize(source, width, height);
            DestroyImmediate(source);

            var channel = new byte[pixels.Length];
            for (int i = 0; i < pixels.Length; i++) channel[i] = pixels[i].r;
            return channel;
        }

        static Color32[] Resize(Texture2D source, int width, int height)
        {
            RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(source, target);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var resized = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            resized.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            resized.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Color32[] pixels = resized.GetPixels32();
            DestroyImmediate(resized);
            return pixels;
        }
    }
}
