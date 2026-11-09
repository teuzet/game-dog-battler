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

        public bool Picking, Surveying;
        public IReadOnlyList<string> Labels = Array.Empty<string>();
        public Action<int> OnPick;
        public Action<string, bool, string, bool> OnSurvey; // verdict, again, boring_at, quit

        private int _verdict = -1;
        private bool _again = true;
        private string _boringAt = "";

        public void OpenSurvey()
        {
            Surveying = true;
            _verdict = -1;
            _again = true;
            _boringAt = "";
        }

        public void Draw()
        {
            if (!Picking && !Surveying) return;
            float w = Mathf.Min(520, Screen.width - 40), h = Surveying ? 300 : 160;
            var area = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
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

        private void Send(bool quit)
        {
            Surveying = false;
            OnSurvey?.Invoke(Verdicts[_verdict], _again, _boringAt.Trim(), quit);
        }
    }
}
