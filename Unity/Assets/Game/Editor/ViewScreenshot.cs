using System.Diagnostics;
using System.IO;
using UnityEngine;

/// <summary>Takes the sandbox preview art/previews/f007-sandbox-swing.png by running the demo build with a
/// scripted swing (see DogBattler.View.ViewCapture). Needs a build (FactoryBuild.Demo) and a real graphics
/// device, so not in -nographics batch runs: the editor's batch tool renders nothing.</summary>
public static class ViewScreenshot
{
    public static void Sandbox()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        string exe = Path.Combine(root, "Unity", "Builds", "demo", "Unity.exe");
        string png = Path.Combine(root, "art", "previews", "f007-sandbox-swing.png");
        Directory.CreateDirectory(Path.GetDirectoryName(png)!);
        using var p = Process.Start(exe, $"-screen-width 1600 -screen-height 900 -screen-fullscreen 0 -arena sandbox -capture \"{png}\"")!;
        p.WaitForExit(60000);
    }
}
