using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// A world-space hand menu that:
/// - attaches to the left controller / left hand transform,
/// - opens / closes when the left-controller Menu button is pressed,
/// - shows a scrollable list of LEGO block types,
/// - spawns the chosen block prefab in front of the player and makes it immediately grabbable.
///
/// Setup:
/// 1. Attach this script to an empty GameObject in your XR Rig (e.g. "HandMenu").
/// 2. Assign LeftHandTransform to your left-hand anchor (e.g. LeftHand Controller).
/// 3. Assign the MenuToggleAction input reference to XRI LeftHand / Menu.
/// 4. Create LegoBlockSpawnEntry ScriptableObjects and add them to BlockEntries.
/// 5. Assign the MenuCanvas (a world-space Canvas with a Panel child).
///    A simple hierarchy:
///      MenuCanvas (Canvas, GraphicRaycaster)
///        └─ Panel (Image)
///             └─ ScrollView (ScrollRect + Mask + Image)
///                  └─ Content (Vertical Layout Group)
///
/// The script auto-generates one button per entry at runtime.
/// </summary>
public class LegoHandMenu : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector
    // -------------------------------------------------------------------------

    [Header("Hand Anchor")]
    [Tooltip("Transform of the left controller / hand. The menu follows this object.")]
    public Transform leftHandTransform;

    [Header("Menu Positioning")]
    [Tooltip("Local offset relative to the left hand where the menu panel appears.")]
    public Vector3 menuOffset = new Vector3(0.05f, 0.1f, 0f);

    [Tooltip("If true the menu always faces the main camera.")]
    public bool faceCamera = true;

    [Header("Input")]
    [Tooltip("InputActionReference for the left controller Menu button (e.g. XRI LeftHand / Menu).")]
    public InputActionReference menuToggleAction;

    [Tooltip("Allows toggling the menu with the M key while testing in the editor.")]
    public bool allowKeyboardFallback = true;

    [Header("Block Entries")]
    [Tooltip("All block types that will appear as buttons in the menu.")]
    public List<LegoBlockSpawnEntry> blockEntries = new List<LegoBlockSpawnEntry>();

    [Header("Spawn Settings")]
    [Tooltip("Distance in front of the player camera at which blocks are spawned.")]
    public float spawnDistance = 0.5f;

    [Header("UI References")]
    [Tooltip("Root Canvas of the menu (world-space). Will be hidden/shown.")]
    public Canvas menuCanvas;

    [Tooltip("Transform of the ScrollRect Content that receives generated buttons.")]
    public Transform buttonContainer;

    [Tooltip("Prefab used for each menu button. Must have a Button + TextMeshPro + optional Image child.")]
    public GameObject buttonPrefab;

    // -------------------------------------------------------------------------
    // Runtime State
    // -------------------------------------------------------------------------

    private bool isMenuOpen;
    private Camera mainCamera;

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        mainCamera = Camera.main;

        if (menuCanvas != null)
            menuCanvas.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (menuToggleAction != null)
        {
            menuToggleAction.action.Enable();
            menuToggleAction.action.performed += OnMenuToggle;
        }
    }

    private void OnDisable()
    {
        if (menuToggleAction != null)
        {
            menuToggleAction.action.performed -= OnMenuToggle;
            menuToggleAction.action.Disable();
        }
    }

    private void Start()
    {
        GenerateButtons();
    }

    private void Update()
    {
        if (allowKeyboardFallback && Keyboard.current != null)
        {
            if (Keyboard.current.mKey.wasPressedThisFrame)
                SetMenuOpen(!isMenuOpen);
        }
    }

    private void LateUpdate()
    {
        if (!isMenuOpen || leftHandTransform == null)
            return;

        // Follow the hand
        transform.position = leftHandTransform.TransformPoint(menuOffset);

        // Optionally face the camera so it is always readable
        if (faceCamera && mainCamera != null)
        {
            Vector3 lookDir = transform.position - mainCamera.transform.position;
            if (lookDir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(lookDir);
        }
    }

    // -------------------------------------------------------------------------
    // Menu Toggle
    // -------------------------------------------------------------------------

    private void OnMenuToggle(InputAction.CallbackContext context)
    {
        SetMenuOpen(!isMenuOpen);
    }

    private void SetMenuOpen(bool open)
    {
        isMenuOpen = open;

        if (menuCanvas != null)
            menuCanvas.gameObject.SetActive(open);
    }

    // -------------------------------------------------------------------------
    // Button Generation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates one UI button per block entry in the button container.
    /// </summary>
    private void GenerateButtons()
    {
        if (buttonContainer == null || buttonPrefab == null)
        {
            Debug.LogWarning("LegoHandMenu: buttonContainer or buttonPrefab not assigned.");
            return;
        }

        // Clear old buttons (useful if entries are changed at runtime)
        foreach (Transform child in buttonContainer)
            Destroy(child.gameObject);

        foreach (LegoBlockSpawnEntry entry in blockEntries)
        {
            if (entry == null || entry.blockPrefab == null)
                continue;

            GameObject buttonGO = Instantiate(buttonPrefab, buttonContainer);
            buttonGO.name = $"Btn_{entry.displayName}";

            // --- Label ---
            TextMeshProUGUI label = buttonGO.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
                label.text = entry.displayName;

            // --- Thumbnail ---
            Image[] images = buttonGO.GetComponentsInChildren<Image>();
            if (images.Length > 1 && entry.thumbnail != null)
            {
                // Convention: images[0] = button background, images[1] = thumbnail image
                images[1].sprite = entry.thumbnail;
                images[1].gameObject.SetActive(true);
            }

            // --- Button color ---
            Image bgImage = buttonGO.GetComponent<Image>();
            if (bgImage != null)
                bgImage.color = entry.buttonColor;

            // --- Click handler ---
            Button button = buttonGO.GetComponent<Button>();
            if (button != null)
            {
                LegoBlockSpawnEntry captured = entry; // capture for closure
                button.onClick.AddListener(() => OnBlockButtonPressed(captured));
            }
        }
    }

    // -------------------------------------------------------------------------
    // Block Spawning
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called when the player taps a block button.
    /// Spawns the block prefab in front of the camera and closes the menu.
    /// </summary>
    private void OnBlockButtonPressed(LegoBlockSpawnEntry entry)
    {
        if (entry == null || entry.blockPrefab == null)
            return;

        Vector3 spawnPosition = GetSpawnPosition();
        Quaternion spawnRotation = Quaternion.identity;

        if (mainCamera != null)
        {
            // Face the same direction as the camera (yaw only, level spawn)
            Vector3 flatForward = mainCamera.transform.forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude > 0.001f)
                spawnRotation = Quaternion.LookRotation(flatForward);
        }

        GameObject block = Instantiate(entry.blockPrefab, spawnPosition, spawnRotation);

        // Notify the block system that it was freshly spawned (not snapped)
        LegoBlock legoBlock = block.GetComponent<LegoBlock>();
        if (legoBlock != null)
            legoBlock.SetSnappedToSocket(false);

        // Close the menu after spawning so the player can immediately grab the block
        SetMenuOpen(false);

        Debug.Log($"LegoHandMenu: Spawned '{entry.displayName}' at {spawnPosition}");
    }

    /// <summary>
    /// Returns the world position where the next block should be spawned.
    /// Prefers a point in front of the camera; falls back to the hand position.
    /// </summary>
    private Vector3 GetSpawnPosition()
    {
        if (mainCamera != null)
        {
            Vector3 camPos = mainCamera.transform.position;
            Vector3 camForward = mainCamera.transform.forward;

            // Cast a bit forward and slightly downward so the block lands near the hands
            Vector3 candidate = camPos + camForward * spawnDistance + Vector3.down * 0.1f;
            return candidate;
        }

        if (leftHandTransform != null)
            return leftHandTransform.position + Vector3.forward * spawnDistance;

        return Vector3.zero;
    }
}