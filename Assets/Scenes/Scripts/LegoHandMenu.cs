using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using UnityEngine.EventSystems;

public class LegoHandMenu : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector
    // -------------------------------------------------------------------------

    [Header("Hand Anchor")]
    public Transform leftHandTransform;

    [Header("Menu Positioning")]
    public Vector3 menuOffset = new Vector3(0.05f, 0.1f, 0f);
    public bool faceCamera = true;

    [Header("Input")]
    public InputActionReference menuToggleAction;
    public bool allowKeyboardFallback = true;

    [Header("Block Entries")]
    public List<LegoBlockSpawnEntry> blockEntries = new List<LegoBlockSpawnEntry>();

    [Header("Block Categories")]
    [Tooltip("Optional button that switches the block list to normal blocks.")]
    public Button blocksCategoryButton;

    [Tooltip("Optional button that switches the block list to plate blocks.")]
    public Button platesCategoryButton;

    [Tooltip("Optional label that shows the current category name.")]
    public TextMeshProUGUI blockCategoryTitle;

    private LegoBlockSpawnEntry.LegoMenuCategory activeBlockCategory = LegoBlockSpawnEntry.LegoMenuCategory.Blocks;

    [Header("Spawn Settings")]
    public float spawnDistance = 0.5f;

    [Tooltip("How high above the detected surface the spawned block is placed.")]
    public float spawnSurfaceOffset = 0.08f;

    [Tooltip("How high above the spawn target the downward raycast starts.")]
    public float spawnRaycastStartHeight = 2f;

    [Tooltip("How far downward the script searches for a surface.")]
    public float spawnRaycastDistance = 5f;

    [Tooltip("Layers that count as valid spawn surfaces. Leave on Everything if unsure.")]
    public LayerMask spawnSurfaceMask = ~0;

    [Tooltip("Small sideways spacing between repeated spawned blocks so they do not spawn inside each other.")]
    public float spawnStackSpacing = 0.08f;

    private int spawnCounter;

    [Header("UI References")]
    public Canvas menuCanvas;

    [Tooltip("ScrollRect that contains the block buttons.")]
    public ScrollRect blockScrollRect;

    [Tooltip("Content RectTransform inside the ScrollRect.")]
    public RectTransform blockContent;

    [Tooltip("RawImage for the Saturation/Value square.")]
    public RawImage svField;

    [Tooltip("RectTransform of the cursor dot on the SV field.")]
    public RectTransform svCursor;

    [Tooltip("RawImage for the vertical hue bar.")]
    public RawImage hueSlider;

    [Tooltip("RectTransform of the cursor dot on the hue bar.")]
    public RectTransform hueCursor;

    [Tooltip("Image that shows the currently selected color.")]
    public Image colorPreview;

    [Tooltip("Parent RectTransform that holds the saved color slot Images. Create children named Slot_0, Slot_1, etc.")]
    public RectTransform savedColorSlots;

    [Header("Texture Resolution")]
    public int svResolution = 256;
    public int hueBarWidth = 20;
    public int hueBarHeight = 256;

    [Header("Cursor Offset Tuning")]
    public Vector2 svCursorOffset = Vector2.zero;
    public Vector2 hueCursorOffset = Vector2.zero;

    [Header("Debug")]
    public bool debugPicker = false;

    public bool IsOpen => IsMenuVisible();
    public bool IsOpenOrClosing => IsMenuVisible() || isClosing;

    private bool isMenuOpen;
    private bool isClosing;
    private Camera mainCamera;

    private float hue = 0f;
    private float sat = 1f;
    private float val = 1f;
    private Color selectedColor = Color.red;

    private Texture2D svTexture;
    private Texture2D hueTexture;

    private bool draggingSV;
    private bool draggingHue;
    private bool waitForMouseReleaseBeforePicker;

    private readonly List<Image> blockButtonImages = new List<Image>();
    private readonly List<Color?> savedColors = new List<Color?>();
    private readonly List<Image> savedColorImages = new List<Image>();

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
        GenerateSVTexture();
        GenerateHueTexture();
        SetupCategoryButtons();
        GenerateBlockButtons();
        UpdateColorFromHSV();
        InitCursorPositions();
        InitSavedColorSlots();
        PreparePickerObjects();
    }

    private void Update()
    {
        // NOTE: The M-key toggle used to live here, but LegoTwoPanelMenuController
        // already toggles this same menu via its own keyboard binding. Having two
        // independent listeners for the same key caused a double-toggle: the menu
        // opened and then immediately closed again in the same frame. The menu's
        // open/closed state is now controlled exclusively from outside via
        // SetMenuOpen(), called by the menu controller.

        if (IsMenuVisible())
            HandleMousePickerInput();
    }

    private void LateUpdate()
    {
        if ((!IsMenuVisible() && !isClosing) || leftHandTransform == null)
            return;

        transform.position = leftHandTransform.TransformPoint(menuOffset);

        if (faceCamera && mainCamera != null)
        {
            Vector3 dir = transform.position - mainCamera.transform.position;

            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir);
        }
    }

    private void OnDestroy()
    {
        if (svTexture != null)
            Destroy(svTexture);

        if (hueTexture != null)
            Destroy(hueTexture);
    }

    private bool IsMenuVisible()
    {
        return menuCanvas != null && menuCanvas.gameObject.activeInHierarchy;
    }

    private void OnMenuToggle(InputAction.CallbackContext ctx)
    {
        SetMenuOpen(!IsMenuVisible());
    }

    public void SetMenuOpen(bool open)
    {
        if (menuCanvas == null)
            return;

        if (open)
        {
            isClosing = false;
            isMenuOpen = true;

            draggingSV = false;
            draggingHue = false;

            if (Mouse.current != null && Mouse.current.leftButton.isPressed)
                waitForMouseReleaseBeforePicker = true;
            else
                waitForMouseReleaseBeforePicker = false;

            menuCanvas.gameObject.SetActive(true);

            InitCursorPositions();
            PreparePickerObjects();

            LegoPanelAnimation openAnim = menuCanvas.GetComponent<LegoPanelAnimation>();

            if (openAnim == null)
                openAnim = menuCanvas.GetComponentInChildren<LegoPanelAnimation>();

            if (openAnim != null)
                openAnim.PlayOpen();

            return;
        }

        if (!menuCanvas.gameObject.activeSelf)
        {
            isMenuOpen = false;
            isClosing = false;
            draggingSV = false;
            draggingHue = false;
            waitForMouseReleaseBeforePicker = false;
            return;
        }

        isMenuOpen = false;
        draggingSV = false;
        draggingHue = false;
        waitForMouseReleaseBeforePicker = false;

        LegoPanelAnimation closeAnim = menuCanvas.GetComponent<LegoPanelAnimation>();

        if (closeAnim == null)
            closeAnim = menuCanvas.GetComponentInChildren<LegoPanelAnimation>();

        if (closeAnim != null)
        {
            isClosing = true;

            closeAnim.PlayClose(() =>
            {
                isClosing = false;

                if (menuCanvas != null)
                    menuCanvas.gameObject.SetActive(false);
            });
        }
        else
        {
            isClosing = false;
            menuCanvas.gameObject.SetActive(false);
        }
    }

    // -------------------------------------------------------------------------
    // Picker XR / UI Drag
    // -------------------------------------------------------------------------

    private void PreparePickerObjects()
    {
        if (svField != null)
        {
            svField.raycastTarget = true;
            SetupPickerDragTarget(svField.gameObject, true);
        }

        if (hueSlider != null)
        {
            hueSlider.raycastTarget = true;
            SetupPickerDragTarget(hueSlider.gameObject, false);
        }

        DisableRaycastsUnder(svCursor);
        DisableRaycastsUnder(hueCursor);
    }

    private void SetupPickerDragTarget(GameObject target, bool isSV)
    {
        if (target == null)
            return;

        PickerDragTarget dragTarget = target.GetComponent<PickerDragTarget>();

        if (dragTarget == null)
            dragTarget = target.AddComponent<PickerDragTarget>();

        dragTarget.Init(this, isSV);
    }

    private void BeginPickerPointer(PointerEventData eventData, bool isSV)
    {
        if (!IsMenuVisible())
            return;

        draggingSV = isSV;
        draggingHue = !isSV;

        ApplyPickerPointer(eventData, isSV);
    }

    private void DragPickerPointer(PointerEventData eventData, bool isSV)
    {
        if (!IsMenuVisible())
            return;

        if (isSV && !draggingSV)
            return;

        if (!isSV && !draggingHue)
            return;

        ApplyPickerPointer(eventData, isSV);
    }

    private void EndPickerPointer(PointerEventData eventData, bool isSV)
    {
        if (isSV)
            draggingSV = false;
        else
            draggingHue = false;
    }

    private void ApplyPickerPointer(PointerEventData eventData, bool isSV)
    {
        RectTransform rectTransform;

        if (isSV)
        {
            if (svField == null)
                return;

            rectTransform = svField.rectTransform;
        }
        else
        {
            if (hueSlider == null)
                return;

            rectTransform = hueSlider.rectTransform;
        }

        Camera eventCamera = eventData.pressEventCamera;

        if (eventCamera == null)
            eventCamera = eventData.enterEventCamera;

        if (eventCamera == null)
            eventCamera = GetUICamera();

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform,
                eventData.position,
                eventCamera,
                out Vector2 localPoint))
        {
            return;
        }

        if (isSV)
            ApplySVLocalPoint(localPoint);
        else
            ApplyHueLocalPoint(localPoint);
    }

    private void DisableRaycastsUnder(Transform root)
    {
        if (root == null)
            return;

        Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);

        foreach (Graphic graphic in graphics)
        {
            if (graphic != null)
                graphic.raycastTarget = false;
        }
    }

    // -------------------------------------------------------------------------
    // Mouse Picker Input
    // -------------------------------------------------------------------------

    private void HandleMousePickerInput()
    {
        Mouse mouse = Mouse.current;

        if (mouse == null)
            return;

        if (waitForMouseReleaseBeforePicker)
        {
            if (!mouse.leftButton.isPressed)
                waitForMouseReleaseBeforePicker = false;
            else
                return;
        }

        Vector2 mousePos = mouse.position.ReadValue();

        bool justPressed = mouse.leftButton.wasPressedThisFrame;
        bool pressed = mouse.leftButton.isPressed;
        bool justReleased = mouse.leftButton.wasReleasedThisFrame;

        if (justReleased)
        {
            draggingSV = false;
            draggingHue = false;
        }

        if (justPressed)
        {
            draggingSV = false;
            draggingHue = false;

            if (TryGetLocalPointInPicker(svField != null ? svField.rectTransform : null, mousePos, out Vector2 svLocal))
            {
                draggingSV = true;
                ApplySVLocalPoint(svLocal);
                return;
            }

            if (TryGetLocalPointInPicker(hueSlider != null ? hueSlider.rectTransform : null, mousePos, out Vector2 hueLocal))
            {
                draggingHue = true;
                ApplyHueLocalPoint(hueLocal);
                return;
            }
        }

        if (!pressed)
            return;

        if (draggingSV && svField != null)
        {
            if (TryGetLocalPointOnRect(svField.rectTransform, mousePos, out Vector2 svLocal))
                ApplySVLocalPoint(svLocal);
        }

        if (draggingHue && hueSlider != null)
        {
            if (TryGetLocalPointOnRect(hueSlider.rectTransform, mousePos, out Vector2 hueLocal))
                ApplyHueLocalPoint(hueLocal);
        }
    }

    private bool TryGetLocalPointInPicker(RectTransform rectTransform, Vector2 mousePos, out Vector2 localPoint)
    {
        localPoint = Vector2.zero;

        if (rectTransform == null)
            return false;

        Camera uiCamera = GetUICamera();

        bool inside = RectTransformUtility.RectangleContainsScreenPoint(
            rectTransform,
            mousePos,
            uiCamera
        );

        if (!inside)
            return false;

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform,
            mousePos,
            uiCamera,
            out localPoint
        );
    }

    private bool TryGetLocalPointOnRect(RectTransform rectTransform, Vector2 mousePos, out Vector2 localPoint)
    {
        localPoint = Vector2.zero;

        if (rectTransform == null)
            return false;

        Camera uiCamera = GetUICamera();

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform,
            mousePos,
            uiCamera,
            out localPoint
        );
    }

    private Camera GetUICamera()
    {
        if (menuCanvas == null)
            return mainCamera;

        if (menuCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;

        if (menuCanvas.worldCamera != null)
            return menuCanvas.worldCamera;

        if (mainCamera == null)
            mainCamera = Camera.main;

        return mainCamera;
    }

    // -------------------------------------------------------------------------
    // Color Picker
    // -------------------------------------------------------------------------

    private void ApplySVLocalPoint(Vector2 local)
    {
        if (svField == null)
            return;

        Rect r = svField.rectTransform.rect;

        sat = Mathf.Clamp01((local.x - r.xMin) / r.width);
        val = Mathf.Clamp01((local.y - r.yMin) / r.height);

        if (svCursor != null)
        {
            svCursor.anchoredPosition = new Vector2(
                r.xMin + sat * r.width,
                r.yMin + val * r.height
            ) + svCursorOffset;
        }

        UpdateColorFromHSV();
    }

    private void ApplyHueLocalPoint(Vector2 local)
    {
        if (hueSlider == null)
            return;

        Rect r = hueSlider.rectTransform.rect;

        float normalizedY = Mathf.Clamp01((local.y - r.yMin) / r.height);
        hue = 1f - normalizedY;

        if (hueCursor != null)
        {
            hueCursor.anchoredPosition = new Vector2(
                r.xMin + r.width * 0.5f,
                r.yMin + (1f - hue) * r.height
            ) + hueCursorOffset;
        }

        RefreshSVTexture();
        UpdateColorFromHSV();
    }

    private void InitCursorPositions()
    {
        if (svCursor != null && svField != null)
        {
            Rect r = svField.rectTransform.rect;

            svCursor.anchoredPosition = new Vector2(
                r.xMin + sat * r.width,
                r.yMin + val * r.height
            ) + svCursorOffset;
        }

        if (hueCursor != null && hueSlider != null)
        {
            Rect r = hueSlider.rectTransform.rect;

            hueCursor.anchoredPosition = new Vector2(
                r.xMin + r.width * 0.5f,
                r.yMin + (1f - hue) * r.height
            ) + hueCursorOffset;
        }
    }

    private void GenerateSVTexture()
    {
        if (svField == null)
            return;

        svTexture = new Texture2D(svResolution, svResolution, TextureFormat.RGBA32, false);
        svTexture.filterMode = FilterMode.Bilinear;
        svField.texture = svTexture;

        RefreshSVTexture();
    }

    private void RefreshSVTexture()
    {
        if (svTexture == null)
            return;

        int res = svResolution;

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float s = (float)x / (res - 1);
                float v = (float)y / (res - 1);

                svTexture.SetPixel(x, y, Color.HSVToRGB(hue, s, v));
            }
        }

        svTexture.Apply();
    }

    private void GenerateHueTexture()
    {
        if (hueSlider == null)
            return;

        hueTexture = new Texture2D(hueBarWidth, hueBarHeight, TextureFormat.RGBA32, false);
        hueTexture.filterMode = FilterMode.Bilinear;

        for (int y = 0; y < hueBarHeight; y++)
        {
            float h = 1f - (float)y / (hueBarHeight - 1);
            Color c = Color.HSVToRGB(h, 1f, 1f);

            for (int x = 0; x < hueBarWidth; x++)
                hueTexture.SetPixel(x, y, c);
        }

        hueTexture.Apply();
        hueSlider.texture = hueTexture;
    }

    private void UpdateColorFromHSV()
    {
        selectedColor = Color.HSVToRGB(hue, sat, val);

        if (colorPreview != null)
            colorPreview.color = selectedColor;

        UpdateButtonColors();
    }

    private void UpdateButtonColors()
    {
        foreach (Image img in blockButtonImages)
        {
            if (img == null)
                continue;

            img.color = selectedColor;

            LegoButtonHover hover = img.GetComponent<LegoButtonHover>();

            if (hover == null)
                hover = img.GetComponentInParent<LegoButtonHover>();

            if (hover != null)
                hover.RefreshCache();
        }
    }

    // -------------------------------------------------------------------------
    // Saved Color Slots
    // -------------------------------------------------------------------------

    private void InitSavedColorSlots()
    {
        savedColorImages.Clear();

        if (savedColorSlots == null)
            return;

        foreach (Transform child in savedColorSlots)
        {
            if (!child.name.StartsWith("Slot_"))
                continue;

            Image img = child.GetComponent<Image>();

            if (img == null)
                continue;

            savedColorImages.Add(img);
        }

        while (savedColors.Count < savedColorImages.Count)
            savedColors.Add(null);

        RebuildAllSlots();
    }

    private void RebuildAllSlots()
    {
        for (int i = 0; i < savedColorImages.Count; i++)
            BuildSlot(i);
    }

    private void BuildSlot(int index)
    {
        if (index >= savedColorImages.Count)
            return;

        Image img = savedColorImages[index];

        foreach (Transform child in img.transform)
            Destroy(child.gameObject);

        bool filled = index < savedColors.Count && savedColors[index].HasValue;

        Button slotBtn = img.GetComponent<Button>();

        if (slotBtn == null)
            slotBtn = img.gameObject.AddComponent<Button>();

        slotBtn.onClick.RemoveAllListeners();

        if (filled)
        {
            Color c = savedColors[index].Value;
            img.color = c;

            GameObject xGO = new GameObject("XButton");
            xGO.transform.SetParent(img.transform, false);

            RectTransform xRT = xGO.AddComponent<RectTransform>();
            xRT.anchorMin = new Vector2(1f, 1f);
            xRT.anchorMax = new Vector2(1f, 1f);
            xRT.pivot = new Vector2(1f, 1f);
            xRT.sizeDelta = new Vector2(25f, 25f);
            xRT.anchoredPosition = Vector2.zero;

            Image xImg = xGO.AddComponent<Image>();
            xImg.color = new Color(0.8f, 0.15f, 0.15f, 1f);
            xImg.raycastTarget = true;

            Button xBtn = xGO.AddComponent<Button>();
            int ci = index;
            xBtn.onClick.AddListener(() => DeleteSlot(ci));

            GameObject xl = new GameObject("XLabel");
            xl.transform.SetParent(xGO.transform, false);

            RectTransform xlRT = xl.AddComponent<RectTransform>();
            xlRT.anchorMin = Vector2.zero;
            xlRT.anchorMax = Vector2.one;
            xlRT.offsetMin = Vector2.zero;
            xlRT.offsetMax = Vector2.zero;

            TextMeshProUGUI xt = xl.AddComponent<TextMeshProUGUI>();
            xt.text = "X";
            xt.fontSize = 12f;
            xt.alignment = TextAlignmentOptions.Center;
            xt.color = Color.white;
            xt.fontStyle = FontStyles.Bold;
            xt.raycastTarget = false;

            slotBtn.onClick.AddListener(() => LoadSlot(ci));
        }
        else
        {
            img.color = new Color(0.25f, 0.25f, 0.25f, 1f);

            GameObject pl = new GameObject("PlusLabel");
            pl.transform.SetParent(img.transform, false);

            RectTransform plRT = pl.AddComponent<RectTransform>();
            plRT.anchorMin = Vector2.zero;
            plRT.anchorMax = Vector2.one;
            plRT.offsetMin = Vector2.zero;
            plRT.offsetMax = Vector2.zero;

            TextMeshProUGUI pt = pl.AddComponent<TextMeshProUGUI>();
            pt.text = "+";
            pt.fontSize = 28f;
            pt.alignment = TextAlignmentOptions.Center;
            pt.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            pt.fontStyle = FontStyles.Bold;
            pt.raycastTarget = false;

            int ci = index;
            slotBtn.onClick.AddListener(() => SaveToSlot(ci));
        }
    }

    private void SaveToSlot(int index)
    {
        if (index >= savedColors.Count)
            return;

        savedColors[index] = selectedColor;
        BuildSlot(index);
    }

    private void LoadSlot(int index)
    {
        if (index >= savedColors.Count || !savedColors[index].HasValue)
            return;

        Color c = savedColors[index].Value;

        Color.RGBToHSV(c, out float h, out float s, out float v);

        hue = h;
        sat = s;
        val = v;

        RefreshSVTexture();
        UpdateColorFromHSV();
        InitCursorPositions();
    }

    private void DeleteSlot(int index)
    {
        if (index >= savedColors.Count)
            return;

        savedColors[index] = null;
        BuildSlot(index);
    }

    // -------------------------------------------------------------------------
    // Block Categories
    // -------------------------------------------------------------------------

    private void SetupCategoryButtons()
    {
        if (blocksCategoryButton != null)
        {
            blocksCategoryButton.onClick.RemoveAllListeners();
            blocksCategoryButton.onClick.AddListener(() => SetBlockCategory(LegoBlockSpawnEntry.LegoMenuCategory.Blocks));
            LegoButtonHover.AddTo(blocksCategoryButton.gameObject);
        }

        if (platesCategoryButton != null)
        {
            platesCategoryButton.onClick.RemoveAllListeners();
            platesCategoryButton.onClick.AddListener(() => SetBlockCategory(LegoBlockSpawnEntry.LegoMenuCategory.Plates));
            LegoButtonHover.AddTo(platesCategoryButton.gameObject);
        }

        RefreshCategoryVisuals();
    }

    public void SetBlockCategory(LegoBlockSpawnEntry.LegoMenuCategory category)
    {
        if (activeBlockCategory == category)
            return;

        activeBlockCategory = category;
        GenerateBlockButtons();
        RefreshCategoryVisuals();
    }

    private void RefreshCategoryVisuals()
    {
        if (blockCategoryTitle != null)
            blockCategoryTitle.text = activeBlockCategory == LegoBlockSpawnEntry.LegoMenuCategory.Blocks ? "Blocks" : "Plates";

        SetCategoryButtonVisual(blocksCategoryButton, activeBlockCategory == LegoBlockSpawnEntry.LegoMenuCategory.Blocks);
        SetCategoryButtonVisual(platesCategoryButton, activeBlockCategory == LegoBlockSpawnEntry.LegoMenuCategory.Plates);
    }

    private void SetCategoryButtonVisual(Button button, bool selected)
    {
        if (button == null)
            return;

        Image image = button.GetComponent<Image>();

        if (image != null)
            image.color = selected ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);
    }

    // -------------------------------------------------------------------------
    // Block Buttons
    // -------------------------------------------------------------------------

    private void GenerateBlockButtons()
    {
        if (blockContent == null)
        {
            Debug.LogWarning("LegoHandMenu: blockContent not assigned.");
            return;
        }

        foreach (Transform child in blockContent)
            Destroy(child.gameObject);

        GridLayoutGroup grid = blockContent.GetComponent<GridLayoutGroup>();

        if (grid == null)
            grid = blockContent.gameObject.AddComponent<GridLayoutGroup>();

        grid.cellSize = new Vector2(78f, 78f);
        grid.spacing = new Vector2(8f, 8f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperLeft;
        grid.padding = new RectOffset(8, 8, 8, 8);

        ContentSizeFitter fitter = blockContent.GetComponent<ContentSizeFitter>();

        if (fitter == null)
            fitter = blockContent.gameObject.AddComponent<ContentSizeFitter>();

        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        blockButtonImages.Clear();

        foreach (LegoBlockSpawnEntry entry in blockEntries)
        {
            if (entry == null || entry.blockPrefab == null)
                continue;

            if (entry.category != activeBlockCategory)
                continue;

            CreateBlockButton(entry);
        }
    }

    private void CreateBlockButton(LegoBlockSpawnEntry entry)
    {
        GameObject go = new GameObject($"Btn_{entry.displayName}");
        go.transform.SetParent(blockContent, false);

        Image bg = go.AddComponent<Image>();
        bg.color = selectedColor;
        blockButtonImages.Add(bg);

        Button btn = go.AddComponent<Button>();

        LegoBlockSpawnEntry captured = entry;
        btn.onClick.AddListener(() => OnBlockButtonPressed(captured));

        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = Color.white;
        cb.pressedColor = Color.white;
        cb.selectedColor = Color.white;
        cb.disabledColor = Color.white;
        btn.colors = cb;
        btn.transition = Selectable.Transition.None;

        if (entry.thumbnail != null)
        {
            GameObject iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(go.transform, false);

            RectTransform iconRT = iconGO.AddComponent<RectTransform>();
            iconRT.anchorMin = Vector2.zero;
            iconRT.anchorMax = Vector2.one;
            iconRT.offsetMin = Vector2.zero;
            iconRT.offsetMax = Vector2.zero;

            Image iconImg = iconGO.AddComponent<Image>();
            iconImg.sprite = entry.thumbnail;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;
            iconImg.color = Color.white;
        }
        else
        {
            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, false);

            RectTransform lr = labelGO.AddComponent<RectTransform>();
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = Vector2.zero;
            lr.offsetMax = Vector2.zero;

            TextMeshProUGUI label = labelGO.AddComponent<TextMeshProUGUI>();
            label.text = entry.displayName;
            label.fontSize = 18f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.fontStyle = FontStyles.Bold;
            label.raycastTarget = false;
        }

        CanvasGroup cg = go.AddComponent<CanvasGroup>();
        cg.alpha = 1f;

        LegoButtonHover hover = LegoButtonHover.AddTo(go);

        StartCoroutine(DelayedCacheRefresh(hover));
    }

    private System.Collections.IEnumerator DelayedCacheRefresh(LegoButtonHover hover)
    {
        yield return null;

        if (hover != null)
            hover.RefreshCache();
    }

    // -------------------------------------------------------------------------
    // Spawning
    // -------------------------------------------------------------------------

    private void OnBlockButtonPressed(LegoBlockSpawnEntry entry)
    {
        if (entry == null || entry.blockPrefab == null)
            return;

        // FIX: this used to compute a rotation facing the camera (even after
        // snapping to the nearest 90 degrees) - but that's still a DIFFERENT
        // rotation than the prefab's own authored default (typically
        // identity/0,0,0). Dragging the same prefab into the scene by hand
        // uses its stored default rotation and looks perfectly correct;
        // spawning it at any other rotation exposed a mismatch between the
        // visual mesh and the logical footprint/collider that only lines up
        // right at the prefab's own original orientation. Always spawning at
        // Quaternion.identity makes a button-spawned block byte-for-byte
        // match what dragging the prefab in manually would give you.
        Quaternion spawnRot = Quaternion.identity;

        Vector3 spawnPos = GetSpawnPosition(entry.blockPrefab, spawnRot);

        GameObject block = Instantiate(entry.blockPrefab, spawnPos, spawnRot);

        // Important for global scaling: newly spawned blocks must use the current LEGO scale.
        block.transform.localScale = Vector3.one * LegoScaleMenu.CurrentScale;

        foreach (Renderer r in block.GetComponentsInChildren<Renderer>())
        {
            // FIX: "one face doesn't get colored" - r.material only reads/writes
            // the FIRST material slot. A wedge/triangle block's renderer often
            // has MULTIPLE material slots (e.g. one for the sloped face, one
            // for the sides) - using .material left every slot after the first
            // completely untouched, keeping its original, uncolored look.
            // .materials (plural) covers every slot on this renderer.
            Material[] mats = r.materials;

            for (int i = 0; i < mats.Length; i++)
            {
                Material mat = mats[i];

                if (mat.HasProperty("_Color"))
                    mat.color = selectedColor;

                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", selectedColor);
            }

            r.materials = mats;
        }

        Rigidbody rb = block.GetComponent<Rigidbody>();

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.Sleep();
        }

        LegoBlock legoBlock = block.GetComponent<LegoBlock>();

        if (legoBlock != null)
            legoBlock.SetSnappedToSocket(false);
    }

    private Vector3 GetSpawnPosition(GameObject prefab, Quaternion spawnRotation)
    {
        Vector3 targetPos = GetSpawnTargetPoint();

        Vector3 sideOffset = Vector3.zero;

        if (mainCamera != null)
        {
            Vector3 right = mainCamera.transform.right;
            right.y = 0f;

            if (right.sqrMagnitude > 0.001f)
            {
                right.Normalize();

                int offsetIndex = spawnCounter % 5 - 2;
                sideOffset = right * offsetIndex * spawnStackSpacing;
            }
        }

        spawnCounter++;

        Vector3 rayStart = targetPos + sideOffset + Vector3.up * spawnRaycastStartHeight;

        if (Physics.Raycast(
                rayStart,
                Vector3.down,
                out RaycastHit hit,
                spawnRaycastDistance,
                spawnSurfaceMask,
                QueryTriggerInteraction.Ignore))
        {
            float bottomOffset = GetPrefabBottomOffset(prefab, spawnRotation);
            return hit.point + Vector3.up * (bottomOffset + spawnSurfaceOffset);
        }

        return targetPos + sideOffset + Vector3.up * 0.5f;
    }

    [Tooltip("Layers that count as room boundaries (walls) that block spawning through them. Leave on Everything if unsure - as long as your walls have colliders, this works automatically.")]
    public LayerMask wallBlockMask = ~0;

    [Tooltip("Safety gap kept between the spawn point and a detected wall, so blocks don't spawn clipped right into it.")]
    public float wallSpawnMargin = 0.15f;

    private Vector3 GetSpawnTargetPoint()
    {
        Vector3 origin;
        Vector3 forward;

        if (mainCamera != null)
        {
            origin = mainCamera.transform.position;
            forward = mainCamera.transform.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.001f)
                forward = mainCamera.transform.forward;
        }
        else if (leftHandTransform != null)
        {
            origin = leftHandTransform.position;
            forward = leftHandTransform.forward;
        }
        else
        {
            origin = transform.position;
            forward = transform.forward;
        }

        forward.Normalize();

        // FIX: previously this always projected spawnDistance meters forward,
        // with no regard for walls in between - standing close to a wall and
        // facing it made blocks spawn on the OTHER side of it (outside the
        // room), since only a downward raycast for the floor was ever done,
        // never a check for something blocking the forward direction itself.
        // A horizontal raycast against the room's own wall colliders (the
        // same ones that already stop the player from walking through them)
        // now clamps the spawn distance to stay safely on this side of any
        // wall in the way.
        float clampedDistance = spawnDistance;

        if (Physics.Raycast(origin, forward, out RaycastHit wallHit, spawnDistance, wallBlockMask, QueryTriggerInteraction.Ignore))
        {
            clampedDistance = Mathf.Max(0f, wallHit.distance - wallSpawnMargin);
        }

        return origin + forward * clampedDistance;
    }

    private float GetPrefabBottomOffset(GameObject prefab, Quaternion spawnRotation)
    {
        if (prefab == null)
            return 0.1f;

        // FIX: this used to Instantiate() a full temporary copy of the prefab
        // (complete with LegoBlockGhostManager, colliders, Rigidbody - every
        // component) purely to measure its bounds, then Destroy() it a moment
        // later. That's an extra Awake()/Start() cycle on a whole throwaway
        // block that never happens when you simply drag the prefab into the
        // scene by hand - and turned out to be exactly the difference between
        // "spawned via button" (corrupted VisualRoot rotation) and "dragged in
        // manually" (always correct). Instead of instantiating anything, this
        // walks the PREFAB ASSET's own mesh/transform data directly to
        // compute the same bottom-offset number, with zero side effects.
        MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);

        if (filters.Length == 0)
            return 0.1f;

        float scale = LegoScaleMenu.CurrentScale;
        Matrix4x4 rootMatrix = Matrix4x4.TRS(Vector3.zero, spawnRotation, Vector3.one * scale);

        float minY = float.MaxValue;
        bool foundAny = false;

        foreach (MeshFilter filter in filters)
        {
            if (filter.sharedMesh == null)
                continue;

            Bounds localBounds = filter.sharedMesh.bounds;

            // Accumulate this renderer's local-to-root matrix by walking up
            // the prefab's own hierarchy (skipping the root itself, since the
            // root's transform is replaced by rootMatrix above to match
            // exactly what Instantiate(prefab, position, spawnRotation) would
            // produce).
            Matrix4x4 localToRoot = Matrix4x4.identity;
            Transform t = filter.transform;

            while (t != null && t != prefab.transform)
            {
                Matrix4x4 localMatrix = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
                localToRoot = localMatrix * localToRoot;
                t = t.parent;
            }

            Matrix4x4 fullMatrix = rootMatrix * localToRoot;

            Vector3 center = localBounds.center;
            Vector3 extents = localBounds.extents;

            for (int xSign = -1; xSign <= 1; xSign += 2)
            {
                for (int ySign = -1; ySign <= 1; ySign += 2)
                {
                    for (int zSign = -1; zSign <= 1; zSign += 2)
                    {
                        Vector3 localCorner = center + new Vector3(xSign * extents.x, ySign * extents.y, zSign * extents.z);
                        Vector3 worldCorner = fullMatrix.MultiplyPoint3x4(localCorner);

                        if (worldCorner.y < minY)
                            minY = worldCorner.y;

                        foundAny = true;
                    }
                }
            }
        }

        if (!foundAny)
            return 0.1f;

        // rootMatrix places the root at world Y=0, so the bottom offset
        // (how far above its own pivot the root needs to sit so its lowest
        // point touches the floor) is simply the negative of the lowest
        // corner found.
        return -minY;
    }

    private class PickerDragTarget : MonoBehaviour,
        IPointerDownHandler,
        IDragHandler,
        IPointerUpHandler
    {
        private LegoHandMenu owner;
        private bool isSV;

        public void Init(LegoHandMenu owner, bool isSV)
        {
            this.owner = owner;
            this.isSV = isSV;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            owner?.BeginPickerPointer(eventData, isSV);
        }

        public void OnDrag(PointerEventData eventData)
        {
            owner?.DragPickerPointer(eventData, isSV);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            owner?.EndPickerPointer(eventData, isSV);
        }
    }
}