using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using GameTouch;

namespace Flopyard
{
    /// The whole game: builds the courtyard, spawns the hero and waves of sparring opponents,
    /// reads input (or the demo bot), runs the camera, hit effects, grey "down" screen and HUD.
    public class FlopyardGame : MonoBehaviour
    {
        public static FlopyardGame Instance { get; private set; }

        [Header("Text")]
        public TMP_FontAsset font;
        public Material outlineMaterial;
        public Material bigOutlineMaterial;

        [Header("Play")]
        public bool autoPlay;          // the demo bot plays the hero

        public Fighter hero;
        public readonly List<Fighter> enemies = new List<Fighter>();
        public int kos;
        public int heroDowns;
        public int best;
        public float runTime;

        Camera cam;
        Transform world;
        Volume volume;
        ColorAdjustments colorAdj;
        float grey, greyGoal;
        float shake;
        Vector3 camFocus;
        float camYaw;
        bool restarting;
        float spawnTimer;
        int spawned;
        float heroCooldown;
        float heroThink;
        Vector3 botStrafe;

        // HUD
        RectTransform hpFill;
        TextMeshProUGUI kosText, promptText, bannerText, titleText;
        float bannerT;
        RectTransform canvasRect;
        readonly List<(TextMeshProUGUI t, Vector3 world, float age)> popups = new List<(TextMeshProUGUI, Vector3, float)>();
        ParticleSystem dust, sparks, flash;

        InputAction moveAction;

        static readonly string[] HitWords = { "WHAM!", "BONK!", "POW!", "THWACK!", "SMACK!" };

        void Awake()
        {
            Instance = this;
            Time.fixedDeltaTime = 0.01f;
            Physics.defaultMaxAngularSpeed = 25;
            world = new GameObject("World").transform;
            Arena.Build(world);
            BuildLightAndCamera();
            BuildFx();
            BuildHud();

            moveAction = new InputAction("Move", InputActionType.Value);
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow").With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            moveAction.AddBinding("<Gamepad>/leftStick");
            moveAction.Enable();

            // phones: a stick on the left half, PUNCH and KICK on the right, usable at the same time
            touch = TouchPad.Create(font, outlineMaterial);
            touch.AddStick(new Rect(0, 0, 0.5f, 0.75f), new Vector2(0, 0), new Vector2(250, 330), 150);
            punchBtn = touch.AddButton("PUNCH", new Vector2(1, 0), new Vector2(-200, 380), 130, new Color(1f, 0.78f, 0.25f), 46);
            kickBtn = touch.AddButton("KICK", new Vector2(1, 0), new Vector2(-450, 230), 110, new Color(0.45f, 0.8f, 1f), 42);
        }

        TouchPad touch;
        TouchPad.Button punchBtn, kickBtn;

        void OnDestroy()
        {
            moveAction?.Disable();
            moveAction?.Dispose();
            Time.timeScale = 1;
        }

        void Start() => StartRun();

        // ---------------------------------------------------------------- run

        public void StartRun()
        {
            StopAllCoroutines();
            Time.timeScale = 1;
            restarting = false;
            foreach (var e in enemies) if (e) Destroy(e.gameObject);
            enemies.Clear();
            if (hero) Destroy(hero.gameObject);
            hero = Fighter.Spawn(FighterKind.Hero, new Vector3(0, 0, -4.5f), 0, world);
            hero.isPlayer = true;
            hero.onHit = OnHit;
            hero.onDown = f => { heroDowns++; };
            kos = 0; spawned = 0; runTime = 0;
            spawnTimer = 0.05f;
            greyGoal = 0; grey = 0;
            camYaw = 0; camFocus = hero.Position;
            bannerT = 0;
            UpdateHud();
        }

        void SpawnEnemy()
        {
            FighterKind kind = spawned == 0 ? FighterKind.Sack : (spawned % 3 == 2 ? FighterKind.Sack : FighterKind.Brute);
            // walk in through the far gate side, away from the hero
            Vector3 heroPos = hero ? hero.Position : Vector3.zero;
            Vector3 best = Vector3.zero; float bestD = -1;
            for (int i = 0; i < 6; i++)
            {
                var p = new Vector3(Random.Range(-5f, 5f), 0, Random.Range(1.5f, 6f));
                float d = (p - heroPos).sqrMagnitude;
                if (d > bestD) { bestD = d; best = p; }
            }
            if (spawned == 0) best = new Vector3(0.3f, 0, -2.6f);
            Vector3 to = heroPos - best; to.y = 0;
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            var e = Fighter.Spawn(kind, best, yaw, world);
            e.onHit = OnHit;
            e.focus = hero;
            enemies.Add(e);
            spawned++;
        }

