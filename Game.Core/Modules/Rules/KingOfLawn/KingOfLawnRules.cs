using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Modules.Arena;
using Game.Core.Modules.Dogs;
using Game.Core.Modules.Hits;
using Game.Core.Modules.Match;
using Game.Core.Sim;

namespace Game.Core.Modules.Rules.KingOfLawn
{
    /// <summary>King of the Lawn (design/arenas.md → «1. Парк»). A circle earns 1 point a second to each
    /// dog inside. A rival knocked out of it (inside before a blow, outside within a second after)
    /// is 5 points for the attacker, 8 if the victim leads. The circle moves every period 8–14 m;
    /// the next place is announced 5 s before. Dogs in the circle are asked of Dogs.</summary>
    public sealed class KingOfLawnRules : IRuleSet
    {
        private const int MaxPlacementTries = 500;

        public string Name => "KingOfLawn";
        public ZoneInfo? Zone => new ZoneInfo(_center, _radius);
        public ZoneInfo? NextZone => _announced ? new ZoneInfo(_next, _radius) : (ZoneInfo?)null;

        private readonly Fixed _radius, _moveMin, _moveMax, _clearance;
        private readonly int _periodTicks, _announceTicks, _pointsPerSecond, _knockoutPoints, _leaderKnockoutPoints, _windowTicks, _decisionTicks;

        private Vec2 _center, _next;
        private bool _announced;
        private int _announcedTick, _nextMoveTick;
        private readonly bool[] _wasInside;
        private readonly int[] _pendingAttacker, _pendingUntil; // a blow on a dog that was inside: until when it counts
        private readonly int[] _knockoutPointsOf, _hits;
        private int _stretch, _longestStretch; // seconds in a row the circle was held with no knockout

        public KingOfLawnRules(GameData data, int players)
        {
            var d = data.Get("rules.king_of_lawn");
            _radius = d["zone_radius_m"]!.AsFixed();
            _moveMin = d["move_min_m"]!.AsFixed();
            _moveMax = d["move_max_m"]!.AsFixed();
            _clearance = d["clearance_m"]!.AsFixed();
            _periodTicks = d["period_s"]!.AsInt() * Simulation.TicksPerSecond;
            _announceTicks = d["announce_s"]!.AsInt() * Simulation.TicksPerSecond;
            _pointsPerSecond = d["points_per_s"]!.AsInt();
            _knockoutPoints = d["knockout_points"]!.AsInt();
            _leaderKnockoutPoints = d["leader_knockout_points"]!.AsInt();
            _windowTicks = d["knockout_window_s"]!.AsInt() * Simulation.TicksPerSecond;
            _decisionTicks = d["decision_window_s"]!.AsInt() * Simulation.TicksPerSecond;
            _center = Vec2.Zero; // the first circle: the middle of the lawn
            _nextMoveTick = _periodTicks;
            _wasInside = new bool[players];
            _pendingAttacker = new int[players];
            _pendingUntil = new int[players];
            _knockoutPointsOf = new int[players];
            _hits = new int[players];
        }

        private KingOfLawnRules(KingOfLawnRules src)
        {
            _radius = src._radius;
            _moveMin = src._moveMin;
            _moveMax = src._moveMax;
            _clearance = src._clearance;
            _periodTicks = src._periodTicks;
            _announceTicks = src._announceTicks;
            _pointsPerSecond = src._pointsPerSecond;
            _knockoutPoints = src._knockoutPoints;
            _leaderKnockoutPoints = src._leaderKnockoutPoints;
            _windowTicks = src._windowTicks;
            _decisionTicks = src._decisionTicks;
            _center = src._center;
            _next = src._next;
            _announced = src._announced;
            _announcedTick = src._announcedTick;
            _nextMoveTick = src._nextMoveTick;
            _wasInside = (bool[])src._wasInside.Clone();
            _pendingAttacker = (int[])src._pendingAttacker.Clone();
            _pendingUntil = (int[])src._pendingUntil.Clone();
            _knockoutPointsOf = (int[])src._knockoutPointsOf.Clone();
            _hits = (int[])src._hits.Clone();
            _stretch = src._stretch;
            _longestStretch = src._longestStretch;
        }

        public bool ZoneJustAnnounced(Simulation sim) => _announced && sim.Tick - _announcedTick < _decisionTicks;

        public void Step(Simulation sim, RulesModule host, TickInputs inputs)
        {
            var dogs = sim.Need<DogsModule>(host, DogsModule.ModuleName);
            var match = sim.Need<MatchModule>(host, MatchModule.ModuleName);
            var hits = sim.Need<HitsModule>(host, HitsModule.ModuleName);
            Schedule(sim, host);

            var inside = new bool[sim.Players];
            for (int p = 0; p < inside.Length; p++) inside[p] = dogs.InZone(sim, p, _center, _radius);

            foreach (var hit in hits.ThisTick)
            {
                if (hit.Attacker < 0) continue; // an object of the arena knocks nobody out for points
                _hits[hit.Attacker]++;
                if (_wasInside[hit.Victim] || _pendingUntil[hit.Victim] > 0)
                {
                    _pendingAttacker[hit.Victim] = hit.Attacker;
                    _pendingUntil[hit.Victim] = sim.Tick + _windowTicks;
                }
            }
            for (int p = 0; p < inside.Length; p++)
            {
                if (_pendingUntil[p] == 0) continue;
                if (sim.Tick > _pendingUntil[p]) _pendingUntil[p] = 0;
                else if (!inside[p]) KnockOut(sim, match, _pendingAttacker[p], p);
            }

            if (sim.Tick % Simulation.TicksPerSecond == 0) ScoreSecond(match, inside);
            for (int p = 0; p < inside.Length; p++) _wasInside[p] = inside[p];
        }

