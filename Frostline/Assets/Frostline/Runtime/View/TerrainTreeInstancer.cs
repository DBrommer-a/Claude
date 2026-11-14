using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Frostline.View
{
    /// <summary>
    /// Zeichnet die Terrain-Bäume gebündelt per GPU-Instancing statt einzeln.
    /// Unity verarbeitet Terrain-Bäume mit LOD-Gruppen Baum für Baum auf der CPU; bei Tausenden Bäumen
    /// in Sichtweite kostet das 60–70 ms pro Bild. Hier sind die Bäume in Zellen vorsortiert: Sichtbarkeit
    /// und LOD gelten pro Zelle, gezeichnet wird je Baumart, LOD-Stufe und Material in wenigen Aufrufen.
    /// Die Terrain-Bäume bleiben in den Daten (Kollision, Editor-Fotos); im Spiel blendet diese Komponente
    /// sie aus, indem sie die Baumdistanz des Terrains übernimmt und dort auf 0 setzt.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class TerrainTreeInstancer : MonoBehaviour
    {
        [Tooltip("Kantenlänge der Zellen in m. Sichtbarkeit und LOD werden pro Zelle bestimmt.")]
        public float cellSize = 128f;
        [Tooltip("Zellen in diesem Umkreis (m) werden auch außerhalb des Blickfelds gezeichnet, damit ihre Schatten ins Bild fallen.")]
        public float shadowRange = 250f;
        [Tooltip("Zeichendistanz in m; wird vom Terrain übernommen, sobald der QualityTuner dort eine Baumdistanz setzt.")]
        public float drawDistance = 1500f;

        const int MaxPerCall = 1023;

        sealed class Part
        {
            public Mesh Mesh;
            public int Submesh;
            public Material Material;
            public Matrix4x4 Local;
            public bool Identity;
        }

        sealed class Lod
        {
            public float ScreenHeight;
            public bool Shadows;
            public readonly List<Part> Parts = new List<Part>();
            public readonly List<Matrix4x4> Visible = new List<Matrix4x4>(4096);
        }

        sealed class Species
        {
            public float Size = 10f;
            public Lod[] Lods;
        }

        sealed class Cell
        {
            public Bounds Bounds;
            public readonly Dictionary<int, List<Matrix4x4>> Trees = new Dictionary<int, List<Matrix4x4>>();
            public readonly Dictionary<int, float> MeanScale = new Dictionary<int, float>();
        }

        readonly List<Species> species = new List<Species>();
        readonly List<Cell> cells = new List<Cell>();
        readonly List<Matrix4x4> scratch = new List<Matrix4x4>(MaxPerCall);
        readonly Plane[] planes = new Plane[6];
        Bounds worldBounds;
        int treeCount;

        public int TreeCount { get { return treeCount; } }

        void Start()
        {
            Build();
        }

        void Build()
        {
            species.Clear();
            cells.Clear();
            treeCount = 0;
            var speciesIndex = new Dictionary<GameObject, int>();
            var grid = new Dictionary<Vector2Int, Cell>();
            bool any = false;

            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                TerrainData data = terrain.terrainData;
                if (data == null) continue;
                TreePrototype[] prototypes = data.treePrototypes;
                var map = new int[prototypes.Length];
                for (int p = 0; p < prototypes.Length; p++)
                {
                    GameObject prefab = prototypes[p].prefab;
                    map[p] = -1;
                    if (prefab == null) continue;
                    int index;
                    if (!speciesIndex.TryGetValue(prefab, out index))
                    {
                        Species s = Describe(prefab);
                        index = s != null ? species.Count : -1;
                        if (s != null) species.Add(s);
                        speciesIndex[prefab] = index;
                    }
                    map[p] = index;
                }

                Vector3 origin = terrain.transform.position;
                Vector3 size = data.size;
                foreach (TreeInstance tree in data.treeInstances)
                {
                    if (tree.prototypeIndex < 0 || tree.prototypeIndex >= map.Length) continue;
                    int s = map[tree.prototypeIndex];
                    if (s < 0) continue;
                    Vector3 position = origin + Vector3.Scale(tree.position, size);
                    var scale = new Vector3(tree.widthScale, tree.heightScale, tree.widthScale);
                    Matrix4x4 matrix = Matrix4x4.TRS(position, Quaternion.AngleAxis(tree.rotation * Mathf.Rad2Deg, Vector3.up), scale);

                    var key = new Vector2Int(Mathf.FloorToInt(position.x / cellSize), Mathf.FloorToInt(position.z / cellSize));
                    Cell cell;
                    if (!grid.TryGetValue(key, out cell))
                    {
                        cell = new Cell { Bounds = new Bounds(position, Vector3.zero) };
                        grid[key] = cell;
                        cells.Add(cell);
                    }
                    List<Matrix4x4> list;
                    if (!cell.Trees.TryGetValue(s, out list))
                    {
                        list = new List<Matrix4x4>();
                        cell.Trees[s] = list;
                        cell.MeanScale[s] = 0f;
                    }
                    list.Add(matrix);
                    cell.MeanScale[s] += tree.heightScale;
                    float height = species[s].Size * tree.heightScale;
                    cell.Bounds.Encapsulate(position + Vector3.up * height);
                    cell.Bounds.Encapsulate(position - Vector3.up * 1f);
                    if (!any) worldBounds = new Bounds(position, Vector3.zero);
                    worldBounds.Encapsulate(cell.Bounds);
                    any = true;
                    treeCount++;
                }
            }

            foreach (Cell cell in cells)
            {
                var keys = new List<int>(cell.MeanScale.Keys);
                foreach (int s in keys) cell.MeanScale[s] /= Mathf.Max(1, cell.Trees[s].Count);
                // Kronen ragen seitlich über den Stammfuß hinaus.
                cell.Bounds.Expand(new Vector3(12f, 0f, 12f));
            }
            worldBounds.Expand(200f);
            if (treeCount > 0) Debug.Log("Frostline: " + treeCount + " Bäume in " + cells.Count + " Zellen gebündelt (" + species.Count + " Arten).");
        }

        /// <summary>Liest LOD-Stufen, Meshes und Materialien einer Baumart aus ihrem Prefab.</summary>
        static Species Describe(GameObject prefab)
        {
            var s = new Species();
            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
            LODGroup group = prefab.GetComponent<LODGroup>();
            if (group != null)
            {
                s.Size = Mathf.Max(group.size, 1f);
                LOD[] lods = group.GetLODs();
                s.Lods = new Lod[lods.Length];
                for (int i = 0; i < lods.Length; i++)
                {
                    s.Lods[i] = new Lod { ScreenHeight = lods[i].screenRelativeTransitionHeight, Shadows = i < 2 };
                    foreach (Renderer r in lods[i].renderers)
                        if (r != null) AddParts(s.Lods[i], r, toRoot);
                }
            }
            else
            {
                var single = new Lod { ScreenHeight = 0f, Shadows = true };
                foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>())
                    AddParts(single, r, toRoot);
                Bounds b;
                s.Size = TryBounds(prefab, out b) ? Mathf.Max(b.size.y, 1f) : 10f;
                s.Lods = new[] { single };
            }
            foreach (Lod lod in s.Lods)
                if (lod.Parts.Count > 0) return s;
            return null;
        }

        static void AddParts(Lod lod, Renderer renderer, Matrix4x4 toRoot)
        {
            if (!(renderer is MeshRenderer)) return;
            var filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) return;
            Material[] materials = renderer.sharedMaterials;
            Matrix4x4 local = toRoot * renderer.transform.localToWorldMatrix;
            for (int sub = 0; sub < mesh.subMeshCount && sub < materials.Length; sub++)
            {
                Material m = materials[sub];
                if (m == null) continue;
                if (!m.enableInstancing) m = new Material(m) { enableInstancing = true };
                lod.Parts.Add(new Part { Mesh = mesh, Submesh = sub, Material = m, Local = local, Identity = local.isIdentity });
            }
        }

        static bool TryBounds(GameObject prefab, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>())
            {
                if (!any) bounds = r.bounds;
                else bounds.Encapsulate(r.bounds);
                any = true;
            }
            return any;
        }

        void LateUpdate()
        {
            // Die Baumdistanz setzt der QualityTuner am Terrain; hier wird sie übernommen und das Terrain
            // zeichnet seine Bäume nicht mehr selbst.
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                if (terrain.treeDistance <= 0f) continue;
                drawDistance = terrain.treeDistance;
                terrain.treeDistance = 0f;
            }
            if (treeCount == 0) return;

            Camera cam = Camera.main;
            if (cam == null) return;
            Draw(cam);
        }

        void Draw(Camera cam)
        {
            foreach (Species s in species)
                foreach (Lod lod in s.Lods) lod.Visible.Clear();

            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            Vector3 eye = cam.transform.position;
            // LOD wie bei der LODGroup: Höhe / (Abstand · 2·tan(FOV/2)) · LOD-Bias = Bildschirmanteil.
            float metric = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(QualitySettings.lodBias, 0.01f);
            float maxSqr = drawDistance * drawDistance;
            float shadowSqr = shadowRange * shadowRange;

            foreach (Cell cell in cells)
            {
                float sqr = cell.Bounds.SqrDistance(eye);
                if (sqr > maxSqr) continue;
                if (sqr > shadowSqr && !GeometryUtility.TestPlanesAABB(planes, cell.Bounds)) continue;
                float distance = Mathf.Sqrt(sqr);
                foreach (KeyValuePair<int, List<Matrix4x4>> entry in cell.Trees)
                {
                    Species s = species[entry.Key];
                    float height = s.Size * cell.MeanScale[entry.Key];
                    float screen = distance > 0.01f ? height / (distance * metric) : 1f;
                    Lod chosen = null;
                    foreach (Lod lod in s.Lods)
                    {
                        if (screen >= lod.ScreenHeight) { chosen = lod; break; }
                    }
                    if (chosen != null) chosen.Visible.AddRange(entry.Value);
                }
            }

            foreach (Species s in species)
            {
                foreach (Lod lod in s.Lods)
                {
                    if (lod.Visible.Count == 0) continue;
                    foreach (Part part in lod.Parts) Render(part, lod);
                }
            }
        }

        void Render(Part part, Lod lod)
        {
            var rp = new RenderParams(part.Material)
            {
                worldBounds = worldBounds,
                shadowCastingMode = lod.Shadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = true,
                layer = gameObject.layer,
            };
            List<Matrix4x4> trees = lod.Visible;
            for (int start = 0; start < trees.Count; start += MaxPerCall)
            {
                int count = Mathf.Min(MaxPerCall, trees.Count - start);
                if (part.Identity)
                {
                    Graphics.RenderMeshInstanced(rp, part.Mesh, part.Submesh, trees, count, start);
                }
                else
                {
                    scratch.Clear();
                    for (int i = 0; i < count; i++) scratch.Add(trees[start + i] * part.Local);
                    Graphics.RenderMeshInstanced(rp, part.Mesh, part.Submesh, scratch, count, 0);
                }
            }
        }
    }
}
