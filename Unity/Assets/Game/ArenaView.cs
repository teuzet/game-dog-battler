using System.Collections.Generic;
using Factory.Shell;
using Game.Core.Modules.Arena;
using Game.Core.Modules.Bodies;
using Game.Core.Modules.Dogs;
using Game.Core.Modules.Hits;
using Game.Core.Modules.Leashes;
using Game.Core.Modules.Match;
using Game.Core.Modules.Owners;
using Game.Core.Modules.Rules;
using Game.Core.Sim;
using UnityEngine;

namespace DogBattler.View
{
    /// <summary>Draws the state of Game.Core with primitives: arena, trees, benches, dogs, owners, leashes,
    /// the scoring circle. It only reads the simulation (sim XY lies on the ground XZ) and keeps no game
    /// logic: heights, shadows, flashes and line widths are looks derived from the state.</summary>
    public sealed class ArenaView : MonoBehaviour
    {
        public const float CameraPitch = 60f; // art/style.md → «Камера»
        public const float CameraFov = 40f;
        public const float CameraMargin = 1.2f;
        const float FlashDecayPerS = 5f;

        private sealed class DogItem
        {
            public Transform Root, Facing;
            public Renderer[] Body;
            public Color[] BodyColor;
            public Vector2 Prev, Cur;
            public float Yaw;
            public float Flash;
        }

        private sealed class OwnerItem
        {
            public Transform Root, Shadow;
            public Material ShadowMat;
            public Vector2 Prev, Cur;
            public float Airborne, PrevAirborne;
        }

        private Simulation _sim;
        private Transform _root;
        private Camera _camera;
        private DogItem[] _dogs = new DogItem[0];
        private OwnerItem[] _owners = new OwnerItem[0];
        private Transform[] _leashLines = new Transform[0];
        private Material[] _leashMats = new Material[0];
        private float[] _tension = new float[0];
        private Transform _zone, _nextZone;
        private float _dogRadius;
        private float _strongThreshold;
        private int _screenW, _screenH;

