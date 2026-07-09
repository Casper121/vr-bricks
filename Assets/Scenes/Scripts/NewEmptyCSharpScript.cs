using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using System.Collections.Generic;

/// <summary>
/// TEMPORARY DIAGNOSTIC SCRIPT.
///
/// Put this on ANY GameObject in your Core scene (a new empty one is fine).
///
/// - Logs scene/canvas state at Awake and Start.
/// - Press L at any time (in Play Mode) to log it again on demand -
///   e.g. right when the scrollbar starts flickering on a map.
/// - Every second, automatically checks for duplicate Canvas names
///   (a strong sign that a scene got loaded additively on top of itself)
///   and logs a warning immediately if found, without you needing to
///   press anything.
///
/// Remove this script once the problem is found and fixed - it's not meant
/// to ship in the final build.
/// </summary>
public class SceneLoadDiagnostic : MonoBehaviour
{
    [Header("Names to search for (leave any blank to skip)")]
    [SerializeField] private string playerRootName = "XR Origin";
    [SerializeField] private string spawnPointName = "RoomspawnNeutral";
    [SerializeField] private string wristMenuName = "";

    [Header("Auto-Check")]
    [Tooltip("How often (seconds) to automatically scan for duplicate canvases while playing.")]
    [SerializeField] private float autoCheckInterval = 1f;

    private float autoCheckTimer;

    private void Awake()
    {
        Debug.Log("========== SCENE LOAD DIAGNOSTIC: AWAKE ==========");
        LogSceneState();
    }

    private void Start()
    {
        Debug.Log("========== SCENE LOAD DIAGNOSTIC: START ==========");
        LogSceneState();
        LogObjectSearch();
    }

    private void Update()
    {
        // Manual trigger: press L any time to log full state on demand -
        // e.g. exactly when you see the scrollbar flicker.
        if (Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame)
        {
            Debug.Log("========== SCENE LOAD DIAGNOSTIC: MANUAL (L KEY) ==========");
            LogSceneState();
            LogObjectSearch();
        }

        // Automatic background check, no key press needed.
        autoCheckTimer += Time.unscaledDeltaTime;

        if (autoCheckTimer >= Mathf.Max(0.1f, autoCheckInterval))
        {
            autoCheckTimer = 0f;
            CheckForDuplicateCanvasesSilently();
        }
    }

    private void LogSceneState()
    {
        Debug.Log($"[DIAG] Total loaded scenes: {SceneManager.sceneCount}");

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);

            Debug.Log(
                $"[DIAG] Scene #{i}: name='{scene.name}' " +
                $"path='{scene.path}' " +
                $"isLoaded={scene.isLoaded} " +
                $"rootCount={scene.rootCount} " +
                $"buildIndex={scene.buildIndex}"
            );

            GameObject[] roots = scene.GetRootGameObjects();

