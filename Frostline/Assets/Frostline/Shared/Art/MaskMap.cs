using System;
using System.Collections.Generic;
using System.IO;

namespace Frostline.Art
{
    /// <summary>
    /// HDRP-Mask-Map aus einzelnen Megascans-Texturen: R = Metallic, G = Ambient Occlusion,
    /// B = Detail-Maske, A = Smoothness (= 1 − Rauheit). Fehlende Kanäle bekommen neutrale Werte.
    /// </summary>
    public static class MaskMap
    {
        /// <summary>
        /// Packt einkanalige Bilder (je ein Byte pro Pixel, alle gleich groß; null = fehlt) zu RGBA.
        /// </summary>
        public static byte[] Pack(int pixelCount, byte[] metallic, byte[] occlusion, byte[] detail, byte[] roughness, byte[] glossiness = null)
        {
            Check(metallic, pixelCount, nameof(metallic));
            Check(occlusion, pixelCount, nameof(occlusion));
            Check(detail, pixelCount, nameof(detail));
            Check(roughness, pixelCount, nameof(roughness));
            Check(glossiness, pixelCount, nameof(glossiness));

            var rgba = new byte[pixelCount * 4];
            for (int i = 0; i < pixelCount; i++)
            {
                int o = i * 4;
                rgba[o] = metallic != null ? metallic[i] : (byte)0;
                rgba[o + 1] = occlusion != null ? occlusion[i] : (byte)255;
                rgba[o + 2] = detail != null ? detail[i] : (byte)255;
                if (glossiness != null) rgba[o + 3] = glossiness[i];
                else rgba[o + 3] = roughness != null ? (byte)(255 - roughness[i]) : (byte)128;
            }
            return rgba;
        }

        static void Check(byte[] channel, int count, string name)
        {
            if (channel != null && channel.Length != count)
            {
                throw new ArgumentException("Kanal „" + name + "“ hat " + channel.Length + " statt " + count + " Pixel.");
            }
        }
    }

    /// <summary>Welche Datei eines Megascans-Downloads welche Aufgabe hat.</summary>
    public enum TextureRole
    {
        Unknown,
        Albedo,
        Normal,
        Roughness,
        Gloss,
        Occlusion,
        Metallic,
        Displacement,
        Opacity,
        Cavity,
        Specular,
    }

    /// <summary>Ordnet die Dateien eines Megascans-/Fab-Downloads nach Namen den Rollen zu.</summary>
    public static class MegascansNaming
    {
        static readonly KeyValuePair<string, TextureRole>[] Suffixes =
        {
            new KeyValuePair<string, TextureRole>("basecolor", TextureRole.Albedo),
            new KeyValuePair<string, TextureRole>("albedo", TextureRole.Albedo),
            new KeyValuePair<string, TextureRole>("diffuse", TextureRole.Albedo),
            new KeyValuePair<string, TextureRole>("color", TextureRole.Albedo),
            new KeyValuePair<string, TextureRole>("normal", TextureRole.Normal),
            new KeyValuePair<string, TextureRole>("normalgl", TextureRole.Normal),
            new KeyValuePair<string, TextureRole>("normaldx", TextureRole.Normal),
            new KeyValuePair<string, TextureRole>("roughness", TextureRole.Roughness),
            new KeyValuePair<string, TextureRole>("gloss", TextureRole.Gloss),
            new KeyValuePair<string, TextureRole>("glossiness", TextureRole.Gloss),
            new KeyValuePair<string, TextureRole>("ao", TextureRole.Occlusion),
            new KeyValuePair<string, TextureRole>("ambientocclusion", TextureRole.Occlusion),
            new KeyValuePair<string, TextureRole>("occlusion", TextureRole.Occlusion),
            new KeyValuePair<string, TextureRole>("metalness", TextureRole.Metallic),
            new KeyValuePair<string, TextureRole>("metallic", TextureRole.Metallic),
            new KeyValuePair<string, TextureRole>("displacement", TextureRole.Displacement),
            new KeyValuePair<string, TextureRole>("height", TextureRole.Displacement),
            new KeyValuePair<string, TextureRole>("opacity", TextureRole.Opacity),
            new KeyValuePair<string, TextureRole>("cavity", TextureRole.Cavity),
            new KeyValuePair<string, TextureRole>("specular", TextureRole.Specular),
        };

        /// <summary>Rolle aus dem Dateinamen, z. B. „rock_cliff_4K_Roughness.jpg“ → Roughness.</summary>
        public static TextureRole RoleOf(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            int cut = Math.Max(name.LastIndexOf('_'), name.LastIndexOf('-'));
            string suffix = cut >= 0 ? name.Substring(cut + 1) : name;
            foreach (KeyValuePair<string, TextureRole> pair in Suffixes)
            {
                if (suffix == pair.Key) return pair.Value;
            }
            return TextureRole.Unknown;
        }

        /// <summary>Gemeinsamer Name einer Textur-Gruppe: Dateiname ohne Rolle und ohne Auflösung („_4K“).</summary>
        public static string BaseName(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            int cut = Math.Max(name.LastIndexOf('_'), name.LastIndexOf('-'));
            if (cut > 0 && RoleOf(path) != TextureRole.Unknown) name = name.Substring(0, cut);
            string[] parts = name.Split('_');
            if (parts.Length > 1)
            {
                string last = parts[parts.Length - 1].ToLowerInvariant();
                if (last == "1k" || last == "2k" || last == "4k" || last == "8k") name = name.Substring(0, name.Length - last.Length - 1);
            }
            return name;
        }

        /// <summary>Gruppiert Dateien nach Material und Rolle.</summary>
        public static Dictionary<string, Dictionary<TextureRole, string>> Group(IEnumerable<string> paths)
        {
            var groups = new Dictionary<string, Dictionary<TextureRole, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                TextureRole role = RoleOf(path);
                if (role == TextureRole.Unknown) continue;
                string key = BaseName(path);
                Dictionary<TextureRole, string> group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new Dictionary<TextureRole, string>();
                    groups[key] = group;
                }
                if (!group.ContainsKey(role)) group[role] = path;
            }
            return groups;
        }
    }
}
