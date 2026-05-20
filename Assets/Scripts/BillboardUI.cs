using UnityEngine;
using TMPro;

[RequireComponent(typeof(RectTransform))]
public class BillboardUI : MonoBehaviour
{
    public enum FaceMode
    {
        FaceCamera,
        FaceCameraOnY
    }

    [Tooltip("Target camera transform. Uses Camera.main when empty.")]
    public Transform targetCamera;

    [Tooltip("Billboard facing mode.")]
    public FaceMode mode = FaceMode.FaceCameraOnY;

    [Range(0f, 20f)]
    public float smoothSpeed = 10f;

    public bool reverse = false;

    [Header("Camera-locked")]
    public bool lockToCameraPlane = false;
    public Vector2 viewportPosition = new Vector2(0.5f, 0.5f);
    public float distanceFromCamera = 2f;
    public bool preserveLocalScaleWhenLocked = true;

    [Header("HUD - Credits")]
    [Tooltip("TMP text for earned credits (same panel as level and countdown).")]
    public TMP_Text creditsText;

    [Tooltip("Format string. {0} is the current credit total.")]
    public string creditsFormat = "credits:{0}";

    Vector3 initialLocalScale;

    void OnEnable()
    {
        SubscribeCredits();
    }

    void OnDisable()
    {
        UnsubscribeCredits();
    }

    void Start()
    {
        if (targetCamera == null && Camera.main != null)
            targetCamera = Camera.main.transform;

        initialLocalScale = transform.localScale;
        SubscribeCredits();
        RefreshCreditsDisplay();
    }

    void SubscribeCredits()
    {
        if (CreditManager.Instance == null) return;
        CreditManager.Instance.OnCreditsChanged -= HandleCreditsChanged;
        CreditManager.Instance.OnCreditsChanged += HandleCreditsChanged;
    }

    void UnsubscribeCredits()
    {
        if (CreditManager.Instance == null) return;
        CreditManager.Instance.OnCreditsChanged -= HandleCreditsChanged;
    }

    void HandleCreditsChanged(int _)
    {
        RefreshCreditsDisplay();
    }

    void RefreshCreditsDisplay()
    {
        if (creditsText == null) return;

        int total = CreditManager.Instance != null ? CreditManager.Instance.credits : 0;
        creditsText.text = string.Format(creditsFormat, total);
    }

    void LateUpdate()
    {
        if (!enabled) return;

        if (targetCamera == null)
        {
            if (Camera.main == null) return;
            targetCamera = Camera.main.transform;
        }

        if (lockToCameraPlane)
        {
            Vector3 viewportPoint = new Vector3(viewportPosition.x, viewportPosition.y, distanceFromCamera);
            Vector3 worldPos = targetCamera.GetComponent<Camera>().ViewportToWorldPoint(viewportPoint);
            transform.position = worldPos;
            transform.rotation = targetCamera.rotation;

            if (preserveLocalScaleWhenLocked)
                transform.localScale = initialLocalScale;

            return;
        }

        Vector3 direction = targetCamera.position - transform.position;

        if (mode == FaceMode.FaceCameraOnY)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;
        }
        else if (direction.sqrMagnitude < 0.000001f)
        {
            return;
        }

        if (reverse) direction = -direction;

        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);

        if (smoothSpeed <= 0f)
            transform.rotation = targetRotation;
        else
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, smoothSpeed * Time.deltaTime);
    }
}
