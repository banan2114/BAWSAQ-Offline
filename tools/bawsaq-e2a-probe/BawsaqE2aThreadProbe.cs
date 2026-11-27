// ---------------------------------------------------------------------------
// BawsaqE2aThreadProbe — read-only active script-thread probe for stage E2a
//
// Target : ScriptHookVDotNetEnhanced (SHVDNE) v1.1.0.6, API v3, net48
// Safety : STRICTLY READ-ONLY.
//          - enumerates active GTA V script threads ONLY via standard natives:
//            SCRIPT_THREAD_ITERATOR_RESET, SCRIPT_THREAD_ITERATOR_GET_NEXT_THREAD_ID,
//            GET_NAME_OF_SCRIPT_WITH_THIS_ID, IS_THREAD_ACTIVE,
//            GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH,
//            DOES_SCRIPT_WITH_NAME_HASH_EXIST, HAS_SCRIPT_WITH_NAME_HASH_LOADED
//          - reads script globals ONLY via GlobalVariable.Get(...).Read<int>()
//          - never modifies memory, never terminates/starts scripts, never
//            writes globals, never hooks or patches natives/YSC/archives.
//
// C# 5 compatible for in-box .NET Framework csc.exe (build.cmd) and net48 SDK.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using GTA;
using GTA.Native;
using WinForms = System.Windows.Forms;

namespace BawsaqE2aThreadProbe
{
    public class BawsaqE2aThreadProbe : Script
    {
        public const string PluginVersion = "1.0.0";

        // JOAAT hashes of the target scripts (case-insensitive in RAGE):
        //   "appinternet" / "appInternet" -> 0x6a172273
        //   "stock_controller"            -> 0xc9833b07
        private const int HashAppInternet = 0x6A172273;
        private const int HashStockController = unchecked((int)0xC9833B07);

        // Non-overlapping global sets: *1 (update2.rpf) vs *2 (x64b.rpf)
        private static readonly int[] V1UpdateGlobals = new int[] { 80362, 65064, 65065, 65066, 65068, 77568 };
        private static readonly int[] V2BaseGlobals = new int[] { 67010, 53868, 53869, 53870, 53872, 66227 };

        private const int TickIntervalMs = 500;
        private const int ManualDebounceMs = 1000;
        private const int MaxThreadsGuard = 1024;
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private string _logPath;
        private string _lastTargetSignature;
        private DateTime _lastManualUtc = DateTime.MinValue;
        private bool _startupDumped;

        public BawsaqE2aThreadProbe()
        {
            Interval = TickIntervalMs;
            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += OnAborted;

            _logPath = ResolveLogPath();
            WriteHeader();
        }

        private string ResolveLogPath()
        {
            try
            {
                string dir = Path.GetDirectoryName(Filename);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    string path = Path.Combine(dir, "bawsaq-e2a-thread-probe.log");
                    File.AppendAllText(path, string.Empty, Utf8NoBom);
                    return path;
                }
            }
            catch (Exception)
            {
            }
            return Path.Combine(Path.GetTempPath(), "bawsaq-e2a-thread-probe.log");
        }

        private void WriteLine(string message)
        {
            if (string.IsNullOrEmpty(_logPath))
            {
                return;
            }
            string line = "[" +
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                "] " + message;
            try
            {
                File.AppendAllText(_logPath, line + Environment.NewLine, Utf8NoBom);
            }
            catch (Exception)
            {
            }
        }

        private static string Sanitize(string text)
        {
            if (text == null)
            {
                return string.Empty;
            }
            string s = text.Replace('\r', ' ').Replace('\n', ' ');
            if (s.Length > 120)
            {
                s = s.Substring(0, 120);
            }
            return s;
        }

