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

    [Header("Spawn Settings")]
    public float spawnDistance = 0.5f;

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
        GenerateBlockButtons();
        UpdateColorFromHSV();
        InitCursorPositions();
        InitSavedColorSlots();
        PreparePickerObjects();
    }

    private void Update()
    {
        if (allowKeyboardFallback && Keyboard.current != null)
        {
            if (Keyboard.current.mKey.wasPressedThisFrame)
                SetMenuOpen(!IsMenuVisible());
        }

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

        Vector3 spawnPos = GetSpawnPosition();
        Quaternion spawnRot = Quaternion.identity;

        if (mainCamera != null)
        {
            Vector3 flat = mainCamera.transform.forward;
            flat.y = 0f;

            if (flat.sqrMagnitude > 0.001f)
                spawnRot = Quaternion.LookRotation(flat);
        }

        GameObject block = Instantiate(entry.blockPrefab, spawnPos, spawnRot);

        foreach (Renderer r in block.GetComponentsInChildren<Renderer>())
        {
            Material mat = new Material(r.material);

            if (mat.HasProperty("_Color"))
                mat.color = selectedColor;

            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", selectedColor);

            r.material = mat;
        }

        LegoBlock legoBlock = block.GetComponent<LegoBlock>();

        if (legoBlock != null)
            legoBlock.SetSnappedToSocket(false);
    }

    private Vector3 GetSpawnPosition()
    {
        if (mainCamera != null)
        {
            return mainCamera.transform.position
                 + mainCamera.transform.forward * spawnDistance
                 + Vector3.down * 0.1f;
        }

        if (leftHandTransform != null)
            return leftHandTransform.position + Vector3.forward * spawnDistance;

        return Vector3.zero;
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