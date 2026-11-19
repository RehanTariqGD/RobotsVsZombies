using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using ZeroSynth.Authoring;
using Random = System.Random;

namespace ZeroSynth.EditorTools
{
    /// <summary>
    /// Dresses the game scene: ground, base pad, cliffs along the terrain
    /// obstacles, scattered ruins, and the light and fog that tie them
    /// together.
    /// </summary>
    /// <remarks>
    /// Everything is generated from a fixed seed under one root object, and the
    /// root is rebuilt from scratch each time, so running it twice gives the
    /// same scene and no leftovers. None of it takes part in the simulation:
    /// the cliffs only decorate boxes that already block the grid, and every
    /// loose prop sits outside the spawn ring or is flat enough to walk over.
    /// </remarks>
    public static class EnvironmentBuilder
    {
        const string GroupName = "--- ENVIRONMENT ---";
        const string Models = "Assets/ZeroSynth/Art/Models/";
        const string Textures = "Assets/ZeroSynth/Art/Textures/";
        const string Materials = "Assets/ZeroSynth/Art/Materials/";

        /// <summary>Ground plane edge length. Big enough that fog hides the edge from the tactical camera.</summary>
        const float GroundSize = 600f;

        /// <summary>World metres covered by one repeat of the ground texture.</summary>
        const float GroundTile = 5f;

        const float PadSize = 40f;
        const float PadTile = 4f;

        static readonly Color s_Fog = new Color(0.17f, 0.165f, 0.18f);

        [MenuItem("ZeroSynth/Build Environment")]
        public static void Build()
        {
            ConfigureTextures();
            // The tints pull the warm palette towards a cooler, duller dusk so
            // props sit back from the units instead of competing with them.
            var env = LoadOrCreateMaterial("M_Env", "T_Palette_Env_BaseColor.png", Vector2.one, 0.1f,
                new Color(0.68f, 0.68f, 0.72f));
            var groundMat = LoadOrCreateMaterial("M_Ground", "T_Ground_BaseColor.png",
                Vector2.one * (GroundSize / GroundTile), 0.05f, new Color(0.80f, 0.82f, 0.88f));
            var padMat = LoadOrCreateMaterial("M_BasePad", "T_BasePad_BaseColor.png",
                Vector2.one * (PadSize / PadTile), 0.35f, Color.white);

            var old = GameObject.Find(GroupName);
            if (old != null)
                Object.DestroyImmediate(old);

            var root = new GameObject(GroupName);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            DressGround(groundMat);
            BuildPad(root.transform, padMat);

            var props = new Props();
            var obstacles = Object.FindObjectsByType<GridObstacleBox>(FindObjectsSortMode.None);
            var blocked = new List<Rect>();

            foreach (var box in obstacles)
            {
                // Cover from an earlier run is regenerated below, not dressed as a cliff.
                if (box.name.StartsWith("Cover_"))
                {
                    Object.DestroyImmediate(box.gameObject);
                    continue;
                }

                blocked.Add(DressObstacle(root.transform, box, props, env));
            }

            BuildCover(root.transform, props, env, blocked);
            Scatter(root.transform, props, env, blocked);
            ApplyAtmosphere();

            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log($"[ZeroSynth] Environment built: {root.transform.childCount} objects.");
        }

        [MenuItem("ZeroSynth/Apply Atmosphere")]
        public static void ApplyAtmosphere()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.46f, 0.50f, 0.58f);
            RenderSettings.ambientEquatorColor = new Color(0.36f, 0.34f, 0.33f);
            RenderSettings.ambientGroundColor = new Color(0.20f, 0.18f, 0.17f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = s_Fog;
            RenderSettings.fogStartDistance = 150f;
            RenderSettings.fogEndDistance = 330f;

            var camera = Camera.main;
            if (camera != null)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = s_Fog;
            }

