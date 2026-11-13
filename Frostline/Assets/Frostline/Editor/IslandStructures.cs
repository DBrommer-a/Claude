using UnityEngine;

namespace Frostline.EditorTools
{
    /// <summary>Materialien des Rohbaus; Farben grob nach der Concept-Art.</summary>
    public sealed class IslandMaterials
    {
        public Material Concrete, Brick, Rust, Steel, Glass, Plaza;
        public Material WoodRed, WoodDark, WoodOchre, Roof, WhitePaint, RedPaint, Stone;
        public Material Ice, Rock, Snow, Hull, Pine, Bark, Gate, Ring, Scaffold;

        public static IslandMaterials Create()
        {
            var m = new IslandMaterials();
            m.Concrete = GameSystemsBuilder.LitMaterial("Beton", new Color(0.46f, 0.45f, 0.43f), 0.15f, 0f, true);
            m.Brick = GameSystemsBuilder.LitMaterial("Ziegel", new Color(0.42f, 0.2f, 0.14f), 0.1f, 0f, true);
            m.Rust = GameSystemsBuilder.LitMaterial("Rost", new Color(0.36f, 0.2f, 0.11f), 0.25f, 0.4f, true);
            m.Steel = GameSystemsBuilder.LitMaterial("Stahl", new Color(0.18f, 0.19f, 0.2f), 0.35f, 0.8f, true);
            m.Glass = GameSystemsBuilder.LitMaterial("Glas", new Color(0.12f, 0.17f, 0.22f), 0.9f, 0.3f, true);
            m.Plaza = GameSystemsBuilder.LitMaterial("Platz", new Color(0.3f, 0.3f, 0.31f), 0.2f);
            m.WoodRed = GameSystemsBuilder.LitMaterial("Holz_Rot", new Color(0.45f, 0.1f, 0.07f), 0.2f, 0f, true);
            m.WoodDark = GameSystemsBuilder.LitMaterial("Holz_Dunkel", new Color(0.16f, 0.12f, 0.09f), 0.2f, 0f, true);
            m.WoodOchre = GameSystemsBuilder.LitMaterial("Holz_Ocker", new Color(0.55f, 0.4f, 0.18f), 0.2f, 0f, true);
            m.Roof = GameSystemsBuilder.LitMaterial("Dach", new Color(0.12f, 0.12f, 0.13f), 0.3f, 0.2f, true);
            m.WhitePaint = GameSystemsBuilder.LitMaterial("Weiss", new Color(0.82f, 0.82f, 0.8f), 0.3f, 0f, true);
            m.RedPaint = GameSystemsBuilder.LitMaterial("Rot", new Color(0.6f, 0.06f, 0.05f), 0.4f, 0f, true);
            m.Stone = GameSystemsBuilder.LitMaterial("Stein", new Color(0.4f, 0.39f, 0.37f), 0.1f, 0f, true);
            m.Ice = GameSystemsBuilder.LitMaterial("Eis", new Color(0.62f, 0.8f, 0.92f), 0.85f, 0f, true);
            m.Rock = GameSystemsBuilder.LitMaterial("Fels", new Color(0.25f, 0.24f, 0.24f), 0.15f, 0f, true);
            m.Snow = GameSystemsBuilder.LitMaterial("Schnee", new Color(0.85f, 0.87f, 0.9f), 0.4f);
            m.Hull = GameSystemsBuilder.LitMaterial("Rumpf", new Color(0.3f, 0.14f, 0.08f), 0.3f, 0.5f, true);
            m.Pine = GameSystemsBuilder.LitMaterial("Nadeln", new Color(0.08f, 0.16f, 0.1f), 0.1f);
            m.Bark = GameSystemsBuilder.LitMaterial("Rinde", new Color(0.2f, 0.14f, 0.1f), 0.1f);
            m.Gate = GameSystemsBuilder.LitMaterial("Gate", new Color(1f, 0.38f, 0.05f), 0.4f);
            m.Ring = GameSystemsBuilder.LitMaterial("Sammelring", new Color(1f, 0.75f, 0.15f), 0.8f, 1f, true);
            m.Ring.SetColor("_EmissiveColor", new Color(1f, 0.7f, 0.2f) * 4000f);
            HdrpAccess.InvokeStatic(HdrpAccess.HdType("HDMaterial"), "ValidateMaterial", m.Ring);
            m.Scaffold = GameSystemsBuilder.LitMaterial("Geruest", new Color(0.55f, 0.5f, 0.4f), 0.4f, 0.6f, true);
            return m;
        }
    }

