using System.Collections.Generic;
using UnityEngine;

namespace Flopyard
{
    public enum PartId { Pelvis, Torso, Head, UpperArmL, LowerArmL, UpperArmR, LowerArmR, UpperLegL, LowerLegL, UpperLegR, LowerLegR }

    public enum FighterKind { Hero, Sack, Brute }

    public class Limb : MonoBehaviour
    {
        public Fighter owner;
        public PartId id;
    }

    /// One wobbly physics fighter: 11 box rigidbodies joined by ConfigurableJoints whose drives
    /// follow procedural poses. Upright torque and a hover spring keep it standing; a knockdown
    /// turns the muscles off so the body flops, then ramps them back on to get up.
    public class Fighter : MonoBehaviour
    {
        public const int Layer = 9;
        const int PartCount = 11;

        public FighterKind kind;
        public bool isPlayer;
        public float maxHp = 60, hp = 60;
        public bool knockedOut;
        public bool down;
        public float downTime;
        public bool wantsGetUp;
        public float speed = 3.0f;
        public float poiseMax = 30;
        public float damageScale = 1;
        public Vector3 moveInput;              // world space, horizontal, 0..1
        public Vector3 faceDir = Vector3.forward;
        public Fighter focus;                  // who we aim at
        public float koTime;

        public System.Action<Fighter, Fighter, float, bool, Vector3, bool> onHit; // attacker, victim, damage, ko, point, heavy
        public System.Action<Fighter> onDown;

        readonly Rigidbody[] rb = new Rigidbody[PartCount];
        readonly ConfigurableJoint[] joint = new ConfigurableJoint[PartCount];
        readonly float[] baseSpring = new float[PartCount];
        readonly float[] baseDamper = new float[PartCount];
        readonly Quaternion[] pose = new Quaternion[PartCount];
        readonly float[] boost = new float[PartCount];
        readonly List<Collider> colliders = new List<Collider>();

        float muscle = 1, muscleGoal = 1, appliedMuscle = -1;
        float staggerT;
        float poise;
        float phase;
        float totalMass;
        float minDown, maxDown;

        enum Atk { None, PunchL, PunchR, Kick }
        Atk atk;
        float atkT;
        bool atkLanded;
        bool nextLeft;
        readonly HashSet<Fighter> atkVictims = new HashSet<Fighter>();

        public const float StandHeight = 1.04f;
        static readonly int GroundMask = 1 << 0;

        public Rigidbody Pelvis => rb[(int)PartId.Pelvis];
        public Rigidbody Torso => rb[(int)PartId.Torso];
        public Rigidbody Head => rb[(int)PartId.Head];
        public Vector3 Position => Pelvis ? Pelvis.position : transform.position;
        public bool Busy => atk != Atk.None;
        public bool CanAct => !knockedOut && !down && muscle > 0.7f;
        public float Muscle => muscle;
        public bool Attacking => atk != Atk.None;

        // ---------------------------------------------------------------- build

        public static Fighter Spawn(FighterKind kind, Vector3 pos, float yaw, Transform parent)
        {
            var go = new GameObject(kind == FighterKind.Hero ? "Hero" : kind.ToString());
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var f = go.AddComponent<Fighter>();
            f.kind = kind;
            f.Build();
            f.faceDir = go.transform.forward;
            return f;
        }

        struct Look { public Color skin, top, sleeve, pants, shoes, fist; }

