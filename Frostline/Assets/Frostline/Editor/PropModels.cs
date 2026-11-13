using UnityEditor;
using UnityEngine;

namespace Frostline.EditorTools
{
    /// <summary>
    /// Setzt die mit Higgsfield erzeugten Requisiten (GLB, importiert über glTFast) in die gebauten
    /// Platzhalter ein. Das Modell wird in die Maße des Platzhalters gebracht, dessen Darstellung
    /// ausgeblendet. Die Kollision des Platzhalters bleibt: Flugrouten, Ringe und Gates verhalten
    /// sich dadurch genau wie vorher. Fehlt ein GLB, bleibt der Platzhalter sichtbar.
    /// </summary>
    public static class PropModels
    {
        public const string Folder = "Assets/Frostline/Models/Requisiten";
        public const string Leuchtturm = Folder + "/Leuchtturm.glb";
        public const string Funkmast = Folder + "/Funkmast.glb";
        public const string Schiffswrack = Folder + "/Schiffswrack.glb";
        public const string Kranbahn = Folder + "/Kranbahn.glb";
        public const string Fischerhuette = Folder + "/Fischerhuette.glb";
        public const string RennGate = Folder + "/RennGate.glb";

        public enum Fit
        {
            /// <summary>Gleichmäßig skalieren, bis die Höhe passt (Türme, Masten).</summary>
            Height,
            /// <summary>Gleichmäßig skalieren, bis die längste Seite der Grundfläche passt.</summary>
            Footprint,
            /// <summary>Jede Achse einzeln auf den Platzhalter ziehen (Gates: Öffnung muss passen).</summary>
            Stretch,
        }

        public static bool TrySwap(GameObject placeholder, string modelPath, Fit fit)
        {
            if (placeholder == null) return false;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (asset == null) return false;

            Transform space = placeholder.transform;
            Bounds target;
            if (!LocalBounds(placeholder, space, true, out target)) return false;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, space);
            if (instance == null) instance = Object.Instantiate(asset, space);
            instance.name = "Modell (Higgsfield)";
            foreach (Collider c in instance.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            Bounds model;
            if (!LocalBounds(instance, space, false, out model))
            {
                Object.DestroyImmediate(instance);
                return false;
            }

            // Die lange Seite der Grundfläche auf die lange Seite des Platzhalters drehen.
            bool turned = false;
            if (fit != Fit.Height && (target.size.x >= target.size.z) != (model.size.x >= model.size.z))
            {
                instance.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                LocalBounds(instance, space, false, out model);
                turned = true;
            }

            Vector3 ratio = new Vector3(
                target.size.x / Mathf.Max(model.size.x, 1e-4f),
                target.size.y / Mathf.Max(model.size.y, 1e-4f),
                target.size.z / Mathf.Max(model.size.z, 1e-4f));
            switch (fit)
            {
                case Fit.Height:
                    instance.transform.localScale = Vector3.one * ratio.y;
                    break;
                case Fit.Footprint:
                    float longTarget = Mathf.Max(target.size.x, target.size.z);
                    float longModel = Mathf.Max(model.size.x, model.size.z);
                    instance.transform.localScale = Vector3.one * (longTarget / Mathf.Max(longModel, 1e-4f));
                    break;
                default:
                    // Skalierung wirkt in den Achsen des Modells; nach der Drehung sind x und z vertauscht.
                    instance.transform.localScale = turned ? new Vector3(ratio.z, ratio.y, ratio.x) : ratio;
                    break;
            }

            // Unten mittig auf den Platzhalter setzen.
            LocalBounds(instance, space, false, out model);
            instance.transform.localPosition += new Vector3(target.center.x - model.center.x, target.min.y - model.min.y, target.center.z - model.center.z);

            StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(placeholder);
            foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = placeholder.layer;
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
            }
            foreach (Renderer r in placeholder.GetComponents<Renderer>()) r.enabled = false;
            return true;
        }

        /// <summary>
        /// Stellt ein Modell ohne Platzhalter frei auf (Kulisse). Es wird auf die angegebene Länge
        /// gebracht, steht mit der Unterkante auf <paramref name="ground"/> (minus <paramref name="sink"/>)
        /// und bekommt eigene Mesh-Kollider, damit man nicht hindurchfliegt.
        /// </summary>
        public static GameObject PlaceDecor(string modelPath, string name, Transform parent, Vector3 ground, Quaternion rotation, float length, float sink)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (asset == null) return null;
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(ground, rotation);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.transform);
            if (instance == null) instance = Object.Instantiate(asset, root.transform);
            instance.name = "Modell (Higgsfield)";
            foreach (Collider c in instance.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            Bounds model;
            if (!LocalBounds(instance, root.transform, false, out model))
            {
                Object.DestroyImmediate(root);
                return null;
            }
            instance.transform.localScale = Vector3.one * (length / Mathf.Max(Mathf.Max(model.size.x, model.size.z), 1e-4f));
            LocalBounds(instance, root.transform, false, out model);
            instance.transform.localPosition += new Vector3(-model.center.x, -model.min.y - sink, -model.center.z);

            foreach (MeshFilter f in instance.GetComponentsInChildren<MeshFilter>())
                if (f.sharedMesh != null) f.gameObject.AddComponent<MeshCollider>().sharedMesh = f.sharedMesh;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(t.gameObject,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic
                    | StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic);
            }
            return root;
        }

        /// <summary>
        /// Begrenzung aller Meshes in den Koordinaten von <paramref name="space"/>. Gemessen wird an
        /// den lokalen Mesh-Grenzen, nicht an Renderer.bounds: Die sind weltachsenparallel und wären
        /// bei gedrehten Platzhaltern (Wrack, Gates) zu groß.
        /// </summary>
        static bool LocalBounds(GameObject go, Transform space, bool ownOnly, out Bounds bounds)
        {
            MeshFilter[] filters = ownOnly ? go.GetComponents<MeshFilter>() : go.GetComponentsInChildren<MeshFilter>();
            Matrix4x4 toSpace = space.worldToLocalMatrix;
            bool has = false;
            bounds = new Bounds();
            foreach (MeshFilter f in filters)
            {
                if (f.sharedMesh == null) continue;
                Bounds b = f.sharedMesh.bounds;
                Vector3 min = b.min, max = b.max;
                Matrix4x4 m = toSpace * f.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
                    Vector3 local = m.MultiplyPoint3x4(corner);
                    if (!has) { bounds = new Bounds(local, Vector3.zero); has = true; }
                    else bounds.Encapsulate(local);
                }
            }
            return has;
        }
    }
}