    /// <summary>
    /// Rohbau-Gebäude und Landmarken der Insel. Jede Methode schreibt in ein MeshKit; Maße in m,
    /// Positionen lokal zum Kit (Ursprung = Boden in der Mitte).
    /// </summary>
    public static class IslandStructures
    {
        // ------------------------------------------------------------ Industrie

        /// <summary>
        /// Fabrikhalle mit teils eingestürzten Dachfeldern, schräg hängenden Bindern,
        /// Fensterbändern und großen Toren an den Stirnseiten. Länge entlang z.
        /// </summary>
        public static void FactoryHall(MeshKit k, IslandMaterials m, float width, float length, System.Random rng)
        {
            const float Eave = 16f;
            const float Rise = 4f;
            const float Bay = 12f;
            const float Column = 0.6f;
            float hw = width * 0.5f;
            float hl = length * 0.5f;
            int bays = Mathf.Max(1, Mathf.RoundToInt(length / Bay));
            float bay = length / bays;

            // Stützen an den Längsseiten.
            for (int i = 0; i <= bays; i++)
            {
                float z = -hl + i * bay;
                k.Box(m.Steel, new Vector3(-hw, Eave * 0.5f, z), new Vector3(Column, Eave, Column));
                k.Box(m.Steel, new Vector3(hw, Eave * 0.5f, z), new Vector3(Column, Eave, Column));
            }

            // Längswände: unten Beton, darüber Fensterband, Blechfelder (teils fehlend), zweites Fensterband.
            foreach (float side in new[] { -1f, 1f })
            {
                float x = side * hw;
                k.Box(m.Concrete, new Vector3(x, 2f, 0f), new Vector3(0.4f, 4f, length));
                for (float z = -hl; z <= hl + 0.01f; z += 3f) k.Box(m.Steel, new Vector3(x, 5.5f, z), new Vector3(0.15f, 3f, 0.15f));
                for (int i = 0; i < bays; i++)
                {
                    if (rng.NextDouble() < 0.22) continue; // Feld fehlt: Einflugloch
                    float z = -hl + (i + 0.5f) * bay;
                    k.Box(m.Rust, new Vector3(x, 9.25f, z), new Vector3(0.2f, 4.5f, bay - Column));
                }
                k.Box(m.Steel, new Vector3(x, 15f, 0f), new Vector3(0.5f, 2f, length));
                k.Box(m.Steel, new Vector3(x, 7f, 0f), new Vector3(0.3f, 0.3f, length));
            }

            // Stirnseiten mit Tor (10 × 9 m) und Giebel.
            const float GateWidth = 10f;
            const float GateHeight = 9f;
            foreach (float end in new[] { -1f, 1f })
            {
                float z = end * hl;
                float sideWidth = hw - GateWidth * 0.5f;
                k.Box(m.Concrete, new Vector3(-hw + sideWidth * 0.5f, Eave * 0.5f, z), new Vector3(sideWidth, Eave, 0.4f));
                k.Box(m.Concrete, new Vector3(hw - sideWidth * 0.5f, Eave * 0.5f, z), new Vector3(sideWidth, Eave, 0.4f));
                k.Box(m.Rust, new Vector3(0f, (GateHeight + Eave) * 0.5f, z), new Vector3(GateWidth, Eave - GateHeight, 0.3f));
                k.Gable(m.Rust, new Vector3(0f, Eave, z), Quaternion.identity, width, Rise, 0.3f);
            }

            // Dachbinder: Untergurt, zwei Obergurte, Pfosten. Manche hängen schräg herab.
            for (int i = 0; i <= bays; i++)
            {
                float z = -hl + i * bay;
                bool hanging = i > 0 && i < bays && rng.NextDouble() < 0.25;
                Vector3 left = new Vector3(-hw, Eave, z);
                Vector3 right = new Vector3(hw, hanging ? Eave - 9f : Eave, z);
                Vector3 ridge = new Vector3(hanging ? hw * 0.3f : 0f, hanging ? Eave - 2f : Eave + Rise, z);
                k.Beam(m.Steel, left, right, 0.35f);
                k.Beam(m.Steel, left, ridge, 0.35f);
                k.Beam(m.Steel, ridge, right, 0.35f);
                for (int p = 1; p < 4; p++)
                {
                    float t = p / 4f;
                    Vector3 bottom = Vector3.Lerp(left, right, t);
                    Vector3 top = t < 0.5f ? Vector3.Lerp(left, ridge, t * 2f) : Vector3.Lerp(ridge, right, (t - 0.5f) * 2f);
                    k.Beam(m.Steel, bottom, top, 0.18f);
                }
            }

            // Dachfelder: rund ein Drittel fehlt, einige sind schräg in die Halle gestürzt.
            float slope = Mathf.Sqrt(hw * hw + Rise * Rise);
            float angle = Mathf.Atan2(Rise, hw) * Mathf.Rad2Deg;
            for (int i = 0; i < bays; i++)
            {
                float z = -hl + (i + 0.5f) * bay;
                foreach (float side in new[] { -1f, 1f })
                {
                    double roll = rng.NextDouble();
                    if (roll < 0.33) continue;
                    Vector3 center = new Vector3(side * hw * 0.5f, Eave + Rise * 0.5f, z);
                    Quaternion rotation = Quaternion.Euler(0f, 0f, -side * angle);
                    if (roll < 0.45)
                    {
                        // Abgestürzt: liegt schräg zwischen Boden und Dach.
                        center = new Vector3(side * hw * 0.45f, 5f, z + (float)(rng.NextDouble() - 0.5) * 4f);
                        rotation = Quaternion.Euler((float)(rng.NextDouble() - 0.5) * 20f, (float)(rng.NextDouble() - 0.5) * 30f, side * 35f);
                    }
                    k.Box(m.Rust, center, new Vector3(slope, 0.15f, bay - 0.4f), rotation);
                }
            }

            // Schutt und Schneeverwehungen innen.
            for (int i = 0; i < 14; i++)
            {
                var p = new Vector3((float)(rng.NextDouble() - 0.5) * (width - 6f), 0f, (float)(rng.NextDouble() - 0.5) * (length - 8f));
                var size = new Vector3(1f + (float)rng.NextDouble() * 3f, 0.6f + (float)rng.NextDouble() * 1.4f, 1f + (float)rng.NextDouble() * 3f);
                k.Box(i % 3 == 0 ? m.Snow : m.Concrete, p + Vector3.up * size.y * 0.4f, size, Quaternion.Euler((float)rng.NextDouble() * 20f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 20f));
            }
        }

