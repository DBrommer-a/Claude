using UnityEditor;
using UnityEngine;

namespace Frostline.EditorTools
{
    /// <summary>
    /// Setzt ein Drohnenmodell (GLB, importiert über glTFast) anstelle des Platzhalters ein.
    /// Zuerst das DJI-FPV-Modell (lokal, aus dem Fab-Download per tools/mac/frostline.sh blender),
    /// sonst das mit Higgsfield erzeugte Modell. Fehlen beide oder der Importer, bleibt der Platzhalter.
    /// </summary>
    public static class DroneModelSwap
    {
        public const string DjiModelPath = "Assets/Frostline/Models/DJI/DJI_FPV.glb";
        public const string ModelPath = "Assets/Frostline/Models/Drohne/FrostlineDrohne.glb";

        /// <summary>Spannweite über die Propellerspitzen eines 5-Zoll-Quads in m.</summary>
        const float TargetSpan = 0.32f;

        public static bool TryUseGeneratedModel(Transform placeholder)
        {
            // DJI: Nase liegt in Blender bei −Y, nach glTF-Export und glTFast-Import bei +Z (vorn) – keine Drehung.
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(DjiModelPath);
            string name = "Modell (DJI FPV)";
            Vector3 euler = Vector3.zero;
            if (asset == null)
            {
                asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                name = "Modell (Higgsfield)";
                euler = DroneModelOrientation.Euler;
            }
            if (asset == null) return false;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, placeholder.parent);
            if (instance == null) instance = Object.Instantiate(asset, placeholder.parent);
            instance.name = name;
            foreach (Collider c in instance.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);

            // Auf echte Größe bringen und mittig über den Rahmen setzen.
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(euler);
            instance.transform.localScale = Vector3.one;
            Bounds bounds = LocalBounds(instance, placeholder.parent);
            float span = Mathf.Max(bounds.size.x, bounds.size.z);
            if (span > 1e-4f)
            {
                float scale = TargetSpan / span;
                instance.transform.localScale = Vector3.one * scale;
                bounds = LocalBounds(instance, placeholder.parent);
                instance.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            }

            placeholder.gameObject.SetActive(false);
            return true;
        }

        static Bounds LocalBounds(GameObject go, Transform space)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            bool has = false;
            var bounds = new Bounds();
            foreach (Renderer r in renderers)
            {
                Bounds b = r.bounds;
                Vector3 min = space.InverseTransformPoint(b.min);
                Vector3 max = space.InverseTransformPoint(b.max);
                var local = new Bounds((min + max) * 0.5f, Vector3.zero);
                local.Encapsulate(min);
                local.Encapsulate(max);
                if (!has) { bounds = local; has = true; }
                else bounds.Encapsulate(local);
            }
            return bounds;
        }
    }

    /// <summary>
    /// Drehung, damit die Nase des erzeugten Modells nach +z (vorn) zeigt. Im GLB zeigt die
    /// Nase (FPV-Kamera) nach +x; glTFast spiegelt beim Import die X-Achse, also liegt sie in
    /// Unity bei −x. 90° um Y dreht sie nach +z.
    /// </summary>
    public static class DroneModelOrientation
    {
        public static readonly Vector3 Euler = new Vector3(0f, 90f, 0f);
    }
}
