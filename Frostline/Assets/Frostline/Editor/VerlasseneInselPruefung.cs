using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Frostline.EditorTools
{
    /// <summary>
    /// Prüft die Verlassene Insel auf kaputte Materialien und rendert einen Standpunkt in mehreren
    /// Qualitätsstufen, damit sich Editor-Fotos und Spiel-Build vergleichen lassen.
    /// Aufruf: tools/mac/frostline.sh pruefen. Ergebnis: Ausgabe/pruefung/ (Bericht und Bilder).
    /// Ändert nichts an der Szene.
    /// </summary>
    public static class VerlasseneInselPruefung
    {
        public static void Pruefen()
        {
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Ausgabe/pruefung"));
            Directory.CreateDirectory(folder);
            var report = new StringBuilder();
            bool ok = true;
            try
            {
                EditorSceneManager.OpenScene(VerlasseneInselSpiel.ScenePath, OpenSceneMode.Single);
                CheckMaterials(report);
                CheckTerrain(report);
                RenderViews(folder, report);
            }
            catch (System.Exception e)
            {
                report.AppendLine("FEHLER: " + e);
                ok = false;
            }
            File.WriteAllText(Path.Combine(folder, "bericht.txt"), report.ToString());
            Debug.Log("Frostline Prüfung:\n" + report);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static void CheckMaterials(StringBuilder report)
        {
            var shaders = new Dictionary<string, int>();
            var suspicious = new SortedSet<string>();
            int nullMaterials = 0;
            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null) { nullMaterials++; continue; }
                    Note(m, shaders, suspicious);
                }
            }
            foreach (Terrain t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude))
            {
                foreach (TreePrototype p in t.terrainData.treePrototypes)
                    if (p.prefab != null)
                        foreach (Renderer r in p.prefab.GetComponentsInChildren<Renderer>(true))
                            foreach (Material m in r.sharedMaterials)
                                if (m != null) Note(m, shaders, suspicious);
                foreach (DetailPrototype d in t.terrainData.detailPrototypes)
                    if (d.prototype != null)
                        foreach (Renderer r in d.prototype.GetComponentsInChildren<Renderer>(true))
                            foreach (Material m in r.sharedMaterials)
                                if (m != null) Note(m, shaders, suspicious);
            }
            report.AppendLine("Shader in der Szene (Anzahl Materialslots):");
            foreach (var kv in shaders.OrderByDescending(k => k.Value)) report.AppendLine("  " + kv.Value + "  " + kv.Key);
            report.AppendLine("Leere Materialslots: " + nullMaterials);
            report.AppendLine("Verdächtige Materialien (nicht HDRP, nicht unterstützt, ohne Grundtextur, auffällige Farbe):");
            foreach (string s in suspicious.Take(200)) report.AppendLine("  " + s);
            report.AppendLine();
        }

        static void Note(Material m, Dictionary<string, int> shaders, SortedSet<string> suspicious)
        {
            string s = m.shader != null ? m.shader.name : "(kein Shader)";
            shaders[s] = shaders.TryGetValue(s, out int n) ? n + 1 : 1;
            string path = AssetDatabase.GetAssetPath(m);
            if (m.shader == null || !m.shader.isSupported || s.Contains("InternalError"))
                suspicious.Add("NICHT UNTERSTÜTZT  " + s + "  " + path);
            else if (!s.StartsWith("HDRP/") && !s.StartsWith("Shader Graphs/") && !s.StartsWith("TerrainEngine") && !s.StartsWith("Hidden/"))
                suspicious.Add("FREMDER SHADER  " + s + "  " + path);
            if (m.HasProperty("_BaseColor"))
            {
                Color c = m.GetColor("_BaseColor");
                // Magenta/Pink als Grundfarbe ist fast immer ein Umstellungsfehler.
                if (c.r > 0.5f && c.b > 0.35f && c.g < 0.35f) suspicious.Add("PINK  " + c + "  " + s + "  " + path);
            }
            if (m.HasProperty("_BaseColorMap") && m.GetTexture("_BaseColorMap") == null && s == "HDRP/Lit"
                && (path.Contains("Viking") || path.Contains("Fab") || path.Contains("Palm")))
                suspicious.Add("OHNE TEXTUR  " + path);
        }

        static void CheckTerrain(StringBuilder report)
        {
            Terrain[] terrains = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude);
            report.AppendLine("Terrain-Kacheln: " + terrains.Length);
            if (terrains.Length == 0) return;
            TerrainData data = terrains[0].terrainData;
            report.AppendLine("Material: " + (terrains[0].materialTemplate != null ? terrains[0].materialTemplate.shader.name + " " + AssetDatabase.GetAssetPath(terrains[0].materialTemplate) : "(Standard)"));
            foreach (TerrainLayer l in data.terrainLayers)
            {
                if (l == null) { report.AppendLine("  Layer: LEER"); continue; }
                report.AppendLine("  Layer " + l.name + "  Textur " + (l.diffuseTexture != null ? l.diffuseTexture.name : "FEHLT")
                    + "  Normal " + (l.normalMapTexture != null ? "ja" : "nein") + "  Mask " + (l.maskMapTexture != null ? "ja" : "nein")
                    + "  Pfad " + AssetDatabase.GetAssetPath(l));
            }
            foreach (DetailPrototype d in data.detailPrototypes)
            {
                string what = d.usePrototypeMesh ? (d.prototype != null ? d.prototype.name : "MESH FEHLT") : (d.prototypeTexture != null ? d.prototypeTexture.name : "TEXTUR FEHLT");
                report.AppendLine("  Detail " + what + "  Modus " + d.renderMode + "  Instanced " + d.useInstancing + "  Dichte " + d.density);
            }
            report.AppendLine("Detail-Distanz " + terrains[0].detailObjectDistance + ", Dichte " + terrains[0].detailObjectDensity
                + ", Baum-Distanz " + terrains[0].treeDistance + ", Bäume in Kachel 1: " + data.treeInstanceCount);
            report.AppendLine();
        }

        static void RenderViews(string folder, StringBuilder report)
        {
            GameObject views = GameObject.Find("Fotostandpunkte");
            if (views == null) { report.AppendLine("Keine Fotostandpunkte."); return; }
            var targets = new List<Transform>();
            foreach (Transform v in views.transform)
                if (v.name.Contains("Dorf am Fluss") || v.name.Contains("Dorfgasse")) targets.Add(v);

            var go = new GameObject("Prüfkamera");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 40000f;
            int saved = QualitySettings.GetQualityLevel();
            try
            {
                foreach (int level in new[] { 1, QualitySettings.names.Length - 1 })
                {
                    QualitySettings.SetQualityLevel(level, true);
                    foreach (Transform v in targets)
                    {
                        go.transform.SetPositionAndRotation(v.position, v.rotation);
                        string file = Path.Combine(folder, QualitySettings.names[level] + "_" + v.name.Replace("Blick: ", "").Replace(' ', '_') + ".png");
                        Render(cam, 1280, 720, file);
                        report.AppendLine("Bild: " + file);
                    }
                }
            }
            finally
            {
                QualitySettings.SetQualityLevel(saved, true);
                Object.DestroyImmediate(go);
            }
        }

        static void Render(Camera cam, int w, int h, string file)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            for (int i = 0; i < 12; i++) cam.Render();
            RenderTexture.active = rt;
            var image = new Texture2D(w, h, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            image.Apply();
            File.WriteAllBytes(file, image.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(image);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