        void Build()
        {
            var L = new Look();
            switch (kind)
            {
                case FighterKind.Hero:
                    L.skin = new Color(0.86f, 0.64f, 0.48f); L.top = new Color(0.10f, 0.55f, 0.55f); L.sleeve = L.skin;
                    L.pants = new Color(0.22f, 0.17f, 0.14f); L.shoes = new Color(0.12f, 0.10f, 0.09f); L.fist = new Color(0.95f, 0.93f, 0.88f);
                    maxHp = 100; speed = 3.3f; poiseMax = 34; damageScale = 1;
                    break;
                case FighterKind.Sack:
                    L.skin = new Color(0.78f, 0.66f, 0.45f); L.top = new Color(0.70f, 0.58f, 0.38f); L.sleeve = L.skin;
                    L.pants = new Color(0.66f, 0.55f, 0.36f); L.shoes = new Color(0.45f, 0.35f, 0.22f); L.fist = L.skin;
                    maxHp = 36; speed = 1.6f; poiseMax = 20; damageScale = 0.45f;
                    break;
                default:
                    L.skin = new Color(0.93f, 0.76f, 0.60f); L.top = new Color(0.62f, 0.10f, 0.10f); L.sleeve = L.skin;
                    L.pants = new Color(0.20f, 0.20f, 0.24f); L.shoes = new Color(0.10f, 0.10f, 0.10f); L.fist = new Color(0.30f, 0.20f, 0.14f);
                    maxHp = 60; speed = 2.6f; poiseMax = 28; damageScale = 0.7f;
                    break;
            }
            hp = maxHp;

            Part(PartId.Pelvis, new Vector3(0, 1.0f, 0), new Vector3(0.5f, 0.3f, 0.3f), 10, PartId.Pelvis, Vector3.zero, 0, 0);
            Visual(PartId.Pelvis, Vector3.zero, new Vector3(0.5f, 0.3f, 0.3f), L.pants);

            Part(PartId.Torso, new Vector3(0, 1.45f, 0), new Vector3(0.6f, 0.6f, 0.32f), 12, PartId.Pelvis, new Vector3(0, 1.15f, 0), 2500, 150);
            Visual(PartId.Torso, Vector3.zero, new Vector3(0.6f, 0.6f, 0.32f), L.top);

            Part(PartId.Head, new Vector3(0, 1.95f, 0), new Vector3(0.4f, 0.4f, 0.4f), 4, PartId.Torso, new Vector3(0, 1.75f, 0), 400, 30);
            Visual(PartId.Head, Vector3.zero, new Vector3(0.4f, 0.4f, 0.4f), L.skin);

            for (int s = 0; s < 2; s++)
            {
                float x = s == 0 ? -0.42f : 0.42f;
                var ua = s == 0 ? PartId.UpperArmL : PartId.UpperArmR;
                var la = s == 0 ? PartId.LowerArmL : PartId.LowerArmR;
                Part(ua, new Vector3(x, 1.48f, 0), new Vector3(0.2f, 0.46f, 0.2f), 2, PartId.Torso, new Vector3(x, 1.71f, 0), 500, 30);
                Visual(ua, Vector3.zero, new Vector3(0.2f, 0.46f, 0.2f), L.sleeve);
                Part(la, new Vector3(x, 1.05f, 0), new Vector3(0.2f, 0.52f, 0.2f), 1.5f, ua, new Vector3(x, 1.25f, 0), 260, 16, new Vector3(0, -0.05f, 0));
                Visual(la, new Vector3(0, 0.02f, 0), new Vector3(0.18f, 0.4f, 0.18f), L.skin);
                Visual(la, new Vector3(0, -0.24f, 0), new Vector3(0.25f, 0.21f, 0.25f), L.fist);

                float lx = s == 0 ? -0.14f : 0.14f;
                var ul = s == 0 ? PartId.UpperLegL : PartId.UpperLegR;
                var ll = s == 0 ? PartId.LowerLegL : PartId.LowerLegR;
                Part(ul, new Vector3(lx, 0.64f, 0), new Vector3(0.24f, 0.44f, 0.26f), 4, PartId.Pelvis, new Vector3(lx, 0.86f, 0), 1500, 80);
                Visual(ul, Vector3.zero, new Vector3(0.24f, 0.44f, 0.26f), L.pants);
                Part(ll, new Vector3(lx, 0.22f, 0), new Vector3(0.22f, 0.42f, 0.24f), 3, ul, new Vector3(lx, 0.42f, 0), 800, 40);
                Visual(ll, new Vector3(0, 0.03f, 0), new Vector3(0.22f, 0.36f, 0.24f), L.pants);
                Visual(ll, new Vector3(0, -0.16f, 0.04f), new Vector3(0.25f, 0.1f, 0.33f), L.shoes);
            }

            Decorate(L);

            foreach (var a in colliders)
                foreach (var b in colliders)
                    if (a != b) Physics.IgnoreCollision(a, b, true);

            totalMass = 0;
            foreach (var r in rb) totalMass += r.mass;
            for (int i = 0; i < PartCount; i++) pose[i] = Quaternion.identity;
        }

