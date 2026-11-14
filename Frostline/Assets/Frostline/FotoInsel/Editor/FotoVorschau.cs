using System.IO;
using UnityEngine;

namespace Frostline.FotoInsel
{
    /// <summary>Draufsicht als PNG (Schummerung, Wasser, Wald, Zonen, Flüsse, Straßen) zum Prüfen der Planung.</summary>
    public static class FotoVorschau
    {
        public static string Write(FotoTerrain t, string path, int size = 1024)
        {
            var pixels = new Color32[size * size];
            float scale = FotoTerrain.Size / size;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float wx = (x + 0.5f) * scale, wz = (y + 0.5f) * scale;
                    float h = t.HeightAt(wx, wz);
                    float hx = t.HeightAt(wx + scale, wz) - t.HeightAt(wx - scale, wz);
                    float hz = t.HeightAt(wx, wz + scale) - t.HeightAt(wx, wz - scale);
                    var n = new Vector3(-hx, 2f * scale, -hz).normalized;
                    float shade = Mathf.Clamp01(Vector3.Dot(n, new Vector3(-0.6f, 0.7f, 0.4f).normalized)) * 0.75f + 0.25f;
                    Color c;
                    if (h < 0f) c = Color.Lerp(new Color(0.15f, 0.4f, 0.5f), new Color(0.03f, 0.1f, 0.22f), Mathf.Clamp01(-h / 60f));
                    else
                    {
                        float slope = t.SlopeAt(wx, wz);
                        if (h < 3f) c = new Color(0.85f, 0.8f, 0.6f);
                        else if (h > (FotoTerrain.Verlassen ? 1650f : 780f)) c = FotoTerrain.Verlassen ? new Color(1f, 0.75f, 0.8f) : new Color(0.95f, 0.95f, 0.97f);
                        else if (FotoTerrain.Verlassen && h > 1150f) c = new Color(0.8f, 0.8f, 0.85f);
                        else if (slope > 36f) c = new Color(0.45f, 0.43f, 0.4f);
                        else if (h > 480f) c = new Color(0.5f, 0.55f, 0.38f);
                        else c = new Color(0.42f, 0.58f, 0.28f);
                        float f = t.ForestAt(wx, wz, h, slope);
                        c = Color.Lerp(c, new Color(0.13f, 0.3f, 0.14f), f);
                        if (t.MaskAt(t.ZoneMask, wx, wz) > 200) c = new Color(0.6f, 0.6f, 0.6f);
                        if (t.MaskAt(t.RoadMask, wx, wz) > 200) c = new Color(0.2f, 0.2f, 0.2f);
                        if (t.MaskAt(t.WaterMask, wx, wz) == 255) c = new Color(0.2f, 0.45f, 0.75f);
                        c *= shade;
                    }
                    foreach (FotoZone zone in t.Zones)
                    {
                        float o = zone.Outside(new Vector2(wx, wz));
                        if (o > 0f && o < scale * 1.5f) c = zone.Flatten ? Color.red : Color.yellow;
                    }
                    c.a = 1f;
                    pixels[y * size + x] = c;
                }
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            return path;
        }
    }
}