        /// <summary>Announces the next place 5 s ahead and moves the circle when the period ends.</summary>
        private void Schedule(Simulation sim, RulesModule host)
        {
            if (!_announced && sim.Tick >= _nextMoveTick - _announceTicks)
            {
                _next = PickNext(sim, host);
                _announced = true;
                _announcedTick = sim.Tick;
                var e = sim.Events.Add("zone_announced", -1, $"следующий круг: ({_next.X}, {_next.Y}) через {(_nextMoveTick - sim.Tick) / Simulation.TicksPerSecond} с");
                e.Novel = "zone_announced";
                e.DecisionOptions = 2; // run to it or fight over this one
            }
            if (sim.Tick < _nextMoveTick) return;
            _center = _next;
            _announced = false;
            _nextMoveTick += _periodTicks;
            _stretch = 0;
            for (int p = 0; p < _pendingUntil.Length; p++) _pendingUntil[p] = 0; // the circle left: nobody is knocked out of it
            for (int p = 0; p < _wasInside.Length; p++) _wasInside[p] = false;
        }

        /// <summary>A random point 8–14 m from the circle, whole circle inside the arena, centre off obstacles.</summary>
        private Vec2 PickNext(Simulation sim, RulesModule host)
        {
            var arena = sim.Need<ArenaModule>(host, ArenaModule.ModuleName);
            ref var rng = ref sim.Rng(host);
            var halfW = arena.Width / 2 - _radius;
            var halfH = arena.Height / 2 - _radius;
            for (int i = 0; i < MaxPlacementTries; i++)
            {
                var c = new Vec2((rng.NextFixed01() * 2 - Fixed.One) * halfW, (rng.NextFixed01() * 2 - Fixed.One) * halfH);
                var dist = c.DistanceTo(_center);
                if (dist >= _moveMin && dist <= _moveMax && OffObstacles(arena, c)) return c;
            }
            throw new System.InvalidOperationException("no place for the next circle: arena too small or full of obstacles");
        }

        private bool OffObstacles(ArenaModule arena, Vec2 c)
        {
            foreach (var o in arena.CircleObstacles)
                if (c.DistanceTo(o.Pos) < o.Radius + _clearance) return false;
            foreach (var r in arena.RectObstacles)
                if ((c.X - r.Center.X).Abs() < r.HalfWidth + _clearance && (c.Y - r.Center.Y).Abs() < r.HalfHeight + _clearance) return false;
            return true;
        }

        private void KnockOut(Simulation sim, MatchModule match, int attacker, int victim)
        {
            _pendingUntil[victim] = 0;
            bool leader = IsSoleLeader(match, victim);
            int points = leader ? _leaderKnockoutPoints : _knockoutPoints;
            match.Award(attacker, points, leader ? "leader_knockout" : "knockout");
            _knockoutPointsOf[attacker] += points;
            _stretch = 0;
            var size = Fixed.Ratio(points, _leaderKnockoutPoints);
            var win = sim.Events.Add("knocked_out", attacker, $"участник {attacker} выбил участника {victim} из круга: +{points}");
            win.Reward = size;
            var lose = sim.Events.Add("knocked_out", victim, $"участник {victim} выбит из круга участником {attacker}{(leader ? ", лидер" : "")}");
            lose.Threat = size;
        }

        private static bool IsSoleLeader(MatchModule match, int p)
        {
            var scores = match.Scores;
            if (scores[p] == 0) return false;
            for (int q = 0; q < scores.Count; q++)
                if (q != p && scores[q] >= scores[p]) return false;
            return true;
        }

        private void ScoreSecond(MatchModule match, bool[] inside)
        {
            bool any = false;
            for (int p = 0; p < inside.Length; p++)
            {
                if (!inside[p]) continue;
                match.Award(p, _pointsPerSecond, "zone");
                any = true;
            }
            _stretch = any ? _stretch + 1 : 0;
            if (_stretch > _longestStretch) _longestStretch = _stretch;
        }

        /// <summary>H4 numbers: points, share of them won by fighting, hits a minute, the longest equal stretch.</summary>
        public IReadOnlyDictionary<string, double> Metrics(Simulation sim, int player)
        {
            double points = sim.Get<MatchModule>(MatchModule.ModuleName).Scores[player];
            double minutes = sim.Seconds / 60.0;
            return new Dictionary<string, double>
            {
                { "knockout_points", _knockoutPointsOf[player] },
                { "fight_share", points == 0 ? 0 : _knockoutPointsOf[player] / points }, // blows themselves earn nothing: only knockouts count
                { "hits_per_min", minutes == 0 ? 0 : _hits[player] / minutes },
                { "longest_equal_stretch_s", _longestStretch },
            };
        }

        public void Hash(ref StateHasher h)
        {
            h.Add(_center);
            h.Add(_next);
            h.Add(_announced);
            h.Add(_announcedTick);
            h.Add(_nextMoveTick);
            h.Add(_stretch);
            h.Add(_longestStretch);
            for (int p = 0; p < _wasInside.Length; p++)
            {
                h.Add(_wasInside[p]);
                h.Add(_pendingAttacker[p]);
                h.Add(_pendingUntil[p]);
                h.Add(_knockoutPointsOf[p]);
                h.Add(_hits[p]);
            }
        }

        public IRuleSet Clone() => new KingOfLawnRules(this);
    }
}