        void Decorate(Look L)
        {
            var head = rb[(int)PartId.Head].transform;
            var torso = rb[(int)PartId.Torso].transform;
            var pelvis = rb[(int)PartId.Pelvis].transform;
            var black = new Color(0.07f, 0.06f, 0.06f);
            switch (kind)
            {
                case FighterKind.Hero:
                    Deco(head, new Vector3(-0.09f, 0.02f, 0.2f), new Vector3(0.07f, 0.09f, 0.02f), black);
                    Deco(head, new Vector3(0.09f, 0.02f, 0.2f), new Vector3(0.07f, 0.09f, 0.02f), black);
                    Deco(head, new Vector3(0, 0.21f, -0.02f), new Vector3(0.44f, 0.1f, 0.46f), new Color(0.16f, 0.10f, 0.07f));
                    Deco(head, new Vector3(0.05f, 0.26f, 0.06f), new Vector3(0.22f, 0.08f, 0.2f), new Color(0.16f, 0.10f, 0.07f), 20);
                    Deco(head, new Vector3(0, 0.12f, 0), new Vector3(0.43f, 0.07f, 0.43f), new Color(0.95f, 0.45f, 0.08f));
                    Deco(head, new Vector3(0.05f, 0.1f, -0.24f), new Vector3(0.06f, 0.05f, 0.14f), new Color(0.95f, 0.45f, 0.08f), -25);
                    Deco(pelvis, new Vector3(0, 0.12f, 0), new Vector3(0.52f, 0.07f, 0.32f), new Color(0.95f, 0.45f, 0.08f));
                    break;
                case FighterKind.Sack:
                    // stitched X eyes and a rope belt
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var e = new Vector3(0.09f * s, 0.03f, 0.2f);
                        DecoRot(head, e, new Vector3(0.11f, 0.025f, 0.02f), black, Quaternion.Euler(0, 0, 45));
                        DecoRot(head, e, new Vector3(0.11f, 0.025f, 0.02f), black, Quaternion.Euler(0, 0, -45));
                    }
                    Deco(head, new Vector3(0, 0.22f, 0), new Vector3(0.18f, 0.06f, 0.18f), new Color(0.55f, 0.44f, 0.28f));
                    Deco(torso, new Vector3(0, 0.05f, 0.165f), new Vector3(0.04f, 0.5f, 0.01f), new Color(0.5f, 0.4f, 0.25f));
                    Deco(pelvis, new Vector3(0, 0.12f, 0), new Vector3(0.52f, 0.06f, 0.32f), new Color(0.45f, 0.36f, 0.2f));
                    break;
                default:
                    Deco(head, new Vector3(-0.09f, 0.03f, 0.2f), new Vector3(0.08f, 0.05f, 0.02f), black);
                    Deco(head, new Vector3(0.09f, 0.03f, 0.2f), new Vector3(0.08f, 0.05f, 0.02f), black);
                    Deco(head, new Vector3(0, 0.1f, 0.205f), new Vector3(0.3f, 0.04f, 0.02f), black);
                    Deco(head, new Vector3(0, -0.13f, 0.17f), new Vector3(0.36f, 0.14f, 0.08f), new Color(0.25f, 0.15f, 0.08f));
                    Deco(pelvis, new Vector3(0, 0.12f, 0), new Vector3(0.52f, 0.07f, 0.32f), new Color(0.12f, 0.1f, 0.08f));
                    Deco(torso, new Vector3(0, 0.1f, 0.165f), new Vector3(0.18f, 0.18f, 0.01f), new Color(0.9f, 0.8f, 0.25f), 45);
                    break;
            }
        }

        void Part(PartId id, Vector3 center, Vector3 size, float mass, PartId parent, Vector3 pivot, float spring, float damper, Vector3? colliderOffset = null)
        {
            var go = new GameObject(id.ToString());
            go.layer = Layer;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = center;
            go.transform.localRotation = Quaternion.identity;
            var col = go.AddComponent<BoxCollider>();
            col.size = size;
            col.center = colliderOffset ?? Vector3.zero;
            colliders.Add(col);
            var limb = go.AddComponent<Limb>();
            limb.owner = this; limb.id = id;
            var body = go.AddComponent<Rigidbody>();
            body.mass = mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.solverIterations = 12;
            body.solverVelocityIterations = 4;
            body.maxAngularVelocity = 25;
            body.angularDamping = 0.6f;
            rb[(int)id] = body;
            if (id == PartId.Pelvis) return;

            var j = go.AddComponent<ConfigurableJoint>();
            j.connectedBody = rb[(int)parent];
            j.anchor = go.transform.InverseTransformPoint(transform.TransformPoint(pivot));
            j.autoConfigureConnectedAnchor = true;
            j.axis = Vector3.right;
            j.secondaryAxis = Vector3.up;
            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
            j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Limited;
            bool hinge = id == PartId.LowerArmL || id == PartId.LowerArmR || id == PartId.LowerLegL || id == PartId.LowerLegR;
            bool neck = id == PartId.Head || id == PartId.Torso;
            j.lowAngularXLimit = new SoftJointLimit { limit = hinge ? -150 : -130 };
            j.highAngularXLimit = new SoftJointLimit { limit = hinge ? 150 : 130 };
            j.angularYLimit = new SoftJointLimit { limit = hinge ? 15 : neck ? 45 : 70 };
            j.angularZLimit = new SoftJointLimit { limit = hinge ? 15 : neck ? 40 : 80 };
            j.rotationDriveMode = RotationDriveMode.Slerp;
            j.enablePreprocessing = false;
            joint[(int)id] = j;
            baseSpring[(int)id] = spring;
            baseDamper[(int)id] = damper;
        }