        public IReadOnlyList<Transform> DogTransforms => System.Array.ConvertAll(_dogs, d => d.Root);
        public IReadOnlyList<Transform> OwnerTransforms => System.Array.ConvertAll(_owners, o => o.Root);
        /// <summary>Each leash is a bar from the dog's collar to the owner's hand: length = distance, thickness = tension.</summary>
        public IReadOnlyList<Transform> Leashes => _leashLines;
        public Camera ViewCamera => _camera;
        /// <summary>Where the player's dog is drawn: the anchor for things that follow the player (popups).</summary>
        public Transform DogOf(int player) => _dogs[player].Root;
        public float FlashOf(int player) => _dogs[player].Flash;
        public int TreeCount { get; private set; }
        public int BenchCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindAnyObjectByType<ArenaView>() != null) return;
            new GameObject("ArenaView").AddComponent<ArenaView>();
        }

        private void Start()
        {
            var game = FactoryGame.Instance;
            game.SessionStarted += Rebuild;
            game.Stepped += Take;
            InputReader.CursorGround = CursorGround;
            if (game.Sim != null) Rebuild(game.Sim);
        }

        private void OnDestroy()
        {
            if (FactoryGame.Instance == null) return;
            FactoryGame.Instance.SessionStarted -= Rebuild;
            FactoryGame.Instance.Stepped -= Take;
        }

        private void LateUpdate()
        {
            if (_sim == null) return;
            if (Screen.width != _screenW || Screen.height != _screenH) PlaceCamera();
            Render(FactoryGame.Instance.Alpha, Time.deltaTime);
        }

        /// <summary>The ground point under the mouse cursor, in metres (cursor mode).</summary>
        public Vector2 CursorGround() => ViewMath.GroundPoint(_camera.ScreenPointToRay(Input.mousePosition));

        // ---------------------------------------------------------------- building

        /// <summary>A new session: rebuild everything from the arena and the pairs.</summary>
        public void Rebuild(Simulation sim)
        {
            _sim = sim;
            if (_root != null) Kill(_root.gameObject);
            _root = new GameObject("World").transform;
            _root.SetParent(transform, false);
            var arena = sim.Get<ArenaModule>(ArenaModule.ModuleName);
            var bodies = sim.Get<BodiesModule>(BodiesModule.ModuleName);
            var dogs = sim.Get<DogsModule>(DogsModule.ModuleName);
            var owners = sim.Get<OwnersModule>(OwnersModule.ModuleName);
            _strongThreshold = (float)sim.Get<HitsModule>(HitsModule.ModuleName).StrongThreshold.ToDouble();

            SetupLight();
            BuildGround((float)arena.Width.ToDouble(), (float)arena.Height.ToDouble());
            BuildObstacles(arena);
            _zone = Prim(PrimitiveType.Cylinder, _root, "Zone", Vector3.zero, new Vector3(1, 0.01f, 1), new Color(0.80f, 0.92f, 0.58f)).transform;
            _nextZone = Prim(PrimitiveType.Cylinder, _root, "NextZone", Vector3.zero, new Vector3(1, 0.008f, 1), new Color(0.60f, 0.76f, 0.44f)).transform;
            _zone.gameObject.SetActive(false);
            _nextZone.gameObject.SetActive(false);

            int n = sim.Players;
            _dogs = new DogItem[n];
            _owners = new OwnerItem[n];
            _leashLines = new Transform[n];
            _leashMats = new Material[n];
            _tension = new float[n];
            for (int p = 0; p < n; p++)
            {
                var color = ViewMath.PlayerColor(p);
                _dogs[p] = BuildDog(p, color, (float)bodies.Radius(dogs.BodyOf(p)).ToDouble());
                _owners[p] = BuildOwner(p, color, (float)bodies.Radius(owners.BodyOf(p)).ToDouble());
                // a line drawn as a stretched bar (LineRenderer is not used: it crashes the editor's renderer in batch mode)
                var line = Prim(PrimitiveType.Cube, _root, $"Leash {p}", Vector3.zero, Vector3.one, color);
                line.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _leashMats[p] = line.GetComponent<Renderer>().sharedMaterial;
                _leashLines[p] = line.transform;
            }
            _dogRadius = _dogs.Length > 0 ? (float)bodies.Radius(dogs.BodyOf(0)).ToDouble() : 0.35f;
            Take(sim);
            for (int p = 0; p < n; p++) { _dogs[p].Prev = _dogs[p].Cur; _owners[p].Prev = _owners[p].Cur; _owners[p].PrevAirborne = _owners[p].Airborne; }
            PlaceCamera();
            Render(0f, 0f);
        }

        private void SetupLight()
        {
            var light = FindAnyObjectByType<Light>();
            if (light == null) light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(60f, -25f, 0f);
            light.intensity = 0.75f;
            light.color = new Color(1f, 0.97f, 0.9f);
            light.shadows = LightShadows.Soft; // soft short shadows (art/style.md)
            light.shadowStrength = 0.55f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.50f);
        }

        private void BuildGround(float w, float h)
        {
            Prim(PrimitiveType.Cube, _root, "Surround", new Vector3(0, -0.1f, 0), new Vector3(w + 8f, 0.1f, h + 8f), new Color(0.78f, 0.72f, 0.60f)); // sand paths
            Prim(PrimitiveType.Cube, _root, "Lawn", new Vector3(0, -0.05f, 0), new Vector3(w, 0.1f, h), new Color(0.40f, 0.62f, 0.30f));
        }

        private void BuildObstacles(ArenaModule arena)
        {
            TreeCount = BenchCount = 0;
            foreach (var c in arena.CircleObstacles)
            {
                float r = (float)c.Radius.ToDouble();
                var at = ViewMath.ToWorld((float)c.Pos.X.ToDouble(), (float)c.Pos.Y.ToDouble());
                Prim(PrimitiveType.Cylinder, _root, "Trunk", at + Vector3.up * 1.0f, new Vector3(r * 2, 1.0f, r * 2), new Color(0.45f, 0.32f, 0.22f));
                Prim(PrimitiveType.Sphere, _root, "Crown", at + Vector3.up * 2.3f, Vector3.one * (r * 5f), new Color(0.25f, 0.48f, 0.22f));
                TreeCount++;
            }
            foreach (var b in arena.RectObstacles)
            {
                float hw = (float)b.HalfWidth.ToDouble(), hh = (float)b.HalfHeight.ToDouble();
                var at = ViewMath.ToWorld((float)b.Center.X.ToDouble(), (float)b.Center.Y.ToDouble());
                var wood = new Color(0.62f, 0.44f, 0.27f);
                Prim(PrimitiveType.Cube, _root, "Bench", at + Vector3.up * 0.25f, new Vector3(hw * 2, 0.5f, hh * 2), wood);
                bool alongX = hw >= hh;
                var backSize = alongX ? new Vector3(hw * 2, 0.5f, 0.08f) : new Vector3(0.08f, 0.5f, hh * 2);
                var backAt = at + (alongX ? new Vector3(0, 0.75f, hh - 0.04f) : new Vector3(hw - 0.04f, 0.75f, 0));
                Prim(PrimitiveType.Cube, _root, "BenchBack", backAt, backSize, wood * 0.85f);
                BenchCount++;
            }
        }

        private DogItem BuildDog(int p, Color player, float r)
        {
            var root = new GameObject($"Dog {p}").transform;
            root.SetParent(_root, false);
            var facing = new GameObject("Facing").transform;
            facing.SetParent(root, false);
            var fur = new Color(0.78f, 0.55f, 0.30f);
            var dark = new Color(0.16f, 0.15f, 0.15f);
            var body = Prim(PrimitiveType.Sphere, facing, "Body", new Vector3(0, r, 0), new Vector3(r * 1.7f, r * 1.7f, r * 2.2f), fur);
            var head = Prim(PrimitiveType.Sphere, facing, "Head", new Vector3(0, r * 1.25f, r * 1.2f), Vector3.one * (r * 1.3f), fur);
            Prim(PrimitiveType.Sphere, facing, "Nose", new Vector3(0, r * 1.2f, r * 1.8f), Vector3.one * (r * 0.3f), dark);
            Prim(PrimitiveType.Sphere, facing, "EyeL", new Vector3(-r * 0.25f, r * 1.5f, r * 1.65f), Vector3.one * (r * 0.16f), dark);
            Prim(PrimitiveType.Sphere, facing, "EyeR", new Vector3(r * 0.25f, r * 1.5f, r * 1.65f), Vector3.one * (r * 0.16f), dark);
            var collar = Prim(PrimitiveType.Cylinder, facing, "Collar", new Vector3(0, r * 1.15f, r * 0.7f), new Vector3(r * 1.25f, 0.05f, r * 1.25f), player);
            collar.transform.localRotation = Quaternion.Euler(70f, 0f, 0f); // a ring round the neck, axis along the dog
            var parts = new[] { body.GetComponent<Renderer>(), head.GetComponent<Renderer>() };
            return new DogItem { Root = root, Facing = facing, Body = parts, BodyColor = new[] { fur, fur } };
        }

        private OwnerItem BuildOwner(int p, Color player, float r)
        {
            var root = new GameObject($"Owner {p}").transform;
            root.SetParent(_root, false);
            var cloth = new Color(0.62f, 0.62f, 0.64f);
            var skin = new Color(0.93f, 0.76f, 0.62f);
            Prim(PrimitiveType.Capsule, root, "Body", new Vector3(0, 0.85f, 0), new Vector3(r * 2, 0.85f, r * 2), cloth);
            Prim(PrimitiveType.Cylinder, root, "Accent", new Vector3(0, 1.25f, 0), new Vector3(r * 2.08f, 0.16f, r * 2.08f), player); // shirt accent
            Prim(PrimitiveType.Sphere, root, "Head", new Vector3(0, 1.95f, 0), Vector3.one * (r * 1.3f), skin);
            var shadowGo = Prim(PrimitiveType.Cylinder, _root, $"Shadow {p}", Vector3.zero, new Vector3(r * 2, 0.005f, r * 2), Color.black);
            var shadowMat = NewShadowMaterial();
            shadowGo.GetComponent<Renderer>().sharedMaterial = shadowMat;
            shadowGo.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return new OwnerItem { Root = root, Shadow = shadowGo.transform, ShadowMat = shadowMat };
        }

        private void PlaceCamera()
        {
            _screenW = Screen.width;
            _screenH = Screen.height;
            if (_camera == null)
            {
                _camera = Camera.main != null ? Camera.main : new GameObject("Camera").AddComponent<Camera>();
                _camera.tag = "MainCamera";
            }
            var arena = _sim.Get<ArenaModule>(ArenaModule.ModuleName);
            _camera.orthographic = false;
            _camera.fieldOfView = CameraFov;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.70f, 0.82f, 0.92f);
            float aspect = _screenH > 0 ? (float)_screenW / _screenH : 16f / 9f;
            _camera.aspect = aspect;
            float d = ViewMath.CameraDistance((float)arena.Width.ToDouble(), (float)arena.Height.ToDouble(), CameraPitch, CameraFov, aspect, CameraMargin);
            var rot = Quaternion.Euler(CameraPitch, 0f, 0f);
            _camera.transform.SetPositionAndRotation(rot * Vector3.back * d, rot);
        }

        // ---------------------------------------------------------------- per tick

        /// <summary>The state after a tick: new interpolation targets, tension, hit flashes.</summary>
        public void Take(Simulation sim)
        {
            var bodies = sim.Get<BodiesModule>(BodiesModule.ModuleName);
            var dogs = sim.Get<DogsModule>(DogsModule.ModuleName);
            var owners = sim.Get<OwnersModule>(OwnersModule.ModuleName);
            var leashes = sim.Get<LeashesModule>(LeashesModule.ModuleName);
            for (int p = 0; p < _dogs.Length; p++)
            {
                var d = _dogs[p];
                d.Prev = d.Cur;
                var dp = dogs.Position(sim, p);
                d.Cur = new Vector2((float)dp.X.ToDouble(), (float)dp.Y.ToDouble());
                var dv = bodies.Velocity(dogs.BodyOf(p));
                if (dv.LengthSq.ToDouble() > 0.25) d.Yaw = Mathf.Atan2((float)dv.X.ToDouble(), (float)dv.Y.ToDouble()) * Mathf.Rad2Deg;

                var o = _owners[p];
                o.Prev = o.Cur;
                var op = owners.Position(sim, p);
                o.Cur = new Vector2((float)op.X.ToDouble(), (float)op.Y.ToDouble());
                o.PrevAirborne = o.Airborne;
                o.Airborne = ViewMath.Airborne((float)bodies.Velocity(owners.BodyOf(p)).Length.ToDouble(), _strongThreshold);
                _tension[p] = (float)leashes.Tension(sim, p).ToDouble();
            }
            foreach (var hit in sim.Get<HitsModule>(HitsModule.ModuleName).ThisTick)
            {
                var d = _dogs[hit.Victim];
                float mass = (float)bodies.Mass(owners.BodyOf(hit.Attacker)).ToDouble();
                d.Flash = Mathf.Max(d.Flash, ViewMath.FlashAmount((float)hit.Strength.ToDouble(), mass, _strongThreshold));
            }
        }

        // ---------------------------------------------------------------- per frame

        /// <summary>Place everything between the last two ticks (alpha 0..1) and fade the flashes.</summary>
        public void Render(float alpha, float dt)
        {
            for (int p = 0; p < _dogs.Length; p++)
            {
                var d = _dogs[p];
                var dp = Vector2.Lerp(d.Prev, d.Cur, alpha);
                d.Root.position = ViewMath.ToWorld(dp.x, dp.y);
                d.Facing.rotation = Quaternion.Euler(0f, d.Yaw, 0f);
                d.Flash = Mathf.Max(0f, d.Flash - FlashDecayPerS * dt);
                for (int i = 0; i < d.Body.Length; i++) d.Body[i].sharedMaterial.color = Color.Lerp(d.BodyColor[i], Color.white, d.Flash);

                var o = _owners[p];
                var op = Vector2.Lerp(o.Prev, o.Cur, alpha);
                float air = Mathf.Lerp(o.PrevAirborne, o.Airborne, alpha);
                o.Root.position = ViewMath.ToWorld(op.x, op.y, ViewMath.OwnerHeight(air));
                o.Root.rotation = Quaternion.Euler(0f, Mathf.Atan2(dp.x - op.x, dp.y - op.y) * Mathf.Rad2Deg, 0f); // the owner faces his dog
                float r = o.Root.Find("Body").localScale.x;
                o.Shadow.position = ViewMath.ToWorld(op.x, op.y, 0.01f);
                float s = ViewMath.ShadowScale(air);
                o.Shadow.localScale = new Vector3(r * s, 0.005f, r * s);
                var sc = o.ShadowMat.color;
                sc.a = ViewMath.ShadowAlpha(air);
                o.ShadowMat.color = sc;

                var line = _leashLines[p];
                float t = _tension[p];
                var from = new Vector3(dp.x, _dogRadius * 1.1f, dp.y);
                var to = new Vector3(op.x, 0.9f + ViewMath.OwnerHeight(air), op.y);
                float w = ViewMath.LeashWidth(t);
                var span = to - from;
                line.position = (from + to) / 2;
                if (span.sqrMagnitude > 1e-8f) line.rotation = Quaternion.LookRotation(span);
                line.localScale = new Vector3(w, w, span.magnitude);
                _leashMats[p].color = ViewMath.LeashColor(ViewMath.PlayerColor(p), t);
            }
            RenderZone();
        }

        private void RenderZone()
        {
            var rules = _sim.Get<RulesModule>(RulesModule.ModuleName).Active;
            SetZone(_zone, rules.Zone);
            SetZone(_nextZone, rules.NextZone);
        }

        private static void SetZone(Transform t, ZoneInfo? zone)
        {
            t.gameObject.SetActive(zone.HasValue);
            if (!zone.HasValue) return;
            float r = (float)zone.Value.Radius.ToDouble();
            t.position = ViewMath.ToWorld((float)zone.Value.Center.X.ToDouble(), (float)zone.Value.Center.Y.ToDouble(), 0.005f);
            t.localScale = new Vector3(r * 2, t.localScale.y, r * 2);
        }

        // ---------------------------------------------------------------- helpers

        // Matte flat colour: Diffuse is lit (Lambert); both shaders are in GraphicsSettings → Always Included,
        // so a build keeps them (Standard of the default primitive material is stripped from the demo build).
        private static Material NewMaterial(Color color) => new Material(Shader.Find("Legacy Shaders/Diffuse")) { color = color };

        /// <summary>Alpha-blended flat colour for shadows (Sprites/Default is unlit and blends by the colour's alpha).</summary>
        private static Material NewShadowMaterial() => new Material(Shader.Find("Sprites/Default")) { color = Color.black };

        private static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var collider = go.GetComponent<Collider>();
            if (collider != null) DestroyComponent(collider);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = NewMaterial(color);
            return go;
        }

        private static void DestroyComponent(Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        private static void Kill(GameObject go) => DestroyComponent(go);
    }
}
