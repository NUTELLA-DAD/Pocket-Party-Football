using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PocketFootball.Sim;
using PocketFootball.View;

namespace PocketFootball.Tests.EditMode
{
    // FEATURES.crashLog: the crash file's text (View/CrashReport.cs; Boot/CrashLog.cs owns the file).
    public sealed class CrashReportTests
    {
        [Test]
        public void TheSwitchAndItsBlockLoad()
        {
            var cfg = ConfigTests.Load();
            Assert.IsTrue(cfg.Feature("crashLog"));
            Assert.Greater(cfg.crashLog.maxEntries, 0);
            Assert.AreEqual("NO_CRASH_LOG", ConfigLoader.EnvName("crashLog"));
            Assert.IsFalse(ConfigTests.Load(new Dictionary<string, string> { { "NO_CRASH_LOG", "1" } }).Feature("crashLog"));
        }

        [Test]
        public void AnErrorIsWrittenWithItsWholeStackAndTheHeaderFirst()
        {
            var sw = new StringWriter();
            var r = new CrashReport(sw, 10);
            r.Header(new[] { new KeyValuePair<string, string>("game", "0.9"), new KeyValuePair<string, string>("os", "Windows 11") });
            Assert.IsTrue(r.Add("2026-10-30 20:00:00", "EXCEPTION", "NullReferenceException: boom", "A.B () (at A.cs:1)\nC.D () (at C.cs:2)\n"));
            string text = sw.ToString();
            StringAssert.StartsWith("# Couch Bowl crash log", text);
            StringAssert.Contains("game: 0.9", text);
            StringAssert.Contains("os: Windows 11", text);
            StringAssert.Contains("== 2026-10-30 20:00:00  EXCEPTION", text);
            StringAssert.Contains("NullReferenceException: boom", text);
            StringAssert.Contains("C.D () (at C.cs:2)", text);
            Assert.Less(text.IndexOf("os: Windows 11"), text.IndexOf("NullReferenceException"), "the header comes first");
        }

        // An error every frame must not fill the disk: a repeat only counts.
        [Test]
        public void ARepeatOnlyCounts()
        {
            var sw = new StringWriter();
            var r = new CrashReport(sw, 10);
            Assert.IsTrue(r.Add("t1", "ERROR", "same", "at X\n"));
            Assert.IsFalse(r.Add("t2", "ERROR", "same", "at X\n"));
            Assert.IsFalse(r.Add("t3", "ERROR", "same", "at X\n"));
            Assert.IsTrue(r.Add("t4", "ERROR", "same", "at Y\n"), "the same words from somewhere else is a different error");
            Assert.AreEqual(2, r.Entries);
            Assert.AreEqual(4, r.Total);
            r.Close("t5");
            StringAssert.Contains("closed: 4 errors, 2 different", sw.ToString());
            Assert.AreEqual(-1, sw.ToString().IndexOf("t2"), "the repeat was not written");
        }

        [Test]
        public void PastTheCapItStopsWritingAndSaysSo()
        {
            var sw = new StringWriter();
            var r = new CrashReport(sw, 2);
            Assert.IsTrue(r.Add("t", "ERROR", "one", null));
            Assert.IsTrue(r.Add("t", "ERROR", "two", null));
            Assert.IsFalse(r.Add("t", "ERROR", "three", null));
            Assert.IsFalse(r.Add("t", "ERROR", "four", null));
            string text = sw.ToString();
            Assert.AreEqual(-1, text.IndexOf("three"));
            Assert.AreEqual(text.IndexOf("# more errors"), text.LastIndexOf("# more errors"), "said once");
            Assert.AreEqual(4, r.Total);
        }

        [Test]
        public void ThePauseMenuRowCountsTheErrors()
        {
            Assert.AreEqual("OPEN LOG FOLDER", CrashReport.FolderLabel(0));
            Assert.AreEqual("OPEN LOG FOLDER (1 ERROR)", CrashReport.FolderLabel(1));
            Assert.AreEqual("OPEN LOG FOLDER (3 ERRORS)", CrashReport.FolderLabel(3));
        }
    }
}
