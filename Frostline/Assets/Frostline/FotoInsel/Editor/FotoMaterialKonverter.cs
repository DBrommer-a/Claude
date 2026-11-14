using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Frostline.FotoInsel
{
    /// <summary>
    /// Stellt Materialien aus Built-in- und URP-Paketen (Viking Village, Palm Tree Pack, Abandoned
    /// Factory …) auf HDRP/Lit um. Texturen, Farbe, Glanz und Alpha-Schnitt werden aus der .mat-Datei
    /// gelesen, damit es auch klappt, wenn der Original-Shader im Projekt fehlt (dann ist er pink).
    /// Bereits umgestellte Materialien bleiben unberührt.
    /// </summary>
    public static class FotoMaterialKonverter
    {
        static readonly Regex TexEnv = new Regex(@"- ([A-Za-z0-9_]+):\s*\n\s*m_Texture: \{fileID: (-?\d+), guid: ([0-9a-f]{32})");
        static readonly Regex FloatEntry = new Regex(@"^\s*- ([A-Za-z0-9_]+): (-?[\d.eE+-]+)\s*$", RegexOptions.Multiline);
        static readonly Regex ColorEntry = new Regex(@"- (_BaseColor|_Color): \{r: ([\d.eE+-]+), g: ([\d.eE+-]+), b: ([\d.eE+-]+), a: ([\d.eE+-]+)\}");
        static readonly string[] BaseSlots = { "_BaseColorMap", "_BaseMap", "_MainTex", "_Albedo", "_AlbedoMap", "_Diffuse", "_BaseColor_Map", "Base_Map" };
        static readonly string[] NormalSlots = { "_NormalMap", "_BumpMap", "Normal_Map", "_Normal" };
        static readonly string[] FoliageWords = { "leaf", "leaves", "foliage", "grass", "fern", "bush", "shrub", "plant", "straw", "palm", "frond", "perennial", "branch", "ivy" };

        static float Parse(string s) { return float.Parse(s, CultureInfo.InvariantCulture); }

        public static string ConvertFolder(string folder)
        {
            Shader lit = Shader.Find("HDRP/Lit");
            int converted = 0, skipped = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) continue;
                if (material.shader == lit || (material.shader != null && material.shader.name.StartsWith("HDRP/")))
                {
                    skipped++;
                    continue;
                }
                string yaml = File.ReadAllText(path);
                if (yaml.Contains("_FrontTex") && yaml.Contains("_BackTex")) { skipped++; continue; }   // Skybox

                var textures = new Dictionary<string, Texture>();
                foreach (Match m in TexEnv.Matches(yaml))
                {
                    if (m.Groups[3].Value == "00000000000000000000000000000000") continue;
                    var texture = AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(m.Groups[3].Value));
                    if (texture != null && !textures.ContainsKey(m.Groups[1].Value)) textures[m.Groups[1].Value] = texture;
                }
                var floats = new Dictionary<string, float>();
                foreach (Match m in FloatEntry.Matches(yaml))
                {
                    float v;
                    if (!floats.ContainsKey(m.Groups[1].Value) && float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                        floats[m.Groups[1].Value] = v;
                }
                Color color = Color.white;
                Match c = ColorEntry.Match(yaml);
                if (c.Success) color = new Color(Parse(c.Groups[2].Value), Parse(c.Groups[3].Value), Parse(c.Groups[4].Value), Parse(c.Groups[5].Value));

                Texture baseMap = First(textures, BaseSlots);
                Texture normalMap = First(textures, NormalSlots);
                Texture maskMap = textures.ContainsKey("_MaskMap") ? textures["_MaskMap"] : null;

                string lowerName = (material.name + " " + path).ToLowerInvariant();
                bool foliage = false;
                foreach (string w in FoliageWords) if (lowerName.Contains(w)) foliage = true;
                bool alpha = Get(floats, "_AlphaClip") > 0.5f || Get(floats, "_AlphaCutoffEnable") > 0.5f || Get(floats, "_Mode") == 1f || foliage;
                float cutoff = floats.ContainsKey("_AlphaCutoff") ? floats["_AlphaCutoff"] : (floats.ContainsKey("_Cutoff") ? floats["_Cutoff"] : 0.5f);
                float smoothness = floats.ContainsKey("_Smoothness") ? floats["_Smoothness"] : (floats.ContainsKey("_Glossiness") ? floats["_Glossiness"] : 0.3f);
                float metallic = Get(floats, "_Metallic");

                material.shader = lit;
                material.shaderKeywords = null;
                material.SetTexture("_BaseColorMap", baseMap);
                material.SetTexture("_NormalMap", normalMap);
                material.SetTexture("_MaskMap", maskMap);
                material.SetColor("_BaseColor", new Color(color.r, color.g, color.b, 1f));
                material.SetFloat("_Smoothness", Mathf.Clamp(smoothness, 0f, 0.8f) * 0.85f);
                material.SetFloat("_Metallic", Mathf.Clamp01(metallic));
                if (maskMap != null)
                {
                    material.SetFloat("_MetallicRemapMax", 1f);
                    material.SetFloat("_SmoothnessRemapMax", 1f);
                    material.SetFloat("_AORemapMax", 1f);
                }
                material.SetFloat("_NormalScale", 1f);
                material.SetFloat("_AlphaCutoffEnable", alpha ? 1f : 0f);
                material.SetFloat("_AlphaCutoff", Mathf.Clamp(cutoff, 0.2f, 0.7f));
                material.SetFloat("_DoubleSidedEnable", alpha ? 1f : 0f);
                material.SetFloat("_DoubleSidedNormalMode", 2f);
                material.SetFloat("_CullMode", alpha ? 0f : 2f);
                material.SetFloat("_CullModeForward", alpha ? 0f : 2f);
                material.doubleSidedGI = alpha;
                material.enableInstancing = true;
                if (foliage && FotoBaeume.FoliageProfile != null)
                {
                    material.SetMaterialType(MaterialId.LitTranslucent);
                    HDMaterial.SetDiffusionProfile(material, FotoBaeume.FoliageProfile);
                    material.SetFloat("_Thickness", 0.4f);
                }
                HDMaterial.ValidateMaterial(material);
                EditorUtility.SetDirty(material);
                converted++;
            }
            AssetDatabase.SaveAssets();
            return folder + ": " + converted + " Materialien auf HDRP/Lit umgestellt, " + skipped + " unverändert";
        }

        static Texture First(Dictionary<string, Texture> textures, string[] slots)
        {
            foreach (string s in slots)
            {
                Texture t;
                if (textures.TryGetValue(s, out t)) return t;
            }
            return null;
        }

        static float Get(Dictionary<string, float> d, string key)
        {
            float v;
            return d.TryGetValue(key, out v) ? v : 0f;
        }

        /// <summary>Megascans-/Fab-Ordner: Materialien bauen (Mask-Map, HDRP/Lit) über den Projekt-Importer.</summary>
        public static string ImportMegascans(string folder)
        {
            List<string> log = Frostline.EditorTools.MegascansImporter.ImportFolder(folder, 2048, false, false);
            int ok = 0;
            foreach (string line in log) if (line.StartsWith("✓")) ok++;
            return folder + ": " + ok + " Megascans-Materialien";
        }
    }
}
