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
        /// <summary>Raised after every tick, for views to take the new state.</summary>
        public event Action<Simulation> Stepped;
        /// <summary>0..1: how far the frame is between the last tick and the next, for interpolation.</summary>
        public float Alpha => Sim == null ? 0f : Mathf.Clamp01(_acc * Simulation.TicksPerSecond);
        /// <summary>True while a window (variant picker, survey) needs the mouse.</summary>
        public bool MenuOpen => _overlay.Picking || _overlay.Surveying || _overlay.Preparing;

        private string _dataDir = "";
        private GameData _base, _data;
        private List<BotDriver> _bots = new List<BotDriver>();
        private InputLog _log;
        private float _acc;
        private int _seed;
        private readonly Overlay _overlay = new Overlay();
        private List<string> _variantNames = new List<string>();
        private string _variantSet = "", _variantLabel = "", _variantName = "";
        private bool _ended, _capture;
        private int? _fixedSeed;
        private MatchSetup _setup = new MatchSetup();

        /// <summary>The human only watches: participant 0 is a bot.</summary>
        public bool BotVsBot => _setup.BotVsBot;
        /// <summary>The seed and the data of the running session, for a capture that drives a bot itself.</summary>
        public int Seed => _seed;
        public GameData Data => _data;
        /// <summary>A screenshot run (-capture) without -seed takes its seed from here when a view sets it
        /// (the seed of a searched moment, from the session's data); otherwise a new one as usual.</summary>
        public static Func<GameData, int> CaptureSeed;
        /// <summary>The player the screen is about; -1 when nobody plays (bot against bot).</summary>
        public int ViewedPlayer => _setup.BotVsBot ? -1 : HumanPlayer;

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
            // "-arena <name>" on the command line plays another arena of Data/arenas (e.g. the sandbox).
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-arena");
            if (at >= 0 && at + 1 < args.Length) _base = _base.WithArena(args[at + 1]);
            _overlay.OnPick = Pick;
            _overlay.OnSurvey = Survey;
            _overlay.OnStart = Begin;
            _overlay.Arenas = MatchSetup.Arenas(_base);
            _overlay.Bots = MatchSetup.Archetypes(_base);
            _overlay.Setup = MatchSetup.Defaults(_base, _base.Str("game.arena"));
            _overlay.DefaultOpponent = a => Archetype.Opponents(_base.WithArena(a))[0];
            _setup = _overlay.Setup.Copy();
            var owners = new List<string>();
            var ownerLabels = new List<string>();
            foreach (var o in _base.Get("game.human_choices").Items)
            {
                owners.Add(o.AsString());
                // the pick screen shows the owner's title and one-line hint from Data/owners/<type>.json
                ownerLabels.Add(_base.Str("owners." + o.AsString() + ".title") + "\n" + _base.Str("owners." + o.AsString() + ".pick_hint"));
            }
            _overlay.Owners = owners;
            _overlay.OwnerLabels = ownerLabels;
            _capture = Array.IndexOf(args, "-capture") >= 0;
            // "-seed <N>" fixes the seed of the session (a screenshot of a known moment); without it every session gets a new one.
            int seedAt = Array.IndexOf(args, "-seed");
            if (seedAt >= 0 && seedAt + 1 < args.Length) _fixedSeed = int.Parse(args[seedAt + 1], System.Globalization.CultureInfo.InvariantCulture);
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
            else Prepare();
        }

        /// <summary>The pre-match screen: arena, opponent, who plays, owner type and mouse mode. A screenshot
        /// run (-capture) skips it and plays the data's own pick.</summary>
        private void Prepare()
        {
            if (_capture) NewSession(_setup.Data(_base, WithVariant));
            else _overlay.OpenPrepare();
        }

        /// <summary>The picked variant on top of the data (none picked: the data as it is).</summary>
        private GameData WithVariant(GameData data) =>
            _variantName.Length == 0 ? data : data.WithVariant(_dataDir, _variantSet, _variantName);

        private void Begin(MatchSetup setup, bool push)
        {
            InputReader.SetPushMode(push);
            _setup = setup.Copy();
            NewSession(_setup.Data(_base, WithVariant));
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
            _variantName = _variantNames[i];
            Prepare();
        }

        private void NewSession(GameData data)
        {
            _data = data;
            _seed = _fixedSeed ?? (_capture && CaptureSeed != null ? CaptureSeed(data) : Environment.TickCount & 0x7fffffff);
            Sim = Game.Create(_seed, data);
            _bots = new List<BotDriver>();
            _bots = _setup.Drivers(Game, data, Sim.Players, _seed);
            _log = new InputLog(_seed, Sim.Players, data.Variant, data.Str("game.human_owner"), data.Str("game.arena"));
            _acc = 0;
            _ended = false;
            SessionStarted?.Invoke(Sim);
        }

        private void Update()
        {
            if (Sim == null) return;
            InputReader.Frame();
            ApplyCursor();
            if (_overlay.Picking || _overlay.Preparing) return;
            if (_setup.BotVsBot && !Sim.IsOver && InputReader.BackPressed())
            {
                SaveLog();
                _overlay.OpenPrepare();
                return;
            }
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
                NewSession(_data);
                return;
            }
            float dt = 1f / Simulation.TicksPerSecond;
            _acc += Time.deltaTime;
            int steps = 0;
            while (_acc >= dt && steps < MaxStepsPerFrame && !Sim.IsOver)
            {
                var inputs = Forks.Inputs(Game, Sim, _bots, -1, PlayerInput.None);
                if (!_setup.BotVsBot) inputs[HumanPlayer] = InputReader.Read();
                _log.Record(inputs);
                Sim.Step(inputs);
                Stepped?.Invoke(Sim);
                _acc -= dt;
                steps++;
            }
            if (steps == MaxStepsPerFrame) _acc = 0; // a slow frame does not snowball
        }

        /// <summary>Push mode plays with a locked cursor; menus and the end of a session free it.</summary>
        private void ApplyCursor()
        {
            bool locked = InputReader.PushMode && !MenuOpen && !Sim.IsOver;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
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
            s.Set("variant", JsonValue.Str(_variantLabel.Length == 0 ? "" : $"{_variantLabel} = {_data.Variant}"));
            s.Set("owner", JsonValue.Str(_data.Str("game.human_owner")));
            s.Set("arena", JsonValue.Str(_data.Str("game.arena")));
            s.Set("mode", JsonValue.Str(_setup.BotVsBot ? "bot_vs_bot" : "human"));
            s.Set("opponent", JsonValue.Str(_setup.Opponent));
            s.Set("bot0", JsonValue.Str(_setup.BotVsBot ? _setup.Bot0 : ""));
            s.Set("seed", Json.Num(_seed));
            s.Set("duration_s", Json.Num(Sim.Seconds));
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
            else Prepare();
        }
    }
}
