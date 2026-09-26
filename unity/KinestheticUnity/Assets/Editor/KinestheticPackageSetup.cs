using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

public static class KinestheticPackageSetup
{
    static AddAndRemoveRequest request;
    static double deadline;
    public static void Install()
    {
        request = Client.AddAndRemove(new[] { "com.unity.cloud.gltfast", "com.unity.pipeline" });
        deadline = EditorApplication.timeSinceStartup + 600;
        EditorApplication.update += Poll;
    }
    static void Poll()
    {
        if (!request.IsCompleted && EditorApplication.timeSinceStartup < deadline) return;
        EditorApplication.update -= Poll;
        if (!request.IsCompleted || request.Status != StatusCode.Success)
        {
            Debug.LogError("[Kinesthetic] Package setup failed: " + request.Error?.message);
            EditorApplication.Exit(1);
            return;
        }
        foreach (var package in request.Result) Debug.Log("[Kinesthetic] " + package.name + "@" + package.version);
        EditorApplication.Exit(0);
    }
}
