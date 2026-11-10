using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Modules.Arena;
using Game.Core.Sim;

namespace Game.Core.Modules.Bodies
{
    public enum BodyKind { Dynamic, Kinematic, Fixed }

    public struct Body
    {
        public BodyKind Kind;
        public Vec2 Pos, Vel;
        public Fixed Mass, Radius;
        /// <summary>Deceleration (m/s²) of an undriven dynamic body.</summary>
        public Fixed Friction;
        public bool Driven;
        public Vec2 DriveVel;
        public Fixed DriveAccel;
    }

    public readonly struct Rope
    {
        public readonly int A, B;
        public readonly Fixed Length, Stiffness;
        public Rope(int a, int b, Fixed length, Fixed stiffness) { A = a; B = b; Length = length; Stiffness = stiffness; }
    }

    /// <summary>Two bodies touching in this tick; Normal points from A to B.</summary>
    public readonly struct Contact
    {
        public readonly int A, B;
        public readonly Vec2 Normal;
        public readonly Fixed Impulse;
        public Contact(int a, int b, Vec2 normal, Fixed impulse) { A = a; B = b; Normal = normal; Impulse = impulse; }
    }

    /// <summary>The arena's 2D physics. It knows no game words: bodies are circles with
    /// a mass, a radius and a friction; they are dynamic, kinematic (move along their own
    /// velocity) or fixed. This is the stub of F-001: plain Euler integration in substeps,
    /// ropes are recorded but do not yet pull, nothing collides, Contacts stay empty.
    /// F-002 fills in ropes and collisions behind the same API.</summary>
    public sealed class BodiesModule : IModule
    {
        public const string ModuleName = "Bodies";
        public string Name => ModuleName;
        public IReadOnlyList<string> DependsOn { get; } = new[] { ArenaModule.ModuleName };

        private readonly int _substeps;
        private readonly List<Body> _bodies = new List<Body>();
        private readonly List<Rope> _ropes = new List<Rope>();
        private readonly List<Contact> _contacts = new List<Contact>();

        public BodiesModule(GameData data) { _substeps = data.Int("bodies.substeps"); }

        private BodiesModule(BodiesModule src)
        {
            _substeps = src._substeps;
            _bodies.AddRange(src._bodies);
            _ropes.AddRange(src._ropes);
        }

        public int Count => _bodies.Count;
        public IReadOnlyList<Rope> Ropes => _ropes;
        /// <summary>Contacts of the last tick.</summary>
        public IReadOnlyList<Contact> Contacts => _contacts;

        public int AddCircle(Vec2 pos, Fixed mass, Fixed radius, Fixed friction) =>
            Add(new Body { Kind = BodyKind.Dynamic, Pos = pos, Mass = mass, Radius = radius, Friction = friction });

        /// <summary>A body that moves with a velocity of its own and cannot be pushed (a car on its route).</summary>
        public int AddKinematic(Vec2 pos, Fixed radius, Vec2 velocity) =>
            Add(new Body { Kind = BodyKind.Kinematic, Pos = pos, Vel = velocity, Radius = radius });

        /// <summary>A body of infinite mass that stays where it is (a post, a grandpa on a stool).</summary>
        public int AddFixed(Vec2 pos, Fixed radius) =>
            Add(new Body { Kind = BodyKind.Fixed, Pos = pos, Radius = radius });

        public int AddRope(int a, int b, Fixed length, Fixed stiffness)
        {
            Check(a); Check(b);
            _ropes.Add(new Rope(a, b, length, stiffness));
            return _ropes.Count - 1;
        }

        /// <summary>Asks a dynamic body to reach <paramref name="velocity"/>, changing its
        /// speed by at most <paramref name="accel"/> m/s². Holds for the next tick only:
        /// whoever drives a body says so every tick.</summary>
        public void Drive(int body, Vec2 velocity, Fixed accel)
        {
            var b = Check(body);
            b.Driven = true;
            b.DriveVel = velocity;
            b.DriveAccel = accel;
            _bodies[body] = b;
        }

        public Vec2 Position(int body) => Check(body).Pos;
        public Vec2 Velocity(int body) => Check(body).Vel;
        public Fixed Mass(int body) => Check(body).Mass;
        public Fixed Radius(int body) => Check(body).Radius;
        public BodyKind Kind(int body) => Check(body).Kind;

        private int Add(Body b)
        {
            _bodies.Add(b);
            return _bodies.Count - 1;
        }

        private Body Check(int body) =>
            body >= 0 && body < _bodies.Count ? _bodies[body] : throw new KeyNotFoundException($"no body {body}");

        public void Init(Simulation sim) { }

        public void Step(Simulation sim, TickInputs inputs)
        {
            _contacts.Clear();
            var dt = Fixed.Ratio(1, Simulation.TicksPerSecond * _substeps);
            for (int s = 0; s < _substeps; s++)
                for (int i = 0; i < _bodies.Count; i++)
                {
                    var b = _bodies[i];
                    if (b.Kind == BodyKind.Fixed) continue;
                    if (b.Kind == BodyKind.Dynamic) b.Vel = Accelerate(b, dt);
                    b.Pos = b.Pos + b.Vel * dt;
                    _bodies[i] = b;
                }
            for (int i = 0; i < _bodies.Count; i++)
            {
                var b = _bodies[i];
                b.Driven = false;
                _bodies[i] = b;
            }
        }

        private static Vec2 Accelerate(Body b, Fixed dt)
        {
            var target = b.Driven ? b.DriveVel : Vec2.Zero;
            var accel = b.Driven ? b.DriveAccel : b.Friction;
            var delta = target - b.Vel;
            var step = accel * dt;
            return delta.LengthSq <= step * step ? target : b.Vel + delta.Normalized() * step;
        }

        public void Hash(ref StateHasher h)
        {
            h.Add(_bodies.Count);
            foreach (var b in _bodies) { h.Add((int)b.Kind); h.Add(b.Pos); h.Add(b.Vel); h.Add(b.Mass); h.Add(b.Radius); h.Add(b.Friction); }
            h.Add(_ropes.Count);
            foreach (var r in _ropes) { h.Add(r.A); h.Add(r.B); h.Add(r.Length); h.Add(r.Stiffness); }
        }

        public IModule Clone() => new BodiesModule(this);
    }
}