        int Alive()
        {
            int n = 0;
            foreach (var e in enemies) if (e && !e.knockedOut) n++;
            return n;
        }

        int MaxAlive() => kos < 1 ? 1 : kos < 6 ? 2 : 3;

        // ---------------------------------------------------------------- loop

        void Update()
        {
            if (!hero) return;
            float dt = Time.deltaTime;
            runTime += dt;

            // spawning and clearing bodies
            if (!hero.knockedOut)
            {
                if (Alive() < MaxAlive())
                {
                    spawnTimer -= dt;
                    if (spawnTimer <= 0) { SpawnEnemy(); spawnTimer = 1.4f; }
                }
            }
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                var e = enemies[i];
                if (!e) { enemies.RemoveAt(i); continue; }
                if (e.knockedOut && Time.time - e.koTime > 4.5f)
                {
                    Burst(dust, e.Position, 18, new Color(0.85f, 0.78f, 0.62f));
                    Destroy(e.gameObject);
                    enemies.RemoveAt(i);
                }
            }

            HeroControl(dt);
            foreach (var e in enemies) if (e && !e.knockedOut) EnemyBrain(e, dt);

            // grey screen when the hero is down
            greyGoal = hero.down || hero.knockedOut ? 1 : 0;
            grey = Mathf.MoveTowards(grey, greyGoal, Time.unscaledDeltaTime * (greyGoal > grey ? 5f : 2.5f));
            if (colorAdj != null) colorAdj.saturation.value = Mathf.Lerp(12f, -100f, grey);

            if (hero.knockedOut && !restarting) StartCoroutine(RestartSoon());
            UpdateHud();
        }

        IEnumerator RestartSoon()
        {
            restarting = true;
            best = Mathf.Max(best, kos);
            ShowBanner("KNOCKED OUT", 3f, 104);
            yield return new WaitForSecondsRealtime(3.2f);
            StartRun();
        }

        Fighter NearestEnemy(Vector3 from, float maxDist = 100)
        {
            Fighter bestF = null; float bd = maxDist * maxDist;
            foreach (var e in enemies)
            {
                if (!e || e.knockedOut) continue;
                float d = (e.Position - from).sqrMagnitude;
                if (d < bd) { bd = d; bestF = e; }
            }
            return bestF;
        }

