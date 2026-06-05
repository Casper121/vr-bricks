using UnityEngine;

/// <summary>
/// Defines a single LEGO block type available in the hand menu.
/// Create instances via: Assets > Create > LEGO > Block Spawn Entry
/// </summary>
[CreateAssetMenu(menuName = "LEGO/Block Spawn Entry", fileName = "NewBlockEntry")]
public class LegoBlockSpawnEntry : ScriptableObject
{
    [Header("Block Identity")]
    [Tooltip("Display name shown on the menu button, e.g. '2x2' or '2x4'.")]
    public string displayName = "2x2";

    [Tooltip("Prefab that will be instantiated when the button is pressed.")]
    public GameObject blockPrefab;

    [Header("Preview")]
    [Tooltip("Optional thumbnail sprite shown next to the label. Leave empty for text-only.")]
    public Sprite thumbnail;

    [Header("Button Color")]
    [Tooltip("Tint color for this entry's button background.")]
    public Color buttonColor = new Color(0.2f, 0.5f, 1f, 1f);
}