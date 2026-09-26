using System.IO;
using System.Xml;
using UnityEditor.Android;

// The headset app only renders what the Mac resolved; it takes no controller input. Without a hand-tracking
// declaration Horizon OS assumes controllers are required and shows "controllers required" instead of launching
// whenever they are off or unpaired. Declaring hands as supported (not required) lets it start either way.
// Written into the generated Gradle project, like AndroidCMakePin, so there is no hand-kept AndroidManifest.xml.
public sealed class QuestManifest : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 100;
    const string Android = "http://schemas.android.com/apk/res/android";

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        var manifestPath = Path.Combine(path, "src/main/AndroidManifest.xml");
        if (!File.Exists(manifestPath)) return;
        var doc = new XmlDocument();
        doc.Load(manifestPath);
        var manifest = doc.DocumentElement;
        Add(doc, manifest, "uses-feature", "oculus.software.handtracking", required: false);
        Add(doc, manifest, "uses-permission", "com.oculus.permission.HAND_TRACKING", required: null);
        doc.Save(manifestPath);
    }

    static void Add(XmlDocument doc, XmlElement manifest, string tag, string name, bool? required)
    {
        foreach (XmlElement e in manifest.GetElementsByTagName(tag))
            if (e.GetAttribute("name", Android) == name) return;
        var element = doc.CreateElement(tag);
        element.SetAttribute("name", Android, name);
        if (required.HasValue) element.SetAttribute("required", Android, required.Value ? "true" : "false");
        manifest.AppendChild(element);
    }
}
