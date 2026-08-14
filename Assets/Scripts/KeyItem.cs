using UnityEngine;

/// <summary>
/// Identifies a GameObject as a key item that can unlock a DoorLock.
/// Attach this alongside a Collider and Rigidbody (and optionally Pickable) on the key object.
/// </summary>
public class KeyItem : MonoBehaviour
{
    [Tooltip("Unique identifier that must match a DoorLock's requiredKeyId.")]
    [SerializeField] private string keyId = "key_default";

    /// <summary>
    /// The unique key identifier.
    /// </summary>
    public string KeyId => keyId;
}
