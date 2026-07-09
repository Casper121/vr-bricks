using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// VR controller controls. Builds its own Input Actions directly in code -
/// no Input Actions asset editing required at all, and (unlike
/// UnityEngine.XR.InputDevices) works correctly with OpenXR-based projects.
///
/// Left Controller:
/// Y = Music Menu
/// X = Room Menu (Settings Menu no longer exists, so X was freed up for this)
/// Left Trigger = Block Menu
///
/// Right Controller:
/// B = Fly up
/// A = Fly down
/// Right Joystick Left/Right (tilt, held) = Continuously turn the whole body left/right
///
/// Keyboard fallback for testing in the Editor: Q/E = fly down/up. Menu keys
/// (M/N/B/L) stay exclusively in LegoTwoPanelMenuController.
///
/// Put this on the XR Origin root.
/// </summary>
public class LeftControllerGameControls : MonoBehaviour
{
    [Header("Menu Controller")]
    [SerializeField] private LegoTwoPanelMenuController twoPanelMenuController;

    [Header("XR Origin / Movement")]
    [Tooltip("Usually your XR Origin root. If empty, this transform is used.")]
    [SerializeField] private Transform xrOriginRoot;

    [Tooltip("Optional. If your XR Origin has a CharacterController, assign it or leave empty.")]
    [SerializeField] private CharacterController characterController;

    [Header("Fly")]
    [SerializeField] private float flySpeed = 2.2f;

    [Header("Body Turn (Right Joystick Left/Right)")]
    [Tooltip("Stick must be pushed further left/right than this before turning starts.")]
    [SerializeField] private float turnDeadzone = 0.2f;

    [Tooltip("Degrees per second turned while the stick is held fully left/right. Partial tilt turns proportionally slower.")]
    [SerializeField] private float turnSpeedDegreesPerSecond = 90f;

    [Header("Keyboard Test Fallback (Fly only - menu keys live in LegoTwoPanelMenuController)")]
    [SerializeField] private bool allowKeyboardFallback = true;

    [Header("Gravity Conflict Fix")]
    [Tooltip("Drag your Locomotion system's separate 'Gravity' component here (the one that keeps constantly pulling you down, independent of Dynamic Move Provider). It gets disabled for as long as you're actively flying, and re-enabled the instant you stop, so normal gravity/grounding resumes right away. Using the generic 'Behaviour' type here means this works regardless of which exact Gravity Provider class your XR Interaction Toolkit version uses - you don't need to know its exact name, just drag the component in.")]
    [SerializeField] private Behaviour gravityProviderToDisableWhileFlying;

    private bool gravityWasEnabledBeforeFlying = true;
    private bool isCurrentlyFlying;

    // -------------------------------------------------------------------------
    // Actions built directly in code - no .inputactions asset needed.
    // -------------------------------------------------------------------------

    private InputAction leftYAction;
    private InputAction leftXAction;
    private InputAction leftTriggerAction;
    private InputAction rightAAction;
    private InputAction rightBAction;
    private InputAction rightJoystickAction;

    private void Awake()
    {
        if (xrOriginRoot == null)
            xrOriginRoot = transform;

        if (characterController == null && xrOriginRoot != null)
            characterController = xrOriginRoot.GetComponent<CharacterController>();

        leftYAction = new InputAction("LeftY", InputActionType.Button, "<XRController>{LeftHand}/secondaryButton");
        leftXAction = new InputAction("LeftX", InputActionType.Button, "<XRController>{LeftHand}/primaryButton");
        leftTriggerAction = new InputAction("LeftTrigger", InputActionType.Button, "<XRController>{LeftHand}/triggerPressed");
        rightAAction = new InputAction("RightA", InputActionType.Button, "<XRController>{RightHand}/primaryButton");
        rightBAction = new InputAction("RightB", InputActionType.Button, "<XRController>{RightHand}/secondaryButton");
        rightJoystickAction = new InputAction("RightJoystick", InputActionType.Value, "<XRController>{RightHand}/primary2DAxis", expectedControlType: "Vector2");
    }

    private void OnEnable()
    {
        leftYAction.Enable();
        leftXAction.Enable();
        leftTriggerAction.Enable();
        rightAAction.Enable();
        rightBAction.Enable();
        rightJoystickAction.Enable();
    }

    private void OnDisable()
    {
        leftYAction.Disable();
        leftXAction.Disable();
        leftTriggerAction.Disable();
        rightAAction.Disable();
        rightBAction.Disable();
        rightJoystickAction.Disable();
    }

    private void Update()
    {
        HandleMenus();
        HandleFly();
        HandleBodyTurn();
    }

