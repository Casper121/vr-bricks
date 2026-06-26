using UnityEngine;

/// <summary>
/// Defines a single LEGO block type available in the hand menu.
/// Create instances via: Assets > Create > LEGO > Block Spawn Entry
/// </summary>
[CreateAssetMenu(menuName = "LEGO/Block Spawn Entry", fileName = "NewBlockEntry")]
public class LegoBlockSpawnEntry : ScriptableObject
{
    public enum LegoMenuCategory
    {
        Blocks,
        Plates
    }

    [Header("Block Identity")]
    [Tooltip("Display name shown on the menu button, e.g. '2x2' or '2x4'.")]
    public string displayName = "2x2";

    [Tooltip("Category where this entry appears in the block menu.")]
    public LegoMenuCategory category = LegoMenuCategory.Blocks;

    [Tooltip("Prefab that will be instantiated when the button is pressed.")]
    public GameObject blockPrefab;

    [Header("Preview")]
    [Tooltip("Optional thumbnail sprite shown next to the label. Leave empty for text-only.")]
    public Sprite thumbnail;

    [Header("Button Color")]
    [Tooltip("Tint color for this entry's button background. The hand menu currently uses the selected paint color as button tint.")]
    public Color buttonColor = new Color(0.2f, 0.5f, 1f, 1f);
}
