using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Math;

namespace Game.Core.Sim
{
    /// <summary>The deterministic session. Unity never holds game logic; it
    /// drives this. Same seed, data and inputs give the same hash on every
    /// machine; a fork is an independent copy the bots and the pace map use
    /// to play options out.</summary>
    public sealed class Simulation
    {
        public const int TicksPerSecond = 30;

        public int Tick { get; private set; }
        public int Seed { get; }
        public int Players { get; }
        public GameData Data { get; }
        public EventLog Events { get; private set; } = new EventLog();
        public SessionResult? Result { get; private set; }
        public bool IsOver => Result != null;
        /// <summary>Hard cap: a session always ends. From data "game.session.max_seconds".</summary>
        public int MaxTicks { get; }

        private readonly IModule[] _modules;
        private readonly DetRandom[] _rngs; // one stream per module, by tick order
        private readonly Dictionary<string, IModule> _byName = new Dictionary<string, IModule>();

        public Simulation(int seed, int players, GameData data, IReadOnlyList<IModule> modules)
        {
            Seed = seed;
            Players = players;
            Data = data;
            MaxTicks = data.Has("game.session.max_seconds") ? data.Int("game.session.max_seconds") * TicksPerSecond : 10 * 60 * TicksPerSecond;
            _modules = new IModule[modules.Count];
            _rngs = new DetRandom[modules.Count];
            for (int i = 0; i < modules.Count; i++)
            {
                _modules[i] = modules[i];
                _rngs[i] = DetRandom.Derive(seed, modules[i].Name);
                if (_byName.ContainsKey(modules[i].Name)) throw new ArgumentException($"module {modules[i].Name} twice");
                _byName[modules[i].Name] = modules[i];
            }
            foreach (var m in _modules)
                foreach (var dep in m.DependsOn)
                    if (!_byName.ContainsKey(dep)) throw new ArgumentException($"{m.Name} depends on unknown module {dep}");
            foreach (var m in _modules) m.Init(this);
        }

        private Simulation(Simulation src)
        {
            Seed = src.Seed;
            Players = src.Players;
            Data = src.Data;
            _rngs = (DetRandom[])src._rngs.Clone();
            Tick = src.Tick;
            MaxTicks = src.MaxTicks;
            Result = src.Result;
            Events = new EventLog { Tick = src.Tick };
            _modules = new IModule[src._modules.Length];
            for (int i = 0; i < _modules.Length; i++)
            {
                _modules[i] = src._modules[i].Clone();
                _byName[_modules[i].Name] = _modules[i];
            }
        }

        public IReadOnlyList<IModule> Modules => _modules;

        /// <summary>The module's own random stream: <c>sim.Rng(this).Chance(p)</c>.</summary>
        public ref DetRandom Rng(IModule module)
        {
            for (int i = 0; i < _modules.Length; i++)
                if (ReferenceEquals(_modules[i], module)) return ref _rngs[i];
            throw new InvalidOperationException($"{module.Name} is not a module of this simulation");
        }

        /// <summary>Another module, for <paramref name="caller"/>, which must name it in DependsOn.</summary>
        public T Need<T>(IModule caller, string name) where T : class, IModule
        {
            bool declared = false;
            foreach (var d in caller.DependsOn) if (d == name) { declared = true; break; }
            if (!declared) throw new InvalidOperationException($"{caller.Name} reads {name} but does not list it in DependsOn (and MODULES.md)");
            return Get<T>(name);
        }

        /// <summary>A module by name, for code outside the modules (game rules, bots' sensors, Unity, tests).</summary>
        public T Get<T>(string name) where T : class, IModule =>
            _byName.TryGetValue(name, out var m) && m is T t ? t : throw new KeyNotFoundException($"no module {name} of type {typeof(T).Name}");

        public void Step(TickInputs inputs)
        {
            if (IsOver) return;
            Tick++;
            Events.Tick = Tick;
            foreach (var m in _modules)
            {
                m.Step(this, inputs);
                if (IsOver) return;
            }
            if (Tick >= MaxTicks) End(SessionResult.Draw(Players, "время вышло"));
        }

        /// <summary>Called by the module that owns the rule of the ending.</summary>
        public void End(SessionResult result)
        {
            if (Result != null) return;
            Result = result;
            Events.Add("end", -1, result.Why);
        }

        public ulong StateHash()
        {
            var h = StateHasher.Create();
            h.Add(Tick);
            foreach (var r in _rngs) h.Add(r.State);
            h.Add(Result != null);
            foreach (var m in _modules) { h.Add(m.Name); m.Hash(ref h); }
            return h.Value;
        }

        /// <summary>An independent copy with an empty event log.</summary>
        public Simulation Fork() => new Simulation(this);

        /// <summary>A fork whose world randomness takes another path: one of the possible futures.</summary>
        public Simulation ForkFuture(ulong salt)
        {
            var f = new Simulation(this);
            for (int i = 0; i < f._rngs.Length; i++) f._rngs[i] = new DetRandom(f._rngs[i].State ^ (salt * 0x9E3779B97F4A7C15UL));
            return f;
        }

        public double Seconds => Tick / (double)TicksPerSecond;
    }
}
