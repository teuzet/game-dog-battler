using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Modules.Bodies;
using Game.Core.Modules.Owners;
using Game.Core.Modules.Props;
using Game.Core.Sim;

namespace Game.Core.Modules.Hits
{
    /// <summary>A blow of this tick. Attacker and victim are players; a blow by an
    /// object of the arena has Attacker = -1. There is no owner type name: who hit with what is
    /// known by the player.</summary>
    public readonly struct Hit
    {
        public readonly int Attacker, Victim;
        public readonly Fixed Strength;
        public readonly bool Strong;

        public Hit(int attacker, int victim, Fixed strength, bool strong)
        {
            Attacker = attacker;
            Victim = victim;
            Strength = strength;
            Strong = strong;
        }
    }

    /// <summary>Blows on dogs (design/roster.md → «Удар»). An owner touching a foreign dog at a relative
    /// speed ≥ <c>hits.threshold_mps</c> hits it if he himself moves toward it at ≥ <c>hits.owner_speed_min_mps</c>
    /// (a dog ramming a standing foreign owner only shoves him). Strength = owner mass × speed². A blow throws the dog
    /// away from the owner by 2–7 m (growing with strength) and stuns it for 0.4 + 0.05 × (v − 5) s,
    /// at most 1.2 s; the stunned dog is not driven (Dogs asks <see cref="IsStunned"/>). Own owner and
    /// own dog never hit each other; owner against owner is a shove without points. An owner flying past
    /// a foreign dog at blow speed without touching it is a <c>near_miss</c>. Numbers: Data/hits.json.</summary>
    public sealed class HitsModule : IModule
    {
        public const string ModuleName = "Hits";
        public string Name => ModuleName;
        public IReadOnlyList<string> DependsOn { get; } = new[] { BodiesModule.ModuleName, OwnersModule.ModuleName, PropsModule.ModuleName };

        /// <summary>Relative speed (m/s) from which a touch is a blow, and a strong one.</summary>
        public Fixed Threshold { get; }
        public Fixed StrongThreshold { get; }
        /// <summary>The owner's own speed (m/s) toward the dog at the touch, from which it is a blow; 0 = any touch at blow speed.</summary>
        public Fixed OwnerSpeedMin { get; }

        private readonly Fixed _knockbackMin, _knockbackMax, _strengthMin, _strengthFull;
        private readonly Fixed _stunBase, _stunPerMps, _stunMax, _threatBase, _rewardBase, _nearGap, _nearThreatMax, _slideEnd, _recoilShare;

        private readonly int _players;
        private readonly List<Hit> _tick = new List<Hit>();
        private readonly List<(int Owner, int Dog)> _nearTick = new List<(int Owner, int Dog)>();
        private readonly int[] _dogBody;
        private readonly int[] _stunLeft; // ticks
        private readonly bool[] _thrown; // the dog still slides from a blow: not driven until it has stopped
        private readonly bool[] _struckStrong; // the player has already landed a strong blow: the first is the novelty
        // per (owner p, dog q) pair at index p * players + q: the pass of an owner by a foreign dog
        private readonly bool[] _near, _nearTouched;
        // per (owner, dog) pair: in contact in the previous tick / in this one; a blow needs a fresh touch
        private bool[] _touchPrev, _touchNow;
        private readonly Fixed[] _nearPeak;

        public HitsModule(GameData data, int players)
        {
            _players = players;
            Threshold = data.Num("hits.threshold_mps");
            StrongThreshold = data.Num("hits.strong_threshold_mps");
            OwnerSpeedMin = data.Num("hits.owner_speed_min_mps");
            _knockbackMin = data.Num("hits.knockback_min_m");
            _knockbackMax = data.Num("hits.knockback_max_m");
            _strengthMin = data.Num("hits.knockback_strength_min");
            _strengthFull = data.Num("hits.knockback_strength_full");
            _stunBase = data.Num("hits.stun_base_s");
            _stunPerMps = data.Num("hits.stun_per_mps_s");
            _stunMax = data.Num("hits.stun_max_s");
            _recoilShare = data.Num("hits.owner_recoil_share");
            _threatBase = data.Num("hits.threat_base");
            _rewardBase = data.Num("hits.reward_base");
            _nearGap = data.Num("hits.near_miss_gap_m");
            _nearThreatMax = data.Num("hits.near_miss_threat_max");
            _slideEnd = data.Num("hits.slide_end_mps");
            _dogBody = new int[players];
            for (int p = 0; p < players; p++) _dogBody[p] = -1;
            _stunLeft = new int[players];
            _thrown = new bool[players];
            _struckStrong = new bool[players];
            _near = new bool[players * players];
            _nearTouched = new bool[players * players];
            _touchPrev = new bool[players * players];
            _touchNow = new bool[players * players];
            _nearPeak = new Fixed[players * players];
        }

