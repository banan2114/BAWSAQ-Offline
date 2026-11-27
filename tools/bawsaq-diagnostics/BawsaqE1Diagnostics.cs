// ---------------------------------------------------------------------------
// BawsaqE1Diagnostics — read-only BAWSAQ state diagnostics for experiment E1
//
// Target : ScriptHookVDotNetEnhanced (SHVDNE) v1.1.0.6, API v3, net48
// Safety : READ-ONLY by construction.
//          - reads script globals ONLY via GTA.Native.GlobalVariable.Get/Read
//          - reads network state ONLY via Function.Call<bool>(Hash...)
//          - never calls Write<T> on globals, never calls STAT_SET_*,
//            never hooks/patches memory, never touches YSC or archives,
//            never changes trading/portfolio/saves.
//
// Source is kept C# 5 compatible so it can also be compiled on any Windows
// machine with the in-box .NET Framework compiler (csc.exe v4.0.30319).
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

namespace BawsaqE1Diagnostics
{
    /// <summary>
    /// SHVDN script: snapshots network natives + key script globals to a log
    /// file. Auto-logs only changes; F9 forces a manual snapshot.
    /// Must inherit GTA.Script directly and expose a public parameterless
    /// constructor (SHVDN discovery rule).
    /// </summary>
    public class BawsaqE1Diagnostic : Script
    {
        public const string PluginVersion = "1.0.0";

        // Script-global indices (absolute, as used by the 1158.16 YSC
        // instructions; see docs/bawsaq-restoration/report.md §4-§5).
        // 77542 is an extra read-only evidence channel: func_9474 stores -1
        // into it on the Global_65068 early-error path.
        private static readonly int[] GlobalIndexes = new int[]
        {
            80362, 65064, 65065, 65068, 77568, 77542
        };

        private const int TickIntervalMs = 500;
        private const int ManualDebounceMs = 1000;
        private const long HeartbeatSeconds = 300;

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private string _logPath;
        private string _lastSnapshot;
        private DateTime _lastManualUtc = DateTime.MinValue;
        private DateTime _lastHeartbeatUtc = DateTime.MinValue;
        private long _ticks;

        public BawsaqE1Diagnostic()
        {
            // Safe during construction: SHVDN resolves the script via its
            // "_executingScript" fallback while the ctor runs (see
            // ScriptDomain.LookupScript).
            Interval = TickIntervalMs;
            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += OnAborted;

            _logPath = ResolveLogPath();
            WriteHeader();
        }

        // ------------------------------------------------------------------
        // Logging
        // ------------------------------------------------------------------

        private string ResolveLogPath()
        {
            try
            {
                string dir = Path.GetDirectoryName(Filename);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    string path = Path.Combine(dir, "bawsaq-e1-diagnostics.log");
                    // Probe writability up front (game dirs can be read-only).
                    File.AppendAllText(path, string.Empty, Utf8NoBom);
                    return path;
                }
            }
            catch (Exception)
            {
                // fall through to %TEMP%
            }