        void Visual(PartId id, Vector3 localPos, Vector3 size, Color c) => Deco(rb[(int)id].transform, localPos, size, c);

        void Deco(Transform parent, Vector3 localPos, Vector3 size, Color c, float yawDeg = 0)
            => DecoRot(parent, localPos, size, c, Quaternion.Euler(0, yawDeg, 0));

        void DecoRot(Transform parent, Vector3 localPos, Vector3 size, Color c, Quaternion rot)
        {
            var v = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(v.GetComponent<Collider>());
            v.layer = Layer;
            v.transform.SetParent(parent, false);
            v.transform.localPosition = localPos;
            v.transform.localRotation = rot;
            v.transform.localScale = size;
            v.GetComponent<MeshRenderer>().sharedMaterial = Mats.Get(c);
        }

        // ---------------------------------------------------------------- actions

        public bool Punch()
        {
            if (!CanAct || atk != Atk.None) return false;
            atk = nextLeft ? Atk.PunchL : Atk.PunchR;
            nextLeft = !nextLeft;
            atkT = 0; atkLanded = false; atkVictims.Clear();
            return true;
        }

        public bool Kick()
        {
            if (!CanAct || atk != Atk.None) return false;
            atk = Atk.Kick;
            atkT = 0; atkLanded = false; atkVictims.Clear();
            return true;
        }

        public void TakeHit(Fighter from, float dmg, Vector3 dir, Vector3 point, Rigidbody part, bool heavy)
        {
            if (knockedOut) return;
            hp -= dmg;
            poise += dmg;
            dir.y = 0; dir.Normalize();
            float push = heavy ? 7f : 4.5f;
            if (part) part.AddForce(dir * push + Vector3.up * 1.0f, ForceMode.VelocityChange);
            Torso.AddForce(dir * push * 0.5f, ForceMode.VelocityChange);
            bool ko = false;
            if (hp <= 0)
            {
                hp = 0; ko = true; knockedOut = true; koTime = Time.time;
                muscleGoal = 0; muscle = 0; ApplyMuscle(true);
                atk = Atk.None;
                float launch = heavy ? 1.35f : 1f;
                foreach (var r in rb) r.AddForce((dir * 6.5f + Vector3.up * 5f) * launch, ForceMode.VelocityChange);
                Torso.AddForce(dir * 3f, ForceMode.VelocityChange);
                Head.AddForce(dir * 3f + Vector3.up * 1.5f, ForceMode.VelocityChange);
                Torso.AddTorque(Vector3.Cross(Vector3.up, dir) * 14f, ForceMode.VelocityChange);
            }
            else if (poise >= poiseMax || (heavy && Random.value < 0.3f))
            {
                KnockDown(dir);
            }
            else
            {
                staggerT = heavy ? 0.45f : 0.28f;
            }
            from?.onHit?.Invoke(from, this, dmg, ko, point, heavy);
        }

        public void KnockDown(Vector3 dir)
        {
            poise = 0;
            down = true; downTime = 0;
            minDown = isPlayer ? 1.1f : 1.4f;
            maxDown = isPlayer ? 3.5f : 2.4f;
            wantsGetUp = false;
            atk = Atk.None;
            muscleGoal = 0.02f; muscle = 0.02f; ApplyMuscle(true);
            Torso.AddForce(dir * 5f + Vector3.up * 3f, ForceMode.VelocityChange);
            Pelvis.AddForce(dir * 3f + Vector3.up * 2f, ForceMode.VelocityChange);
            Torso.AddTorque(Vector3.Cross(Vector3.up, dir) * 10f, ForceMode.VelocityChange);
            onDown?.Invoke(this);
        }

