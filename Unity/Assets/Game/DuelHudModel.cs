using System.Collections.Generic;
using Game.Core.Math;
using Game.Core.Modules.Match;
using Game.Core.Modules.Rules;
using Game.Core.Sim;

namespace DogBattler.View
{
    /// <summary>What the duel HUD shows, read from the simulation without any scene objects so it can be
    /// tested: the health shares and the round clock come from the rules' own sensor
    /// (<c>RulesModule.Active.Sense</c>), the round score from Match, the knockout from the
    /// <c>knocked_out</c> event. The health and the rounds are counted by the core; this only reads them.</summary>
    public sealed class DuelHudModel
    {
        public const string RulesName = "Duel";
        public const float BannerS = 1.5f;

        /// <summary>The view picks the panel by the name of the active rules.</summary>
        public static bool Applies(Simulation sim) => sim.Get<RulesModule>(RulesModule.ModuleName).Active.Name == RulesName;

        /// <summary>Health of each dog as a share of the full, 0..1.</summary>
        public float[] Hp = new float[0];
        /// <summary>Rounds won by each participant (Match points).</summary>
        public int[] Wins = new int[0];
        /// <summary>1-based number of the round that is on, or was just played while the pause runs.</summary>
        public int Round;
        public float RoundTimeLeft;
        /// <summary>Between rounds: the dogs stand on their spawns.</summary>
        public bool Pause;
        public bool Over;
        /// <summary>Who knocked a dog out last, -1 = nobody yet.</summary>
        public int KnockedOutBy = -1;
        public int KnockoutTick = -1;
        public int Tick;

        private int _seen, _roundStartTick;
        private int _lastRound = -1;
        private readonly Dictionary<string, Fixed> _hud = new Dictionary<string, Fixed>();

        public void Take(Simulation sim)
        {
            var match = sim.Get<MatchModule>(MatchModule.ModuleName);
            _hud.Clear();
            sim.Get<RulesModule>(RulesModule.ModuleName).Active.Sense(sim, _hud);
            int n = match.Scores.Count;
            Hp = new float[n];
            Wins = new int[n];
            for (int p = 0; p < n; p++)
            {
                Hp[p] = (float)_hud["hp_" + p].ToDouble();
                Wins[p] = match.Scores[p];
            }
            Round = _hud["round"].FloorToInt();
            RoundTimeLeft = (float)_hud["round_time_left"].ToDouble();
            Over = sim.IsOver;
            Tick = sim.Tick;
            Pause = !Over && Round > 0 && RoundTimeLeft <= 0f;
            if (Round != _lastRound) { _lastRound = Round; _roundStartTick = sim.Tick; }
            var all = sim.Events.All;
            for (; _seen < all.Count; _seen++)
            {
                var e = all[_seen];
                if (e.Kind != "knocked_out" || e.Reward == null) continue; // the attacker's line of the pair
                KnockedOutBy = e.Actor;
                KnockoutTick = e.Tick;
            }
        }

        /// <summary>The round score «2 : 1».</summary>
        public string Score() => string.Join(" : ", Wins);

        /// <summary>The banner over the arena: «Нокаут!» just after a knockout, «Раунд N» on the pause before it
        /// and in the first second of it; empty otherwise and when the match is over.</summary>
        public string Banner()
        {
            if (Over) return "";
            int bannerTicks = (int)(BannerS * Simulation.TicksPerSecond);
            if (Pause && KnockoutTick >= 0 && Tick - KnockoutTick < bannerTicks) return "Нокаут!";
            if (Pause) return $"Раунд {Round + 1}";
            if (Round > 0 && Tick - _roundStartTick < Simulation.TicksPerSecond) return $"Раунд {Round}";
            return "";
        }

        /// <summary>The headline of the end of the match: «Победа 3:1» / «Поражение 1:3» for the viewed player,
        /// «Синий победил» when nobody plays (<paramref name="viewed"/> = -1).</summary>
        public string Result(SessionResult result, int viewed)
        {
            int winner = -1;
            for (int p = 0; p < result.Outcomes.Length; p++)
                if (result.Outcomes[p] == Outcome.Win) winner = p;
            if (winner < 0) return "Ничья";
            if (viewed < 0)
            {
                string c = HudMath.ColourName(winner);
                return char.ToUpperInvariant(c[0]) + c.Substring(1) + " победил";
            }
            int mine = Wins[viewed], theirs = 0;
            for (int p = 0; p < Wins.Length; p++)
                if (p != viewed && Wins[p] > theirs) theirs = Wins[p];
            return (winner == viewed ? "Победа " : "Поражение ") + mine + ":" + theirs;
        }
    }
}
