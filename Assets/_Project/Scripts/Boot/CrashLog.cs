using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PocketFootball.Sim;
using PocketFootball.View;
using UnityEngine;

namespace PocketFootball.Boot
{
    // The crash catcher (FEATURES.crashLog, NO_CRASH_LOG=1 turns it off). From the first moment of a run it listens for
    // the game's errors and exceptions (Unity's log on every thread, plus any exception nothing caught) and writes
    // them to logs\crash-<date>-<time>.txt next to the game log, so a player at a party can send one file. The file is
    // made only when the first error comes: a clean run leaves nothing behind. The pause menu's SETTINGS has an OPEN
    // LOG FOLDER row that opens the folder in Explorer. (A hard crash of Unity itself is Unity's own crash handler;
    // the file's header names Unity's Player.log, which has the rest.) Nothing here writes to the sim.
    public static class CrashLog
    {
        static readonly object gate = new object();
        static CrashReport report;
        static StreamWriter file;
        static bool installed, failed;
        static int maxEntries;

        public static string Path { get; private set; }
        public static int Errors { get; private set; }

        // The folder the game log and the crash file go in: logs\ next to the build (the editor: .logs\games\).
        public static string Folder
        {
            get
            {
#if UNITY_EDITOR
                return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), ".logs", "games");
#else
                return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "logs");
#endif
            }
        }

        // (the editor may keep statics between play sessions: each session starts clean)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            OnQuit();
            Application.logMessageReceivedThreaded -= OnUnityLog;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
            Application.quitting -= OnQuit;
            installed = failed = false;
            Errors = 0;
            Path = null;
        }

        // Boot calls this once a scene load (a second call changes nothing). Not in batch runs (the tests).
        public static void Install(GameConfig cfg)
        {
            if (installed || Application.isBatchMode || !cfg.Feature("crashLog")) return;
            installed = true;
            maxEntries = cfg.crashLog.maxEntries;
            Application.logMessageReceivedThreaded += OnUnityLog;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
            Application.quitting += OnQuit;
        }

        static void OnUnityLog(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            Write(type.ToString().ToUpperInvariant(), message, stack);
        }

        static void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            Write(e.IsTerminating ? "FATAL" : "UNHANDLED", ex != null ? ex.GetType().Name + ": " + ex.Message : "" + e.ExceptionObject, ex?.StackTrace);
        }

        static void Write(string kind, string message, string stack)
        {
            lock (gate)
            {
                if (failed) return;
                Errors++;
                try
                {
                    if (report == null) Open();
                    report.Add(Now("yyyy-MM-dd HH:mm:ss"), kind, message, stack);
                }
                catch (Exception)
                {
                    failed = true;   // no crash file is better than an error about the crash file (never log from here)
                }
            }
        }

        static void Open()
        {
            string dir = Folder;
            Directory.CreateDirectory(dir);
            Path = System.IO.Path.Combine(dir, "crash-" + Now("yyyyMMdd-HHmmss") + ".txt");
            file = new StreamWriter(new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
            report = new CrashReport(file, maxEntries);
            report.Header(Facts());
        }

        static List<KeyValuePair<string, string>> Facts()
        {
            var f = new List<KeyValuePair<string, string>>();
            void Add(string k, Func<string> v)
            {
                string s;
                try { s = v(); } catch (Exception) { s = "?"; }   // (some of these are main-thread only)
                f.Add(new KeyValuePair<string, string>(k, s));
            }
            Add("started", () => Now("yyyy-MM-dd HH:mm:ss"));
            Add("game", () => Application.version);
            Add("unity", () => Application.unityVersion);
            Add("os", () => SystemInfo.operatingSystem);
            Add("cpu", () => SystemInfo.processorType + " x" + SystemInfo.processorCount);
            Add("memory", () => SystemInfo.systemMemorySize + " MB");
            Add("graphics", () => SystemInfo.graphicsDeviceName + " (" + SystemInfo.graphicsDeviceVersion + ", " + SystemInfo.graphicsMemorySize + " MB)");
            Add("screen", () => Screen.width + " x " + Screen.height + (Screen.fullScreen ? " full screen" : " windowed"));
            Add("game log", () => GameLog.Path ?? "(none yet)");
            Add("unity log", () => Application.consoleLogPath);
            return f;
        }

        static string Now(string format) => DateTime.Now.ToString(format, CultureInfo.InvariantCulture);

        static void OnQuit()
        {
            lock (gate)
            {
                try { report?.Close(Now("yyyy-MM-dd HH:mm:ss")); file?.Dispose(); } catch (Exception) { }
                report = null;
                file = null;
            }
        }

        // The pause menu's OPEN LOG FOLDER: the folder in Explorer (made first, so it opens even before any log).
        public static void OpenFolder()
        {
            try
            {
                string dir = Folder;
                Directory.CreateDirectory(dir);
                Application.OpenURL("file:///" + dir.Replace('\\', '/'));
                Debug.Log("[crash] opened the log folder: " + dir);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[crash] cannot open the log folder: " + e.Message);
            }
        }
    }
}