        // ---------------------------------------------------------------- simulation

        void FixedUpdate()
        {
            if (!Pelvis) return;
            float dt = Time.fixedDeltaTime;

            if (knockedOut) { muscleGoal = 0; }
            else if (down)
            {
                downTime += dt;
                if (downTime > minDown && (wantsGetUp || downTime > maxDown)) { down = false; muscleGoal = 1; }
            }
            else
            {
                if (staggerT > 0) { staggerT -= dt; muscleGoal = 0.35f; }
                else muscleGoal = 1;
            }
            float rate = muscleGoal > muscle ? (down || muscle < 0.5f ? 1.1f : 4f) : 12f;
            muscle = Mathf.MoveTowards(muscle, muscleGoal, rate * dt);
            ApplyMuscle(false);

            UpdatePose(dt);
            for (int i = 1; i < PartCount; i++)
                if (joint[i]) JointTargets.SetTargetLocal(joint[i], pose[i], Quaternion.identity);

            if (knockedOut || muscle < 0.05f) return;

            // balance: upright PD torque on pelvis and torso, yaw towards faceDir
            Vector3 flatFace = faceDir; flatFace.y = 0;
            if (flatFace.sqrMagnitude < 0.01f) flatFace = Pelvis.transform.forward;
            Upright(Pelvis, flatFace, 1.0f);
            Upright(Torso, flatFace, 0.6f);

            // hover spring keeps the pelvis at standing height, the legs only animate
            if (Physics.Raycast(Pelvis.position + Vector3.up * 0.3f, Vector3.down, out var hit, StandHeight + 1.2f, GroundMask, QueryTriggerInteraction.Ignore))
            {
                float h = Pelvis.position.y - hit.point.y;
                float wantH = StandHeight - (atk == Atk.Kick ? 0.06f : 0f);
                float vy = Pelvis.linearVelocity.y;
                float acc = 9.81f + 70f * (wantH - h) - 12f * vy;
                acc = Mathf.Clamp(acc, 0, 40) * muscle;
                Pelvis.AddForce(Vector3.up * acc * totalMass * 0.55f, ForceMode.Force);
                Torso.AddForce(Vector3.up * acc * totalMass * 0.45f, ForceMode.Force);

                // walking: drive horizontal velocity
                Vector3 v = Pelvis.linearVelocity; v.y = 0;
                Vector3 want = Vector3.ClampMagnitude(moveInput, 1) * speed * muscle;
                if (atk != Atk.None) want *= 0.35f;
                Vector3 a = Vector3.ClampMagnitude((want - v) * 9f, 25f);
                Pelvis.AddForce(a * totalMass * 0.6f, ForceMode.Force);
                Torso.AddForce(a * totalMass * 0.4f, ForceMode.Force);
            }
        }

        void Upright(Rigidbody body, Vector3 face, float yawGain)
        {
            Vector3 up = body.transform.up;
            Vector3 axis = Vector3.Cross(up, Vector3.up);
            float ang = Vector3.Angle(up, Vector3.up) * Mathf.Deg2Rad;
            Vector3 w = body.angularVelocity;
            Vector3 acc = Vector3.zero;
            if (axis.sqrMagnitude > 1e-6f) acc += axis.normalized * ang * 320f;
            acc -= new Vector3(w.x, 0, w.z) * 32f;
            Vector3 fwd = body.transform.forward; fwd.y = 0;
            if (fwd.sqrMagnitude < 0.01f) fwd = -body.transform.up;
            float yawErr = Vector3.SignedAngle(fwd, face, Vector3.up) * Mathf.Deg2Rad;
            acc.y = (yawErr * 120f - w.y * 18f) * yawGain;
            body.AddTorque(acc * muscle, ForceMode.Acceleration);
        }

        void ApplyMuscle(bool force)
        {
            float m = muscle;
            if (!force && Mathf.Abs(m - appliedMuscle) < 0.02f && !Attacking && !AnyBoost()) return;
            appliedMuscle = m;
            for (int i = 1; i < PartCount; i++)
            {
                if (!joint[i]) continue;
                float b = 1 + boost[i];
                float floor = knockedOut ? 0.005f : 0.02f;
                float k = Mathf.Max(floor, m);
                joint[i].slerpDrive = new JointDrive
                {
                    positionSpring = baseSpring[i] * k * b,
                    positionDamper = baseDamper[i] * Mathf.Max(0.1f, k) * Mathf.Sqrt(b),
                    maximumForce = float.MaxValue
                };
            }
        }