            var sun = Object.FindFirstObjectByType<Light>();
            if (sun != null && sun.type == LightType.Directional)
            {
                sun.color = new Color(1f, 0.90f, 0.76f);
                sun.intensity = 1.15f;
                sun.transform.rotation = Quaternion.Euler(48f, 325f, 0f);
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        // ---------------------------------------------------------------- ground

        static void DressGround(Material material)
        {
            var ground = GameObject.Find("Ground");
            if (ground == null)
            {
                Debug.LogWarning("[ZeroSynth] No Ground object to dress.");
                return;
            }

            // The built-in plane is 10 units across, so this is 600 m.
            ground.transform.localScale = new Vector3(GroundSize / 10f, 1f, GroundSize / 10f);

            var renderer = ground.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        static void BuildPad(Transform parent, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "BasePad";
            Object.DestroyImmediate(go.GetComponent<Collider>());

            go.transform.SetParent(parent, false);

            // Just above the ground so the two never z-fight. Taps pass through
            // it to the ground collider beneath.
            go.transform.position = new Vector3(0f, 0.03f, 0f);
            go.transform.localScale = new Vector3(PadSize / 10f, 1f, PadSize / 10f);

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.isStatic = true;
        }

        // -------------------------------------------------------------- obstacles

        /// <summary>
        /// Lines a long obstacle box with cliff segments. Returns the ground
        /// area it covers, so scattered props stay off it.
        /// </summary>
        static Rect DressObstacle(Transform parent, GridObstacleBox box, Props props, Material material)
        {
            var size = new SerializedObject(box).FindProperty("m_Size").vector3Value;
            Vector3 centre = box.transform.position;

            bool alongZ = size.z >= size.x;
            float length = alongZ ? size.z : size.x;
            float depth = alongZ ? size.x : size.z;

            int count = Mathf.Max(1, Mathf.CeilToInt(length / Props.CliffLength));
            float step = length / count;

            // Cliff meshes are 3 m deep: widen them to fill a wider box.
            float depthScale = Mathf.Clamp(depth / 3f, 0.8f, 4f);

            for (int i = 0; i < count; i++)
            {
                float along = -length * 0.5f + step * (i + 0.5f);
                Vector3 position = centre + (alongZ ? new Vector3(0f, 0f, along) : new Vector3(along, 0f, 0f));
                position.y = 0f;

                var mesh = (i % 2 == 0) ? props.CliffA : props.CliffB;
                Place(parent, "Cliff_" + box.name + "_" + i, mesh, material, position,
                    alongZ ? 90f : 0f, new Vector3(step / Props.CliffLength, 1f, depthScale));
            }

            var half = alongZ ? new Vector2(depth, length) * 0.5f : new Vector2(length, depth) * 0.5f;
            return Rect.MinMaxRect(centre.x - half.x, centre.z - half.y, centre.x + half.x, centre.z + half.y);
        }

        // ----------------------------------------------------------------- cover

        /// <summary>
        /// Ruined walls, wrecks and boulders just inside the spawn ring, each
        /// with a real obstacle box so the swarm has to path around it. They
        /// break up the approach into lanes without sealing it: there are few,
        /// and well apart.
        /// </summary>
        static void BuildCover(Transform parent, Props props, Material material, List<Rect> blocked)
        {
            // Leftovers from a previous run live under Terrain, not the env root.
            var terrain = GameObject.Find("Terrain");
            if (terrain != null)
            {
                for (int i = terrain.transform.childCount - 1; i >= 0; i--)
                {
                    var child = terrain.transform.GetChild(i);
                    if (child.name.StartsWith("Cover_"))
                        Object.DestroyImmediate(child.gameObject);
                }
            }

            var random = new Random(77031);
            var clear = CollectClearance();

            // (kind, long side along X in metres, depth in metres)
            const int count = 9;
            for (int n = 0; n < count; n++)
            {
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    float angle = (n + (float)random.NextDouble() * 0.6f) * (Mathf.PI * 2f / count);
                    float radius = Range(random, 32f, 41f);
                    var p = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                    int kind = n % 3; // 0 ruin wall, 1 wreck, 2 boulder
                    bool alongX = random.Next(2) == 0;

                    Vector3 size;
                    Mesh mesh;
                    float yaw;
                    Vector3 scale = Vector3.one;

                    if (kind == 0)
                    {
                        mesh = (n % 2 == 0) ? props.RuinA : props.RuinB;
                        size = alongX ? new Vector3(8f, 3f, 1.8f) : new Vector3(1.8f, 3f, 8f);
                        yaw = alongX ? 0f : 90f;
                    }
                    else if (kind == 1)
                    {
                        mesh = props.Wreck;
                        size = alongX ? new Vector3(5.6f, 2.4f, 2.6f) : new Vector3(2.6f, 2.4f, 5.6f);
                        yaw = alongX ? 90f : 0f;
                    }
                    else
                    {
                        mesh = props.RockC;
                        scale = Vector3.one * 1.2f;
                        size = new Vector3(4.8f, 2.6f, 3.6f);
                        yaw = (float)(random.NextDouble() * 360.0);
                    }

                    var rect = Rect.MinMaxRect(p.x - size.x * 0.5f, p.z - size.z * 0.5f,
                        p.x + size.x * 0.5f, p.z + size.z * 0.5f);

                    if (!IsFree(p, 6f, blocked, clear)) continue;

                    blocked.Add(rect);

                    var box = new GameObject("Cover_" + n);
                    if (terrain != null) box.transform.SetParent(terrain.transform, false);
                    box.transform.position = new Vector3(p.x, 0f, p.z);
                    var comp = box.AddComponent<GridObstacleBox>();
                    var serial = new SerializedObject(comp);
                    serial.FindProperty("m_Size").vector3Value = size;
                    serial.ApplyModifiedProperties();

                    if (kind == 0)
                    {
                        // Two wall sections end to end, a pillar at each end.
                        Vector3 axis = alongX ? Vector3.right : Vector3.forward;
                        Place(parent, "Ruin_Wall", mesh, material, p - axis * 2f, yaw, Vector3.one);
                        Place(parent, "Ruin_Wall", (mesh == props.RuinA) ? props.RuinB : props.RuinA, material,
                            p + axis * 2f, yaw + 180f, Vector3.one);
                        Place(parent, "Ruin_Pillar", props.Pillar, material, p - axis * 4.2f,
                            (float)(random.NextDouble() * 360.0), Vector3.one);
                    }
                    else
                    {
                        Place(parent, mesh.name, mesh, material, p, yaw, scale);
                    }

                    break;
                }
            }
        }

        // --------------------------------------------------------------- scatter

        static void Scatter(Transform parent, Props props, Material material, List<Rect> blocked)
        {
            var random = new Random(20261021);
            var clear = CollectClearance();

            // Far field, beyond the spawn ring: clusters of heavy props.
            const int clusters = 20;
            for (int c = 0; c < clusters; c++)
            {
                float angle = (float)(random.NextDouble() * Mathf.PI * 2.0);
                float radius = Range(random, 95f, 142f);
                var centre = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                int members = random.Next(4, 9);
                for (int m = 0; m < members; m++)
                {
                    float a = (float)(random.NextDouble() * Mathf.PI * 2.0);
                    float r = Range(random, 0f, 11f);
                    var p = centre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);

                    if (!IsFree(p, 5f, blocked, clear)) continue;

                    Mesh mesh = props.PickHeavy(random, out float scaleMin, out float scaleMax);
                    float scale = Range(random, scaleMin, scaleMax);
                    Place(parent, mesh.name, mesh, material, p, (float)(random.NextDouble() * 360.0),
                        Vector3.one * scale);
                }
            }

            // Mid field: low rubble only. Agents walk straight over it, so it
            // stays under half a metre.
            for (int i = 0; i < 45; i++)
            {
                float angle = (float)(random.NextDouble() * Mathf.PI * 2.0);
                float radius = Range(random, 26f, 92f);
                var p = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                if (!IsFree(p, 3f, blocked, clear)) continue;

                Place(parent, props.Debris.name, props.Debris, material, p,
                    (float)(random.NextDouble() * 360.0), Vector3.one * Range(random, 0.8f, 1.4f));
            }
        }

