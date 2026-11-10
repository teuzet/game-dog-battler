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

    /// <summary>Two bodies touching in this tick; Normal points from A to B. B is negative when
    /// A touched the arena's own geometry: <c>-1 - k</c>, k being the index of a circle obstacle
    /// (0..), of a rectangle obstacle (after the circles) or of a bound (after the rectangles:
    /// left, right, bottom, top); see <see cref="StaticIndex"/>. Normal then points from A into it.
    /// RelSpeed is the magnitude of the relative velocity just before the collision was
    /// resolved; VelA and VelB are the two velocities at that same moment (VelB is zero for geometry);
    /// one contact per (A, B) per tick, the hardest one.</summary>
    public readonly struct Contact
    {
        public bool IsStatic => B < 0;
        public int StaticIndex => -1 - B;
        public static int StaticCode(int index) => -1 - index;
        public readonly int A, B;
        public readonly Vec2 Normal;
        public readonly Fixed RelSpeed;
        public readonly Vec2 VelA, VelB;
        public Contact(int a, int b, Vec2 normal, Fixed relSpeed, Vec2 velA, Vec2 velB)
        {
            A = a; B = b; Normal = normal; RelSpeed = relSpeed; VelA = velA; VelB = velB;
        }
    }

    /// <summary>The arena's 2D physics. It knows no game words: bodies are circles with
    /// a mass, a radius and a friction; they are dynamic, kinematic (move along their own
    /// velocity) or fixed. Per tick, in <c>bodies.substeps</c> substeps: acceleration and
    /// friction, integration, rope constraints (distance ≤ length, slack allowed; the excess
    /// distance and the outward velocity along the rope are both removed by the factor
    /// <c>leash.stiffness</c>·dt per substep, so a rope with stiffness·dt ≥ 1 is a rod and a
    /// lower one is elastic), then collisions body–body, body–obstacle and body–bounds with
    /// <c>bodies.restitution</c>. Contacts of the tick are listed in <see cref="Contacts"/>.</summary>
    public sealed class BodiesModule : IModule
    {
        public const string ModuleName = "Bodies";
        public string Name => ModuleName;
        public IReadOnlyList<string> DependsOn { get; } = new[] { ArenaModule.ModuleName };

        private readonly int _substeps;
        private readonly List<Body> _bodies = new List<Body>();
        private readonly List<Rope> _ropes = new List<Rope>();
        private readonly List<Contact> _contacts = new List<Contact>();

        private readonly Fixed _restitution, _driveMassFactor;
        private ArenaModule _arena = null!; // immutable, shared with forks

        public BodiesModule(GameData data)
        {
            _substeps = data.Int("bodies.substeps");
            _restitution = data.Num("bodies.restitution");
            _driveMassFactor = data.Num("bodies.drive_mass_factor");
        }

        private BodiesModule(BodiesModule src)
        {
            _substeps = src._substeps;
            _restitution = src._restitution;
            _driveMassFactor = src._driveMassFactor;
            _arena = src._arena;
            _bodies.AddRange(src._bodies);
            _ropes.AddRange(src._ropes);
            _contacts.AddRange(src._contacts);
        }

        public int Count => _bodies.Count;
        public IReadOnlyList<Rope> Ropes => _ropes;
        /// <summary>Contacts of the last tick.</summary>
        public IReadOnlyList<Contact> Contacts => _contacts;

        public int AddCircle(Vec2 pos, Fixed mass, Fixed radius, Fixed friction) =>
            Add(new Body { Kind = BodyKind.Dynamic, Pos = pos, Mass = mass, Radius = radius, Friction = friction });

        /// <summary>A body that moves with a velocity of its own and cannot be pushed (it moves along its route).</summary>
        public int AddKinematic(Vec2 pos, Fixed radius, Vec2 velocity) =>
            Add(new Body { Kind = BodyKind.Kinematic, Pos = pos, Vel = velocity, Radius = radius });

        /// <summary>A body of infinite mass that stays where it is (a post, an axis).</summary>
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

        /// <summary>Replaces the velocity of a body (a driver turning it at once).</summary>
        public void SetVelocity(int body, Vec2 velocity)
        {
            var b = Check(body);
            b.Vel = velocity;
            _bodies[body] = b;
        }

        /// <summary>Puts a body at a point at rest (scripted scenes of tests and stands).</summary>
        public void Teleport(int body, Vec2 position)
        {
            var b = Check(body);
            b.Pos = position;
            b.Vel = Vec2.Zero;
            _bodies[body] = b;
        }

        public Vec2 Position(int body) => Check(body).Pos;
        public Vec2 Velocity(int body) => Check(body).Vel;
        public Fixed Mass(int body) => Check(body).Mass;
        public Fixed Radius(int body) => Check(body).Radius;
        public Fixed Friction(int body) => Check(body).Friction;
        public BodyKind Kind(int body) => Check(body).Kind;

        private int Add(Body b)
        {
            _bodies.Add(b);
            return _bodies.Count - 1;
        }

        private Body Check(int body) =>
            body >= 0 && body < _bodies.Count ? _bodies[body] : throw new KeyNotFoundException($"no body {body}");

        public void Init(Simulation sim) { _arena = sim.Need<ArenaModule>(this, ArenaModule.ModuleName); }

        public void Step(Simulation sim, TickInputs inputs)
        {
            _contacts.Clear();
            var dt = Fixed.Ratio(1, Simulation.TicksPerSecond * _substeps);
            for (int s = 0; s < _substeps; s++)
            {
                for (int i = 0; i < _bodies.Count; i++)
                {
                    var b = _bodies[i];
                    if (b.Kind == BodyKind.Fixed) continue;
                    if (b.Kind == BodyKind.Dynamic) b.Vel = Accelerate(b, dt);
                    b.Pos = b.Pos + b.Vel * dt;
                    _bodies[i] = b;
                }
                foreach (var r in _ropes) SolveRope(r, dt);
                for (int i = 0; i < _bodies.Count; i++)
                    for (int j = i + 1; j < _bodies.Count; j++) CollideBodies(i, j);
                for (int i = 0; i < _bodies.Count; i++)
                    if (_bodies[i].Kind == BodyKind.Dynamic) CollideStatic(i);
            }
            for (int i = 0; i < _bodies.Count; i++)
            {
                var b = _bodies[i];
                b.Driven = false;
                _bodies[i] = b;
            }
        }

        /// <summary>Shares of a velocity or position change that fall on a and b: only dynamic
        /// bodies move, the lighter one more.</summary>
        private static void Shares(Body a, Body b, out Fixed wa, out Fixed wb)
        {
            bool am = a.Kind == BodyKind.Dynamic, bm = b.Kind == BodyKind.Dynamic;
            if (am && bm) { wa = b.Mass / (a.Mass + b.Mass); wb = Fixed.One - wa; }
            else if (am) { wa = Fixed.One; wb = Fixed.Zero; }
            else if (bm) { wa = Fixed.Zero; wb = Fixed.One; }
            else { wa = Fixed.Zero; wb = Fixed.Zero; }
        }

        /// <summary>Like <see cref="Shares"/>, but a driven body is an engine: on the rope it
        /// counts as <c>bodies.drive_mass_factor</c> times its mass, so a driven dog pulls with a
        /// bounded weight that grows with its mass, and the owner's mass and momentum still count.
        /// A stopped dog (driven to zero speed) resists the same way; nobody becomes a fixed axis.</summary>
        private void RopeShares(Body a, Body b, out Fixed wa, out Fixed wb)
        {
            if (a.Kind != BodyKind.Dynamic || b.Kind != BodyKind.Dynamic) { Shares(a, b, out wa, out wb); return; }
            var ma = a.Driven ? a.Mass * _driveMassFactor : a.Mass;
            var mb = b.Driven ? b.Mass * _driveMassFactor : b.Mass;
            wa = mb / (ma + mb);
            wb = Fixed.One - wa;
        }

        private void SolveRope(Rope r, Fixed dt)
        {
            var a = _bodies[r.A];
            var b = _bodies[r.B];
            RopeShares(a, b, out var wa, out var wb);
            if (wa == Fixed.Zero && wb == Fixed.Zero) return;
            var delta = b.Pos - a.Pos;
            var dist = delta.Length;
            if (dist <= r.Length) return;
            var n = delta / dist;
            var k = Fixed.Clamp01(r.Stiffness * dt);
            var along = (b.Vel - a.Vel).Dot(n);
            if (along > Fixed.Zero)
            {
                a.Vel = a.Vel + n * (along * k * wa);
                b.Vel = b.Vel - n * (along * k * wb);
            }
            var fix = (dist - r.Length) * k;
            a.Pos = a.Pos + n * (fix * wa);
            b.Pos = b.Pos - n * (fix * wb);
            _bodies[r.A] = a;
            _bodies[r.B] = b;
        }

        private void CollideBodies(int i, int j)
        {
            var a = _bodies[i];
            var b = _bodies[j];
            Shares(a, b, out var wa, out var wb);
            if (wa == Fixed.Zero && wb == Fixed.Zero) return;
            var reach = a.Radius + b.Radius;
            var delta = b.Pos - a.Pos;
            if (delta.LengthSq >= reach * reach) return;
            var dist = delta.Length;
            var n = dist == Fixed.Zero ? new Vec2(Fixed.One, Fixed.Zero) : delta / dist; // coincident centres: push along +X
            var rel = b.Vel - a.Vel;
            var speed = rel.Length;
            var push = reach - dist;
            a.Pos = a.Pos - n * (push * wa);
            b.Pos = b.Pos + n * (push * wb);
            var closing = rel.Dot(n);
            var velA = a.Vel;
            var velB = b.Vel;
            if (closing < Fixed.Zero)
            {
                var change = -(Fixed.One + _restitution) * closing;
                a.Vel = a.Vel - n * (change * wa);
                b.Vel = b.Vel + n * (change * wb);
            }
            _bodies[i] = a;
            _bodies[j] = b;
            Record(new Contact(i, j, n, speed, velA, velB));
        }

        private void CollideStatic(int i)
        {
            var b = _bodies[i];
            int code = 0;
            foreach (var c in _arena.CircleObstacles)
            {
                int ci = code++;
                var delta = b.Pos - c.Pos;
                var reach = b.Radius + c.Radius;
                if (delta.LengthSq >= reach * reach) continue;
                var dist = delta.Length;
                var outward = dist == Fixed.Zero ? new Vec2(Fixed.One, Fixed.Zero) : delta / dist;
                PushOut(i, ref b, outward, reach - dist, ci);
            }
            foreach (var r in _arena.RectObstacles)
            {
                int ri = code++;
                var rel = b.Pos - r.Center;
                var nearest = new Vec2(Fixed.Clamp(rel.X, -r.HalfWidth, r.HalfWidth), Fixed.Clamp(rel.Y, -r.HalfHeight, r.HalfHeight));
                var delta = rel - nearest;
                if (delta.LengthSq >= b.Radius * b.Radius) continue;
                var edgeDist = delta.Length;
                if (edgeDist == Fixed.Zero)
                {
                    // centre inside the rectangle (or on its edge, where the fixed-point length rounds to zero): leave by the nearest side
                    var dx = r.HalfWidth - rel.X.Abs();
                    var dy = r.HalfHeight - rel.Y.Abs();
                    if (dx < dy) PushOut(i, ref b, new Vec2(rel.X < Fixed.Zero ? -Fixed.One : Fixed.One, Fixed.Zero), dx + b.Radius, ri);
                    else PushOut(i, ref b, new Vec2(Fixed.Zero, rel.Y < Fixed.Zero ? -Fixed.One : Fixed.One), dy + b.Radius, ri);
                }
                else
                {
                    PushOut(i, ref b, delta / edgeDist, b.Radius - edgeDist, ri);
                }
            }
            var hw = _arena.Width / 2;
            var hh = _arena.Height / 2;
            if (b.Pos.X - b.Radius < -hw) PushOut(i, ref b, new Vec2(Fixed.One, Fixed.Zero), -hw - (b.Pos.X - b.Radius), code);
            if (b.Pos.X + b.Radius > hw) PushOut(i, ref b, new Vec2(-Fixed.One, Fixed.Zero), b.Pos.X + b.Radius - hw, code + 1);
            if (b.Pos.Y - b.Radius < -hh) PushOut(i, ref b, new Vec2(Fixed.Zero, Fixed.One), -hh - (b.Pos.Y - b.Radius), code + 2);
            if (b.Pos.Y + b.Radius > hh) PushOut(i, ref b, new Vec2(Fixed.Zero, -Fixed.One), b.Pos.Y + b.Radius - hh, code + 3);
            _bodies[i] = b;
        }

        /// <summary>Moves the body out of static geometry along <paramref name="outward"/> (the
        /// direction away from it) and bounces the velocity that points into it.</summary>
        private void PushOut(int i, ref Body b, Vec2 outward, Fixed depth, int staticIndex)
        {
            var speed = b.Vel.Length;
            var vel = b.Vel;
            b.Pos = b.Pos + outward * depth;
            var into = b.Vel.Dot(outward);
            if (into < Fixed.Zero) b.Vel = b.Vel - outward * ((Fixed.One + _restitution) * into);
            Record(new Contact(i, Contact.StaticCode(staticIndex), -outward, speed, vel, Vec2.Zero));
        }

        private void Record(Contact c)
        {
            for (int k = 0; k < _contacts.Count; k++)
            {
                if (_contacts[k].A != c.A || _contacts[k].B != c.B) continue;
                if (c.RelSpeed > _contacts[k].RelSpeed) _contacts[k] = c;
                return;
            }
            _contacts.Add(c);
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
            foreach (var b in _bodies) { h.Add((int)b.Kind); h.Add(b.Pos); h.Add(b.Vel); h.Add(b.Mass); h.Add(b.Radius); h.Add(b.Friction); h.Add(b.Driven); h.Add(b.DriveVel); h.Add(b.DriveAccel); }
            h.Add(_ropes.Count);
            foreach (var r in _ropes) { h.Add(r.A); h.Add(r.B); h.Add(r.Length); h.Add(r.Stiffness); }
        }

        public IModule Clone() => new BodiesModule(this);
    }
}
