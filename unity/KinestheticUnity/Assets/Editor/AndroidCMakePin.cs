#if UNITY_ANDROID
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Android;

// This Mac has an Intel-only /usr/local/bin/cmake on PATH, which Gradle picks up and cannot run.
// Pin the generated Android project to the universal CMake that ships with Unity's Android SDK.
public sealed class AndroidCMakePin : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 0;
    public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
    {
        var sdk = AndroidExternalToolsSettings.sdkRootPath;
        var cmakeRoot = Path.Combine(sdk ?? "", "cmake");
        if (!Directory.Exists(cmakeRoot)) return;
        var version = Directory.GetDirectories(cmakeRoot).OrderByDescending(d => d).FirstOrDefault();
        if (version == null) return;
        var properties = Path.Combine(Directory.GetParent(unityLibraryPath).FullName, "local.properties");
        var lines = File.Exists(properties) ? File.ReadAllLines(properties).Where(l => !l.StartsWith("cmake.dir=")).ToList() : new System.Collections.Generic.List<string>();
        lines.Add("cmake.dir=" + version.Replace("\\", "/"));
        File.WriteAllLines(properties, lines);
    }
}
#endif