        static float Range(Random random, float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }

        /// <summary>Positions of things the player or the scene put down, which scatter must not sit on.</summary>
        static List<Vector3> CollectClearance()
        {
            var list = new List<Vector3>();
            foreach (var name in new[] { "Structures", "Units" })
            {
                var group = GameObject.Find(name);
                if (group == null) continue;
                foreach (Transform child in group.transform)
                    list.Add(child.position);
            }
            return list;
        }

        static bool IsFree(Vector3 p, float margin, List<Rect> blocked, List<Vector3> clear)
        {
            foreach (var rect in blocked)
            {
                var grown = Rect.MinMaxRect(rect.xMin - margin, rect.yMin - margin, rect.xMax + margin, rect.yMax + margin);
                if (grown.Contains(new Vector2(p.x, p.z))) return false;
            }

            foreach (var c in clear)
            {
                if ((new Vector2(c.x, c.z) - new Vector2(p.x, p.z)).sqrMagnitude < 144f) return false;
            }

            return true;
        }

        static GameObject Place(Transform parent, string name, Mesh mesh, Material material,
            Vector3 position, float yaw, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = scale;

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.isStatic = true;
            return go;
        }

        // ---------------------------------------------------------------- assets

        sealed class Props
        {
            public const float CliffLength = 8f;

