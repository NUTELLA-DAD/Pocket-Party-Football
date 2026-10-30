using System;
using System.Collections.Generic;
using System.IO;

namespace PocketFootball.View
{
    // The crash file's text (FEATURES.crashLog; Boot/CrashLog.cs feeds it the game's errors and owns the file). A
    // header about the PC, then one block per different error: when, what, and the whole stack. An error already
    // written only counts; past crashLog.maxEntries different ones it stops writing and says so. At the end (Close) one
    // line with the counts. No Unity here, so the EditMode tests can feed it.
    public sealed class CrashReport
    {
        readonly TextWriter w;
        readonly int maxEntries;
        readonly Dictionary<string, int> seen = new Dictionary<string, int>();
        bool full;

        public int Entries => seen.Count;
        public int Total { get; private set; }

        public CrashReport(TextWriter w, int maxEntries)
        {
            this.w = w;
            this.maxEntries = Math.Max(1, maxEntries);
        }

        // The header: one "key: value" line each (game version, Unity, Windows, graphics card, ...), then a blank line.
        public void Header(IEnumerable<KeyValuePair<string, string>> facts)
        {
            w.WriteLine("# Couch Bowl crash log. Send this file (and the game log next to it) to the developer.");
            foreach (var kv in facts) w.WriteLine(kv.Key + ": " + Clean(kv.Value));
            w.WriteLine();
            w.Flush();
        }

        // One error. Returns true when it was written (a new one under the cap), false when it only counted.
        public bool Add(string when, string kind, string message, string stack)
        {
            Total++;
            string first = FirstLine(stack);
            string key = kind + "|" + message + "|" + first;
            if (seen.TryGetValue(key, out int n)) { seen[key] = n + 1; return false; }
            if (seen.Count >= maxEntries)
            {
                if (!full) { full = true; w.WriteLine("# more errors after this are counted, not written"); w.Flush(); }
                return false;
            }
            seen[key] = 1;
            w.WriteLine("== " + when + "  " + kind);
            w.WriteLine(Clean(message));
            if (!string.IsNullOrEmpty(stack)) w.WriteLine(stack.TrimEnd());
            w.WriteLine();
            w.Flush();
            return true;
        }

        // The closing line: how many errors in all and how many different ones.
        public void Close(string when)
        {
            w.WriteLine("# " + when + "  closed: " + Total + " errors, " + seen.Count + " different");
            w.Flush();
        }

        static string FirstLine(string stack)
        {
            if (string.IsNullOrEmpty(stack)) return "";
            int nl = stack.IndexOf('\n');
            return (nl < 0 ? stack : stack.Substring(0, nl)).Trim();
        }

        static string Clean(string s) => (s ?? "").Replace("\r", "");

        // The pause menu's row: OPEN LOG FOLDER, with the count of errors this run when there were any.
        public static string FolderLabel(int errors) =>
            errors <= 0 ? "OPEN LOG FOLDER" : "OPEN LOG FOLDER (" + errors + (errors == 1 ? " ERROR)" : " ERRORS)");
    }
}