            for (int r = 0; r < roots.Length; r++)
            {
                Debug.Log($"[DIAG]    root object: '{roots[r].name}' (active={roots[r].activeInHierarchy})");
            }
        }

        Scene active = SceneManager.GetActiveScene();
        Debug.Log($"[DIAG] ACTIVE scene is: '{active.name}'");

        Debug.Log($"[DIAG] Total scenes in Build Settings: {SceneManager.sceneCountInBuildSettings}");
    }

    private void LogObjectSearch()
    {
        LogFind("Player Root", playerRootName);
        LogFind("Spawn Point", spawnPointName);
        LogFind("Wrist Menu", wristMenuName);

        Camera mainCam = Camera.main;
        Debug.Log($"[DIAG] Camera.main found: {(mainCam != null ? mainCam.gameObject.name : "NONE - no camera tagged MainCamera!")}");

        RoomMapSwitcher switcher = FindObjectOfType<RoomMapSwitcher>();
        Debug.Log($"[DIAG] RoomMapSwitcher found: {(switcher != null ? switcher.gameObject.name + " in scene " + switcher.gameObject.scene.name : "NONE FOUND")}");

        LegoHandMenu[] handMenus = FindObjectsOfType<LegoHandMenu>(true);
        Debug.Log($"[DIAG] LegoHandMenu instance count: {handMenus.Length}");

        for (int i = 0; i < handMenus.Length; i++)
        {
            Debug.Log($"[DIAG]    LegoHandMenu: '{handMenus[i].gameObject.name}' active={handMenus[i].gameObject.activeInHierarchy} scene={handMenus[i].gameObject.scene.name}");
        }

        EventSystem_LogCount();

        Canvas[] canvases = FindObjectsOfType<Canvas>(true);
        Debug.Log($"[DIAG] Canvas count in whole game (incl. inactive): {canvases.Length}");

        for (int i = 0; i < canvases.Length; i++)
        {
            Debug.Log($"[DIAG]    Canvas: '{canvases[i].gameObject.name}' active={canvases[i].gameObject.activeInHierarchy} scene={canvases[i].gameObject.scene.name}");
        }
    }

    private void EventSystem_LogCount()
    {
        UnityEngine.EventSystems.EventSystem[] eventSystems =
            FindObjectsOfType<UnityEngine.EventSystems.EventSystem>(true);

        Debug.Log($"[DIAG] EventSystem count: {eventSystems.Length}");

        for (int i = 0; i < eventSystems.Length; i++)
        {
            Debug.Log($"[DIAG]    EventSystem: '{eventSystems[i].gameObject.name}' active={eventSystems[i].gameObject.activeInHierarchy} scene={eventSystems[i].gameObject.scene.name}");
        }

        if (eventSystems.Length > 1)
            Debug.LogWarning("[DIAG] MORE THAN ONE EVENTSYSTEM FOUND - this alone can cause UI flicker/input issues!");
    }

    /// <summary>
    /// Runs quietly every autoCheckInterval seconds. Only logs something if it
    /// actually finds a duplicate Canvas name (i.e. two Canvas GameObjects with
    /// the identical name existing at the same time, in possibly different
    /// scenes) - a strong sign of a scene loaded additively on top of itself.
    /// </summary>
    private void CheckForDuplicateCanvasesSilently()
    {
        Canvas[] canvases = FindObjectsOfType<Canvas>(true);
        Dictionary<string, List<string>> nameToScenes = new Dictionary<string, List<string>>();

        for (int i = 0; i < canvases.Length; i++)
        {
            string name = canvases[i].gameObject.name;
            string sceneName = canvases[i].gameObject.scene.name;

            if (!nameToScenes.TryGetValue(name, out List<string> scenes))
            {
                scenes = new List<string>();
                nameToScenes[name] = scenes;
            }

            scenes.Add(sceneName);
        }

        foreach (KeyValuePair<string, List<string>> entry in nameToScenes)
        {
            if (entry.Value.Count <= 1)
                continue;

            Debug.LogWarning($"[DIAG] DUPLICATE CANVAS DETECTED: '{entry.Key}' exists {entry.Value.Count}x right now, in scenes: {string.Join(", ", entry.Value)}");
        }

        // Also passively watch for duplicate scenes with the same name loaded
        // at once (e.g. "Basic" loaded twice), independent of canvases.
        Dictionary<string, int> sceneNameCounts = new Dictionary<string, int>();

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            string sceneName = SceneManager.GetSceneAt(i).name;
            sceneNameCounts.TryGetValue(sceneName, out int count);
            sceneNameCounts[sceneName] = count + 1;
        }

        foreach (KeyValuePair<string, int> entry in sceneNameCounts)
        {
            if (entry.Value > 1)
                Debug.LogWarning($"[DIAG] SCENE '{entry.Key}' IS LOADED {entry.Value}x AT THE SAME TIME!");
        }
    }

    private void LogFind(string label, string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
        {
            Debug.Log($"[DIAG] {label}: (no name given, skipped)");
            return;
        }

        GameObject found = GameObject.Find(objectName);
        Debug.Log($"[DIAG] {label} ('{objectName}') found: {(found != null ? "YES, in scene " + found.scene.name : "NO")}");
    }
}