            public readonly Mesh CliffA = Load("SM_Cliff_A");
            public readonly Mesh CliffB = Load("SM_Cliff_B");
            public readonly Mesh Debris = Load("SM_Debris_Pile");
            public readonly Mesh RuinA = Load("SM_Ruin_Wall_A");
            public readonly Mesh RuinB = Load("SM_Ruin_Wall_B");
            public readonly Mesh Pillar = Load("SM_Ruin_Pillar");
            public readonly Mesh Wreck = Load("SM_Wreck_Truck");
            public readonly Mesh RockC = Load("SM_Rock_C");

            readonly Mesh[] m_Heavy =
            {
                Load("SM_Rock_B"), Load("SM_Rock_C"), Load("SM_Rock_C"),
                Load("SM_Cliff_A"), Load("SM_Cliff_B"),
                Load("SM_Ruin_Wall_A"), Load("SM_Ruin_Wall_B"), Load("SM_Ruin_Pillar"),
                Load("SM_Wreck_Truck"), Load("SM_Crate_Stack"), Load("SM_Tree_Dead"),
            };

            public Mesh PickHeavy(Random random, out float scaleMin, out float scaleMax)
            {
                var mesh = m_Heavy[random.Next(m_Heavy.Length)];

                // Rocks vary a lot, built things barely at all.
                bool rock = mesh.name.StartsWith("SM_Rock");
                scaleMin = rock ? 0.8f : 0.95f;
                scaleMax = rock ? 1.5f : 1.1f;
                return mesh;
            }

            static Mesh Load(string name)
            {
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(Models + name + ".fbx"))
                {
                    if (asset is Mesh mesh)
                        return mesh;
                }

                throw new System.InvalidOperationException("Missing mesh " + name);
            }
        }

        static Material LoadOrCreateMaterial(string name, string texture, Vector2 tiling, float smoothness, Color tint)
        {
            string path = Materials + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Textures + texture);
            material.SetTexture("_BaseMap", tex);
            material.SetTextureScale("_BaseMap", tiling);
            material.SetColor("_BaseColor", tint);
            material.SetFloat("_Smoothness", smoothness);
            material.SetColor("_SpecColor", new Color(0.12f, 0.12f, 0.12f));
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        /// <summary>
        /// The palette strip must not blur between swatches; the two ground
        /// textures must repeat and may use mipmaps.
        /// </summary>
        static void ConfigureTextures()
        {
            var palette = (TextureImporter)AssetImporter.GetAtPath(Textures + "T_Palette_Env_BaseColor.png");
            if (palette != null)
            {
                palette.filterMode = FilterMode.Point;
                palette.mipmapEnabled = false;
                palette.wrapMode = TextureWrapMode.Clamp;
                palette.SaveAndReimport();
            }

            foreach (var file in new[] { "T_Ground_BaseColor.png", "T_BasePad_BaseColor.png" })
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(Textures + file);
                if (importer == null) continue;

                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
        }
    }
}
