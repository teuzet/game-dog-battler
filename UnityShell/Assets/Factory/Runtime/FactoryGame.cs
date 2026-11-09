using System;
using System.Collections.Generic;
using Game.Core;
using Game.Core.Bots;
using Game.Core.Data;
using Game.Core.Sim;
using UnityEngine;

namespace Factory.Shell
{
    /// <summary>Entry point of the game in Unity. Appears by itself in any
    /// scene. Steps the core at a fixed rate, the human is participant 0,
    /// bots play the rest. No game rules here: they live in Game.Core.</summary>
    public sealed class FactoryGame : MonoBehaviour
    {
        public const int HumanPlayer = 0;
        const int MaxStepsPerFrame = 5;

        public static FactoryGame Instance { get; private set; }
        public Simulation Sim { get; private set; }
        public IGame Game => GameSetup.Game;
        /// <summary>Raised on every new session, for views to rebuild.</summary>
        public event Action<Simulation> SessionStarted;

        private string _dataDir = "";
        private GameData _base, _data;
        private List<BotDriver> _bots = new List<BotDriver>();
        private InputLog _log;
        private float _acc;
        private int _seed;
        private readonly Overlay _overlay = new Overlay();
        private List<string> _variantNames = new List<string>();
        private string _variantSet = "", _variantLabel = "";
        private bool _ended;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindAnyObjectByType<FactoryGame>() != null) return;
            new GameObject("FactoryGame").AddComponent<FactoryGame>();
        }

        private void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 60;
            _dataDir = NickFiles.DataDir();
            _base = GameData.Load(_dataDir);
            _overlay.OnPick = Pick;
            _overlay.OnSurvey = Survey;
            var sets = GameData.VariantSets(_dataDir);
            if (sets.Count > 0)
            {
                _variantSet = sets[sets.Count - 1];
                _variantNames = new List<string>(GameData.VariantNames(_dataDir, _variantSet));
            }
        }

        private void Start()
        {
            if (_variantNames.Count > 1) ShowPicker();
            else NewSession(_base!);
        }

        private void ShowPicker()
        {
            // Blind labels: the order of the variants behind A, B, C changes every launch.
            var rng = new System.Random(Environment.TickCount);
            for (int i = _variantNames.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (_variantNames[i], _variantNames[j]) = (_variantNames[j], _variantNames[i]);
            }
            var labels = new List<string>();
            for (int i = 0; i < _variantNames.Count; i++) labels.Add(((char)('A' + i)).ToString());
            _overlay.Labels = labels;
            _overlay.Picking = true;
        }

        private void Pick(int i)
        {
            _variantLabel = ((char)('A' + i)).ToString();
            NewSession(_base!.WithVariant(_dataDir, _variantSet, _variantNames[i]));
        }

        private void NewSession(GameData data)
        {
            _data = data;
            _seed = Environment.TickCount & 0x7fffffff;
            Sim = Game.Create(_seed, data);
            _bots = new List<BotDriver>();
            var opponents = data.Get("bots.session.opponents").Items;
            for (int p = 0; p < Sim.Players; p++)
            {
                if (p == HumanPlayer) continue;
                var a = Archetype.Find(data, opponents[(p - 1) % opponents.Count].AsString());
                _bots.Add(new BotDriver(p, a, Game.Strategy(a, data), (ulong)_seed));
            }
            _log = new InputLog(_seed, Sim.Players, data.Variant);
            _acc = 0;
            _ended = false;
            SessionStarted?.Invoke(Sim);
        }

        private void Update()
        {
            if (Sim == null || _overlay.Picking) return;
            if (Sim.IsOver)
            {
                if (!_ended)
                {
                    _ended = true;
                    SaveLog();
                    _overlay.OpenSurvey();
                }
                return;
            }
            if (InputReader.RestartPressed())
            {
                SaveLog();
                NewSession(_data!);
                return;
            }
            float dt = 1f / Simulation.TicksPerSecond;
            _acc += Time.deltaTime;
            int steps = 0;
            while (_acc >= dt && steps < MaxStepsPerFrame && !Sim.IsOver)
            {
                var inputs = Forks.Inputs(Game, Sim, _bots, -1, PlayerInput.None);
                inputs[HumanPlayer] = InputReader.Read();
                _log!.Record(inputs);
                Sim.Step(inputs);
                _acc -= dt;
                steps++;
            }
            if (steps == MaxStepsPerFrame) _acc = 0; // a slow frame does not snowball
        }

        private void OnGUI() => _overlay.Draw();

        private void SaveLog()
        {
            if (_log == null || _log.Ticks.Count == 0) return;
            NickFiles.Write($"inputs-{NickFiles.Stamp()}.txt", _log.Serialize());
        }

        private void Survey(string verdict, bool again, string boringAt, bool quit)
        {
            var s = JsonValue.Object();
            s.Set("verdict", JsonValue.Str(verdict));
            s.Set("again", JsonValue.Bool(again));
            s.Set("boring_at", JsonValue.Str(boringAt));
            s.Set("variant", JsonValue.Str(_variantLabel.Length == 0 ? "" : $"{_variantLabel} = {_data!.Variant}"));
            s.Set("seed", Json.Num(_seed));
            s.Set("duration_s", Json.Num(Sim!.Seconds));
            var outcome = Sim.Result?.Outcomes[HumanPlayer];
            s.Set("outcome", JsonValue.Str(outcome == Outcome.Win ? "win" : outcome == Outcome.Loss ? "loss" : "draw"));
            s.Set("time", JsonValue.Str(DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture)));
            NickFiles.WriteJson($"survey-{NickFiles.Stamp()}.json", s);
            if (quit)
            {
                Application.Quit();
                return;
            }
            if (_variantNames.Count > 1) ShowPicker();
            else NewSession(_data!);
        }
    }
}
