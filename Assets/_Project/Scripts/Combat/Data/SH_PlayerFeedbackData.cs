using UnityEngine;

namespace Game.Combat.Data
{
    [CreateAssetMenu(
        fileName = "PlayerFeedbackData",
        menuName = "ScapeHorizon/Settings/PlayerFeedbackData",
        order = 305)]
    public class SH_PlayerFeedbackData : ScriptableObject
    {
        [Header("Hit Received")]
        [Tooltip("Prefab instantiated on the player when a hit is received. " +
                 "Include VFX and AudioSource inside.")]
        public GameObject hitEffectPrefab;

        [Tooltip("AudioClip played at the player's position on each hit.")]
        public AudioClip hitAudioClip;

        [Tooltip("Fallback lifetime for the hit effect prefab if it does not self-terminate.")]
        [Min(0.1f)]
        public float effectAutoDestroyTime = 1f;
    }
}