            return Path.Combine(Path.GetTempPath(), "bawsaq-e1-diagnostics.log");
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
                // Logging must never break the game or the script; drop the line.
            }
        }

        private static string Sanitize(string text)
        {
            if (text == null)
            {
                return string.Empty;
            }

            string s = text.Replace('\r', ' ').Replace('\n', ' ');
            if (s.Length > 80)
            {
                s = s.Substring(0, 80);
            }

            return s;
        }

        private void WriteHeader()
        {
            WriteLine("==================== bawsaq-e1-diagnostics v" + PluginVersion + " ====================");
            WriteLine("mode=READ-ONLY (no global writes, no STAT_SET_*, no hooks, no memory patches, no YSC changes)");
            WriteLine("hotkeys: F9 = manual snapshot (values are always written, even if unchanged)");

            try
            {
                Assembly pluginAsm = typeof(BawsaqE1Diagnostic).Assembly;
                WriteLine("plugin=" + pluginAsm.GetName().FullName);
            }
            catch (Exception ex)
            {
                WriteLine("plugin=ERR:" + Sanitize(ex.Message));
            }

            try
            {
                Assembly shvdnAsm = typeof(Script).Assembly;
                WriteLine("shvdn=" + shvdnAsm.FullName);
                WriteLine("shvdn_location=" + shvdnAsm.Location);
                WriteLine("shvdn_file_version=" +
                    FileVersionInfo.GetVersionInfo(shvdnAsm.Location).FileVersion);
            }
            catch (Exception ex)
            {
                WriteLine("shvdn=ERR:" + Sanitize(ex.Message));
            }

            WriteLine("shvdn_api=v3 (GTA.Script, GTA.Native.Function, GTA.Native.GlobalVariable)");
            WriteLine("runtime_clr=" + Environment.Version +
                " os=" + Environment.OSVersion +
                " 64bit=" + Environment.Is64BitProcess.ToString(CultureInfo.InvariantCulture) +
                " utc=" + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

            try
            {
                WriteLine("game_file_version=" + Game.FileVersion);
            }
            catch (Exception ex)
            {
                WriteLine("game_file_version=ERR:" + Sanitize(ex.Message));
            }

            try
            {
                WriteLine("game_enum_version=" + Game.Version +
                    " raw=" + ((int)Game.Version).ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                WriteLine("game_enum_version=ERR:" + Sanitize(ex.Message));
            }

            try
            {
                WriteLine("game_is_enhanced=" + Game.isEnhanced.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                WriteLine("game_is_enhanced=ERR:" + Sanitize(ex.Message));
            }

            try
            {
                WriteLine("game_path=" + AppDomain.CurrentDomain.BaseDirectory);
            }
            catch (Exception ex)
            {
                WriteLine("game_path=ERR:" + Sanitize(ex.Message));
            }

            StringBuilder g = new StringBuilder();
            for (int i = 0; i < GlobalIndexes.Length; i++)
            {
                if (i > 0)
                {
                    g.Append(',');
                }
                g.Append("G").Append(GlobalIndexes[i].ToString(CultureInfo.InvariantCulture));
            }
            WriteLine("globals=" + g.ToString());
            WriteLine("natives=NETWORK_IS_SIGNED_ONLINE, NETWORK_HAS_VALID_ROS_CREDENTIALS," +
                " NETWORK_IS_SIGNED_IN, NETWORK_HAS_SOCIAL_CLUB_ACCOUNT (GTA.Native.Hash names)");
            WriteLine("log_path=" + _logPath);
            WriteLine("====================================================================");
        }

        // ------------------------------------------------------------------
        // State readers (read-only)
        // ------------------------------------------------------------------

        private static string ReadNativeBool(Hash hash)
        {
            try
            {
                return Function.Call<bool>(hash) ? "True" : "False";
            }
            catch (Exception ex)
            {
                return "ERR:" + Sanitize(ex.Message);
            }
        }

        private static string ReadGlobalInt(int index)
        {
            try
            {
                // Read-only: GlobalVariable.Write<T> is deliberately never used.
                return GlobalVariable.Get(index).Read<int>().ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                return "ERR:" + Sanitize(ex.Message);
            }
        }

        private string BuildSnapshot()
        {
            StringBuilder sb = new StringBuilder(256);
            sb.Append("signed_online=").Append(ReadNativeBool(Hash.NETWORK_IS_SIGNED_ONLINE));
            sb.Append(" ros=").Append(ReadNativeBool(Hash.NETWORK_HAS_VALID_ROS_CREDENTIALS));
            sb.Append(" signed_in=").Append(ReadNativeBool(Hash.NETWORK_IS_SIGNED_IN));
            sb.Append(" sc_account=").Append(ReadNativeBool(Hash.NETWORK_HAS_SOCIAL_CLUB_ACCOUNT));

            for (int i = 0; i < GlobalIndexes.Length; i++)
            {
                sb.Append(" G")
                  .Append(GlobalIndexes[i].ToString(CultureInfo.InvariantCulture))
                  .Append("=")
                  .Append(ReadGlobalInt(GlobalIndexes[i]));
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Events
        // ------------------------------------------------------------------

        private void OnTick(object sender, EventArgs e)
        {
            _ticks++;

            string snap;
            try
            {
                snap = BuildSnapshot();
            }
            catch (Exception ex)
            {
                // Defense in depth: a snapshot must never throw out of Tick.
                WriteLine("SNAPSHOT_ERROR " + Sanitize(ex.Message));
                return;
            }

            if (_lastSnapshot == null)
            {
                _lastSnapshot = snap;
                _lastHeartbeatUtc = DateTime.UtcNow;
                WriteLine("INITIAL " + snap);
                return;
            }

            if (!string.Equals(snap, _lastSnapshot, StringComparison.Ordinal))
            {
                _lastSnapshot = snap;
                WriteLine("CHANGED " + snap);
            }

            if ((DateTime.UtcNow - _lastHeartbeatUtc).TotalSeconds >= HeartbeatSeconds)
            {
                _lastHeartbeatUtc = DateTime.UtcNow;
                WriteLine("HEARTBEAT ticks=" + _ticks.ToString(CultureInfo.InvariantCulture) +
                    " last_snapshot=" + snap);
            }
        }

        private void OnKeyDown(object sender, WinForms.KeyEventArgs e)
        {
            if (e.KeyCode != WinForms.Keys.F9)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            if ((now - _lastManualUtc).TotalMilliseconds < ManualDebounceMs)
            {
                return;
            }
            _lastManualUtc = now;

            string snap;
            try
            {
                snap = BuildSnapshot();
            }
            catch (Exception ex)
            {
                WriteLine("MANUAL_SNAPSHOT_ERROR " + Sanitize(ex.Message));
                return;
            }

            _lastSnapshot = snap;
            WriteLine("MANUAL " + snap);
        }

        private void OnAborted(object sender, EventArgs e)
        {
            WriteLine("SESSION_ABORTED ticks=" + _ticks.ToString(CultureInfo.InvariantCulture));
        }
    }
}