        private HitsModule(HitsModule src)
        {
            _players = src._players;
            Threshold = src.Threshold;
            StrongThreshold = src.StrongThreshold;
            OwnerSpeedMin = src.OwnerSpeedMin;
            _knockbackMin = src._knockbackMin;
            _knockbackMax = src._knockbackMax;
            _strengthMin = src._strengthMin;
            _strengthFull = src._strengthFull;
            _stunBase = src._stunBase;
            _stunPerMps = src._stunPerMps;
            _stunMax = src._stunMax;
            _recoilShare = src._recoilShare;
            _threatBase = src._threatBase;
            _rewardBase = src._rewardBase;
            _nearGap = src._nearGap;
            _nearThreatMax = src._nearThreatMax;
            _slideEnd = src._slideEnd;
            _tick.AddRange(src._tick);
            _nearTick.AddRange(src._nearTick);
            _dogBody = (int[])src._dogBody.Clone();
            _stunLeft = (int[])src._stunLeft.Clone();
            _thrown = (bool[])src._thrown.Clone();
            _struckStrong = (bool[])src._struckStrong.Clone();
            _near = (bool[])src._near.Clone();
            _nearTouched = (bool[])src._nearTouched.Clone();
            _touchPrev = (bool[])src._touchPrev.Clone();
            _touchNow = (bool[])src._touchNow.Clone();
            _nearPeak = (Fixed[])src._nearPeak.Clone();
        }

        /// <summary>Blows of the last tick.</summary>
        public IReadOnlyList<Hit> ThisTick => _tick;

        /// <summary>Passes told in the last tick: the owner of player <c>Owner</c> flew past the dog of player <c>Dog</c>
        /// (the same facts as the <c>near_miss</c> events, for rule sets that count them). Not hashed: it is
        /// rebuilt every tick and read by Rules in the same tick.</summary>
        public IReadOnlyList<(int Owner, int Dog)> NearMissesThisTick => _nearTick;

        /// <summary>A blow detected in this tick; the one place a blow enters the list.</summary>
        public void Record(Hit hit) => _tick.Add(hit);

        /// <summary>The player's dog body, registered by Dogs: blows land on it.</summary>
        public void BindDog(int player, int dogBody) => _dogBody[player] = dogBody;

        /// <summary>The stun of a blow is on.</summary>
        public bool IsStunned(int player) => _stunLeft[player] > 0;

        /// <summary>Dogs do not drive a dog that is stunned or still sliding from a blow, so the knockback
        /// goes its whole 2–7 m instead of being cut short by the dog running off when the stun ends.</summary>
        public bool IsHelpless(int player) => _stunLeft[player] > 0 || _thrown[player];

        public void Init(Simulation sim) { }

        public void Step(Simulation sim, TickInputs inputs)
        {
            _tick.Clear();
            _nearTick.Clear();
            for (int p = 0; p < _players; p++)
                if (_stunLeft[p] > 0) _stunLeft[p]--;
            var bodies = sim.Need<BodiesModule>(this, BodiesModule.ModuleName);
            var owners = sim.Need<OwnersModule>(this, OwnersModule.ModuleName);
            for (int p = 0; p < _players; p++)
                if (_thrown[p] && bodies.Velocity(_dogBody[p]).Length < _slideEnd) _thrown[p] = false;
            System.Array.Clear(_touchNow, 0, _touchNow.Length);
            foreach (var c in bodies.Contacts)
            {
                if (c.IsStatic) continue;
                int ownerA = OwnerOf(owners, c.A), dogA = DogOf(c.A);
                int ownerB = OwnerOf(owners, c.B), dogB = DogOf(c.B);
                if (ownerA >= 0 && dogB >= 0 && ownerA != dogB) Touch(sim, bodies, owners, ownerA, dogB, c.Normal, c.RelSpeed, c.VelA);
                else if (ownerB >= 0 && dogA >= 0 && ownerB != dogA) Touch(sim, bodies, owners, ownerB, dogA, -c.Normal, c.RelSpeed, c.VelB);
            }
            var swap = _touchPrev; _touchPrev = _touchNow; _touchNow = swap;
            WatchPasses(sim, bodies, owners);
        }

        private int OwnerOf(OwnersModule owners, int body)
        {
            for (int p = 0; p < _players; p++) if (owners.BodyOf(p) == body) return p;
            return -1;
        }

        private int DogOf(int body)
        {
            for (int p = 0; p < _players; p++) if (_dogBody[p] == body) return p;
            return -1;
        }

