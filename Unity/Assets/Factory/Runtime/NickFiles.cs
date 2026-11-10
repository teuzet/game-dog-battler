using System;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Core.Data;
using UnityEngine;

namespace Factory.Shell
{
    /// <summary>Where the build keeps what Nick did: nick/ next to the exe
    /// (in the editor: Unity/Builds/nick). The dispatcher collects it.</summary>
    public static class NickFiles
    {
        public static string Dir()
        {
            string dir = Application.isEditor
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "nick"))
                : Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "nick");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string Stamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        public static void Write(string name, string text) =>
            File.WriteAllText(Path.Combine(Dir(), name), text, new UTF8Encoding(false));

        public static void WriteJson(string name, JsonValue v) => Write(name, v.ToString() + "\n");

        /// <summary>Game.Core/Data: next to the sources in the editor, StreamingAssets in a build.</summary>
        public static string DataDir() => Application.isEditor
            ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game.Core", "Data"))
            : Path.Combine(Application.streamingAssetsPath, "Data");
    }
}