    private void HandleMenus()
    {
        // Physical controller buttons only. Keyboard equivalents (M/N/B/L) are
        // handled exclusively by LegoTwoPanelMenuController.HandleKeyboardMenuInput -
        // intentionally NOT duplicated here, so each input has exactly one path
        // to its menu toggle.

        // Y = Music Menu
        if (leftYAction.WasPressedThisFrame())
            ToggleMusicMenu();

        // X = Room Menu (Settings Menu no longer exists - X was freed up for this)
        if (leftXAction.WasPressedThisFrame())
            ToggleRoomMenu();

        // Left Trigger = Block Menu
        if (leftTriggerAction.WasPressedThisFrame())
            ToggleBlockMenu();
    }

    private void HandleFly()
    {
        bool upPressed = rightBAction.IsPressed();
        bool downPressed = rightAAction.IsPressed();

        if (allowKeyboardFallback && Keyboard.current != null)
        {
            if (Keyboard.current.eKey.isPressed)
                upPressed = true;

            if (Keyboard.current.qKey.isPressed)
                downPressed = true;
        }

        // FIX: instead of an exact "am I touching the ground yes/no" check
        // (which occasionally missed even when close, and made gravity
        // re-enable feel like an abrupt snap), we measure the actual
        // distance to the ground below. Once that distance drops to
        // reEnableGravityHeight or less: gravity comes back on (so you fall
        // the last little bit naturally, like a normal short drop) AND
        // fly-down input is ignored from that point (gravity is already
        // doing the job) - but fly-UP still always works, even right near
        // the ground, so you can take off again immediately if you want.
        float distanceToGround = GetDistanceToGround();
        bool nearGround = distanceToGround <= reEnableGravityHeight;

        if (nearGround)
            downPressed = false;

        float vertical = (upPressed ? 1f : 0f) - (downPressed ? 1f : 0f);
        bool wantsToMove = !Mathf.Approximately(vertical, 0f);

        // Gravity should be OFF whenever we're actively flying OR simply
        // hovering in open air (not near ground and not pressing anything) -
        // and back ON once near ground (unless actively flying up right now,
        // which takes priority and turns it back off to let you ascend).
        bool gravityShouldBeOff = upPressed || !nearGround;

        if (gravityShouldBeOff)
        {
            if (!isCurrentlyFlying && gravityProviderToDisableWhileFlying != null)
                gravityWasEnabledBeforeFlying = gravityProviderToDisableWhileFlying.enabled;

            isCurrentlyFlying = true;

            if (gravityProviderToDisableWhileFlying != null)
                gravityProviderToDisableWhileFlying.enabled = false;
        }
        else if (isCurrentlyFlying)
        {
            isCurrentlyFlying = false;

            if (gravityProviderToDisableWhileFlying != null)
                gravityProviderToDisableWhileFlying.enabled = gravityWasEnabledBeforeFlying;
        }

        if (!wantsToMove)
            return;

        vertical = Mathf.Clamp(vertical, -1f, 1f);

        Vector3 move = Vector3.up * vertical * flySpeed * Time.deltaTime;
        MoveXRRoot(move);
    }

    [Header("Ground Distance (gravity hand-back threshold)")]
    [Tooltip("Once you're this close to the ground while flying down, Gravity turns back on (so you fall the last bit naturally) and further fly-down input is ignored - flying up still always works.")]
    [SerializeField] private float reEnableGravityHeight = 0.5f;

    [Tooltip("How far below the CharacterController this looks for the ground at all. Increase if you fly very high and it isn't finding the floor.")]
    [SerializeField] private float groundCheckMaxDistance = 50f;

    /// <summary>
    /// Measures the actual distance from the CharacterController's bottom
    /// down to the nearest solid ground below (ignoring the player's own
    /// collider/rig), or float.PositiveInfinity if nothing is found within
    /// groundCheckMaxDistance (e.g. flying high above a big drop).
    /// </summary>
    private float GetDistanceToGround()
    {
        if (characterController == null)
            return 0f; // No CharacterController to check against - treat as "always near ground" so gravity/normal behaviour just applies.

        Transform ccTransform = characterController.transform;
        Vector3 center = ccTransform.TransformPoint(characterController.center);
        float worldHeight = characterController.height * ccTransform.lossyScale.y;
        float bottomY = center.y - worldHeight * 0.5f;

        Vector3 origin = new Vector3(center.x, bottomY + 0.1f, center.z);
        float checkRadius = Mathf.Max(0.05f, characterController.radius * ccTransform.lossyScale.x * 0.9f);

        RaycastHit[] hits = Physics.SphereCastAll(origin, checkRadius, Vector3.down, groundCheckMaxDistance, ~0, QueryTriggerInteraction.Ignore);

        float closest = float.PositiveInfinity;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;

            // Ignore the player's own collider/rig - we only care about
            // real, external ground.
            if (hitCollider == characterController)
                continue;

            if (xrOriginRoot != null && hitCollider.transform.IsChildOf(xrOriginRoot))
                continue;

            if (hits[i].distance < closest)
                closest = hits[i].distance;
        }