        /// <summary>Schornstein: oben offen, unten zwischen vier Pfeilern offen („oben rein, unten raus“).</summary>
        public static void Chimney(MeshKit k, IslandMaterials m, float radius, float height)
        {
            const float BaseHeight = 6f;
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + i * 90f) * Mathf.Deg2Rad;
                k.Box(m.Brick, new Vector3(Mathf.Cos(a) * radius, BaseHeight * 0.5f, Mathf.Sin(a) * radius), new Vector3(1.6f, BaseHeight, 1.6f), Quaternion.Euler(0f, -45f - i * 90f, 0f));
            }
            k.HollowCylinder(m.Brick, new Vector3(0f, BaseHeight, 0f), radius, height - BaseHeight, 0.6f, 28);
            k.HollowCylinder(m.Concrete, new Vector3(0f, height - 1.2f, 0f), radius + 0.3f, 1.2f, 0.9f, 28);
        }

        public static void Silo(MeshKit k, IslandMaterials m, float radius, float height)
        {
            k.Cylinder(m.Concrete, Vector3.zero, radius, height, 28);
            k.Frustum(m.Rust, new Vector3(0f, height, 0f), radius + 0.3f, 1.2f, 4f, 28);
        }

        /// <summary>Förderband-Tunnel entlang x, lichte Weite 4,5 × 4,5 m, auf Stützen.</summary>
        public static void ConveyorTunnel(MeshKit k, IslandMaterials m, float length, float floorHeight)
        {
            const float Inner = 4.5f;
            const float Wall = 0.3f;
            float y = floorHeight + Inner * 0.5f;
            k.Box(m.Rust, new Vector3(0f, floorHeight - Wall * 0.5f, 0f), new Vector3(length, Wall, Inner + Wall * 2f));
            k.Box(m.Rust, new Vector3(0f, floorHeight + Inner + Wall * 0.5f, 0f), new Vector3(length, Wall, Inner + Wall * 2f));
            k.Box(m.Rust, new Vector3(0f, y, Inner * 0.5f + Wall * 0.5f), new Vector3(length, Inner, Wall));
            k.Box(m.Rust, new Vector3(0f, y, -Inner * 0.5f - Wall * 0.5f), new Vector3(length, Inner, Wall));
            // Förderband innen
            k.Box(m.Steel, new Vector3(0f, floorHeight + 0.6f, 0f), new Vector3(length, 0.3f, 1.2f));
            for (float x = -length * 0.5f + 3f; x < length * 0.5f; x += 10f)
            {
                k.Box(m.Steel, new Vector3(x, floorHeight * 0.5f, -1.8f), new Vector3(0.5f, floorHeight, 0.5f));
                k.Box(m.Steel, new Vector3(x, floorHeight * 0.5f, 1.8f), new Vector3(0.5f, floorHeight, 0.5f));
            }
        }

        /// <summary>Kranbahn entlang x: zwei Schienen auf Stützen, Kranbrücke quer darüber.</summary>
        public static void CraneRunway(MeshKit k, IslandMaterials m, float length, float span, float height, float bridgeX)
        {
            float hz = span * 0.5f;
            for (float x = -length * 0.5f; x <= length * 0.5f + 0.01f; x += 15f)
            {
                k.Box(m.Steel, new Vector3(x, height * 0.5f, -hz), new Vector3(0.7f, height, 0.7f));
                k.Box(m.Steel, new Vector3(x, height * 0.5f, hz), new Vector3(0.7f, height, 0.7f));
            }
            k.Box(m.Steel, new Vector3(0f, height + 0.5f, -hz), new Vector3(length, 1f, 0.8f));
            k.Box(m.Steel, new Vector3(0f, height + 0.5f, hz), new Vector3(length, 1f, 0.8f));
            // Kranbrücke mit Laufkatze und Haken
            k.Box(m.RedPaint, new Vector3(bridgeX, height + 2f, 0f), new Vector3(2.2f, 2f, span + 2f));
            k.Box(m.Steel, new Vector3(bridgeX, height + 0.6f, 3f), new Vector3(2.6f, 1.4f, 2.6f));
            k.Beam(m.Steel, new Vector3(bridgeX, height, 3f), new Vector3(bridgeX, height - 6f, 3f), 0.08f);
            k.Box(m.Steel, new Vector3(bridgeX, height - 6.5f, 3f), new Vector3(0.8f, 1f, 0.5f));
        }

        public static void Rubble(MeshKit k, IslandMaterials m, Vector3 center, float radius, int count, System.Random rng)
        {
            for (int i = 0; i < count; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = (float)rng.NextDouble() * radius;
                var size = new Vector3(0.6f + (float)rng.NextDouble() * 2.5f, 0.4f + (float)rng.NextDouble() * 1.2f, 0.6f + (float)rng.NextDouble() * 2.5f);
                k.Box(i % 2 == 0 ? m.Concrete : m.Brick, center + new Vector3(Mathf.Cos(a) * r, size.y * 0.3f, Mathf.Sin(a) * r), size,
                    Quaternion.Euler((float)rng.NextDouble() * 30f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 30f));
            }
        }

        // ------------------------------------------------------------ Stadt

        /// <summary>Rohbau-Hochhaus: offene Etagen, Stützenraster, Treppenkern, Gerüst an einer Seite.</summary>
        public static void UnfinishedTower(MeshKit k, IslandMaterials m, float size, int floors, float floorHeight, System.Random rng)
        {
            float h = size * 0.5f;
            float[] grid = { -h + 1.5f, -h / 3f, h / 3f, h - 1.5f };
            float top = floors * floorHeight;

            foreach (float x in grid)
            {
                foreach (float z in grid)
                {
                    k.Box(m.Concrete, new Vector3(x, top * 0.5f, z), new Vector3(0.7f, top, 0.7f));
                }
            }
            // Treppenkern in einer Ecke, damit es nicht nur Stützen sind.
            k.Box(m.Concrete, new Vector3(h - 4f, top * 0.5f + 1.5f, h - 4f), new Vector3(5f, top + 3f, 5f));

            for (int f = 1; f <= floors; f++)
            {
                float y = f * floorHeight;
                bool unfinished = f > floors - 3;
                if (unfinished)
                {
                    // Oben fehlen Teile der Decken.
                    k.Box(m.Concrete, new Vector3(-h * 0.5f, y, 0f), new Vector3(h, 0.35f, size));
                    if (f == floors - 2) k.Box(m.Concrete, new Vector3(h * 0.5f, y, -h * 0.5f), new Vector3(h, 0.35f, h));
                }
                else
                {
                    k.Box(m.Concrete, new Vector3(0f, y, 0f), new Vector3(size, 0.35f, size));
                }
                // Brüstungen nur an zwei Seiten, Rest offen.
                if (f % 2 == 0 && !unfinished) k.Box(m.Concrete, new Vector3(-h + 0.15f, y + 0.6f, 0f), new Vector3(0.3f, 1f, size));
            }

            // Gerüst an der Südseite (−z): Ständer und Riegel.
            float zOut = -h - 1.2f;
            for (float x = -h; x <= h + 0.01f; x += 2.5f)
            {
                k.Beam(m.Scaffold, new Vector3(x, 0f, zOut), new Vector3(x, top + 2f, zOut), 0.08f);
                k.Beam(m.Scaffold, new Vector3(x, 0f, zOut + 0.9f), new Vector3(x, top + 2f, zOut + 0.9f), 0.08f);
            }
            for (float y = 2f; y <= top + 1f; y += 2f)
            {
                k.Beam(m.Scaffold, new Vector3(-h, y, zOut), new Vector3(h, y, zOut), 0.06f);
                k.Box(m.WoodDark, new Vector3(0f, y, zOut + 0.45f), new Vector3(size, 0.05f, 0.9f));
            }
        }

        /// <summary>Glasturm mit Geschossbändern und Dachaufbau.</summary>
        public static void GlassTower(MeshKit k, IslandMaterials m, float width, float depth, float height)
        {
            k.Box(m.Glass, new Vector3(0f, height * 0.5f, 0f), new Vector3(width, height, depth));
            for (float y = 15.2f; y < height; y += 15.2f)
            {
                k.Box(m.Concrete, new Vector3(0f, y, 0f), new Vector3(width + 0.3f, 0.6f, depth + 0.3f));
            }
            k.Box(m.Concrete, new Vector3(0f, height + 1.5f, 0f), new Vector3(width * 0.5f, 3f, depth * 0.5f));
        }

        // ------------------------------------------------------------ Dörfer

        public static void House(MeshKit k, Material walls, IslandMaterials m, Vector3 center, float yaw, float width, float length, float wallHeight)
        {
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            k.Box(walls, center + Vector3.up * wallHeight * 0.5f, new Vector3(width, wallHeight, length), r);
            float rise = width * 0.4f;
            k.GableRoof(m.Roof, center + Vector3.up * wallHeight, r, width, length, rise);
            k.Gable(walls, center + Vector3.up * wallHeight + r * new Vector3(0f, 0f, length * 0.5f - 0.15f), r, width, rise, 0.3f);
            k.Gable(walls, center + Vector3.up * wallHeight + r * new Vector3(0f, 0f, -length * 0.5f + 0.15f), r, width, rise, 0.3f);
            // Schornstein
            k.Box(m.Stone, center + r * new Vector3(width * 0.2f, wallHeight + rise * 0.8f, length * 0.25f), new Vector3(0.8f, 2.4f, 0.8f), r);
        }

        /// <summary>Offene Scheune: an beiden Stirnseiten ein großes Tor zum Durchfliegen.</summary>
        public static void Barn(MeshKit k, Material walls, IslandMaterials m, Vector3 center, float yaw)
        {
            const float Width = 14f;
            const float Length = 24f;
            const float WallHeight = 6.5f;
            const float DoorWidth = 7f;
            const float DoorHeight = 5.5f;
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            float hw = Width * 0.5f;
            float hl = Length * 0.5f;

            k.Box(walls, center + r * new Vector3(-hw, WallHeight * 0.5f, 0f), new Vector3(0.3f, WallHeight, Length), r);
            k.Box(walls, center + r * new Vector3(hw, WallHeight * 0.5f, 0f), new Vector3(0.3f, WallHeight, Length), r);
            foreach (float end in new[] { -1f, 1f })
            {
                float z = end * hl;
                float side = (Width - DoorWidth) * 0.5f;
                k.Box(walls, center + r * new Vector3(-hw + side * 0.5f, WallHeight * 0.5f, z), new Vector3(side, WallHeight, 0.3f), r);
                k.Box(walls, center + r * new Vector3(hw - side * 0.5f, WallHeight * 0.5f, z), new Vector3(side, WallHeight, 0.3f), r);
                k.Box(walls, center + r * new Vector3(0f, (DoorHeight + WallHeight) * 0.5f, z), new Vector3(DoorWidth, WallHeight - DoorHeight, 0.3f), r);
                k.Gable(walls, center + r * new Vector3(0f, WallHeight, z), r, Width, 5f, 0.3f);
            }
            k.GableRoof(m.Roof, center + Vector3.up * WallHeight, r, Width, Length, 5f);
            // Heuboden halb, Balken quer
            k.Box(m.WoodDark, center + r * new Vector3(0f, 3.4f, hl * 0.5f), new Vector3(Width - 0.6f, 0.2f, hl - 1f), r);
            for (float z = -hl + 3f; z < hl; z += 4f) k.Box(m.WoodDark, center + r * new Vector3(0f, WallHeight - 0.3f, z), new Vector3(Width, 0.3f, 0.3f), r);
        }

        /// <summary>Kirche mit Glockenstuhl: im Turm zwischen 16 und 21 m auf allen Seiten offen.</summary>
        public static void Church(MeshKit k, IslandMaterials m, Vector3 center, float yaw)
        {
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            const float NaveWidth = 10f;
            const float NaveLength = 22f;
            const float NaveHeight = 8f;
            k.Box(m.WhitePaint, center + Vector3.up * NaveHeight * 0.5f, new Vector3(NaveWidth, NaveHeight, NaveLength), r);
            k.GableRoof(m.Roof, center + Vector3.up * NaveHeight, r, NaveWidth, NaveLength, 5f);
            k.Gable(m.WhitePaint, center + Vector3.up * NaveHeight + r * new Vector3(0f, 0f, -NaveLength * 0.5f + 0.15f), r, NaveWidth, 5f, 0.3f);

            Vector3 tower = center + r * new Vector3(0f, 0f, NaveLength * 0.5f + 2.5f);
            k.Box(m.WhitePaint, tower + Vector3.up * 8f, new Vector3(5f, 16f, 5f), r);
            foreach (Vector2 c in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
            {
                k.Box(m.WhitePaint, tower + r * new Vector3(c.x * 1.9f, 18.5f, c.y * 1.9f), new Vector3(1.2f, 5f, 1.2f), r);
            }
            k.Box(m.WhitePaint, tower + Vector3.up * 21.2f, new Vector3(5.2f, 0.5f, 5.2f), r);
            k.Frustum(m.Roof, tower + Vector3.up * 21.4f, 3.4f, 0.1f, 8f, 4);
            // Glocke
            k.Cylinder(m.Steel, tower + Vector3.up * 18.6f, 0.6f, 1.2f, 10);
        }

        /// <summary>Steinbrücke: Bogen mit Fahrbahn, unten durchfliegbar.</summary>
        public static void Bridge(MeshKit k, IslandMaterials m, Vector3 center, float yaw, float span)
        {
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            k.Arch(m.Stone, center, r, span, 1.4f, 4f);
            k.Box(m.Stone, center + Vector3.up * (span * 0.5f + 0.9f), new Vector3(span + 4f, 0.6f, 4f), r);
            foreach (float side in new[] { -1f, 1f })
            {
                k.Box(m.Stone, center + r * new Vector3(0f, span * 0.5f + 1.6f, side * 1.9f), new Vector3(span + 4f, 0.8f, 0.3f), r);
                k.Box(m.Stone, center + r * new Vector3(side * (span * 0.5f + 1f), span * 0.25f, 0f), new Vector3(2f, span * 0.5f + 1f, 4f), r);
            }
        }

        // ------------------------------------------------------------ Hafen

        /// <summary>Pier auf Pfählen, Deck in 4,5 m Höhe: darunter durchfliegbar. Länge entlang lokal z.</summary>
        public static void Pier(MeshKit k, IslandMaterials m, float length, float width)
        {
            const float Deck = 4.5f;
            k.Box(m.WoodDark, new Vector3(0f, Deck, length * 0.5f), new Vector3(width, 0.4f, length));
            for (float z = 0f; z <= length + 0.01f; z += 6f)
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    k.Cylinder(m.WoodDark, new Vector3(side * (width * 0.5f - 0.4f), -4f, z), 0.3f, Deck + 3.8f, 8);
                }
                k.Box(m.WoodDark, new Vector3(0f, Deck - 0.5f, z), new Vector3(width, 0.3f, 0.3f));
            }
            // Poller und Geländer
            for (float z = 4f; z < length; z += 12f)
            {
                k.Cylinder(m.Steel, new Vector3(width * 0.5f - 0.6f, Deck + 0.2f, z), 0.25f, 0.7f, 8);
            }
            k.Box(m.WoodDark, new Vector3(-width * 0.5f + 0.1f, Deck + 1.1f, length * 0.5f), new Vector3(0.12f, 0.12f, length));
        }

        public static void Lighthouse(MeshKit k, IslandMaterials m, out float galleryHeight)
        {
            const float Height = 26f;
            const float Radius = 3.2f;
            galleryHeight = Height;
            k.Cylinder(m.WhitePaint, Vector3.zero, Radius, Height, 24);
            k.Cylinder(m.RedPaint, new Vector3(0f, 18f, 0f), Radius + 0.05f, 3f, 24);
            // Galerie: Ringplattform außen um den Turm, mit Geländer
            k.HollowCylinder(m.Steel, new Vector3(0f, Height - 0.4f, 0f), Radius + 1.8f, 0.4f, 1.8f, 24);
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI * 2f / 16f;
                Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (Radius + 1.7f);
                k.Beam(m.Steel, p + Vector3.up * Height, p + Vector3.up * (Height + 1.1f), 0.06f);
            }
            k.HollowCylinder(m.Steel, new Vector3(0f, Height + 1.05f, 0f), Radius + 1.75f, 0.08f, 0.1f, 24);
            k.Cylinder(m.Glass, new Vector3(0f, Height, 0f), 2f, 4f, 16);
            k.Frustum(m.RedPaint, new Vector3(0f, Height + 4f, 0f), 2.4f, 0.3f, 2.5f, 16);
        }

        /// <summary>Lagerhalle, an beiden Stirnseiten offen, Flachdach. Länge entlang z.</summary>
        public static void Warehouse(MeshKit k, IslandMaterials m, float width, float length, float height)
        {
            float hw = width * 0.5f;
            k.Box(m.Rust, new Vector3(-hw, height * 0.5f, 0f), new Vector3(0.3f, height, length));
            k.Box(m.Rust, new Vector3(hw, height * 0.5f, 0f), new Vector3(0.3f, height, length));
            k.Box(m.Rust, new Vector3(0f, height, 0f), new Vector3(width + 0.6f, 0.3f, length + 0.6f));
            foreach (float end in new[] { -1f, 1f })
            {
                k.Box(m.Rust, new Vector3(0f, height - 1.5f, end * length * 0.5f), new Vector3(width, 3f, 0.3f));
            }
            for (float z = -length * 0.5f; z <= length * 0.5f; z += 8f) k.Box(m.Steel, new Vector3(0f, height - 0.4f, z), new Vector3(width, 0.5f, 0.4f));
        }

        public static void Boat(MeshKit k, IslandMaterials m, Vector3 position, float yaw, Material hull)
        {
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            k.Box(hull, position + Vector3.up * 0.4f, new Vector3(3f, 1.6f, 9f), r);
            k.Box(m.WhitePaint, position + r * new Vector3(0f, 1.9f, -1.5f), new Vector3(2.2f, 1.6f, 2.6f), r);
        }

        // ------------------------------------------------------------ Strand, Gletscher, Landmarken

        /// <summary>Schiffswrack auf der Seite liegend, an beiden Enden offen zum Durchfliegen. Länge entlang z.</summary>
        public static void Shipwreck(MeshKit k, IslandMaterials m)
        {
            k.Tube(m.Hull, Vector3.zero, Quaternion.identity, 7f, 10f, 70f, 0.6f, 28);
            // Spanten innen
            for (float z = -30f; z <= 30f; z += 6f) k.Tube(m.Rust, new Vector3(0f, 0f, z), Quaternion.identity, 6.4f, 9.4f, 0.4f, 0.5f, 28);
            // Zwischendeck: teilt den Rumpf in einen hohen unteren und einen flachen oberen Durchflug.
            k.Box(m.Rust, new Vector3(0f, 3f, 0f), new Vector3(13f, 0.3f, 60f));
            // Brückenaufbau außen auf dem Deck
            k.Box(m.Rust, new Vector3(0f, 13f, -12f), new Vector3(8f, 6f, 10f));
        }

        /// <summary>Eistunnel: halbe Röhre aus Eis, liegt im Gletschertal.</summary>
        public static void IceTunnel(MeshKit k, IslandMaterials m, float length)
        {
            k.Tube(m.Ice, Vector3.zero, Quaternion.identity, 12f, 10f, length, 5f, 24, 200f, -10f);
            // Eiszapfen an der Decke
            for (float z = -length * 0.5f + 8f; z < length * 0.5f - 6f; z += 7f)
            {
                k.Frustum(m.Ice, new Vector3(((z * 7.3f) % 5f) - 2.5f, 9f, z), 0.1f, 0.7f, -2.5f, 6);
            }
        }

        public static void Seracs(MeshKit k, IslandMaterials m, System.Random rng, float spread, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var p = new Vector3((float)(rng.NextDouble() - 0.5) * spread, 0f, (float)(rng.NextDouble() - 0.5) * spread * 3f);
                var size = new Vector3(4f + (float)rng.NextDouble() * 8f, 3f + (float)rng.NextDouble() * 7f, 4f + (float)rng.NextDouble() * 8f);
                k.Box(m.Ice, p + Vector3.up * size.y * 0.3f, size, Quaternion.Euler((float)rng.NextDouble() * 25f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 25f));
            }
        }

        /// <summary>Felsbogen, stehend, Öffnung entlang lokal z.</summary>
        public static void RockArch(MeshKit k, IslandMaterials m, float span)
        {
            k.Arch(m.Rock, Vector3.zero, Quaternion.identity, span, span * 0.18f, span * 0.25f, 18);
        }

        /// <summary>Funkmast: drei Beine, Ringe und Diagonalen, Antennen oben.</summary>
        public static void RadioMast(MeshKit k, IslandMaterials m, float height, out Vector3 top)
        {
            const float BaseRadius = 3.5f;
            const float TopRadius = 0.8f;
            var legs = new Vector3[3];
            var legsTop = new Vector3[3];
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f;
                legs[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * BaseRadius;
                legsTop[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * TopRadius + Vector3.up * height;
                k.Beam(m.RedPaint, legs[i], legsTop[i], 0.25f);
            }
            int levels = Mathf.RoundToInt(height / 6f);
            for (int l = 1; l <= levels; l++)
            {
                float t0 = (l - 1f) / levels;
                float t1 = (float)l / levels;
                for (int i = 0; i < 3; i++)
                {
                    int j = (i + 1) % 3;
                    Vector3 a0 = Vector3.Lerp(legs[i], legsTop[i], t1);
                    Vector3 b0 = Vector3.Lerp(legs[j], legsTop[j], t1);
                    Vector3 a1 = Vector3.Lerp(legs[i], legsTop[i], t0);
                    k.Beam(l % 2 == 0 ? m.WhitePaint : m.RedPaint, a0, b0, 0.12f);
                    k.Beam(m.Steel, a1, b0, 0.08f);
                }
            }
            top = Vector3.up * height;
            k.Beam(m.Steel, top, top + Vector3.up * 8f, 0.15f);
            k.Box(m.WhitePaint, top + new Vector3(0f, 3f, 0f), new Vector3(2.2f, 0.6f, 0.4f));
        }

        /// <summary>Kiefer als Prefab-Mesh für Terrain-Bäume.</summary>
        public static void PineTree(MeshKit k, IslandMaterials m)
        {
            k.Cylinder(m.Bark, Vector3.zero, 0.25f, 4f, 8);
            k.Frustum(m.Pine, new Vector3(0f, 2.5f, 0f), 2.6f, 1.2f, 4f, 9);
            k.Frustum(m.Pine, new Vector3(0f, 5.5f, 0f), 2.1f, 0.7f, 4f, 9);
            k.Frustum(m.Pine, new Vector3(0f, 8.5f, 0f), 1.5f, 0.05f, 4.5f, 9);
        }

        /// <summary>Sammelring: steht aufrecht, Öffnung entlang lokal z.</summary>
        public static void CollectRing(MeshKit k, IslandMaterials m)
        {
            k.Tube(m.Ring, Vector3.zero, Quaternion.identity, 1.6f, 1.6f, 0.25f, 0.28f, 32);
        }

        /// <summary>Rennen-Gate als Rahmen um eine Öffnung (Breite × Höhe), Mitte im Ursprung.</summary>
        public static void GateFrame(MeshKit k, IslandMaterials m, float width, float height)
        {
            const float Bar = 0.3f;
            float hw = width * 0.5f + Bar * 0.5f;
            float hh = height * 0.5f + Bar * 0.5f;
            k.Box(m.Gate, new Vector3(-hw, 0f, 0f), new Vector3(Bar, height + Bar * 2f, Bar));
            k.Box(m.Gate, new Vector3(hw, 0f, 0f), new Vector3(Bar, height + Bar * 2f, Bar));
            k.Box(m.Gate, new Vector3(0f, hh, 0f), new Vector3(width + Bar * 2f, Bar, Bar));
            k.Box(m.Gate, new Vector3(0f, -hh, 0f), new Vector3(width + Bar * 2f, Bar, Bar));
        }
    }
}
