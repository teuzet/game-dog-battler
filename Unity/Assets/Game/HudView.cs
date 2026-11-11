using System.Collections.Generic;
using Factory.Shell;
using Game.Core.Modules.Match;
using Game.Core.Modules.Rules;
using Game.Core.Sim;
using UnityEngine;

namespace DogBattler.View
{
    /// <summary>The match HUD on OnGUI: timer with the final-seconds highlight, scores in player colours,
    /// the next-circle hint, points popups above the dogs and the winner screen. It reads the simulation
    /// and the events of the tick; the survey (shell) opens under the winner screen.</summary>
    public sealed class HudView : MonoBehaviour
    {
        private struct Live
        {
            public Popup Popup;
            public float Born;
        }

        private Simulation _sim;
        private ArenaView _arena;
        private readonly List<Live> _popups = new List<Live>();
        private float _finalSeconds;
        private DuelHudModel _duel; // set when the active rules are the duel's: that panel replaces the King of the Lawn one

        public DuelHudModel Duel => _duel;

        public IReadOnlyList<Popup> ActivePopups
        {
            get
            {
                var list = new List<Popup>();
                foreach (var l in _popups) list.Add(l.Popup);
                return list;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindAnyObjectByType<HudView>() != null) return;
            new GameObject("HudView").AddComponent<HudView>();
        }

        private void Start()
        {
            var game = FactoryGame.Instance;
            game.SessionStarted += Rebuild;
            game.Stepped += Take;
            if (game.Sim != null) Rebuild(game.Sim);
        }

        private void OnDestroy()
        {
            if (FactoryGame.Instance == null) return;
            FactoryGame.Instance.SessionStarted -= Rebuild;
            FactoryGame.Instance.Stepped -= Take;
        }

        /// <summary>A new session: forget the old popups, read the HUD numbers from the data.</summary>
        public void Rebuild(Simulation sim)
        {
            _sim = sim;
            _popups.Clear();
            _finalSeconds = sim.Data.Int("match.final_seconds");
            _duel = DuelHudModel.Applies(sim) ? new DuelHudModel() : null;
            _duel?.Take(sim);
        }

        /// <summary>The state after a tick: popups for the points the rules awarded in it.</summary>
        public void Take(Simulation sim)
        {
            if (_duel != null) { _duel.Take(sim); return; }
            foreach (var p in HudMath.Popups(sim.Get<MatchModule>(MatchModule.ModuleName).LastTickAwards))
                _popups.Add(new Live { Popup = p, Born = Time.unscaledTime });
        }

        // ---------------------------------------------------------------- drawing

        private void OnGUI()
        {
            if (_sim == null) return;
            if (_arena == null) _arena = FindAnyObjectByType<ArenaView>();
            float k = Screen.height / 720f;
            if (_duel != null)
            {
                DrawDuel(k);
                DrawHelp(k);
                return;
            }
            var match = _sim.Get<MatchModule>(MatchModule.ModuleName);
            DrawTimer(match, k);
            DrawScores(match, k);
            DrawNextZone(k);
            DrawHelp(k);
            DrawPopups(k);
            if (_sim.IsOver) DrawWinner(match, k);
        }

        /// <summary>The duel panel: round clock and number, the round score, a health bar over each dog,
        /// the banner («Раунд N», «Нокаут!») and the end of the match.</summary>
        private void DrawDuel(float k)
        {
            var d = _duel;
            var center = TextAnchor.UpperCenter;
            Outlined(new Rect(0, 4 * k, Screen.width, 50 * k), d.Pause || d.Over ? "" : HudMath.Clock(d.RoundTimeLeft), Style(Mathf.RoundToInt(40 * k), center));
            Outlined(new Rect(0, 54 * k, Screen.width, 40 * k), d.Score(), Style(Mathf.RoundToInt(34 * k), center));
            if (d.Round > 0) Outlined(new Rect(0, 96 * k, Screen.width, 26 * k), $"Раунд {d.Round}", Colored(Style(Mathf.RoundToInt(20 * k), center), new Color(0.85f, 0.85f, 0.85f)));
            if (_arena != null && _arena.ViewCamera != null && !d.Over)
                for (int p = 0; p < d.Hp.Length; p++) DrawHpBar(p, d.Hp[p], k);
            string banner = d.Banner();
            if (banner.Length > 0)
                Outlined(new Rect(0, Screen.height * 0.3f, Screen.width, 80 * k), banner, Colored(Style(Mathf.RoundToInt(64 * k), TextAnchor.MiddleCenter), new Color(1f, 0.9f, 0.3f)));
            if (!d.Over) return;
            float w = Mathf.Min(520 * k, Screen.width - 40), h = 110 * k;
            var rect = new Rect((Screen.width - w) / 2, 130 * k, w, h);
            Fill(rect, new Color(0f, 0f, 0f, 0.7f));
            Outlined(new Rect(rect.x, rect.y + 10 * k, rect.width, 50 * k), d.Result(_sim.Result!, FactoryGame.Instance.ViewedPlayer), Style(Mathf.RoundToInt(40 * k), center));
            Outlined(new Rect(rect.x, rect.y + 64 * k, rect.width, 36 * k), d.Score(), Colored(Style(Mathf.RoundToInt(28 * k), center), new Color(0.85f, 0.85f, 0.85f)));
        }

