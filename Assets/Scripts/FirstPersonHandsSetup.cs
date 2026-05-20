using UnityEngine;

/// <summary>
/// 修正第一人称手部模型被近裁剪面裁切、蒙皮网格被错误剔除等问题。
/// 挂到手部预制体根节点，进入场景时自动执行。
/// </summary>
[DefaultExecutionOrder(-100)]
public class FirstPersonHandsSetup : MonoBehaviour
{
    [Header("挂载到摄像机")]
    [Tooltip("将手部挂到 Main Camera 下，避免随身体移动产生错位与裁切。")]
    public bool attachToCamera = true;

    public Vector3 cameraLocalPosition = new Vector3(0f, -0.32f, 0.42f);
    public Vector3 cameraLocalEuler = Vector3.zero;

    [Header("近裁剪面")]
    [Tooltip("第一人称建议 0.01~0.05。过大会裁掉贴近镜头的手臂与盾牌。")]
    public float targetNearClip = 0.02f;

    public bool adjustMainCameraNearClip = true;

    [Header("蒙皮网格")]
    public bool forceUpdateWhenOffscreen = true;

    [Tooltip("扩大局部包围盒，防止动画时 Unity 误判为在视锥外而不渲染。")]
    public bool expandSkinnedBounds = true;

    public float boundsPadding = 2.5f;

    [Header("渲染")]
    [Tooltip("第一人称手部专用层（可选）。需在 Project Settings > Tags and Layers 中新建，例如 FirstPersonHands。")]
    public string firstPersonLayerName = "";

    public bool disableOcclusionCullingOnRenderers = true;

    Camera handsCamera;
    bool applied;

    void Awake()
    {
        Apply();
    }

    void OnEnable()
    {
        if (!applied)
            Apply();
    }

    public void Apply()
    {
        if (attachToCamera)
            AttachToCamera();

        if (adjustMainCameraNearClip)
            ConfigureNearClip();

        ConfigureSkinnedMeshes();
        TryAssignLayer();
        applied = true;
    }

    void AttachToCamera()
    {
        Camera cam = ResolveViewCamera();
        if (cam == null) return;

        Transform camTransform = cam.transform;
        if (transform.parent == camTransform) return;

        transform.SetParent(camTransform, false);
        transform.localPosition = cameraLocalPosition;
        transform.localRotation = Quaternion.Euler(cameraLocalEuler);
    }

    Camera ResolveViewCamera()
    {
        if (handsCamera != null) return handsCamera;

        var interaction = GetComponentInParent<CharacterInteraction>();
        if (interaction != null && interaction.cameraTransform != null)
        {
            handsCamera = interaction.cameraTransform.GetComponent<Camera>();
            if (handsCamera != null) return handsCamera;
        }

        var move = GetComponentInParent<CharacterMove>();
        if (move != null && move.cameraTransform != null)
        {
            handsCamera = move.cameraTransform.GetComponent<Camera>();
            if (handsCamera != null) return handsCamera;
        }

        if (Camera.main != null)
            handsCamera = Camera.main;

        return handsCamera;
    }

    void ConfigureNearClip()
    {
        Camera cam = ResolveViewCamera();
        if (cam == null) return;

        if (cam.nearClipPlane > targetNearClip)
            cam.nearClipPlane = targetNearClip;
    }

    void ConfigureSkinnedMeshes()
    {
        var renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer smr = renderers[i];
            if (smr == null) continue;

            if (forceUpdateWhenOffscreen)
                smr.updateWhenOffscreen = true;

            if (expandSkinnedBounds)
            {
                Bounds bounds = smr.localBounds;
                bounds.Expand(boundsPadding);
                smr.localBounds = bounds;
            }

            if (disableOcclusionCullingOnRenderers)
                smr.allowOcclusionWhenDynamic = false;
        }

        var staticRenderers = GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < staticRenderers.Length; i++)
        {
            if (staticRenderers[i] != null && disableOcclusionCullingOnRenderers)
                staticRenderers[i].allowOcclusionWhenDynamic = false;
        }
    }

    void TryAssignLayer()
    {
        if (string.IsNullOrEmpty(firstPersonLayerName)) return;

        int layer = LayerMask.NameToLayer(firstPersonLayerName);
        if (layer < 0) return;

        SetLayerRecursive(transform, layer);
    }

    static void SetLayerRecursive(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursive(root.GetChild(i), layer);
    }
}
