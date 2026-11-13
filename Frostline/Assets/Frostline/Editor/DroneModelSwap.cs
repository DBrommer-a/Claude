using UnityEditor;
using UnityEngine;

namespace Frostline.EditorTools
{
    /// <summary>
    /// Setzt das mit Higgsfield erzeugte Drohnenmodell (GLB, importiert über glTFast) anstelle
    /// des Platzhalters ein. Fehlt das Modell oder der Importer, bleibt der Platzhalter.
    /// </summary>
    public static class DroneModelSwap
    {
        public const string ModelPath = "Assets/Frostline/Models/Drohne/FrostlineDrohne.glb";

        /// <summary>Spannweite über die Propellerspitzen eines 5-Zoll-Quads in m.</summary>
        const float TargetSpan = 0.30f;

        public static bool TryUseGeneratedModel(Transform placeholder)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (asset == null) return false;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, placeholder.parent);
            if (instance == null) instance = Object.Instantiate(asset, placeholder.parent);
            instance.name = "Modell (Higgsfield)";
            foreach (Collider c in instance.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);

            // Auf echte Größe bringen und mittig über den Rahmen setzen.
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(DroneModelOrientation.Euler);
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

    /// <summary>Drehung, damit die Nase des erzeugten Modells nach +z (vorn) zeigt.</summary>
    public static class DroneModelOrientation
    {
        public static readonly Vector3 Euler = new Vector3(0f, 0f, 0f);
    }
}
