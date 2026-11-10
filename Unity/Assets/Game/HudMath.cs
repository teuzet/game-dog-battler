using System.Collections.Generic;
using Game.Core.Modules.Match;
using Game.Core.Sim;

namespace DogBattler.View
{
    public enum PopupKind { Zone, Knockout, LeaderKnockout }

    /// <summary>Points that one player just got, for the popup above the dog.</summary>
    public readonly struct Popup
    {
        public readonly int Player, Points;
        public readonly PopupKind Kind;
        public Popup(int player, int points, PopupKind kind) { Player = player; Points = points; Kind = kind; }
    }

    /// <summary>The numbers and words of the HUD, free of scene objects so they can be tested. No game rules:
    /// points and their reasons are read from the awards the rules gave through Match.</summary>
    public static class HudMath
    {
        public const float PopupLifeS = 1.6f;
        public const float PopupRiseM = 1.8f;

        private static readonly string[] ColourNames = { "красный", "синий", "жёлтый", "фиолетовый" };
        public static string ColourName(int player) => ColourNames[player % ColourNames.Length];

        /// <summary>The final seconds of the match, highlighted on the timer.</summary>
        public static bool IsFinal(float timeLeft, float finalSeconds) => timeLeft > 0f && timeLeft <= finalSeconds;

        public static string Clock(float seconds)
        {
            int s = UnityEngine.Mathf.CeilToInt(seconds);
            return $"{s / 60}:{s % 60:00}";
        }

        /// <summary>The sole top scorer, -1 while the top is shared or nobody has points.</summary>
        public static int Leader(IReadOnlyList<int> scores)
        {
            int best = 0, who = -1;
            for (int p = 0; p < scores.Count; p++)
            {
                if (scores[p] > best) { best = scores[p]; who = p; }
                else if (scores[p] == best) who = -1;
            }
            return who;
        }

        /// <summary>One popup per award the rules gave in the last tick (Match.LastTickAwards).</summary>
        public static List<Popup> Popups(IEnumerable<PointsAward> awards)
        {
            var popups = new List<Popup>();
            foreach (var a in awards)
                popups.Add(new Popup(a.Player, a.Points, KindOf(a.Reason)));
            return popups;
        }

        private static PopupKind KindOf(string reason)
        {
            switch (reason)
            {
                case "zone": return PopupKind.Zone;
                case "knockout": return PopupKind.Knockout;
                case "leader_knockout": return PopupKind.LeaderKnockout;
                default: throw new System.ArgumentException($"no popup for the award reason '{reason}'");
            }
        }

        public static string PopupText(Popup p)
        {
            switch (p.Kind)
            {
                case PopupKind.Knockout: return $"ВЫБИЛ! +{p.Points}";
                case PopupKind.LeaderKnockout: return $"ВЫБИЛ ЛИДЕРА! +{p.Points}";
                default: return $"+{p.Points} в круге";
            }
        }

        /// <summary>The headline of the winner screen.</summary>
        public static string WinnerTitle(SessionResult result, int human)
        {
            int winner = -1, wins = 0;
            for (int p = 0; p < result.Outcomes.Length; p++)
                if (result.Outcomes[p] == Outcome.Win) { winner = p; wins++; }
            if (wins == 0) return "Ничья";
            return winner == human ? "Ты победил!" : $"Победил {ColourName(winner)}";
        }
    }
}
