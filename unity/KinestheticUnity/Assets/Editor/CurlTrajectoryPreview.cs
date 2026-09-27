using System;
using System.IO;
using Kinesthetic.Rehab;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// Shows the studio's curl path without two AirPods: in Play, in the studio, hands CurlTrajectory a simulated
/// mid-set payload. Make it first with `node coordinator/exercise/curl-synth.ts --payload`.
public static class CurlTrajectoryPreview
{
    const string PayloadPath = "Temp/curl-payload.json";

    [MenuItem("Kinesthetic/Rehab/Show a simulated curl path (Play mode)")]
    public static string Show()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Run in Play mode, in the studio.");
        var curl = UnityEngine.Object.FindAnyObjectByType<CurlTrajectory>()
            ?? throw new InvalidOperationException("No CurlTrajectory: open the studio (Rehab) first.");
        if (!File.Exists(PayloadPath)) throw new FileNotFoundException("Run: node coordinator/exercise/curl-synth.ts --payload", PayloadPath);
        curl.Apply(JObject.Parse(File.ReadAllText(PayloadPath)));
        return "Simulated curl path shown.";
    }
}
