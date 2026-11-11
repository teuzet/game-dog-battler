using System;
using System.Collections.Generic;
using UnityEngine;

namespace Factory.Shell
{
    /// <summary>The two screens the factory needs from Nick, on OnGUI so they
    /// need no assets: picking a variant under a blind label, and the
    /// survey after a session.</summary>
    public sealed class Overlay
    {
        public static readonly string[] Verdicts = { "весело", "так себе", "скучно" };

        public bool Picking, Surveying, Preparing;
        public IReadOnlyList<string> Labels = Array.Empty<string>();
        /// <summary>Owner types the player may pick before the match (Data/game.json → human_choices).</summary>
        public IReadOnlyList<string> Owners = Array.Empty<string>();
        /// <summary>What the pick screen shows for each of <see cref="Owners"/> (read from the data by the game).</summary>
        public IReadOnlyList<string> OwnerLabels = Array.Empty<string>();
        /// <summary>Arenas to pick (name, title) and bot archetypes for the opponent and for the watched participant 0.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Arenas = Array.Empty<KeyValuePair<string, string>>();
        public IReadOnlyList<string> Bots = Array.Empty<string>();
        /// <summary>The picks of the match; the game fills the defaults when the screen opens.</summary>
        public MatchSetup Setup = new MatchSetup();
        /// <summary>The first opponent of an arena's rules, for when another arena is picked.</summary>
        public Func<string, string> DefaultOpponent = a => "";
        public Action<int> OnPick;
        public Action<MatchSetup, bool> OnStart; // the picks, push mode
        public Action<string, bool, string, bool> OnSurvey; // verdict, again, boring_at, quit

        private int _owner;
        private bool _push = true;
        private Vector2 _scroll;
        private int _verdict = -1;
        private bool _again = true;
        private string _boringAt = "";

        public void OpenPrepare()
        {
            Preparing = true;
            _owner = 0;
        }

        public void OpenSurvey()
        {
            Surveying = true;
            _verdict = -1;
            _again = true;
            _boringAt = "";
        }

        public void Draw()
        {
            if (!Picking && !Surveying && !Preparing) return;
            float w = Mathf.Min(620, Screen.width - 40), h = Surveying ? 300 : Preparing ? Mathf.Min(620, Screen.height - 24) : 160;
            // the survey sits at the bottom: the winner screen of the view is above it
            float y = Surveying ? Screen.height - h - 12 : (Screen.height - h) / 2;
            var area = new Rect((Screen.width - w) / 2, y, w, h);
            GUI.Box(area, "");
            GUILayout.BeginArea(new Rect(area.x + 16, area.y + 16, area.width - 32, area.height - 32));
            var big = new GUIStyle(GUI.skin.label) { fontSize = 20, wordWrap = true };
            var button = new GUIStyle(GUI.skin.button) { fontSize = 18 };
            if (Picking)
            {
                GUILayout.Label("Выбери версию. Чем они отличаются, не скажу.", big);
                GUILayout.BeginHorizontal();
                for (int i = 0; i < Labels.Count; i++)
                    if (GUILayout.Button(Labels[i], button, GUILayout.Height(48))) { Picking = false; OnPick?.Invoke(i); }
                GUILayout.EndHorizontal();
            }
            else if (Preparing)
            {
                _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(area.height - 32 - 56));
                GUILayout.Label("Арена:", big);
                GUILayout.BeginHorizontal();
                foreach (var a in Arenas)
                    if (GUILayout.Button((Setup.Arena == a.Key ? "● " : "") + a.Value, button, GUILayout.Height(40)))
                    {
                        Setup.Arena = a.Key;
                        Setup.Opponent = DefaultOpponent(a.Key);
                    }
                GUILayout.EndHorizontal();
                GUILayout.Label("Соперник-бот:", big);
                Row(Bots, Setup.Opponent, b => Setup.Opponent = b, button);
                GUILayout.Label("Кто играет за участника 0:", big);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button((Setup.BotVsBot ? "" : "● ") + "Я играю", button, GUILayout.Height(40))) Setup.BotVsBot = false;
                if (GUILayout.Button((Setup.BotVsBot ? "● " : "") + "Бот против бота", button, GUILayout.Height(40))) Setup.BotVsBot = true;
                GUILayout.EndHorizontal();
                if (Setup.BotVsBot) Row(Bots, Setup.Bot0, b => Setup.Bot0 = b, button);
                GUILayout.Label("Кто твой хозяин?", big);
                GUILayout.BeginHorizontal();
                for (int i = 0; i < Owners.Count; i++)
                {
                    string text = (_owner == i ? "● " : "") + OwnerLabels[i];
                    if (GUILayout.Button(text, button, GUILayout.Height(72))) _owner = i;
                }
                GUILayout.EndHorizontal();
                GUILayout.Label("Мышь:", big);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button((_push ? "● " : "") + "Толчок (курсор скрыт)", button, GUILayout.Height(40))) _push = true;
                if (GUILayout.Button((_push ? "" : "● ") + "Курсор (бежит к точке)", button, GUILayout.Height(40))) _push = false;
                GUILayout.EndHorizontal();
                GUILayout.EndScrollView();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(Setup.BotVsBot ? "Смотреть" : "Играть", button, GUILayout.Height(48)))
                {
                    Preparing = false;
                    Setup.Owner = Owners[_owner];
                    OnStart?.Invoke(Setup, _push);
                }
            }
            else
            {
                GUILayout.Label("Как было?", big);
                GUILayout.BeginHorizontal();
                for (int i = 0; i < Verdicts.Length; i++)
                {
                    string text = (_verdict == i ? "● " : "") + Verdicts[i];
                    if (GUILayout.Button(text, button, GUILayout.Height(40))) _verdict = i;
                }
                GUILayout.EndHorizontal();
                _again = GUILayout.Toggle(_again, " Сыграл бы ещё раз", new GUIStyle(GUI.skin.toggle) { fontSize = 16 });
                GUILayout.Label("Где стало скучно или странно (можно пусто):", new GUIStyle(GUI.skin.label) { fontSize = 16 });
                _boringAt = GUILayout.TextField(_boringAt, 200, new GUIStyle(GUI.skin.textField) { fontSize = 16 });
                GUILayout.FlexibleSpace();
                GUI.enabled = _verdict >= 0;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Ещё раз", button, GUILayout.Height(40))) Send(false);
                if (GUILayout.Button("Выйти", button, GUILayout.Height(40))) Send(true);
                GUILayout.EndHorizontal();
                GUI.enabled = true;
            }
            GUILayout.EndArea();
        }

        private static void Row(IReadOnlyList<string> names, string current, Action<string> pick, GUIStyle button)
        {
            GUILayout.BeginHorizontal();
            foreach (var n in names)
                if (GUILayout.Button((current == n ? "● " : "") + n, button, GUILayout.Height(36))) pick(n);
            GUILayout.EndHorizontal();
        }

        private void Send(bool quit)
        {
            Surveying = false;
            OnSurvey?.Invoke(Verdicts[_verdict], _again, _boringAt.Trim(), quit);
        }
    }
}
