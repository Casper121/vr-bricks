using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple wrist button script for exactly 3 buttons:
/// - Block Menu Button
/// - Music Player Button
/// - Settings Menu Button
///
/// It does NOT open/close panels itself.
/// It only forwards button clicks to LegoExclusiveMenuController.
/// </summary>
public class LegoWristButtons : MonoBehaviour
{
    [Header("Menu Controller")]
    [SerializeField] private LegoExclusiveMenuController menuController;

    [Header("Button References")]
    [SerializeField] private Button blockMenuButton;
    [SerializeField] private Button musicPlayerButton;
    [SerializeField] private Button settingsMenuButton;

    [Header("Button Icons")]
    [SerializeField] private Sprite blockMenuIcon;
    [SerializeField] private Sprite musicPlayerIcon;
    [SerializeField] private Sprite settingsMenuIcon;

    [Header("Buttons Root")]
    [SerializeField] private GameObject buttonsRoot;

    private void Start()
    {
        SetupButton(blockMenuButton, blockMenuIcon, OnBlockMenuPressed);
        SetupButton(musicPlayerButton, musicPlayerIcon, OnMusicPlayerPressed);
        SetupButton(settingsMenuButton, settingsMenuIcon, OnSettingsMenuPressed);

        if (buttonsRoot != null)
            buttonsRoot.SetActive(true);
    }

    private void SetupButton(Button button, Sprite icon, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
            return;

        button.interactable = true;

        if (icon != null)
        {
            Image image = button.GetComponent<Image>();

            if (image != null)
            {
                image.sprite = icon;
                image.color = Color.white;
            }
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Color.white;
        colors.pressedColor = Color.white;
        colors.selectedColor = Color.white;
        colors.disabledColor = Color.white;
        button.colors = colors;

        LegoButtonHover.AddTo(button.gameObject);
    }

    private void OnBlockMenuPressed()
    {
        if (menuController != null)
            menuController.ToggleBlockMenu();
    }

    private void OnMusicPlayerPressed()
    {
        if (menuController != null)
            menuController.ToggleMusicPanel();
    }

    private void OnSettingsMenuPressed()
    {
        if (menuController != null)
            menuController.ToggleSettingsPanel();
    }
}