        // The cast started 0.1 above the CharacterController's actual
        // bottom, so subtract that back out for an accurate "distance from
        // your feet to the ground" number.
        return Mathf.Max(0f, closest - 0.1f);
    }

    /// <summary>
    /// Continuously rotates the whole body (XR Origin) left/right for as long
    /// as the RIGHT joystick is held to the side - speed scales with how far
    /// the stick is tilted. Real head-rotation from the headset itself
    /// already works independently of this; this only turns your
    /// FORWARD/body orientation to match.
    /// </summary>
    private void HandleBodyTurn()
    {
        if (xrOriginRoot == null)
            return;

        Vector2 joystick = rightJoystickAction.ReadValue<Vector2>();
        float horizontal = joystick.x;

        if (Mathf.Abs(horizontal) < turnDeadzone)
            return;

        horizontal = Mathf.Clamp(horizontal, -1f, 1f);

        float degreesThisFrame = horizontal * turnSpeedDegreesPerSecond * Time.deltaTime;
        xrOriginRoot.Rotate(Vector3.up, degreesThisFrame, Space.World);
    }

    private void MoveXRRoot(Vector3 move)
    {
        if (xrOriginRoot == null)
            return;

        float distance = move.magnitude;

        if (distance <= 0f)
            return;

        Vector3 direction = move.normalized;
        float skinMargin = 0.05f;

        // FIX: a single thin ray down the center can slip past corners/edges
        // a real body would catch, AND (very likely the cause of the
        // "pulls back slightly every time" feeling) could occasionally graze
        // the player's OWN CharacterController collider depending on exactly
        // where the cast origin sits relative to it, clamping movement
        // against yourself. Casting a capsule matching the CharacterController's
        // actual radius/height is both more reliable (catches real body-width
        // collisions a center ray would miss) and lets us explicitly ignore
        // any hit that belongs to the player's own hierarchy.
        if (characterController != null)
        {
            Vector3 center = xrOriginRoot.TransformPoint(characterController.center);
            float radius = characterController.radius;
            float halfHeight = Mathf.Max(characterController.height * 0.5f - radius, 0.01f);

            Vector3 point1 = center + Vector3.up * halfHeight;
            Vector3 point2 = center - Vector3.up * halfHeight;

            RaycastHit[] hits = Physics.CapsuleCastAll(point1, point2, radius, direction, distance + skinMargin, ~0, QueryTriggerInteraction.Ignore);

            float closestDistance = distance + skinMargin;
            bool blocked = false;

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hitCollider = hits[i].collider;

                // Ignore any collider that's part of the player's own rig
                // (the CharacterController itself, controller/hand models,
                // the loading fade sphere, etc.) - we only care about REAL
                // outside obstacles.
                if (hitCollider == characterController || hitCollider.transform.IsChildOf(xrOriginRoot))
                    continue;

                if (hits[i].distance < closestDistance)
                {
                    closestDistance = hits[i].distance;
                    blocked = true;
                }
            }

            float allowedDistance = blocked ? Mathf.Max(0f, closestDistance - skinMargin) : distance;
            xrOriginRoot.position += direction * allowedDistance;
            return;
        }

        // Fallback (no CharacterController assigned): simple single ray.
        Vector3 castOrigin = xrOriginRoot.position + Vector3.up * 1.2f;

        if (Physics.Raycast(castOrigin, direction, out RaycastHit hit, distance + skinMargin, ~0, QueryTriggerInteraction.Ignore))
        {
            float allowedDistance = Mathf.Max(0f, hit.distance - skinMargin);
            xrOriginRoot.position += direction * allowedDistance;
            return;
        }

        xrOriginRoot.position += move;
    }

    private void ToggleBlockMenu()
    {
        if (twoPanelMenuController != null)
            twoPanelMenuController.ToggleBlockMenu();
    }

    private void ToggleMusicMenu()
    {
        if (twoPanelMenuController != null)
            twoPanelMenuController.ToggleMusicMenu();
    }

    private void ToggleRoomMenu()
    {
        if (twoPanelMenuController != null)
            twoPanelMenuController.ToggleRoomMenu();
    }
}