        private void DrawHpBar(int player, float share, float k)
        {
            var world = _arena.DogOf(player).position + Vector3.up * 1.6f;
            var screen = _arena.ViewCamera.WorldToScreenPoint(world);
            if (screen.z <= 0f) return;
            float w = 110 * k, h = 14 * k;
            var rect = new Rect(screen.x - w / 2, Screen.height - screen.y - h / 2, w, h);
            Fill(new Rect(rect.x - 2 * k, rect.y - 2 * k, rect.width + 4 * k, rect.height + 4 * k), new Color(0f, 0f, 0f, 0.7f));
            Fill(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(share), rect.height), ViewMath.PlayerColor(player));
        }

        private void DrawTimer(MatchModule match, float k)
        {
            float left = (float)match.TimeLeft.ToDouble();
            bool final = HudMath.IsFinal(left, _finalSeconds);
            int size = Mathf.RoundToInt(40 * k * (final ? 1f + 0.15f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f)) : 1f));
            var style = Style(size, TextAnchor.UpperCenter);
            style.normal.textColor = final ? new Color(1f, 0.25f, 0.2f) : Color.white;
            Outlined(new Rect(0, 4 * k, Screen.width, 50 * k), HudMath.Clock(left), style);
            if (final) Outlined(new Rect(0, 54 * k, Screen.width, 26 * k), "ФИНАЛ!", Colored(Style(Mathf.RoundToInt(22 * k), TextAnchor.UpperCenter), new Color(1f, 0.25f, 0.2f)));
        }

        private void DrawScores(MatchModule match, float k)
        {
            int n = match.Scores.Count;
            float w = 118 * k, h = 44 * k, gap = 8 * k, y = (_sim.IsOver ? 4 * k : 84 * k);
            float x0 = (Screen.width - (n * w + (n - 1) * gap)) / 2;
            int leader = HudMath.Leader(match.Scores);
            for (int p = 0; p < n; p++)
            {
                var rect = new Rect(x0 + p * (w + gap), y, w, h);
                var colour = ViewMath.PlayerColor(p);
                Fill(rect, new Color(0f, 0f, 0f, 0.55f));
                Fill(new Rect(rect.x, rect.y, 8 * k, rect.height), colour);
                if (p == leader) Frame(rect, Color.white, 2 * k);
                var score = Colored(Style(Mathf.RoundToInt(28 * k), TextAnchor.MiddleCenter), colour);
                Outlined(new Rect(rect.x + 8 * k, rect.y, rect.width - 8 * k, rect.height), match.Scores[p].ToString(), score);
                if (p == FactoryGame.HumanPlayer)
                    Outlined(new Rect(rect.x + 12 * k, rect.y + 1 * k, 40 * k, 16 * k), "ТЫ", Colored(Style(Mathf.RoundToInt(13 * k), TextAnchor.UpperLeft), Color.white));
            }
        }

        private void DrawNextZone(float k)
        {
            if (_sim.IsOver || _sim.Get<RulesModule>(RulesModule.ModuleName).Active.NextZone == null) return;
            Outlined(new Rect(0, 132 * k, Screen.width, 28 * k), "Круг скоро переедет — следующий уже отмечен на газоне",
                Colored(Style(Mathf.RoundToInt(18 * k), TextAnchor.UpperCenter), new Color(0.75f, 0.95f, 0.6f)));
        }

        private void DrawHelp(float k)
        {
            var s = Style(Mathf.RoundToInt(16 * k), TextAnchor.LowerLeft);
            string mode = InputReader.PushMode ? "ТОЛЧОК (курсор скрыт, двигай мышь)" : "КУРСОР (собака бежит к точке)";
            Outlined(new Rect(12 * k, Screen.height - 62 * k, Screen.width, 26 * k), $"Режим мыши: {mode}   [Tab — переключить]", s);
            string keys = _sim.IsOver ? "Матч окончен: «Ещё раз» в опросе — новая игра (R работает только в матче)"
                : FactoryGame.Instance.BotVsBot ? "Бот против бота: ты смотришь   R — заново   Esc — в меню"
                : "ЛКМ/Q — абилка собаки   ПКМ/E — абилка хозяина   R — заново";
            Outlined(new Rect(12 * k, Screen.height - 36 * k, Screen.width, 26 * k), keys, s);
        }

        private void DrawPopups(float k)
        {
            if (_arena == null || _arena.ViewCamera == null) return;
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                float age = Time.unscaledTime - _popups[i].Born;
                if (age > HudMath.PopupLifeS) { _popups.RemoveAt(i); continue; }
                var popup = _popups[i].Popup;
                float t = age / HudMath.PopupLifeS;
                var world = _arena.DogOf(popup.Player).position + Vector3.up * (1.2f + HudMath.PopupRiseM * t);
                var screen = _arena.ViewCamera.WorldToScreenPoint(world);
                if (screen.z <= 0f) continue;
                bool big = popup.Kind != PopupKind.Zone;
                var colour = ViewMath.PlayerColor(popup.Player);
                colour.a = 1f - t * t;
                var style = Colored(Style(Mathf.RoundToInt((big ? 30 : 22) * k), TextAnchor.MiddleCenter), colour);
                Outlined(new Rect(screen.x - 150 * k, Screen.height - screen.y - 20 * k, 300 * k, 40 * k), HudMath.PopupText(popup), style);
            }
        }

        private void DrawWinner(MatchModule match, float k)
        {
            float w = Mathf.Min(460 * k, Screen.width - 40), h = 270 * k;
            var rect = new Rect((Screen.width - w) / 2, 62 * k, w, h);
            Fill(rect, new Color(0f, 0f, 0f, 0.7f));
            Outlined(new Rect(rect.x, rect.y + 8 * k, rect.width, 48 * k), HudMath.WinnerTitle(_sim.Result!, FactoryGame.HumanPlayer), Style(Mathf.RoundToInt(36 * k), TextAnchor.UpperCenter));
            var order = new List<int>();
            for (int p = 0; p < match.Scores.Count; p++) order.Add(p);
            order.Sort((a, b) => match.Scores[b] != match.Scores[a] ? match.Scores[b].CompareTo(match.Scores[a]) : a.CompareTo(b));
            for (int row = 0; row < order.Count; row++)
            {
                int p = order[row];
                var line = new Rect(rect.x + 24 * k, rect.y + 64 * k + row * 44 * k, rect.width - 48 * k, 38 * k);
                Fill(new Rect(line.x, line.y + 4 * k, 10 * k, line.height - 8 * k), ViewMath.PlayerColor(p));
                string who = HudMath.ColourName(p) + (p == FactoryGame.HumanPlayer ? " (ты)" : "");
                Outlined(new Rect(line.x + 20 * k, line.y, line.width, line.height), $"{row + 1}. {who}", Colored(Style(Mathf.RoundToInt(24 * k), TextAnchor.MiddleLeft), Color.white));
                Outlined(line, match.Scores[p].ToString(), Colored(Style(Mathf.RoundToInt(28 * k), TextAnchor.MiddleRight), ViewMath.PlayerColor(p)));
            }
        }

        // ---------------------------------------------------------------- helpers

        private static GUIStyle Style(int size, TextAnchor anchor)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Bold, alignment = anchor };
            s.normal.textColor = Color.white;
            return s;
        }

        private static GUIStyle Colored(GUIStyle s, Color c)
        {
            s.normal.textColor = c;
            return s;
        }

        private static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private static void Frame(Rect r, Color c, float t)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c);
            Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        private static void Outlined(Rect r, string text, GUIStyle style)
        {
            var color = style.normal.textColor;
            var shadow = new Color(0f, 0f, 0f, 0.8f * color.a);
            style.normal.textColor = shadow;
            foreach (var o in new[] { new Vector2(-1.5f, 0), new Vector2(1.5f, 0), new Vector2(0, -1.5f), new Vector2(0, 1.5f) })
                GUI.Label(new Rect(r.x + o.x, r.y + o.y, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
        }
    }
}
