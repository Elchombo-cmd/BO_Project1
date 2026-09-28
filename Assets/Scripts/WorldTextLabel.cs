using TMPro;
using UnityEngine;

/// <summary>
/// Adds an editable text label to any object (a note, a piece of paper, a sign, etc.).
/// Attach this to the object you want to label. It creates and drives a child
/// TextMeshPro (3D) object so you can align the text box to the surface.
///
/// Everything is editable from the Inspector and updates live in the editor:
///   - Local Position / Rotation let you place the text on the object's surface.
///   - Box Size sets the width and height of the text area (in local units).
///   - Font Size, colour, and alignment control the look of the text.
///
/// A wireframe gizmo is drawn around the text box so you can see and align it
/// in the Scene view without entering Play mode.
/// </summary>
[ExecuteAlways]
public class WorldTextLabel : MonoBehaviour
{
    [Header("Text")]
    [Tooltip("The text shown on the object.")]
    [TextArea(2, 6)]
    [SerializeField] private string text = "Note";

    [Tooltip("Font size of the text.")]
    [SerializeField] private float fontSize = 4f;

    [Tooltip("Colour of the text.")]
    [SerializeField] private Color textColor = Color.black;

    [Tooltip("How the text is aligned inside the box.")]
    [SerializeField] private TextAlignmentOptions alignment = TextAlignmentOptions.Center;

    [Tooltip("Optional font asset. Leave empty to use the TMP default.")]
    [SerializeField] private TMP_FontAsset fontAsset;

    [Header("Text Box Transform (local to this object)")]
    [Tooltip("Position of the text box relative to this object.")]
    [SerializeField] private Vector3 localPosition = new Vector3(0f, 0.5f, -0.01f);

    [Tooltip("Rotation of the text box relative to this object (Euler angles).")]
    [SerializeField] private Vector3 localRotation = Vector3.zero;

    [Tooltip("Width and height of the text area, in this object's local units.")]
    [SerializeField] private Vector2 boxSize = new Vector2(0.7f, 0.9f);

    [Header("Gizmo")]
    [Tooltip("Draw a wireframe box in the Scene view to help align the text.")]
    [SerializeField] private bool drawGizmo = true;

    [SerializeField, HideInInspector] private TextMeshPro _tmp;

    private const string ChildName = "TextLabel";

    // Guards against re-entrant / stacked deferred calls in the editor.
    private bool _validateQueued;

    private void OnEnable()
    {
        EnsureTextObject();
        Apply();
    }

    /// <summary>Change the displayed text at runtime.</summary>
    public void SetText(string value)
    {
        text = value;
        Apply();
    }

    /// <summary>The current text value.</summary>
    public string Text => text;

    private void EnsureTextObject()
    {
        // Collect every existing child that looks like our label. There should be
        // exactly one, but domain reloads, prefab instancing, and repeated editor
        // callbacks can leave duplicates behind, so we reconcile down to one.
        TextMeshPro kept = null;

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child == null || child.name != ChildName)
            {
                continue;
            }

            var tmp = child.GetComponent<TextMeshPro>();
            if (tmp == null)
            {
                continue;
            }

            if (kept == null)
            {
                // First valid label found — keep it.
                kept = tmp;
            }
            else
            {
                // Any additional labels are duplicates: remove them.
                DestroyChild(child.gameObject);
            }
        }

        if (kept == null)
        {
            var go = new GameObject(ChildName);
            go.transform.SetParent(transform, false);
            kept = go.AddComponent<TextMeshPro>();
        }

        _tmp = kept;
    }

    private static void DestroyChild(GameObject go)
    {
        if (Application.isPlaying)
        {
            Destroy(go);
        }
        else
        {
            DestroyImmediate(go);
        }
    }

    private void Apply()
    {
        if (_tmp == null)
        {
            return;
        }

        Transform t = _tmp.transform;
        t.localPosition = localPosition;
        t.localEulerAngles = localRotation;
        t.localScale = Vector3.one;

        if (fontAsset != null)
        {
            _tmp.font = fontAsset;
        }

        _tmp.text = text;
        _tmp.fontSize = fontSize;
        _tmp.color = textColor;
        _tmp.alignment = alignment;
        _tmp.enableWordWrapping = true;

        // The RectTransform on the TMP object defines the text box size.
        var rect = _tmp.rectTransform;
        rect.sizeDelta = boxSize;
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

#if UNITY_EDITOR
    // Live-update in the editor when values change in the Inspector.
    private void OnValidate()
    {
        // Avoid stacking multiple deferred callbacks: only queue one at a time.
        if (_validateQueued)
        {
            return;
        }

        _validateQueued = true;

        UnityEditor.EditorApplication.delayCall += DeferredValidate;
    }

    private void DeferredValidate()
    {
        UnityEditor.EditorApplication.delayCall -= DeferredValidate;
        _validateQueued = false;

        // The object may have been destroyed between queueing and running.
        if (this == null)
        {
            return;
        }

        // Don't touch the hierarchy while this component lives on a prefab asset
        // being imported — only reconcile scene instances and the open prefab stage.
        if (UnityEditor.EditorUtility.IsPersistent(this) &&
            UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() == null)
        {
            return;
        }

        EnsureTextObject();
        Apply();
    }
#endif

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmo)
        {
            return;
        }

        // Draw the text box outline in this object's local space.
        Matrix4x4 boxMatrix = Matrix4x4.TRS(
            transform.TransformPoint(localPosition),
            transform.rotation * Quaternion.Euler(localRotation),
            transform.lossyScale);

        Gizmos.matrix = boxMatrix;
        Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.9f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(boxSize.x, boxSize.y, 0f));
    }
}
