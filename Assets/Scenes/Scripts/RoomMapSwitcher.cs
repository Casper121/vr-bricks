using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Switches between different "Map" scenes (loaded additively on top of your
/// permanent Core scene - the one with the XR Rig, wrist menu, etc.) from
/// Room Menu buttons, and teleports the player to that map's spawn point
/// once it has finished loading.
///
/// SETUP:
/// 1. Put this component on any GameObject in your CORE scene (NOT inside a
///    Map scene - it needs to survive across map switches, since it's the
///    thing doing the switching).
/// 2. Assign "Player Root" - usually your XR Origin (the object that should
///    actually move when teleporting).
/// 3. Make sure every Map scene (Map1, Map2, ...) is added to
///    File > Build Settings > Scenes In Build - Unity refuses to load by
///    name otherwise, even in the Editor.
/// 4. Inside each Map scene, place one empty GameObject named exactly like
///    "Spawn Point Name" below (default "RoomspawnNeutral" - matching the
///    naming already used elsewhere in this project) at wherever the player
///    should appear when that map loads.
/// 5. On each Room Menu button: Button (Script) > On Click () > drag this
///    GameObject in, then pick RoomMapSwitcher > SwitchToMap (string), and
///    type the exact Scene name (the .unity file name, without extension)
///    into the text field that appears.
/// </summary>
public class RoomMapSwitcher : MonoBehaviour
{
    [Header("Player / XR Origin")]
    [Tooltip("The object that gets moved to the spawn point - usually your XR Origin root.")]
    [SerializeField] private Transform playerRoot;

    [Tooltip("Optional. If your XR Origin has a CharacterController, assign it here - it gets briefly disabled during teleport so it can't fight the manual position change (same reasoning as any other direct teleport in this project).")]
    [SerializeField] private CharacterController characterController;

    [Header("Spawn Point")]
    [Tooltip("Name of the empty GameObject expected inside each Map scene, marking where the player should appear.")]
    [SerializeField] private string spawnPointName = "RoomspawnNeutral";

    [Header("Loading Screen (optional)")]
    [Tooltip("Optional object (e.g. a black fullscreen fade canvas) shown while the new map is loading, hidden again once teleport is done.")]
    [SerializeField] private GameObject loadingScreen;

    [Header("Behaviour")]
    [Tooltip("If enabled, only one map scene is ever kept loaded - switching maps automatically unloads the previous one first. Turn this off only if you deliberately want multiple maps loaded at once.")]
    [SerializeField] private bool unloadPreviousMap = true;

    [Header("Startup")]
    [Tooltip("If set, this map is loaded automatically as soon as the Core scene starts - so you always land somewhere playable instead of an empty scene, both in the Editor and in real builds. Leave empty to load nothing automatically (e.g. if you always keep a map scene open in the Editor yourself).")]
    [SerializeField] private string defaultMapSceneName;

    [Header("Core Scene Cleanup")]
    [Tooltip("Core (Basic Room) is never unloaded like a map scene is - it has to stay loaded permanently for the XR Rig, wrist menu, etc. That means blocks spawned while standing in Core would otherwise just sit there forever, even after you leave. When enabled, every LegoBlock found sitting directly in the Core scene gets destroyed the moment you leave Core for a map - exactly mirroring what UnloadSceneAsync already does automatically for every other map.")]
    [SerializeField] private bool clearSpawnedBlocksInCoreOnLeave = true;

    // -------------------------------------------------------------------------
    // Runtime State
    // -------------------------------------------------------------------------

    private string currentMapSceneName;
    private bool isSwitching;

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    private void Start()
    {
        if (!string.IsNullOrEmpty(defaultMapSceneName))
            SwitchToMap(defaultMapSceneName);
    }

    // -------------------------------------------------------------------------
    // Public API - wire this to your Room Menu buttons
    // -------------------------------------------------------------------------

    /// <summary>
    /// Call this from a Room Menu button's OnClick, passing the exact Scene
    /// name (the .unity file's name, without the ".unity" extension) of the
    /// map you want to switch to. That scene must be listed in
    /// File > Build Settings > Scenes In Build.
    /// </summary>
    public void SwitchToMap(string mapSceneName)
    {
        if (string.IsNullOrEmpty(mapSceneName))
        {
            Debug.LogWarning("RoomMapSwitcher: SwitchToMap called with an empty scene name.", this);
            return;
        }

        if (isSwitching)
        {
            Debug.LogWarning("RoomMapSwitcher: Already switching maps, ignoring extra button press.", this);
            return;
        }

        if (mapSceneName == currentMapSceneName)
        {
            Debug.Log($"RoomMapSwitcher: '{mapSceneName}' is already the active map.", this);
            return;
        }

        StartCoroutine(SwitchToMapRoutine(mapSceneName));
    }

    /// <summary>
    /// Call this from your NEUTRAL ROOM button instead of SwitchToMap.
    ///
    /// Neutral Room lives inside the Core scene itself (the same scene this
    /// component is on) - it's already loaded all the time, so there is
    /// nothing to "load" here. This only UNLOADS whatever map is currently
    /// active (if any) and teleports back to the spawnPointName object found
    /// inside THIS component's own scene (i.e. Core).
    ///
    /// Wiring the Neutral Room button to SwitchToMap("YourCoreSceneName")
    /// instead of this method is exactly what causes Core (players, menus,
    /// Lego blocks, everything) to get loaded additively ON TOP OF itself
    /// every single click - since SwitchToMap only skips reloading a scene
    /// it already recognizes as the current MAP, and Core was never tracked
    /// as one.
    /// </summary>
    public void ReturnToCore()
    {
        if (isSwitching)
        {
            Debug.LogWarning("RoomMapSwitcher: Already switching maps, ignoring extra button press.", this);
            return;
        }

        if (string.IsNullOrEmpty(currentMapSceneName))
        {
            Debug.Log("RoomMapSwitcher: No map is currently loaded - already effectively in Core.", this);
            return;
        }

        StartCoroutine(ReturnToCoreRoutine());
    }

    // -------------------------------------------------------------------------
    // Internal Logic
    // -------------------------------------------------------------------------

    private IEnumerator ReturnToCoreRoutine()
    {
        isSwitching = true;

        if (loadingScreen != null)
            loadingScreen.SetActive(true);

        bool hadCharacterController = characterController != null && characterController.enabled;

        if (hadCharacterController)
            characterController.enabled = false;

        Scene previousScene = SceneManager.GetSceneByName(currentMapSceneName);

        if (previousScene.IsValid() && previousScene.isLoaded)
        {
            AsyncOperation unload = SceneManager.UnloadSceneAsync(currentMapSceneName);

            while (unload != null && !unload.isDone)
                yield return null;
        }

        currentMapSceneName = null;

        // This component lives in the Core scene itself, so gameObject.scene
        // IS the Core scene - no separate "core scene name" field needed.
        Scene coreScene = gameObject.scene;

        // FIX: "blocks always spawn in Basic Room, never in the map you're
        // actually standing in". SceneManager.LoadSceneAsync(..., Additive)
        // does NOT change which scene is the "Active Scene" - that stays
        // whatever it was before (Core, in this project, since it was loaded
        // first and never explicitly switched). Any Instantiate() call that
        // doesn't specify a target scene/parent (like the block-spawning code
        // in LegoHandMenu) always creates the new object inside the CURRENT
        // Active Scene, regardless of where in the world the object actually
        // is. Explicitly setting Core back as the Active Scene here makes
        // sure that once you return to Neutral Room, anything spawned again
        // correctly belongs to Core, not to whatever map you just left.
        if (coreScene.IsValid())
            SceneManager.SetActiveScene(coreScene);

        Transform spawnPoint = FindInScene(coreScene, spawnPointName);

        if (spawnPoint == null)
            Debug.LogWarning($"RoomMapSwitcher: No object named '{spawnPointName}' found inside Core scene '{coreScene.name}'.", this);
        else
            yield return WaitForGroundReady(spawnPoint.position);

        TeleportToSpawnPoint(coreScene.name, spawnPoint, hadCharacterController);

        if (spawnPoint != null)
            yield return MonitorAndRecoverFromFallThrough(spawnPoint);

        if (loadingScreen != null)
            loadingScreen.SetActive(false);

        isSwitching = false;
    }

    private IEnumerator SwitchToMapRoutine(string mapSceneName)
    {
        isSwitching = true;

        if (loadingScreen != null)
            loadingScreen.SetActive(true);

        // FIX: "falls through when switching between two non-Neutral rooms"
        // (i.e. any switch that has to unload a previous map first, which
        // takes noticeably longer than the very first load into an empty
        // Core scene). Previously the CharacterController was only disabled
        // for the brief instant of the final teleport - but it stayed fully
        // ENABLED during the entire unload+load process before that. If your
        // locomotion/gravity system keeps calling controller.Move() every
        // frame regardless (which most XR locomotion setups do), the player
        // keeps falling through the real gap that exists the moment the old
        // floor is unloaded and before the new one has finished loading -
        // well before we ever get to the teleport step. Disabling the
        // controller for the WHOLE switch (not just the teleport moment)
        // freezes the player in place for the entire transition, so there's
        // nothing left to fall through in the first place.
        bool hadCharacterController = characterController != null && characterController.enabled;

        if (hadCharacterController)
            characterController.enabled = false;

        // FIX: "blocks built in Neutral Room/Core never disappear". Every map
        // scene gets its spawned blocks destroyed automatically the moment it
        // is unloaded below (Unity destroys everything belonging to a scene
        // when that scene is unloaded) - but Core itself is NEVER unloaded
        // (it holds the XR Rig, wrist menu, RoomMapSwitcher itself, etc., and
        // has to survive across every map switch). That means blocks built
        // while standing in Core would otherwise pile up forever. An empty
        // currentMapSceneName at this point means "we are currently in Core"
        // (ReturnToCoreRoutine/the very first SwitchToMap call both leave it
        // empty/null), so this is exactly the moment to clear Core's blocks -
        // right as we're about to leave it for a map.
        if (clearSpawnedBlocksInCoreOnLeave && string.IsNullOrEmpty(currentMapSceneName))
            ClearSpawnedBlocksInScene(gameObject.scene);

        // Unload the previous map BEFORE loading the new one, so both maps
        // are never simultaneously in memory (avoids doubled floors/lighting
        // clashing with each other, and keeps memory use down).
        if (unloadPreviousMap && !string.IsNullOrEmpty(currentMapSceneName))
        {
            Scene previousScene = SceneManager.GetSceneByName(currentMapSceneName);

            if (previousScene.IsValid() && previousScene.isLoaded)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(currentMapSceneName);

                while (unload != null && !unload.isDone)
                    yield return null;
            }
        }

        AsyncOperation load = SceneManager.LoadSceneAsync(mapSceneName, LoadSceneMode.Additive);

        if (load == null)
        {
            Debug.LogError($"RoomMapSwitcher: Could not start loading '{mapSceneName}'. Is it added to Build Settings > Scenes In Build?", this);
            isSwitching = false;

            if (loadingScreen != null)
                loadingScreen.SetActive(false);

            if (hadCharacterController)
                characterController.enabled = true;

            yield break;
        }

        while (!load.isDone)
            yield return null;

        currentMapSceneName = mapSceneName;

        Scene mapScene = SceneManager.GetSceneByName(mapSceneName);

        // FIX: "blocks always spawn in Basic Room, never in the map you're
        // actually standing in". See the matching comment in
        // ReturnToCoreRoutine() above for the full explanation - in short,
        // Instantiate() without an explicit scene/parent always creates new
        // objects in whatever scene is currently the "Active Scene", and
        // LoadSceneAsync(..., Additive) never changes that on its own. This
        // makes the freshly loaded map the Active Scene, so anything spawned
        // from here on (e.g. LEGO blocks from the hand menu) correctly ends
        // up inside THIS map instead of piling up in Core/Basic Room.
        if (mapScene.IsValid())
            SceneManager.SetActiveScene(mapScene);

        Transform spawnPoint = mapScene.IsValid() ? FindInScene(mapScene, spawnPointName) : null;

        if (spawnPoint == null)
        {
            Debug.LogWarning($"RoomMapSwitcher: No object named '{spawnPointName}' found inside '{mapSceneName}'. Add one so the player has somewhere to spawn.", this);
        }
        else
        {
            // Actively confirm the ground is really there (handles Terrain
            // and other colliders that can take longer than a frame to be
            // ready) - raycast straight down from the spawn point every
            // frame until it hits something, with a safety timeout so a
            // genuinely missing floor doesn't hang forever. The loading
            // screen (and the disabled CharacterController) stay in place the
            // entire time this is happening.
            yield return WaitForGroundReady(spawnPoint.position);
        }

        // Now safe to move the player and hand control back to normal
        // locomotion - the floor is either confirmed present, or we've timed
        // out and are proceeding anyway.
        TeleportToSpawnPoint(mapSceneName, spawnPoint, hadCharacterController);

        if (spawnPoint != null)
            yield return MonitorAndRecoverFromFallThrough(spawnPoint);

        if (loadingScreen != null)
            loadingScreen.SetActive(false);

        isSwitching = false;
    }

    [Header("Ground Check (fixes fall-through on first load)")]
    [Tooltip("How far above the spawn point the downward ground-check ray starts.")]
    [SerializeField] private float groundCheckRayStartHeight = 2f;

    [Tooltip("Maximum real-world seconds to wait for a floor collider to appear below the spawn point before giving up and teleporting anyway.")]
    [SerializeField] private float groundCheckTimeoutSeconds = 3f;

    /// <summary>
    /// Waits (real time, not affected by timeScale) until a downward raycast
    /// from just above the spawn point actually hits something solid, or until
    /// groundCheckTimeoutSeconds elapses - whichever comes first. This is what
    /// actively confirms the floor's collider is ready, instead of guessing a
    /// fixed number of frames.
    /// </summary>
    private IEnumerator WaitForGroundReady(Vector3 spawnPosition)
    {
        float deadline = Time.realtimeSinceStartup + Mathf.Max(0.1f, groundCheckTimeoutSeconds);
        Vector3 rayOrigin = spawnPosition + Vector3.up * Mathf.Max(0.1f, groundCheckRayStartHeight);

        while (Time.realtimeSinceStartup < deadline)
        {
            Physics.SyncTransforms();

            if (Physics.Raycast(rayOrigin, Vector3.down, groundCheckRayStartHeight + 5f, ~0, QueryTriggerInteraction.Ignore))
            {
                Debug.Log("RoomMapSwitcher: Ground confirmed ready below spawn point.", this);
                yield break;
            }

            yield return null;
        }

        Debug.LogWarning("RoomMapSwitcher: Ground check timed out - no collider found below the spawn point in time. Teleporting anyway.", this);
    }

    [Header("Fall-Through Safety Net")]
    [Tooltip("After teleporting, keep watching the player's height for this many real-world seconds. If they drop further than the threshold below, snap them straight back to the spawn point - catches cases where the raycast ground-check said 'ready' but the CharacterController's own collision (which can use a different, slower-to-initialize representation, especially for Terrain) still let the player fall through.")]
    [SerializeField] private float fallThroughMonitorDuration = 1.5f;

    [Tooltip("How far below the spawn point's height counts as 'falling through' during the monitor window.")]
    [SerializeField] private float fallThroughDropThreshold = 1.5f;

    /// <summary>
    /// Watches playerRoot's height for a short window right after teleporting.
    /// If it drops well below the spawn point (a real, ongoing fall - not just
    /// normal head-bob/crouching), snaps the player straight back to the spawn
    /// point and keeps watching for the remainder of the window. This is a
    /// safety net UNDER the raycast-based WaitForGroundReady check: that check
    /// can report "ground found" via a fast raycast against a Terrain's
    /// heightmap data even while the CharacterController's own, separately
    /// initialized collision representation still isn't ready yet - so the
    /// raycast alone isn't a 100% guarantee for every collider type. The
    /// loading screen (and disabled locomotion, if you disable it during
    /// falls elsewhere) stays up for this entire window, so even if a
    /// recovery snap happens, the player never actually sees themselves fall.
    /// </summary>
    private IEnumerator MonitorAndRecoverFromFallThrough(Transform spawnPoint)
    {
        if (playerRoot == null)
            yield break;

        float deadline = Time.realtimeSinceStartup + Mathf.Max(0f, fallThroughMonitorDuration);
        float floorY = spawnPoint.position.y;

        while (Time.realtimeSinceStartup < deadline)
        {
            if (playerRoot.position.y < floorY - fallThroughDropThreshold)
            {
                Debug.LogWarning("RoomMapSwitcher: Detected a fall-through right after teleporting (ground check passed but CharacterController still fell) - snapping back to spawn point.", this);

                bool wasEnabled = characterController != null && characterController.enabled;

                if (wasEnabled)
                    characterController.enabled = false;

                playerRoot.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
                Physics.SyncTransforms();

                if (wasEnabled)
                    characterController.enabled = true;
            }

            yield return null;
        }
    }

    /// <summary>
    /// Moves the player to the given spawn point (already found and confirmed
    /// safe to land on by WaitForGroundReady). Pass null spawnPoint if none
    /// was found - the method then simply does nothing except re-enable the
    /// CharacterController so the player isn't left permanently frozen.
    /// wasControllerEnabled reflects whatever the CharacterController's state
    /// was BEFORE the whole switch began (captured in SwitchToMapRoutine) -
    /// it's only re-enabled here, at the very end, once teleporting is done.
    /// </summary>
    private void TeleportToSpawnPoint(string mapSceneName, Transform spawnPoint, bool wasControllerEnabled)
    {
        if (playerRoot == null)
        {
            Debug.LogWarning("RoomMapSwitcher: No Player Root assigned, cannot teleport.", this);
        }
        else if (spawnPoint != null)
        {
            playerRoot.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);

            // Push the physics-side pose to match this new Transform position
            // immediately, so the very first physics step after re-enabling
            // the CharacterController below already sees the player at the
            // correct spot relative to the floor, instead of possibly reading
            // a stale/one-frame-old physics pose.
            Physics.SyncTransforms();

            Debug.Log($"RoomMapSwitcher: Teleported to '{spawnPointName}' in '{mapSceneName}'.", this);
        }

        // Re-enable the CharacterController now, regardless of whether the
        // teleport itself succeeded - otherwise a missing spawn point would
        // leave the player permanently frozen in place, which is worse than
        // just not moving them.
        if (wasControllerEnabled && characterController != null)
            characterController.enabled = true;
    }

    /// <summary>
    /// Destroys every LegoBlock currently sitting in the given scene. Used to
    /// clean up blocks built in Core/Basic Room, since that scene (unlike
    /// every map scene) is never unloaded and would otherwise keep every
    /// block you ever spawned there forever. Safe to call on a map scene too
    /// (though normally redundant there, since UnloadSceneAsync already
    /// destroys everything in it) - only objects with a LegoBlock component
    /// are touched, so Floor/FloorGrid/menus/the XR Rig are never affected.
    /// </summary>
    private void ClearSpawnedBlocksInScene(Scene scene)
    {
        if (!scene.IsValid())
            return;

        // true = include inactive blocks too, so none are missed.
        LegoBlock[] blocksEverywhere = FindObjectsOfType<LegoBlock>(true);
        int destroyedCount = 0;

        for (int i = 0; i < blocksEverywhere.Length; i++)
        {
            LegoBlock block = blocksEverywhere[i];

            if (block == null || block.gameObject.scene != scene)
                continue;

            Destroy(block.gameObject);
            destroyedCount++;
        }

        if (destroyedCount > 0)
            Debug.Log($"RoomMapSwitcher: Cleared {destroyedCount} LEGO block(s) left behind in scene '{scene.name}'.", this);
    }

    /// <summary>
    /// Searches only the given Scene's own root GameObjects (and their
    /// children) for an object with the given name - never anything from a
    /// different loaded scene.
    /// </summary>
    private Transform FindInScene(Scene scene, string objectName)
    {
        GameObject[] roots = scene.GetRootGameObjects();

        for (int i = 0; i < roots.Length; i++)
        {
            Transform found = FindRecursive(roots[i].transform, objectName);

            if (found != null)
                return found;
        }

        return null;
    }

    private Transform FindRecursive(Transform parent, string objectName)
    {
        if (parent.name == objectName)
            return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindRecursive(parent.GetChild(i), objectName);

            if (found != null)
                return found;
        }

        return null;
    }
}