        /// <summary>Owner <paramref name="attacker"/> touches the dog of <paramref name="victim"/>;
        /// <paramref name="toDog"/> points from the owner to the dog.</summary>
        private void Touch(Simulation sim, BodiesModule bodies, OwnersModule owners, int attacker, int victim, Vec2 toDog, Fixed relSpeed, Vec2 ownerVel)
        {
            int pair = attacker * _players + victim;
            _nearTouched[pair] = true;
            _touchNow[pair] = true;
            // one touch is one blow: the pair must have been apart in the previous tick (a second player's owner can still hit a sliding dog)
            if (relSpeed < Threshold || _touchPrev[pair]) return;
            // a parked owner is an obstacle, not a weapon: his own speed toward the dog (before the collision) must reach the minimum, else the touch is a shove
            if (ownerVel.Dot(toDog.Normalized()) < OwnerSpeedMin) return;
            var strength = bodies.Mass(owners.BodyOf(attacker)) * relSpeed * relSpeed;
            bool strong = relSpeed >= StrongThreshold;
            var power = Fixed.Clamp01((strength - _strengthMin) / (_strengthFull - _strengthMin));
            Throw(bodies, victim, toDog, power);
            _thrown[victim] = true;
            // the owner does not run on into the thrown dog and drive her further than 2–7 m
            int ob = owners.BodyOf(attacker);
            var n = toDog.Normalized();
            var ov = bodies.Velocity(ob);
            var along = ov.Dot(n);
            var thrownSpeed = bodies.Velocity(_dogBody[victim]).Length;
            var recoil = thrownSpeed * _recoilShare; // the owner keeps this share of the thrown dog speed along the normal and falls behind her
            if (along > recoil) bodies.SetVelocity(ob, ov - n * (along - recoil));
            var stun = Fixed.Min(_stunMax, _stunBase + _stunPerMps * (relSpeed - Threshold));
            int ticks = (stun * Simulation.TicksPerSecond).FloorToInt();
            if (ticks > _stunLeft[victim]) _stunLeft[victim] = ticks;
            _tick.Add(new Hit(attacker, victim, strength, strong));

            string kind = strong ? "strong_hit" : "hit";
            string what = strong ? "сильный удар" : "удар";
            var win = sim.Events.Add(kind, attacker, $"участник {attacker} нанёс {what} собаке участника {victim}: {relSpeed} м/с");
            var reward = _rewardBase + (Fixed.One - _rewardBase) * power;
            win.Reward = strong ? reward * 2 : reward;
            if (strong && !_struckStrong[attacker]) { win.Novel = "strong_hit"; _struckStrong[attacker] = true; }
            var lose = sim.Events.Add(kind, victim, $"собака участника {victim} получила {what} от участника {attacker}: оглушена на {stun} с");
            lose.Threat = _threatBase + (Fixed.One - _threatBase) * power;
        }

        /// <summary>Throws the dog along <paramref name="dir"/> so that, left to the ground's friction, it
        /// slides 2–7 m (<c>v² = 2·friction·distance</c>); the dog is not driven until it has
        /// stopped, so only the leash and obstacles shorten it.</summary>
        private void Throw(BodiesModule bodies, int victim, Vec2 dir, Fixed power)
        {
            int dog = _dogBody[victim];
            var distance = _knockbackMin + (_knockbackMax - _knockbackMin) * power;
            var speed = Fixed.Sqrt(bodies.Friction(dog) * 2 * distance);
            bodies.SetVelocity(dog, dir.Normalized() * speed);
        }

        /// <summary>A near miss: an owner at blow speed passed a foreign dog within <c>hits.near_miss_gap_m</c>
        /// and did not touch it. It is told when the pass ends, with the speed of the closest approach.</summary>
        private void WatchPasses(Simulation sim, BodiesModule bodies, OwnersModule owners)
        {
            for (int p = 0; p < _players; p++)
            {
                int ob = owners.BodyOf(p);
                var ov = bodies.Velocity(ob);
                for (int q = 0; q < _players; q++)
                {
                    if (p == q) continue;
                    int i = p * _players + q;
                    int db = _dogBody[q];
                    var away = bodies.Position(db) - bodies.Position(ob);
                    var gap = away.Length - bodies.Radius(ob) - bodies.Radius(db);
                    if (gap <= _nearGap)
                    {
                        _near[i] = true;
                        var rel = ov - bodies.Velocity(db);
                        if (away.LengthSq != Fixed.Zero && rel.Dot(away) > Fixed.Zero && ov.Length >= Threshold && rel.Length > _nearPeak[i])
                            _nearPeak[i] = rel.Length;
                        continue;
                    }
                    if (!_near[i]) continue;
                    if (!_nearTouched[i] && _nearPeak[i] >= Threshold)
                    {
                        var e = sim.Events.Add("near_miss", q, $"хозяин участника {p} пролетел мимо собаки участника {q}: {_nearPeak[i]} м/с");
                        e.Threat = _nearThreatMax * Fixed.Clamp01(_nearPeak[i] / StrongThreshold);
                        _nearTick.Add((p, q));
                    }
                    _near[i] = false;
                    _nearTouched[i] = false;
                    _nearPeak[i] = Fixed.Zero;
                }
            }
        }

        public void Hash(ref StateHasher h)
        {
            h.Add(_tick.Count);
            foreach (var x in _tick) { h.Add(x.Attacker); h.Add(x.Victim); h.Add(x.Strength); h.Add(x.Strong); }
            for (int p = 0; p < _players; p++) { h.Add(_dogBody[p]); h.Add(_stunLeft[p]); h.Add(_thrown[p]); h.Add(_struckStrong[p]); }
            for (int i = 0; i < _near.Length; i++) { h.Add(_near[i]); h.Add(_nearTouched[i]); h.Add(_nearPeak[i]); }
            for (int i = 0; i < _touchPrev.Length; i++) h.Add(_touchPrev[i]);
        }

        public IModule Clone() => new HitsModule(this);
    }
}
