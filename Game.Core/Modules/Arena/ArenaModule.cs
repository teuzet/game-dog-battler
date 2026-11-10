using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Sim;

namespace Game.Core.Modules.Arena
{
    public readonly struct Spawn
    {
        public readonly Vec2 Dog, Owner;
        public Spawn(Vec2 dog, Vec2 owner) { Dog = dog; Owner = owner; }
    }

    /// <summary>A static round obstacle (a tree).</summary>
    public readonly struct CircleObstacle
    {
        public readonly Vec2 Pos;
        public readonly Fixed Radius;
        public CircleObstacle(Vec2 pos, Fixed radius) { Pos = pos; Radius = radius; }
    }

    /// <summary>A static axis-aligned rectangular obstacle (a bench), by centre and half-sizes.</summary>
    public readonly struct RectObstacle
    {
        public readonly Vec2 Center;
        public readonly Fixed HalfWidth, HalfHeight;
        public RectObstacle(Vec2 center, Fixed halfWidth, Fixed halfHeight) { Center = center; HalfWidth = halfWidth; HalfHeight = halfHeight; }
    }

    /// <summary>An object of the arena as listed in its json; Props turns it into a thing.</summary>
    public readonly struct PropSpec
    {
        public readonly string Kind;
        public readonly Vec2 Pos;
        public PropSpec(string kind, Vec2 pos) { Kind = kind; Pos = pos; }
    }

    /// <summary>The geometry of the chosen arena (Data/arenas/&lt;name&gt;.json, name in
    /// "game.arena"). The origin is the centre of the arena, the X axis runs along the width.
    /// It never changes during a session, so it has no state to hash or clone.</summary>
    public sealed class ArenaModule : IModule
    {
        public const string ModuleName = "Arena";
        public string Name => ModuleName;
        public IReadOnlyList<string> DependsOn { get; } = new string[0];

        public string ArenaName { get; }
        public Fixed Width { get; }
        public Fixed Height { get; }
        public string RulesName { get; }
        public IReadOnlyList<Spawn> Spawns => _spawns;
        public IReadOnlyList<PropSpec> Props => _props;
        public IReadOnlyList<CircleObstacle> CircleObstacles => _circles;
        public IReadOnlyList<RectObstacle> RectObstacles => _rects;

        private readonly List<Spawn> _spawns = new List<Spawn>();
        private readonly List<PropSpec> _props = new List<PropSpec>();
        private readonly List<CircleObstacle> _circles = new List<CircleObstacle>();
        private readonly List<RectObstacle> _rects = new List<RectObstacle>();

        public ArenaModule(GameData data)
        {
            ArenaName = data.Str("game.arena");
            var root = data.Get("arenas." + ArenaName);
            Width = root["width"]!.AsFixed();
            Height = root["height"]!.AsFixed();
            RulesName = root["rules"]!.AsString();
            foreach (var s in root["spawns"]!.Items) _spawns.Add(new Spawn(Point(s["dog"]!), Point(s["owner"]!)));
            foreach (var o in root["obstacles"]!.Items)
            {
                string shape = o["shape"]!.AsString();
                if (shape == "circle") _circles.Add(new CircleObstacle(Point(o), o["radius"]!.AsFixed()));
                else if (shape == "rect") _rects.Add(new RectObstacle(Point(o), o["width"]!.AsFixed() / 2, o["height"]!.AsFixed() / 2));
                else throw new KeyNotFoundException($"arena '{ArenaName}': unknown obstacle shape '{shape}'");
            }
            foreach (var p in root["props"]!.Items) _props.Add(new PropSpec(p["kind"]!.AsString(), Point(p)));
        }

        private static Vec2 Point(JsonValue v) => new Vec2(v["x"]!.AsFixed(), v["y"]!.AsFixed());

        public void Init(Simulation sim) { }
        public void Step(Simulation sim, TickInputs inputs) { }
        public void Hash(ref StateHasher h) => h.Add(ArenaName);
        public IModule Clone() => this;
    }
}