        bool AnyBoost() { for (int i = 0; i < PartCount; i++) if (boost[i] > 0) return true; return false; }

        void UpdatePose(float dt)
        {
            for (int i = 0; i < PartCount; i++) { pose[i] = Quaternion.identity; boost[i] = 0; }
            Vector3 v = Pelvis.linearVelocity; v.y = 0;
            float s = Mathf.Clamp01(v.magnitude / 2.5f);
            phase += dt * (2f + v.magnitude * 3.2f) * (s > 0.05f ? 1 : 0.2f);
            float sw = Mathf.Sin(phase);
            float cw = Mathf.Cos(phase);
            bool guard = focus != null && !focus.knockedOut && (focus.Position - Position).sqrMagnitude < 25f;

            // legs: hip swing and knee bend, forward foot needs negative X
            pose[(int)PartId.UpperLegL] = Quaternion.Euler(-38 * s * sw - 6, 0, -3);
            pose[(int)PartId.UpperLegR] = Quaternion.Euler(38 * s * sw - 6, 0, 3);
            pose[(int)PartId.LowerLegL] = Quaternion.Euler(Mathf.Max(0, -cw) * 55 * s + 8, 0, 0);
            pose[(int)PartId.LowerLegR] = Quaternion.Euler(Mathf.Max(0, cw) * 55 * s + 8, 0, 0);

            // arms: boxing guard or walk swing
            if (guard && !down)
            {
                float bob = Mathf.Sin(phase * 1.3f) * 6;
                pose[(int)PartId.UpperArmL] = Quaternion.Euler(-45 + bob, 0, -12);
                pose[(int)PartId.UpperArmR] = Quaternion.Euler(-45 - bob, 0, 12);
                pose[(int)PartId.LowerArmL] = Quaternion.Euler(-105, 0, 0);
                pose[(int)PartId.LowerArmR] = Quaternion.Euler(-105, 0, 0);
                pose[(int)PartId.Torso] = Quaternion.Euler(8, 0, 0);
            }
            else
            {
                pose[(int)PartId.UpperArmL] = Quaternion.Euler(30 * s * sw, 0, -8);
                pose[(int)PartId.UpperArmR] = Quaternion.Euler(-30 * s * sw, 0, 8);
                pose[(int)PartId.LowerArmL] = Quaternion.Euler(-20, 0, 0);
                pose[(int)PartId.LowerArmR] = Quaternion.Euler(-20, 0, 0);
                pose[(int)PartId.Torso] = Quaternion.Euler(4 * s, 0, 0);
            }

            if (atk != Atk.None) DoAttack(dt);
        }

        Vector3 AimPoint()
        {
            if (focus != null && !focus.knockedOut) return focus.Torso.position + Vector3.up * 0.15f;
            return Torso.position + Pelvis.transform.forward * 1.5f;
        }

        void DoAttack(float dt)
        {
            atkT += dt;
            if (atk == Atk.Kick) { DoKick(); return; }

            bool left = atk == Atk.PunchL;
            var ua = left ? PartId.UpperArmL : PartId.UpperArmR;
            var la = left ? PartId.LowerArmL : PartId.LowerArmR;
            const float wind = 0.08f, strike = 0.24f, end = 0.42f;
            Vector3 aimWorld = (AimPoint() - rb[(int)ua].position).normalized;
            Vector3 aimLocal = Quaternion.Inverse(Torso.rotation) * aimWorld;
            if (atkT < wind)
            {
                pose[(int)ua] = Quaternion.Euler(-10, 0, left ? -25 : 25);
                pose[(int)la] = Quaternion.Euler(-125, 0, 0);
                pose[(int)PartId.Torso] = Quaternion.Euler(4, left ? -18 : 18, 0);
            }
            else if (atkT < strike)
            {
                if (atkT - dt < wind)
                {
                    rb[(int)la].AddForce(aimWorld * 9f, ForceMode.VelocityChange);
                    rb[(int)ua].AddForce(aimWorld * 4f, ForceMode.VelocityChange);
                    Torso.AddForce(aimWorld * 1.2f, ForceMode.VelocityChange);
                }
                pose[(int)ua] = Quaternion.FromToRotation(Vector3.down, aimLocal);
                pose[(int)la] = Quaternion.identity;
                pose[(int)PartId.Torso] = Quaternion.Euler(14, left ? 22 : -22, 0);
                boost[(int)ua] = 2.5f; boost[(int)la] = 2.5f;
                CheckHit(la, 0.34f, 1f, false);
            }
            else if (atkT >= end) atk = Atk.None;
        }

