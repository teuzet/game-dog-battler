using System;
using System.Collections.Generic;
using Game.Core.Bots.Skills;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Sim;

namespace Game.Core.Bots
{
    /// <summary>A strategy that tells how often it chose each of its skills: the scorer of evolve fines a duel bot that
    /// leaves a verb out (circling on the spot instead of fighting).</summary>
    public interface IPicks
    {
        /// <summary>Skill name → how many decisions chose it.</summary>
        IReadOnlyDictionary<string, int> Picks { get; }
    }

    /// <summary>The decision layer of the duel bot (design/bots-duel.md → «Слой решений»): every <c>decide_period_s</c> it scores
    /// the five skills of the <see cref="SkillSet"/> as <c>bias_&lt;skill&gt;</c> + Σ <c>w_&lt;skill&gt;_&lt;feature&gt;</c> × feature over
    /// ten features 0..1 read off the view, and leads the chosen skill until the next decision; it changes skill only when the best
    /// beats the current one by more than <c>switch_margin</c>. Strike and Dodge work against the nearest rival dog, Move goes to the
    /// point <c>approach_m</c> from it on my side, Spin and Brake have no target. Numbers: Game.Core/Data/bots/decide.json.</summary>
    public sealed class DuelistStrategy : IBotStrategy, IPicks
    {
        public static readonly string[] Skills = { "move", "spin", "strike", "dodge", "brake" };
        public static readonly string[] Features =
            { "dist", "in_reach", "my_owner_speed", "rival_owner_speed", "threat", "cover", "my_hp", "rival_hp", "my_speed", "round_time" };
        private const int Move = 0, Spin = 1, Strike = 2, Dodge = 3, Brake = 4;

        private readonly SkillSet _skills;
        private readonly Fixed[] _bias; // per skill
        private readonly Fixed[,] _w; // skill × feature
        private readonly Fixed _margin, _approach, _blowSpeed, _roundS;
        private readonly Fixed[] _maxSpeed; // the breed speed of each pair of game.pairs: player p plays pair p % count
        private readonly int _periodTicks;
        private int _current = -1;
        private int _nextTick;
        private readonly int[] _picks;

        public DuelistStrategy(SkillSet skills, GameData data)
        {
            _skills = skills;
            _bias = new Fixed[Skills.Length];
            _w = new Fixed[Skills.Length, Features.Length];
            for (int s = 0; s < Skills.Length; s++)
            {
                _bias[s] = data.Num("bots.decide.bias_" + Skills[s]);
                for (int f = 0; f < Features.Length; f++) _w[s, f] = data.Num("bots.decide.w_" + Skills[s] + "_" + Features[f]);
            }
            _margin = data.Num("bots.decide.switch_margin");
            _approach = data.Num("bots.decide.approach_m");
            _periodTicks = System.Math.Max(1, (data.Num("bots.decide.decide_period_s") * Simulation.TicksPerSecond).FloorToInt());
            _blowSpeed = data.Num("hits.threshold_mps");
            // a match without rounds (the park, the sandbox) is one round as long as the match; the HUD shows its time_left then
            var roundPath = Archetype.RulesKey(data) + ".round_s";
            _roundS = data.Has(roundPath) ? data.Num(roundPath) : data.Num("match.seconds");
            var pairs = data.Get("game.pairs").Items;
            _maxSpeed = new Fixed[pairs.Count];
            for (int i = 0; i < pairs.Count; i++) _maxSpeed[i] = data.Num("dogs." + pairs[i]["dog"]!.AsString() + ".speed");
            _picks = new int[Skills.Length];
        }

        private DuelistStrategy(DuelistStrategy src)
        {
            _skills = src._skills.Clone();
            _bias = src._bias;
            _w = src._w;
            _margin = src._margin;
            _approach = src._approach;
            _blowSpeed = src._blowSpeed;
            _roundS = src._roundS;
            _maxSpeed = src._maxSpeed;
            _periodTicks = src._periodTicks;
            _current = src._current;
            _nextTick = src._nextTick;
            _picks = (int[])src._picks.Clone();
        }

