using UnityEngine;

/// <summary>
/// Simple static store for the current "Sound" (SFX) volume.
///
/// LegoMusicMenuController writes into this whenever the Sound slider moves.
/// Any script that plays a sound effect (e.g. block snap sound) reads
/// LegoAudioSettings.SoundVolume when it plays the clip.
///
/// The Master slider does NOT need to touch this class - it already
/// controls AudioListener.volume, which Unity automatically multiplies
/// on top of every sound in the game (music AND effects).
/// </summary>
public static class LegoAudioSettings
{
    public static float SoundVolume { get; private set; } = 1f;

    public static void SetSoundVolume(float value)
    {
        SoundVolume = Mathf.Clamp01(value);
    }
}