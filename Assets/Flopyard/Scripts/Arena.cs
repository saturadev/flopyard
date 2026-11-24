using System.Collections.Generic;
using UnityEngine;

namespace Flopyard
{
    /// Builds the training courtyard from primitives and procedural textures: sandy tiles,
    /// a spiked wooden fence, stone walls with a gate, banners, torches, grass tufts and pebbles.
    public static class Arena
    {
        public const float Half = 8f;   // fence half size
        const float WallHalf = 12f;

        class Batch
        {
            readonly List<CombineInstance> items = new List<CombineInstance>();
            static Mesh cube;
            public void Add(Vector3 p, Quaternion r, Vector3 s)
            {
                if (!cube) cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                items.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(p, r, s) });
            }
            public void Add(Vector3 p, Vector3 s) => Add(p, Quaternion.identity, s);
            public GameObject Build(string name, Material mat, Transform parent, bool shadows = true)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.CombineMeshes(items.ToArray(), true, true);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                return go;
            }
        }

        public static Transform Build(Transform parent)
        {
            var root = new GameObject("Arena").transform;
            root.SetParent(parent, false);
            var rnd = new System.Random(7);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

            // ground
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(root, false);
            floor.transform.localPosition = new Vector3(0, -0.5f, 0);
            floor.transform.localScale = new Vector3(2 * WallHalf + 2, 1, 2 * WallHalf + 2);
            floor.GetComponent<MeshRenderer>().sharedMaterial = Mats.Textured(TileTexture(), new Vector2((2 * WallHalf + 2) / 4f, (2 * WallHalf + 2) / 4f), 0.05f);
            var pm = new PhysicsMaterial("Ground") { dynamicFriction = 0.6f, staticFriction = 0.7f };
            floor.GetComponent<Collider>().sharedMaterial = pm;

            // fence: posts with diamond spikes, two rails and X braces
            var wood = new Batch();
            var spikes = new Batch();
            for (int side = 0; side < 4; side++)
            {
                Quaternion q = Quaternion.Euler(0, 90 * side, 0);
                Vector3 n = q * Vector3.forward;
                Vector3 t = q * Vector3.right;
                for (float x = -Half; x <= Half + 0.01f; x += 2f)
                {
                    Vector3 p = n * Half + t * x;
                    wood.Add(p + Vector3.up * 0.65f, q, new Vector3(0.2f, 1.3f, 0.2f));
                    spikes.Add(p + Vector3.up * 1.38f, q * Quaternion.Euler(45, 0, 45), new Vector3(0.14f, 0.14f, 0.14f));
                    if (x < Half - 0.01f)
                    {
                        Vector3 mid = p + t * 1f;
                        wood.Add(mid + Vector3.up * 1.05f, q, new Vector3(2f, 0.16f, 0.12f));
                        wood.Add(mid + Vector3.up * 0.35f, q, new Vector3(2f, 0.12f, 0.1f));
                        wood.Add(mid + Vector3.up * 0.7f, q * Quaternion.Euler(0, 0, 31), new Vector3(2.1f, 0.1f, 0.08f));
                        wood.Add(mid + Vector3.up * 0.7f, q * Quaternion.Euler(0, 0, -31), new Vector3(2.1f, 0.1f, 0.08f));
                    }
                }
                var col = new GameObject("FenceCollider" + side);
                col.transform.SetParent(root, false);
                col.transform.localPosition = n * (Half + 0.05f) + Vector3.up * 0.7f;
                col.transform.localRotation = q;
                col.AddComponent<BoxCollider>().size = new Vector3(2 * Half + 0.4f, 1.4f, 0.3f);
            }
            wood.Build("Fence", Mats.Get(new Color(0.23f, 0.15f, 0.10f), 0.1f), root);
            spikes.Build("FenceSpikes", Mats.Get(new Color(0.75f, 0.72f, 0.66f), 0.3f), root);

            // stone walls
            var wallMat = Mats.Textured(StoneTexture(), new Vector2(6, 2), 0.05f);
            var trimMat = Mats.Get(new Color(0.55f, 0.50f, 0.46f), 0.05f);
            var trim = new Batch();
            for (int side = 0; side < 4; side++)
            {
                Quaternion q = Quaternion.Euler(0, 90 * side, 0);
                Vector3 n = q * Vector3.forward;
                var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
                w.name = "Wall" + side;
                w.transform.SetParent(root, false);
                w.transform.localPosition = n * (WallHalf + 0.5f) + Vector3.up * 4f;
                w.transform.localRotation = q;
                w.transform.localScale = new Vector3(2 * WallHalf + 2, 8f, 1f);
                w.GetComponent<MeshRenderer>().sharedMaterial = wallMat;
                // battlements on top
                for (float x = -WallHalf; x <= WallHalf; x += 1.6f)
                    trim.Add(n * (WallHalf + 0.5f) + q * Vector3.right * x + Vector3.up * 8.4f, q, new Vector3(0.9f, 0.8f, 1.1f));
                // pillars
                for (float x = -WallHalf + 3; x <= WallHalf - 3; x += 6f)
                    trim.Add(n * (WallHalf - 0.1f) + q * Vector3.right * x + Vector3.up * 3.5f, q, new Vector3(0.9f, 7f, 0.5f));
            }
            trim.Build("WallTrim", trimMat, root);

            // gate in the far wall: dark arch with wooden doors and iron studs
            var doorWood = new Batch();
            var dark = new Batch();
            var iron = new Batch();
            for (int g = 0; g < 2; g++)
            {
                float gx = g == 0 ? 0f : 0f;
                Quaternion q = Quaternion.Euler(0, g == 0 ? 0 : 90, 0);
                Vector3 n = q * Vector3.forward;
                Vector3 t = q * Vector3.right;
                Vector3 c = n * (WallHalf - 0.05f) + t * gx;
                dark.Add(c + Vector3.up * 2.0f, q, new Vector3(3.4f, 4.0f, 0.2f));
                dark.Add(c + Vector3.up * 4.0f, q * Quaternion.Euler(0, 0, 45), new Vector3(2.4f, 2.4f, 0.2f));
                for (int p = -3; p <= 3; p++)
                    doorWood.Add(c - n * 0.1f + t * (p * 0.42f) + Vector3.up * 1.75f, q, new Vector3(0.38f, 3.5f, 0.12f));
                for (int p = -3; p <= 3; p++)
                    for (int h = 0; h < 3; h++)
                        iron.Add(c - n * 0.18f + t * (p * 0.42f) + Vector3.up * (0.6f + h * 1.2f), q, new Vector3(0.07f, 0.07f, 0.05f));
                iron.Add(c - n * 0.17f + Vector3.up * 1.2f, q, new Vector3(2.9f, 0.08f, 0.04f));
                iron.Add(c - n * 0.17f + Vector3.up * 2.6f, q, new Vector3(2.9f, 0.08f, 0.04f));
            }
            dark.Build("GateArch", Mats.Get(new Color(0.12f, 0.10f, 0.09f), 0), root);
            doorWood.Build("GateDoors", Mats.Get(new Color(0.36f, 0.24f, 0.15f), 0.1f), root);
            iron.Build("GateIron", Mats.Get(new Color(0.18f, 0.18f, 0.2f), 0.5f), root);

            // banners: deep blue with a gold fist emblem, and torches
            var banner = new Batch(); var emblem = new Batch(); var torchWood = new Batch(); var flames = new Batch();
            for (int side = 0; side < 4; side++)
            {
                Quaternion q = Quaternion.Euler(0, 90 * side, 0);
                Vector3 n = q * Vector3.forward;
                Vector3 t = q * Vector3.right;
                foreach (float x in new[] { -6f, 6f })
                {
                    Vector3 b = n * (WallHalf - 0.12f) + t * x;
                    banner.Add(b + Vector3.up * 5.2f, q, new Vector3(1.4f, 2.6f, 0.05f));
                    banner.Add(b + Vector3.up * 3.75f, q * Quaternion.Euler(0, 0, 45), new Vector3(0.99f, 0.99f, 0.05f));
                    emblem.Add(b - n * 0.04f + Vector3.up * 5.3f, q, new Vector3(0.6f, 0.45f, 0.03f));
                    emblem.Add(b - n * 0.04f + Vector3.up * 4.95f, q, new Vector3(0.3f, 0.35f, 0.03f));
                    emblem.Add(b - n * 0.04f + Vector3.up * 6.25f, q, new Vector3(1.2f, 0.1f, 0.03f));
                }
                foreach (float x in new[] { -9f, -3f, 3f, 9f })
                {
                    Vector3 b = n * (WallHalf - 0.2f) + t * x + Vector3.up * 2.6f;
                    torchWood.Add(b, q * Quaternion.Euler(-20, 0, 0), new Vector3(0.12f, 0.6f, 0.12f));
                    torchWood.Add(b - n * 0.05f - Vector3.up * 0.2f, q, new Vector3(0.18f, 0.1f, 0.3f));
                    flames.Add(b - n * 0.12f + Vector3.up * 0.38f, q * Quaternion.Euler(45, 0, 45), new Vector3(0.16f, 0.16f, 0.16f));
                }
            }
            banner.Build("Banners", Mats.Get(new Color(0.12f, 0.2f, 0.45f), 0.05f), root);
            emblem.Build("BannerEmblems", Mats.Get(new Color(0.95f, 0.72f, 0.2f), 0.4f), root);
            torchWood.Build("Torches", Mats.Get(new Color(0.2f, 0.13f, 0.08f), 0.1f), root);
            var flameMat = Mats.Get(new Color(1f, 0.6f, 0.15f), 0.5f);
            var fm = new Material(flameMat);
            fm.EnableKeyword("_EMISSION");
            if (fm.HasProperty("_EmissionColor")) fm.SetColor("_EmissionColor", new Color(3f, 1.4f, 0.3f));
            flames.Build("Flames", fm, root, false);

            // grass tufts in clumps, pebbles and dark scuff marks
            var grass = new Batch(); var pebbles = new Batch();
            for (int c = 0; c < 70; c++)
            {
                Vector3 cc;
                if (c < 40)
                {
                    int side = rnd.Next(4);
                    Quaternion q = Quaternion.Euler(0, 90 * side, 0);
                    cc = q * new Vector3(R(-Half, Half), 0, Half + R(-0.8f, 2.5f));
                }
                else cc = new Vector3(R(-Half + 0.5f, Half - 0.5f), 0, R(-Half + 0.5f, Half - 0.5f));
                int blades = rnd.Next(5, 11);
                for (int b = 0; b < blades; b++)
                {
                    Vector3 p = cc + new Vector3(R(-0.35f, 0.35f), 0, R(-0.35f, 0.35f));
                    float h = R(0.12f, 0.26f);
                    grass.Add(p + Vector3.up * h * 0.45f, Quaternion.Euler(R(-35, 35), R(0, 180), R(-35, 35)), new Vector3(0.025f, h, 0.025f));
                }
            }
            for (int i = 0; i < 70; i++)
            {
                Vector3 p = new Vector3(R(-WallHalf + 1, WallHalf - 1), 0, R(-WallHalf + 1, WallHalf - 1));
                float s = R(0.07f, 0.16f);
                pebbles.Add(p + Vector3.up * s * 0.3f, Quaternion.Euler(R(0, 30), R(0, 90), R(0, 30)), new Vector3(s, s * 0.7f, s));
            }
            grass.Build("Grass", Mats.Get(new Color(0.20f, 0.33f, 0.12f), 0.1f), root, false);
            pebbles.Build("Pebbles", Mats.Get(new Color(0.42f, 0.40f, 0.38f), 0.1f), root);

            return root;
        }

        static Texture2D TileTexture()
        {
            const int N = 256, tiles = 4, t = N / tiles;
            var tex = new Texture2D(N, N, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var rnd = new System.Random(3);
            var px = new Color[N * N];
            var baseC = new Color(0.86f, 0.80f, 0.66f);
            var tileShade = new float[tiles * tiles];
            for (int i = 0; i < tileShade.Length; i++) tileShade[i] = 0.9f + (float)rnd.NextDouble() * 0.14f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int tx = x / t, ty = y / t, lx = x % t, ly = y % t;
                    float shade = tileShade[ty * tiles + tx];
                    float noise = 0.96f + (float)rnd.NextDouble() * 0.06f;
                    // small inner square pattern like dressed flagstones
                    bool inner = (lx > t / 2 - 2 && lx < t / 2 + 1) || (ly > t / 2 - 2 && ly < t / 2 + 1);
                    Color c = baseC * shade * noise * (inner ? 0.96f : 1f);
                    bool grout = lx < 2 || ly < 2;
                    if (grout) c = new Color(0.62f, 0.56f, 0.45f);
                    c.a = 1;
                    px[y * N + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        static Texture2D StoneTexture()
        {
            const int W = 256, H = 128, rows = 8, bw = 32;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            var rnd = new System.Random(5);
            var px = new Color[W * H];
            int bh = H / rows;
            var shades = new float[rows * (W / bw + 1)];
            for (int i = 0; i < shades.Length; i++) shades[i] = 0.82f + (float)rnd.NextDouble() * 0.18f;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int r = y / bh;
                    int off = (r % 2) * bw / 2;
                    int bx = ((x + off) / bw) % (W / bw);
                    int lx = (x + off) % bw, ly = y % bh;
                    float s = shades[r * (W / bw + 1) + bx] * (0.95f + (float)rnd.NextDouble() * 0.06f);
                    Color c = new Color(0.78f, 0.72f, 0.66f) * s;
                    if (lx < 2 || ly < 2) c = new Color(0.45f, 0.41f, 0.38f);
                    c.a = 1;
                    px[y * W + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }
    }
}
