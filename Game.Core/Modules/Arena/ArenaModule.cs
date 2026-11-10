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

        private readonly List<Spawn> _spawns = new List<Spawn>();
        private readonly List<PropSpec> _props = new List<PropSpec>();

        public ArenaModule(GameData data)
        {
            ArenaName = data.Str("game.arena");
            var root = data.Get("arenas." + ArenaName);
            Width = root["width"]!.AsFixed();
            Height = root["height"]!.AsFixed();
            RulesName = root["rules"]!.AsString();
            foreach (var s in root["spawns"]!.Items) _spawns.Add(new Spawn(Point(s["dog"]!), Point(s["owner"]!)));
            foreach (var p in root["props"]!.Items) _props.Add(new PropSpec(p["kind"]!.AsString(), Point(p)));
        }

        private static Vec2 Point(JsonValue v) => new Vec2(v["x"]!.AsFixed(), v["y"]!.AsFixed());

        public void Init(Simulation sim) { }
        public void Step(Simulation sim, TickInputs inputs) { }
        public void Hash(ref StateHasher h) => h.Add(ArenaName);
        public IModule Clone() => this;
    }
}