        private void WriteHeader()
        {
            WriteLine("==================== bawsaq-e2a-thread-probe v" + PluginVersion + " ====================");
            WriteLine("mode=READ-ONLY (thread iterator natives + GlobalVariable.Read<int> only)");
            WriteLine("hotkeys: F10 = full active script thread dump + globals cross-check");
            try
            {
                Assembly shvdnAsm = typeof(Script).Assembly;
                WriteLine("shvdn=" + shvdnAsm.FullName + " ver=" +
                    FileVersionInfo.GetVersionInfo(shvdnAsm.Location).FileVersion);
            }
            catch (Exception ex)
            {
                WriteLine("shvdn=ERR:" + Sanitize(ex.Message));
            }
            try
            {
                WriteLine("game_file_version=" + Game.FileVersion +
                    " is_enhanced=" + Game.isEnhanced.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                WriteLine("game_version=ERR:" + Sanitize(ex.Message));
            }
            WriteLine("target_hashes: appinternet=0x6a172273 stock_controller=0xc9833b07");
            WriteLine("log_path=" + _logPath);
            WriteLine("========================================================================");
        }

        private struct ThreadEntry
        {
            public int Id;
            public string Name;
            public bool Active;
        }

        private static List<ThreadEntry> EnumerateThreads(out string error)
        {
            error = null;
            List<ThreadEntry> list = new List<ThreadEntry>();
            try
            {
                Function.Call(Hash.SCRIPT_THREAD_ITERATOR_RESET);
                for (int i = 0; i < MaxThreadsGuard; i++)
                {
                    int tid = Function.Call<int>(Hash.SCRIPT_THREAD_ITERATOR_GET_NEXT_THREAD_ID);
                    if (tid == 0)
                    {
                        break;
                    }
                    string name = string.Empty;
                    bool active = false;
                    try
                    {
                        name = Function.Call<string>(Hash.GET_NAME_OF_SCRIPT_WITH_THIS_ID, tid);
                    }
                    catch (Exception ex)
                    {
                        name = "ERR:" + Sanitize(ex.Message);
                    }
                    try
                    {
                        active = Function.Call<bool>(Hash.IS_THREAD_ACTIVE, tid);
                    }
                    catch (Exception)
                    {
                        active = false;
                    }
                    ThreadEntry entry;
                    entry.Id = tid;
                    entry.Name = name ?? string.Empty;
                    entry.Active = active;
                    list.Add(entry);
                }
            }
            catch (Exception ex)
            {
                error = Sanitize(ex.Message);
            }
            return list;
        }

        private static string ReadHashProbe(string label, int nameHash)
        {
            try
            {
                int running = Function.Call<int>(
                    Hash.GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH, nameHash);
                bool exists = Function.Call<bool>(
                    Hash.DOES_SCRIPT_WITH_NAME_HASH_EXIST, nameHash);
                bool loaded = Function.Call<bool>(
                    Hash.HAS_SCRIPT_WITH_NAME_HASH_LOADED, nameHash);
                return label + "(running=" + running.ToString(CultureInfo.InvariantCulture) +
                    ",exists=" + exists.ToString(CultureInfo.InvariantCulture) +
                    ",loaded=" + loaded.ToString(CultureInfo.InvariantCulture) + ")";
            }
            catch (Exception ex)
            {
                return label + "(ERR:" + Sanitize(ex.Message) + ")";
            }
        }

        private static string ReadGlobalsLine(string tag, int[] indexes)
        {
            StringBuilder sb = new StringBuilder(tag);
            sb.Append("=[");
            for (int i = 0; i < indexes.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(' ');
                }
                sb.Append('G').Append(indexes[i].ToString(CultureInfo.InvariantCulture)).Append('=');
                try
                {
                    sb.Append(GlobalVariable.Get(indexes[i]).Read<int>().ToString(CultureInfo.InvariantCulture));
                }
                catch (Exception ex)
                {
                    sb.Append("ERR:").Append(Sanitize(ex.Message));
                }
            }
            sb.Append(']');
            return sb.ToString();
        }

        private void CaptureSnapshot(string trigger, bool dumpAllThreads)
        {
            string iterErr;
            List<ThreadEntry> threads = EnumerateThreads(out iterErr);

            StringBuilder targetSig = new StringBuilder();
            StringBuilder targetDetails = new StringBuilder();
            int matchCount = 0;

            for (int i = 0; i < threads.Count; i++)
            {
                ThreadEntry t = threads[i];
                string low = t.Name.ToLowerInvariant();
                if (low.Contains("appinternet") || low.Contains("stock_controller"))
                {
                    if (matchCount > 0)
                    {
                        targetDetails.Append("; ");
                        targetSig.Append('|');
                    }
                    matchCount++;
                    targetDetails.Append("tid=").Append(t.Id.ToString(CultureInfo.InvariantCulture))
                        .Append(" exact_name=\"").Append(Sanitize(t.Name)).Append('"')
                        .Append(" active=").Append(t.Active.ToString(CultureInfo.InvariantCulture));
                    targetSig.Append(t.Id.ToString(CultureInfo.InvariantCulture))
                        .Append(':').Append(t.Name)
                        .Append(':').Append(t.Active ? "1" : "0");
                }
            }

            string appHashInfo = ReadHashProbe("appinternet", HashAppInternet);
            string stockHashInfo = ReadHashProbe("stock_controller", HashStockController);
            string g1 = ReadGlobalsLine("star1_update2_globals", V1UpdateGlobals);
            string g2 = ReadGlobalsLine("star2_x64b_globals", V2BaseGlobals);

            string fullSig = (iterErr ?? "ok") + "#" + targetSig.ToString() + "#" +
                appHashInfo + "#" + stockHashInfo + "#" + g1 + "#" + g2;

            if (!dumpAllThreads && fullSig == _lastTargetSignature)
            {
                return;
            }
            _lastTargetSignature = fullSig;

            WriteLine("SNAPSHOT trigger=" + trigger +
                " total_threads=" + threads.Count.ToString(CultureInfo.InvariantCulture) +
                " iter_err=" + (iterErr ?? "none"));
            WriteLine("  hashes: " + appHashInfo + " | " + stockHashInfo);
            WriteLine("  target_threads(" + matchCount.ToString(CultureInfo.InvariantCulture) + "): " +
                (matchCount > 0 ? targetDetails.ToString() : "none"));
            WriteLine("  " + g1);
            WriteLine("  " + g2);

            if (dumpAllThreads)
            {
                StringBuilder all = new StringBuilder();
                for (int i = 0; i < threads.Count; i++)
                {
                    if (i > 0)
                    {
                        all.Append(", ");
                    }
                    all.Append(threads[i].Id.ToString(CultureInfo.InvariantCulture))
                        .Append(":\"").Append(Sanitize(threads[i].Name)).Append('"')
                        .Append(threads[i].Active ? "" : "(inactive)");
                }
                WriteLine("  all_threads: [" + all.ToString() + "]");
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!_startupDumped)
            {
                _startupDumped = true;
                CaptureSnapshot("STARTUP", true);
                return;
            }
            CaptureSnapshot("CHANGE", false);
        }

        private void OnKeyDown(object sender, WinForms.KeyEventArgs e)
        {
            if (e.KeyCode == WinForms.Keys.F10)
            {
                DateTime now = DateTime.UtcNow;
                if ((now - _lastManualUtc).TotalMilliseconds < ManualDebounceMs)
                {
                    return;
                }
                _lastManualUtc = now;
                CaptureSnapshot("MANUAL_F10", true);
            }
        }

        private void OnAborted(object sender, EventArgs e)
        {
            WriteLine("ABORTED");
        }
    }
}
