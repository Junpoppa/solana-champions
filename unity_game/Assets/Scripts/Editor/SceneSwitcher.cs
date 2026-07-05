using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// One-click EXCLUSIVE scene switcher. Each menu item opens a single game-mode scene in
/// OpenSceneMode.Single, which closes every other open scene first — so the Hierarchy and
/// Scene view only ever show that one level. No additive overlap, no cross-scene confusion.
///
/// Canonical map (one clean scene per mode):
///   Spinner              -> Course.unity
///   Last Man Standing    -> LastManStanding.unity
///   Obstacle Course Race -> ObstacleCourse.unity
///   (loader)             -> Boot.unity   (Build Settings index 0)
///
/// Always edit ONE mode at a time via this menu. Never open mode scenes additively.
/// </summary>
public static class SceneSwitcher
{
    const string kCourse         = "Assets/Scenes/Course.unity";
    const string kLastManStanding = "Assets/Scenes/LastManStanding.unity";
    const string kObstacleCourse  = "Assets/Scenes/ObstacleCourse.unity";
    const string kRollOut         = "Assets/Scenes/RollOut.unity";
    const string kBoot           = "Assets/Scenes/Boot.unity";

    [MenuItem("Tools/Scenes/Open Spinner (Course)", priority = 0)]
    public static void OpenSpinner() => OpenSingle(kCourse);

    [MenuItem("Tools/Scenes/Open Last Man Standing", priority = 1)]
    public static void OpenLastManStanding() => OpenSingle(kLastManStanding);

    [MenuItem("Tools/Scenes/Open Obstacle Course Race", priority = 2)]
    public static void OpenObstacleCourse() => OpenSingle(kObstacleCourse);

    [MenuItem("Tools/Scenes/Open Roll Out", priority = 3)]
    public static void OpenRollOut() => OpenSingle(kRollOut);

    // Separator + loader, kept apart from the three play modes.
    [MenuItem("Tools/Scenes/Open Boot (loader)", priority = 20)]
    public static void OpenBoot() => OpenSingle(kBoot);

    static void OpenSingle(string path)
    {
        // Prompt to save anything dirty, then open exclusively (Single closes all other open scenes).
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
    }
}
