using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Factory.Shell;
using Game.Core;
using Game.Core.Bots;
using Game.Core.Data;
using Game.Core.Modules.Bodies;
using Game.Core.Modules.Hits;
using Game.Core.Modules.Owners;
using Game.Core.Sim;
using UnityEngine;

namespace DogBattler.View
{
    /// <summary>Screenshots for the project's previews. "Unity.exe -arena sandbox -capture shot.png" plays a
    /// scripted swing (the dog runs at the mannequin of player 3, then circles and whirls its owner through
    /// it), saves the screen at the tick of the blow and <see cref="SlideTicks"/> later (shot-slide.png) and
    /// quits. "Unity.exe -arena park -seed <see cref="ParkSeed"/> -capture shot.png" plays a park match of four
    /// bots (participant 0 is a "smart" bot fed through the scripted input) and saves the screen at the first
    /// strong blow of the match, and again <see cref="SlideTicks"/> later. It does nothing without -capture.</summary>
    public sealed class ViewCapture : MonoBehaviour
    {
        public const int SwingTick = 60;   // the blow lands by this tick on the sandbox spawns (the test proves it)
        public const int SlideTicks = 15;  // 0.5 s after the blow
        private const int RunTicks = 30;   // straight toward the mannequin, then the circle
        private const float Turn = 0.16f;  // rad per tick
        private const float Reach = 38f;   // mouse shift, arena cm per tick

        /// <summary>The park frame is of the first strong blow of the match, if it comes before this tick (100 s).</summary>
        public const int ParkGiveUpTick = 100 * Simulation.TicksPerSecond;
        public const string ParkArena = "park";

        // Found by running the park with four bots (ParkBots: "smart" for participant 0, the data's opponents for
        // the rest) over seeds 1, 2, 3… and taking the first whose first strong blow met the conditions of the frame
        // (a dog inside the circle, the next circle announced or the circle visible, 30..100 s left, not all scores
        // zero, the attacker's leash taut ≥ 0.9 at the blow or the tick before). ParkCaptureTick is the tick after
        // which that blow was recorded; the park frame test replays both and writes art/frame-base.json.
        // Seed 1 qualified too, but at 99.6 s left, on the edge of the range; seed 2 is the first comfortable one.
        public const int ParkSeed = 2;
        public const int ParkCaptureTick = 1003; // 86.6 s left

        private string _path = "";

        /// <summary>Push-mode input of the scripted swing at a tick: the mouse shift (arena cm) runs the dog
        /// toward +Y (the mannequin at (-6, 4)), then turns on a circle that swings the owner into that dog.</summary>
        public static PlayerInput SwingInput(int tick)
        {
            float a = Mathf.PI / 2 + Mathf.Max(0, tick - RunTicks) * Turn;
            return new PlayerInput((int)(Mathf.Cos(a) * Reach), (int)(Mathf.Sin(a) * Reach), PlayerInput.PushBit);
        }

        /// <summary>The second picture's name: shot.png → shot-slide.png.</summary>
        public static string SlidePath(string path) =>
            Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path) + "-slide" + Path.GetExtension(path));

        /// <summary>The bots of the park frame: participant 0 is "smart", the others are the data's opponents,
        /// exactly as in a normal match.</summary>
        public static List<BotDriver> ParkBots(GameData data, int players, int seed)
        {
            var setup = new MatchSetup { Arena = ParkArena, Opponent = Archetype.Opponents(data)[0], BotVsBot = true, Bot0 = "smart" };
            return setup.Drivers(GameSetup.Game, data, players, seed);
        }

        /// <summary>A strong blow among the hits of the tick that has just been stepped: the flash is full
        /// (ViewMath.FlashAmount ≥ 1, the speed taken out of the strength against the strong threshold).</summary>
        public static bool StrongBlow(Simulation sim, out Hit blow)
        {
            var hits = sim.Get<HitsModule>(HitsModule.ModuleName);
            var bodies = sim.Get<BodiesModule>(BodiesModule.ModuleName);
            var owners = sim.Get<OwnersModule>(OwnersModule.ModuleName);
            foreach (var hit in hits.ThisTick)
            {
                float mass = (float)bodies.Mass(owners.BodyOf(hit.Attacker)).ToDouble();
                if (ViewMath.FlashAmount((float)hit.Strength.ToDouble(), mass, (float)hits.StrongThreshold.ToDouble()) >= 1f)
                {
                    blow = hit;
                    return true;
                }
            }
            blow = default;
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-capture");
            if (at < 0 || at + 1 >= args.Length) return;
            new GameObject("ViewCapture").AddComponent<ViewCapture>()._path = args[at + 1];
        }

        private static bool ParkRequested()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-arena");
            return at >= 0 && at + 1 < args.Length && args[at + 1] == ParkArena;
        }

        private IEnumerator Start()
        {
            var game = FactoryGame.Instance;
            while (game.Sim == null) yield return null;
            bool park = ParkRequested();
            int limit = park ? ParkGiveUpTick : SwingTick;
            if (park)
            {
                // participant 0 is not the human here: its bot's input goes in as the scripted input
                BotDriver driver = null;
                foreach (var b in ParkBots(game.Data, game.Sim.Players, game.Seed))
                    if (b.Player == FactoryGame.HumanPlayer) driver = b;
                InputReader.Scripted = () => driver.Next(game.Game.Sense(game.Sim, FactoryGame.HumanPlayer));
            }
            else InputReader.Scripted = () => SwingInput(game.Sim.Tick);
            int blowTick = -1;
            game.Stepped += sim =>
            {
                if (blowTick >= 0) return;
                if (park)
                {
                    if (StrongBlow(sim, out _)) blowTick = sim.Tick;
                    return;
                }
                foreach (var hit in sim.Get<HitsModule>(HitsModule.ModuleName).ThisTick)
                    if (hit.Attacker == 0) blowTick = sim.Tick;
            };
            while (blowTick < 0)
            {
                if (game.Sim.Tick > limit) throw new InvalidOperationException($"no {(park ? "strong blow" : "scripted swing")} by tick {limit}");
                yield return null;
            }
            yield return Save(_path, game);
            int slideAt = blowTick + SlideTicks;
            while (game.Sim.Tick < slideAt) yield return null;
            yield return Save(SlidePath(_path), game);
            Application.Quit();
        }

        private static IEnumerator Save(string path, FactoryGame game)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(path);
            yield return null;
            yield return null;
            Debug.Log($"ViewCapture: saved {path} at tick {game.Sim.Tick}");
        }
    }
}