        void HeroControl(float dt)
        {
            if (touch) touch.hidden = autoPlay;
            if (hero.knockedOut) { hero.moveInput = Vector3.zero; return; }
            var target = NearestEnemy(hero.Position, 12);
            hero.focus = target;
            heroCooldown -= dt;

            Vector3 camFwd = Quaternion.Euler(0, camYaw, 0) * Vector3.forward;
            Vector3 camRight = Quaternion.Euler(0, camYaw, 0) * Vector3.right;

            if (autoPlay)
            {
                BotHero(target, dt);
                return;
            }

            Vector2 m = moveAction.ReadValue<Vector2>();
            if (touch && touch.StickHeld) m = Vector2.ClampMagnitude(m + touch.Stick, 1f);
            Vector3 move = camFwd * m.y + camRight * m.x;
            hero.moveInput = Vector3.ClampMagnitude(move, 1);
            hero.wantsGetUp = m.sqrMagnitude > 0.04f;
            if (target) { var d = target.Position - hero.Position; d.y = 0; hero.faceDir = d.normalized; }
            else if (move.sqrMagnitude > 0.01f) hero.faceDir = move.normalized;

            var kb = Keyboard.current; var pad = Gamepad.current;
            var mouse = TouchPad.RecentTouch ? null : Mouse.current; // ignore mouse clicks a browser emulates from taps
            bool punch = (mouse != null && mouse.leftButton.wasPressedThisFrame) || (kb != null && (kb.jKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) || (pad != null && pad.buttonWest.wasPressedThisFrame);
            bool kick = (mouse != null && mouse.rightButton.wasPressedThisFrame) || (kb != null && kb.kKey.wasPressedThisFrame) || (pad != null && pad.buttonNorth.wasPressedThisFrame);
            if (touch) { punch |= punchBtn.pressed; kick |= kickBtn.pressed; }
            if (punch) hero.Punch();
            if (kick) hero.Kick();
            if (kb != null && kb.rKey.wasPressedThisFrame) StartRun();
        }

        void BotHero(Fighter target, float dt)
        {
            heroThink -= dt;
            if (hero.down) { hero.wantsGetUp = hero.downTime > 1.3f; hero.moveInput = hero.wantsGetUp ? hero.faceDir : Vector3.zero; return; }
            hero.wantsGetUp = false;
            if (!target)
            {
                Vector3 home = new Vector3(0, 0, -2f) - hero.Position; home.y = 0;
                hero.moveInput = home.magnitude > 0.6f ? home.normalized * 0.6f : Vector3.zero;
                return;
            }
            Vector3 d = target.Position - hero.Position; d.y = 0;
            float dist = d.magnitude;
            Vector3 dir = d / Mathf.Max(dist, 0.01f);
            hero.faceDir = dir;
            if (heroThink <= 0)
            {
                heroThink = Random.Range(0.5f, 1.1f);
                botStrafe = Vector3.Cross(Vector3.up, dir) * Random.Range(-0.5f, 0.5f);
            }
            if (dist > 1.35f) hero.moveInput = dir + botStrafe * 0.4f;
            else if (dist < 0.95f) hero.moveInput = -dir * 0.5f;
            else hero.moveInput = botStrafe * 0.6f;

            if (dist < 1.75f && heroCooldown <= 0 && !hero.Busy)
            {
                bool kick = Random.value < 0.3f && dist > 1.0f;
                if (kick ? hero.Kick() : hero.Punch()) heroCooldown = kick ? Random.Range(0.55f, 0.8f) : Random.Range(0.3f, 0.5f);
            }
        }

        void EnemyBrain(Fighter e, float dt)
        {
            if (!hero || hero.knockedOut)
            {
                e.moveInput = Vector3.zero;
                return;
            }
            e.focus = hero;
            if (e.down) { e.wantsGetUp = true; return; }
            Vector3 d = hero.Position - e.Position; d.y = 0;
            float dist = d.magnitude;
            Vector3 dir = d / Mathf.Max(dist, 0.01f);
            e.faceDir = dir;

            // keep apart from other opponents
            Vector3 sep = Vector3.zero;
            foreach (var o in enemies)
            {
                if (!o || o == e || o.knockedOut) continue;
                Vector3 a = e.Position - o.Position; a.y = 0;
                float m = a.magnitude;
                if (m < 1.6f && m > 0.01f) sep += a / m * (1.6f - m);
            }
            float reach = e.kind == FighterKind.Sack ? 1.1f : 1.3f;
            Vector3 mv = dist > reach ? dir : (dist < 0.9f ? -dir * 0.4f : Vector3.zero);
            e.moveInput = Vector3.ClampMagnitude(mv + sep, 1);

            // attack timing kept in the fighter's poise-free cooldown via a hashed timer
            if (!attackTimers.TryGetValue(e, out float t)) t = Random.Range(0.8f, 1.5f);
            t -= dt;
            if (t <= 0 && dist < reach + 0.45f && hero.CanAct)
            {
                bool started = e.kind == FighterKind.Brute && Random.value < 0.25f ? e.Kick() : e.Punch();
                t = started ? (e.kind == FighterKind.Sack ? Random.Range(1.6f, 2.6f) : Random.Range(0.9f, 1.6f)) : 0.2f;
            }
            attackTimers[e] = t;
        }

        readonly Dictionary<Fighter, float> attackTimers = new Dictionary<Fighter, float>();

        void OnHit(Fighter attacker, Fighter victim, float dmg, bool ko, Vector3 point, bool heavy)
        {
            Burst(sparks, point, heavy ? 24 : 16, new Color(1f, 0.95f, 0.75f));
            Burst(flash, point, 1, new Color(1f, 1f, 0.9f, 0.9f));
            Burst(dust, point, heavy ? 10 : 6, new Color(0.9f, 0.85f, 0.7f));
            shake = Mathf.Max(shake, ko ? 0.35f : heavy ? 0.22f : 0.12f);
            if (attacker == hero)
            {
                Popup(ko ? "K.O.!" : HitWords[Random.Range(0, HitWords.Length)], point + Vector3.up * 0.3f, ko ? 1.4f : 1f);
                if (ko)
                {
                    kos++;
                    ShowBanner("KO  x" + kos, 1.1f);
                    StartCoroutine(HitStop(0.25f, 0.55f));
                }
                else StartCoroutine(HitStop(0.08f, heavy ? 0.09f : 0.05f));
            }
            else
            {
                StartCoroutine(HitStop(0.2f, 0.05f));
            }
        }

        IEnumerator HitStop(float scale, float seconds)
        {
            Time.timeScale = scale;
            yield return new WaitForSecondsRealtime(seconds);
            Time.timeScale = 1;
        }

        // ---------------------------------------------------------------- camera

        void BuildLightAndCamera()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(52, -35, 0);
            sun.color = new Color(1f, 0.94f, 0.84f);
            sun.intensity = 1.5f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.transform.SetParent(world, true);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.70f, 0.85f);
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.58f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.40f, 0.36f, 0.31f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.78f, 0.84f, 0.92f);
            RenderSettings.fogStartDistance = 25; RenderSettings.fogEndDistance = 70;

            cam = Camera.main;
            if (!cam)
            {
                var cgo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = cgo.AddComponent<Camera>();
                cgo.AddComponent<AudioListener>();
            }
            cam.fieldOfView = 62;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 120;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.45f, 0.68f, 0.95f);
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

            var vgo = new GameObject("PostFX");
            vgo.transform.SetParent(world, false);
            volume = vgo.AddComponent<Volume>();
            volume.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            colorAdj = profile.Add<ColorAdjustments>(true);
            colorAdj.saturation.Override(12f);
            colorAdj.contrast.Override(8f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.22f);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            volume.sharedProfile = profile;
        }

        void LateUpdate()
        {
            if (!hero || !cam) return;
            float dt = Time.unscaledDeltaTime;
            Vector3 p = hero.Torso.position;
            camFocus = Vector3.Lerp(camFocus, p, 1 - Mathf.Exp(-6f * dt));
            var target = hero.focus;
            Vector3 look = hero.faceDir;
            if (target && !target.knockedOut) { look = target.Position - camFocus; }
            look.y = 0;
            if (look.sqrMagnitude > 0.01f)
            {
                float want = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
                camYaw = Mathf.LerpAngle(camYaw, want, 1 - Mathf.Exp(-2.2f * dt));
            }
            Quaternion yaw = Quaternion.Euler(0, camYaw, 0);
            Vector3 back = yaw * new Vector3(0.45f, 1.85f, -2.55f);
            Vector3 pos = camFocus + back;
            Vector3 aim = camFocus + yaw * new Vector3(0.1f, -0.35f, 2.4f);
            shake = Mathf.MoveTowards(shake, 0, dt * 1.2f);
            Vector3 jitter = Random.insideUnitSphere * shake * 0.25f;
            cam.transform.position = pos + jitter;
            cam.transform.rotation = Quaternion.LookRotation(aim - pos + jitter * 0.5f, Vector3.up);

            UpdatePopups(dt);
        }

        // ---------------------------------------------------------------- fx

        void BuildFx()
        {
            dust = MakeParticles("Dust", 0.35f, 0.7f, 2.2f, 0.25f, -0.15f);
            sparks = MakeParticles("Sparks", 0.16f, 0.28f, 8f, 0.4f, 0.6f);
            flash = MakeParticles("Flash", 0.9f, 0.12f, 0f, 0.2f, 0f);
        }

        ParticleSystem MakeParticles(string name, float size, float life, float speed, float sizeVar, float gravity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(world, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.maxParticles = 300;
            main.startSize = new ParticleSystem.MinMaxCurve(size * (1 - sizeVar), size * (1 + sizeVar));
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.05f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, 0.6f, 1, 1.3f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (!sh) sh = Shader.Find("Sprites/Default");
            var m = new Material(sh);
            m.mainTexture = SoftDot();
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", m.mainTexture);
            if (m.HasProperty("_Surface")) { m.SetFloat("_Surface", 1); m.SetOverrideTag("RenderType", "Transparent"); }
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
            r.sharedMaterial = m;
            return ps;
        }

        static Texture2D softDot;
        static Texture2D SoftDot()
        {
            if (softDot) return softDot;
            const int N = 32;
            softDot = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N / 2f, N / 2f)) / (N / 2f);
                    softDot.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1 - d) * Mathf.Clamp01(1 - d)));
                }
            softDot.Apply();
            return softDot;
        }

        void Burst(ParticleSystem ps, Vector3 at, int count, Color c)
        {
            if (!ps) return;
            var ep = new ParticleSystem.EmitParams { position = at, applyShapeToPosition = true, startColor = c };
            ps.Emit(ep, count);
        }

        // ---------------------------------------------------------------- HUD

        void BuildHud()
        {
            var cgo = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler));
            cgo.transform.SetParent(world, false);
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            canvasRect = cgo.GetComponent<RectTransform>();

            titleText = Text("Title", "FLOPYARD", 96, new Vector2(0.5f, 1), new Vector2(0, -70), new Vector2(900, 120), bigOutlineMaterial, new Color(1f, 0.78f, 0.25f));
            kosText = Text("KOs", "KOs 0", 70, new Vector2(0.5f, 1), new Vector2(0, -175), new Vector2(700, 90), outlineMaterial, Color.white);

            // health bar
            var bg = new GameObject("HpBack", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(cgo.transform, false);
            var bgr = bg.GetComponent<RectTransform>();
            bgr.anchorMin = bgr.anchorMax = new Vector2(0.5f, 1);
            bgr.pivot = new Vector2(0.5f, 0.5f);
            bgr.anchoredPosition = new Vector2(0, -245);
            bgr.sizeDelta = new Vector2(520, 30);
            bg.GetComponent<Image>().color = new Color(0.08f, 0.06f, 0.05f, 0.85f);
            var fill = new GameObject("HpFill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(bg.transform, false);
            hpFill = fill.GetComponent<RectTransform>();
            hpFill.anchorMin = new Vector2(0, 0); hpFill.anchorMax = new Vector2(1, 1);
            hpFill.offsetMin = new Vector2(4, 4); hpFill.offsetMax = new Vector2(-4, -4);
            fill.GetComponent<Image>().color = new Color(0.35f, 0.85f, 0.35f);

            promptText = Text("Prompt", "DOWN!\nHOLD A DIRECTION TO GET UP", 82, new Vector2(0.5f, 0.5f), new Vector2(0, 260), new Vector2(1040, 240), outlineMaterial, Color.white);
            bannerText = Text("Banner", "", 150, new Vector2(0.5f, 0.5f), new Vector2(0, 520), new Vector2(1000, 220), bigOutlineMaterial, new Color(1f, 0.85f, 0.3f));
        }

        TextMeshProUGUI Text(string name, string s, float size, Vector2 anchor, Vector2 pos, Vector2 box, Material mat, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(canvasRect, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var t = go.GetComponent<TextMeshProUGUI>();
            if (font) t.font = font;
            if (mat) t.fontSharedMaterial = mat;
            t.text = s;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            return t;
        }

        void ShowBanner(string s, float seconds, float size = 150)
        {
            bannerText.text = s;
            bannerText.fontSize = size;
            bannerT = seconds;
        }

        void Popup(string s, Vector3 worldPos, float scale)
        {
            var t = Text("Popup", s, 118 * scale, new Vector2(0, 0), Vector2.zero, new Vector2(900, 260 * scale), bigOutlineMaterial, new Color(1f, 0.92f, 0.4f));
            popups.Add((t, worldPos, 0));
        }

        void UpdatePopups(float dt)
        {
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                var (t, w, age) = popups[i];
                age += dt;
                if (age > 0.75f || !t) { if (t) Destroy(t.gameObject); popups.RemoveAt(i); continue; }
                popups[i] = (t, w, age);
                Vector3 sp = cam.WorldToScreenPoint(w + Vector3.up * age * 0.6f);
                if (sp.z < 0) { t.enabled = false; continue; }
                t.enabled = true;
                Vector2 local;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, sp, null, out local);
                var rt = t.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = local;
                float pop = age < 0.1f ? Mathf.Lerp(0.4f, 1.15f, age / 0.1f) : Mathf.Lerp(1.15f, 1f, (age - 0.1f) / 0.2f);
                rt.localScale = Vector3.one * pop;
                var c = t.color; c.a = age > 0.55f ? 1 - (age - 0.55f) / 0.2f : 1; t.color = c;
            }
        }

        void UpdateHud()
        {
            if (!kosText) return;
            kosText.text = "KOs " + kos;
            if (hero && hpFill)
            {
                float f = Mathf.Clamp01(hero.hp / hero.maxHp);
                hpFill.anchorMax = new Vector2(Mathf.Max(0.001f, f), 1);
                hpFill.GetComponent<Image>().color = Color.Lerp(new Color(0.9f, 0.25f, 0.2f), new Color(0.35f, 0.85f, 0.35f), f);
            }
            promptText.enabled = hero && hero.down && !hero.knockedOut;
            bannerT -= Time.unscaledDeltaTime;
            bannerText.enabled = bannerT > 0;
            if (bannerT > 0) bannerText.rectTransform.localScale = Vector3.one * (1 + Mathf.Max(0, bannerT - 0.9f) * 0.8f);
        }
    }
}
