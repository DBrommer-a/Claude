using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Frostline.EditorTools
{
    /// <summary>
    /// Heruntergeladene Asset-Store-Pakete, entpackt mit „tools/mac/frostline.sh assetstore“ (nur lokal,
    /// Unity-Lizenz). Stellt alte Built-in-Materialien auf HDRP um und findet Prefabs und Terrain-Schichten.
    /// Fehlt ein Paket, liefern die Methoden null und der Inselbau nimmt die freien Vorlagen.
    /// </summary>
    public static class AssetStorePakete
    {
        public const string Industrial = "Assets/RPG_FPS_game_assets_industrial";
        public const string TerrainPack = "Assets/TerrainSampleAssets";

        static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        static readonly Regex TexEnv = new Regex(@"- (_MainTex|_BumpMap):\s*\n\s*m_Texture: \{fileID: \d+, guid: ([0-9a-f]{32})");
        static readonly Regex ColorEntry = new Regex(@"- _Color: \{r: ([\d.eE+-]+), g: ([\d.eE+-]+), b: ([\d.eE+-]+), a: ([\d.eE+-]+)\}");

        public static bool HasIndustrial { get { return AssetDatabase.IsValidFolder(Industrial); } }
        public static bool HasTerrainPack { get { return AssetDatabase.IsValidFolder(TerrainPack); } }

        /// <summary>
        /// Stellt die Materialien des Industrial Set (alter Built-in-Mobile-Shader, in HDRP pink) auf HDRP/Lit um.
        /// Texturen und Farbe kommen aus der .mat-Datei selbst, damit es auch ohne den alten Shader klappt.
        /// Läuft nur für Materialien, die noch nicht umgestellt sind.
        /// </summary>
        public static void PrepareMaterials()
        {
            prefabs.Clear();
            if (!HasIndustrial) return;
            Shader lit = Shader.Find("HDRP/Lit");
            if (lit == null) return;
            int converted = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { Industrial }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || material.shader == lit) continue;

                string yaml = File.ReadAllText(path);
                Texture main = null, bump = null;
                foreach (Match m in TexEnv.Matches(yaml))
                {
                    var texture = AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(m.Groups[2].Value));
                    if (m.Groups[1].Value == "_MainTex") main = texture;
                    else bump = texture;
                }
                Color color = Color.white;
                Match c = ColorEntry.Match(yaml);
                if (c.Success)
                    color = new Color(float.Parse(c.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(c.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(c.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture), 1f);

                material.shader = lit;
                if (main != null) material.SetTexture("_BaseColorMap", main);
                if (bump != null) material.SetTexture("_NormalMap", bump);
                material.SetColor("_BaseColor", color);
                // Mobile-Set ohne Glanzkarten: matt, nicht metallisch, damit Beton und Lack stimmig wirken.
                material.SetFloat("_Smoothness", 0.22f);
                material.SetFloat("_Metallic", 0f);
                HdrpAccess.InvokeStatic(HdrpAccess.HdType("HDMaterial"), "ValidateMaterial", material);
                EditorUtility.SetDirty(material);
                converted++;
            }
            if (converted > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log("Frostline: Industrial Set – " + converted + " Materialien auf HDRP/Lit umgestellt.");
            }
        }

        /// <summary>Prefab eines Pakets nach Dateinamen (ohne .prefab), z. B. „Hangar_v4“ oder „Grass_A“.</summary>
        public static GameObject Prefab(string name)
        {
            GameObject cached;
            if (prefabs.TryGetValue(name, out cached)) return cached;
            var folders = new List<string>();
            if (HasIndustrial) folders.Add(Industrial);
            if (HasTerrainPack) folders.Add(TerrainPack);
            GameObject found = null;
            if (folders.Count > 0)
            {
                foreach (string guid in AssetDatabase.FindAssets(name + " t:Prefab", folders.ToArray()))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetFileNameWithoutExtension(path) != name) continue;
                    found = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    break;
                }
            }
            prefabs[name] = found;
            return found;
        }

        /// <summary>Terrain-Schicht aus dem Terrain Sample Asset Pack, z. B. „Rock“; Kachelgröße in m.</summary>
        public static TerrainLayer Layer(string name, float tile)
        {
            if (!HasTerrainPack) return null;
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(TerrainPack + "/TerrainLayers/" + name + "_TerrainLayer.terrainlayer");
            if (layer == null) return null;
            if (tile > 0f) layer.tileSize = Vector2.one * tile;
            EditorUtility.SetDirty(layer);
            return layer;
        }
    }
}
