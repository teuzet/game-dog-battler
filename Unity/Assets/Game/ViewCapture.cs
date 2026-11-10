using System;
using System.Collections;
using System.IO;
using Factory.Shell;
using Game.Core.Modules.Hits;
using Game.Core.Sim;
using UnityEngine;

namespace DogBattler.View
{
    /// <summary>Screenshots for the project's previews: "Unity.exe -arena sandbox -capture shot.png" plays a
    /// scripted swing (the dog runs at the mannequin of player 3, then circles and whirls its owner through
    /// it), saves the screen at the tick of the blow and <see cref="SlideTicks"/> later (shot-slide.png) and
    /// quits. It does nothing without the argument.</summary>
    public sealed class ViewCapture : MonoBehaviour
    {
        public const int SwingTick = 60;   // the blow lands by this tick on the sandbox spawns (the test proves it)
        public const int SlideTicks = 15;  // 0.5 s after the blow
        private const int RunTicks = 30;   // straight toward the mannequin, then the circle
        private const float Turn = 0.16f;  // rad per tick
        private const float Reach = 38f;   // mouse shift, arena cm per tick
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-capture");
            if (at < 0 || at + 1 >= args.Length) return;
            new GameObject("ViewCapture").AddComponent<ViewCapture>()._path = args[at + 1];
        }

        private IEnumerator Start()
        {
            var game = FactoryGame.Instance;
            while (game.Sim == null) yield return null;
            InputReader.Scripted = () => SwingInput(game.Sim.Tick);
            int blowTick = -1;
            game.Stepped += sim =>
            {
                if (blowTick >= 0) return;
                foreach (var hit in sim.Get<HitsModule>(HitsModule.ModuleName).ThisTick)
                    if (hit.Attacker == 0) blowTick = sim.Tick;
            };
            while (blowTick < 0)
            {
                if (game.Sim.Tick > SwingTick) throw new InvalidOperationException($"the scripted swing did not connect by tick {SwingTick}");
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
