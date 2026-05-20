using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ItemInfoWorldUI : MonoBehaviour
{
    [Tooltip("\u9762\u677F\u5728\u7269\u4F53\u53F3\u4FA7\u7684\u4E16\u754C\u7A7A\u95F4\u504F\u79FB")]
    public float sideOffset = 0.65f;

    [Tooltip("\u9762\u677F\u5C3A\u5BF8\uFF08\u50CF\u7D20\uFF09")]
    public Vector2 panelSize = new Vector2(300f, 130f);

    [Tooltip("\u7070\u8272\u5E95\u8272")]
    public Color backgroundColor = new Color(0.22f, 0.22f, 0.22f, 0.9f);

    [Tooltip("\u6587\u5B57\u989C\u8272")]
    public Color textColor = Color.white;

    [Tooltip("\u540D\u79F0\u5B57\u53F7")]
    public float nameFontSize = 22f;

    [Tooltip("\u4ECB\u7ECD\u5B57\u53F7")]
    public float descriptionFontSize = 16f;

    Canvas canvas;
    RectTransform panelRect;
    TMP_Text nameText;
    TMP_Text descriptionText;
    Camera uiCamera;
    Transform followTarget;

    public void Initialize(Camera camera)
    {
        uiCamera = camera;
        if (canvas != null) return;
        BuildUi();
    }

    public void Show(ItemInformation info, Transform anchor)
    {
        if (info == null || anchor == null || panelRect == null) return;

        followTarget = anchor;
        nameText.text = info.ResolvedDisplayName;
        descriptionText.text = info.itemDescription ?? string.Empty;
        panelRect.gameObject.SetActive(true);
        UpdatePosition();
    }

    public void Hide()
    {
        followTarget = null;
        if (panelRect != null)
            panelRect.gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        if (followTarget == null || !panelRect.gameObject.activeSelf) return;
        UpdatePosition();
    }

    void UpdatePosition()
    {
        if (uiCamera == null || followTarget == null) return;

        Bounds bounds = CalculateWorldBounds(followTarget.gameObject);
        Vector3 worldPos = bounds.center + uiCamera.transform.right * sideOffset;
        Vector3 screenPos = uiCamera.WorldToScreenPoint(worldPos);

        if (screenPos.z <= 0f)
        {
            panelRect.gameObject.SetActive(false);
            return;
        }

        panelRect.gameObject.SetActive(true);
        panelRect.position = screenPos;
    }

    public static Bounds CalculateWorldBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(root.transform.position, Vector3.one * 0.5f);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    void BuildUi()
    {
        var canvasGo = new GameObject("ItemInfoCanvas");
        canvasGo.transform.SetParent(transform, false);

        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120;

        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGo.AddComponent<GraphicRaycaster>();

        var panelGo = new GameObject("ItemInfoPanel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        panelRect = panelGo.AddComponent<RectTransform>();
        panelRect.sizeDelta = panelSize;

        var bg = panelGo.AddComponent<Image>();
        bg.color = backgroundColor;

        var nameGo = new GameObject("ItemName");
        nameGo.transform.SetParent(panelGo.transform, false);
        var nameRect = nameGo.AddComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0f, 0.52f);
        nameRect.anchorMax = new Vector2(1f, 1f);
        nameRect.offsetMin = new Vector2(12f, 0f);
        nameRect.offsetMax = new Vector2(-12f, -10f);

        nameText = nameGo.AddComponent<TextMeshProUGUI>();
        nameText.color = textColor;
        nameText.fontSize = nameFontSize;
        nameText.fontStyle = FontStyles.Bold;
        nameText.alignment = TextAlignmentOptions.TopLeft;
        nameText.enableWordWrapping = true;

        var descGo = new GameObject("ItemDescription");
        descGo.transform.SetParent(panelGo.transform, false);
        var descRect = descGo.AddComponent<RectTransform>();
        descRect.anchorMin = new Vector2(0f, 0f);
        descRect.anchorMax = new Vector2(1f, 0.5f);
        descRect.offsetMin = new Vector2(12f, 10f);
        descRect.offsetMax = new Vector2(-12f, 0f);

        descriptionText = descGo.AddComponent<TextMeshProUGUI>();
        descriptionText.color = textColor;
        descriptionText.fontSize = descriptionFontSize;
        descriptionText.alignment = TextAlignmentOptions.TopLeft;
        descriptionText.enableWordWrapping = true;

        panelRect.gameObject.SetActive(false);
    }
}
