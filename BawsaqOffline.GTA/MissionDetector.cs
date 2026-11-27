
using GTA;
using GTA.Native;

namespace BawsaqOffline.GTA
{
    public sealed class MissionDetector
    {
        private static readonly string[] MissionScripts =
        {
            "assassin_valet",
            "assassin_multi",
            "assassin_hooker",
            "assassin_bus",
            "assassin_construction"
        };

        private static readonly uint[] MissionHashes =
        {
            StringHash.AtStringHashUtf8("assassin_valet", 0u),
            StringHash.AtStringHashUtf8("assassin_multi", 0u),
            StringHash.AtStringHashUtf8("assassin_hooker", 0u),
            StringHash.AtStringHashUtf8("assassin_bus", 0u),
            StringHash.AtStringHashUtf8("assassin_construction", 0u)
        };

        private const int MissingTicksBeforeStop = 30;

        private readonly bool[] _wasRunning = new bool[5];
        private readonly bool[] _completionSignalSeen = new bool[5];
        private readonly bool[] _sawCompletionReset = new bool[5];
        private readonly int[] _missingTicks = new int[5];

        public void Tick()
        {
            for (int i = 0; i < MissionHashes.Length; i++)
            {
                int count = Function.Call<int>(
                    Hash.GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH,
                    MissionHashes[i]);

                bool running = count > 0;

                if (running)
                {
                    if (!_wasRunning[i])
                    {
                        _completionSignalSeen[i] = false;
                        _sawCompletionReset[i] = false;
                        _missingTicks[i] = 0;

                        global::GTA.UI.Screen.ShowSubtitle(
                            "[BAWSAQ DEBUG] Started: " + MissionScripts[i],
                            3000);
                    }

                    bool ready = Function.Call<bool>(
                        Hash.IS_MISSION_COMPLETE_READY_FOR_UI);

                    if (!ready)
                    {
                        _sawCompletionReset[i] = true;
                    }
                    else if (_sawCompletionReset[i] &&
                             !_completionSignalSeen[i])
                    {
                        _completionSignalSeen[i] = true;

                        global::GTA.UI.Screen.ShowSubtitle(
                            "[BAWSAQ DEBUG] Completion signal: " +
                            MissionScripts[i],
                            5000);
                    }

                    _missingTicks[i] = 0;
                    _wasRunning[i] = true;
                }
                else if (_wasRunning[i])
                {
                    _missingTicks[i]++;

                    if (_missingTicks[i] >= MissingTicksBeforeStop)
                    {
                        string result = _completionSignalSeen[i]
                            ? "POSSIBLE SUCCESS"
                            : "OUTCOME UNKNOWN";

                        global::GTA.UI.Screen.ShowSubtitle(
                            "[BAWSAQ DEBUG] " + MissionScripts[i] +
                            ": " + result,
                            5000);

                        _wasRunning[i] = false;
                        _completionSignalSeen[i] = false;
                        _sawCompletionReset[i] = false;
                        _missingTicks[i] = 0;
                    }
                }
            }
        }
    }
}