        public IBotStrategy Clone() => new DuelistStrategy(this);

        /// <summary>The skill being led now (one of <see cref="Skills"/>), or null before the first decision.</summary>
        public string? Current => _current < 0 ? null : Skills[_current];

        public IReadOnlyDictionary<string, int> Picks
        {
            get
            {
                var d = new SortedDictionary<string, int>(StringComparer.Ordinal);
                for (int s = 0; s < Skills.Length; s++) d[Skills[s]] = _picks[s];
                return d;
            }
        }

        public PlayerInput Decide(BotView view, ref DetRandom rng)
        {
            var me = Seen.Me(view);
            if (!Cursor.NearestRival(view, me, -Fixed.One, out var rival))
                throw new InvalidOperationException("duelist: no rival dog on the screen");
            if (view.Tick >= _nextTick)
            {
                Choose(Sense(view, me, rival));
                _picks[_current]++;
                _nextTick = view.Tick + _periodTicks;
            }
            switch (_current)
            {
                case Move:
                    var from = me - rival.Pos;
                    var point = rival.Pos + from.Normalized() * _approach;
                    return _skills.Move.Act(view, new SeenObject { X = point.X, Y = point.Y }, ref rng);
                case Spin: return _skills.Spin.Act(view, default, ref rng);
                case Strike: return _skills.Strike.Act(view, rival, ref rng);
                case Dodge: return _skills.Dodge.Act(view, rival, ref rng);
                case Brake: return _skills.Brake.Act(view, default, ref rng);
                default: throw new InvalidOperationException($"duelist: no skill {_current}");
            }
        }

        /// <summary>The ten features, each 0..1, in the order of <see cref="Features"/>.</summary>
        public Fixed[] Sense(BotView v, Vec2 me, SeenObject rival)
        {
            var hw = v.Hud["arena_half_width"];
            var hh = v.Hud["arena_half_height"];
            var diagonal = Fixed.Sqrt(hw * hw + hh * hh) * 2;
            var dist = rival.Pos.DistanceTo(me);
            var pass = _skills.Dodge.Pass(v, rival);
            var far = _skills.Dodge.Danger * 2;
            int myPair = v.Self % _maxSpeed.Length;
            return new[]
            {
                Fixed.Clamp01(dist / diagonal),
                dist <= v.Hud["leash_m"] ? Fixed.One : Fixed.Zero,
                Fixed.Clamp01(SwingSkill.Owner(v, v.Self).Vel.Length / _blowSpeed),
                Fixed.Clamp01(SwingSkill.Owner(v, rival.Id).Vel.Length / _blowSpeed),
                pass < Fixed.Zero ? Fixed.Zero : Fixed.Clamp01(Fixed.One - pass / far),
                _skills.Dodge.HasCover(v, me) ? Fixed.One : Fixed.Zero,
                // no health bars on the screen (the park, the sandbox): nobody can be hurt, everybody is whole
                v.HudOr("hp_" + v.Self, Fixed.One),
                v.HudOr("hp_" + rival.Id, Fixed.One),
                Fixed.Clamp01(new Vec2(v.VX, v.VY).Length / _maxSpeed[myPair]),
                Fixed.Clamp01(v.HudOr("round_time_left", v.Hud["time_left"]) / _roundS),
            };
        }

        /// <summary>The utility of each skill for these features.</summary>
        public Fixed[] Utilities(Fixed[] features)
        {
            var u = new Fixed[Skills.Length];
            for (int s = 0; s < Skills.Length; s++)
            {
                var sum = _bias[s];
                for (int f = 0; f < Features.Length; f++) sum += _w[s, f] * features[f];
                u[s] = sum;
            }
            return u;
        }

        private void Choose(Fixed[] features)
        {
            var u = Utilities(features);
            int best = 0;
            for (int s = 1; s < Skills.Length; s++) if (u[s] > u[best]) best = s;
            if (_current < 0 || u[best] > u[_current] + _margin) _current = best;
        }
    }
}
