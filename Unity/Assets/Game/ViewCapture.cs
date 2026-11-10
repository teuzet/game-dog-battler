using System;
using System.Collections;
using Factory.Shell;
using Game.Core.Sim;
using UnityEngine;

namespace DogBattler.View
{
    /// <summary>Screenshots for the project's previews: "Unity.exe -arena sandbox -capture shot.png" plays a
    /// scripted swing (the dog circles in push mode and whirls its owner), saves the screen at the tick of
    /// the swing and quits. It does nothing without the argument.</summary>
    public sealed class ViewCapture : MonoBehaviour
    {
        const int SwingTick = 66; // where the scripted swing is fastest (3.6 m/s) in the sandbox
        private string _path = "";

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
            InputReader.Scripted = () =>
            {
                float a = game.Sim.Tick * 0.16f;
                return new PlayerInput((int)(Mathf.Cos(a) * 38), (int)(Mathf.Sin(a) * 38), PlayerInput.PushBit);
            };
            while (game.Sim.Tick < SwingTick) yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(_path);
            yield return null;
            yield return null;
            Debug.Log($"ViewCapture: saved {_path} at tick {game.Sim.Tick}");
            Application.Quit();
        }
    }
}
