using UnityEngine;

/// <summary>
/// Plays this block's own snap sound (using the AudioSource + AudioClip you
/// already put on the block prefab) at a volume controlled by the Sound slider.
///
/// Setup:
/// 1. Add this component to your LEGO block prefab, next to the AudioSource
///    that already has your snap sound assigned as its clip.
/// 2. On LegoBlockGhostManager, open the "Snapped" UnityEvent list, click "+",
///    drag the block (this component) into the object slot, and pick
///    LegoSfxPlayer -> PlaySnapSound() from the function dropdown.
///
/// No AudioClip field needed here - it reuses whatever clip is already set
/// on the AudioSource. The Master slider still applies automatically on top
/// via AudioListener.volume, since that's global.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class LegoSfxPlayer : MonoBehaviour
{
    private AudioSource audioSource;

    // The volume you originally set on the AudioSource in the Inspector.
    // Used as the "100%" reference point, then scaled by the Sound slider.
    private float baseVolume;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        baseVolume = audioSource.volume;

        // Prevent the sound from firing on its own when the block spawns.
        audioSource.playOnAwake = false;
    }

    /// <summary>
    /// Call this from LegoBlockGhostManager's "Snapped" UnityEvent.
    /// </summary>
    public void PlaySnapSound()
    {
        if (audioSource == null || audioSource.clip == null)
            return;

        audioSource.volume = Mathf.Clamp01(baseVolume * LegoAudioSettings.SoundVolume);
        audioSource.Play();
    }
}