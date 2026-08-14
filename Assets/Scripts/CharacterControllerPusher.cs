using UnityEngine;

/// <summary>
/// Lets a CharacterController push physics objects.
///
/// Unity's CharacterController does NOT move Rigidbodies it walks into — it just slides
/// against them. Without this component the player can walk into an unlocked door and
/// nothing happens. Attach this to the player (the object with the CharacterController).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class CharacterControllerPusher : MonoBehaviour
{
    [Tooltip("How hard the player shoves rigidbodies.")]
    [Min(0f)]
    [SerializeField] private float pushForce = 15f;

    [Tooltip("Objects the player can push. Set to Everything if unsure.")]
    [SerializeField] private LayerMask pushableLayers = ~0;

    [Tooltip("Don't push objects the player is standing on top of.")]
    [SerializeField] private bool ignoreObjectsBelow = true;

    [Tooltip("Keep pushes horizontal, so walking into things doesn't drive them into the floor.")]
    [SerializeField] private bool horizontalPushOnly = true;

    [Tooltip("Upper limit on how fast a push can make something move.")]
    [Min(0f)]
    [SerializeField] private float maxPushSpeed = 4f;

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Rigidbody body = hit.collider.attachedRigidbody;

        if (body == null || body.isKinematic)
            return;

        if ((pushableLayers.value & (1 << body.gameObject.layer)) == 0)
            return;

        // Standing on something shouldn't push it.
        if (ignoreObjectsBelow && hit.moveDirection.y < -0.3f)
            return;

        Vector3 direction = hit.moveDirection;
        if (horizontalPushOnly)
            direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
            return;

        direction.Normalize();

        // Push at the contact point so doors and levers get proper torque about their hinge.
        body.AddForceAtPosition(direction * pushForce, hit.point, ForceMode.Force);

        if (body.linearVelocity.magnitude > maxPushSpeed)
            body.linearVelocity = body.linearVelocity.normalized * maxPushSpeed;
    }
}