        void DoKick()
        {
            const float wind = 0.13f, strike = 0.36f, end = 0.6f;
            var ul = PartId.UpperLegR; var ll = PartId.LowerLegR;
            Vector3 aimWorld = AimPoint() - rb[(int)ul].position; aimWorld.y = Mathf.Max(aimWorld.y, 0.2f);
            aimWorld.Normalize();
            Vector3 aimLocal = Quaternion.Inverse(Pelvis.rotation) * aimWorld;
            if (atkT < wind)
            {
                pose[(int)ul] = Quaternion.Euler(-55, 0, 0);
                pose[(int)ll] = Quaternion.Euler(100, 0, 0);
                pose[(int)PartId.Torso] = Quaternion.Euler(-12, 0, 0);
                pose[(int)PartId.UpperArmL] = Quaternion.Euler(-20, 0, -40);
                pose[(int)PartId.UpperArmR] = Quaternion.Euler(-20, 0, 40);
            }
            else if (atkT < strike)
            {
                if (atkT - Time.fixedDeltaTime < wind)
                {
                    rb[(int)ll].AddForce(aimWorld * 10f, ForceMode.VelocityChange);
                    rb[(int)ul].AddForce(aimWorld * 4f, ForceMode.VelocityChange);
                }
                pose[(int)ul] = Quaternion.FromToRotation(Vector3.down, aimLocal);
                pose[(int)ll] = Quaternion.identity;
                pose[(int)PartId.Torso] = Quaternion.Euler(-18, 0, 0);
                pose[(int)PartId.UpperArmL] = Quaternion.Euler(-30, 0, -55);
                pose[(int)PartId.UpperArmR] = Quaternion.Euler(30, 0, 55);
                boost[(int)ul] = 1.5f; boost[(int)ll] = 1.5f;
                CheckHit(ll, 0.38f, 1.8f, true);
            }
            else if (atkT >= end) atk = Atk.None;
        }

        static readonly Collider[] overlap = new Collider[32];

        void CheckHit(PartId limb, float radius, float dmgMul, bool heavy)
        {
            var t = rb[(int)limb].transform;
            Vector3 tip = t.TransformPoint(0, -0.27f, 0.03f);
            int n = Physics.OverlapSphereNonAlloc(tip, radius, overlap, 1 << Layer, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var other = overlap[i].GetComponent<Limb>();
                if (!other || other.owner == this || other.owner.knockedOut || atkVictims.Contains(other.owner)) continue;
                if (!isPlayer && !other.owner.isPlayer) continue; // enemies don't brawl each other
                atkVictims.Add(other.owner);
                atkLanded = true;
                Vector3 dir = other.owner.Position - Position;
                float dmg = (heavy ? 20f : 12f) * damageScale * Random.Range(0.85f, 1.15f) * (dmgMul > 1 ? 1 : 1);
                other.owner.TakeHit(this, dmg, dir, overlap[i].ClosestPoint(tip), other.GetComponent<Rigidbody>(), heavy);
            }
        }

        public void ResetHealth() { hp = maxHp; poise = 0; }
    }

    public static class Mats
    {
        static readonly Dictionary<Color, Material> cache = new Dictionary<Color, Material>();
        static Shader lit;

        public static Shader Lit
        {
            get
            {
                if (!lit) lit = Shader.Find("Universal Render Pipeline/Lit");
                if (!lit) lit = Shader.Find("Standard");
                return lit;
            }
        }

        public static Material Get(Color c, float smooth = 0.15f)
        {
            if (cache.TryGetValue(c, out var m) && m) return m;
            m = new Material(Lit) { color = c, enableInstancing = true };
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            cache[c] = m;
            return m;
        }

        public static Material Textured(Texture2D tex, Vector2 tiling, float smooth = 0.1f)
        {
            var m = new Material(Lit) { color = Color.white };
            if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", tiling); }
            m.mainTexture = tex;
            m.mainTextureScale = tiling;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            return m;
        }
    }
}
