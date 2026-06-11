using UnityEditor;
using UnityEngine;

/// <summary>
/// 在 Inspector 内渲染与运行时审视界面一致的 2D 物品预览，并支持从预览区域射线检测。
/// </summary>
sealed class InspectableItemKnifeCutPreview : System.IDisposable
{
    const float DisplayDistance = 1.2f;
    const float DisplayDistancePadding = 1.1f;
    const float FieldOfView = 60f;

    readonly PreviewRenderUtility _utility;
    GameObject _previewInstance;
    InspectableItem _sourceItem;

    public Camera PreviewCamera => _utility != null ? _utility.camera : null;
    public GameObject PreviewInstance => _previewInstance;

    public InspectableItemKnifeCutPreview()
    {
        _utility = new PreviewRenderUtility();
        _utility.cameraFieldOfView = FieldOfView;
        _utility.camera.clearFlags = CameraClearFlags.SolidColor;
        _utility.camera.backgroundColor = new Color(0.12f, 0.12f, 0.14f, 1f);
        _utility.camera.nearClipPlane = 0.05f;
        _utility.camera.farClipPlane = 100f;
    }

    public void Dispose()
    {
        DestroyPreviewInstance();
        _utility?.Cleanup();
    }

    public void Sync(InspectableItem item)
    {
        if (item == null) return;

        if (_sourceItem != item || _previewInstance == null)
            Rebuild(item);

        _previewInstance.transform.rotation = Quaternion.Euler(item.inspectionDisplayEulers);
        FrameCamera();
    }

    public Texture Render(Rect rect)
    {
        if (_previewInstance == null) return null;

        _utility.BeginPreview(rect, GUIStyle.none);
        FrameCamera();
        _utility.camera.Render();
        return _utility.EndPreview();
    }

    public bool Raycast(Rect previewRect, Vector2 guiMousePos, out Vector3 hitPoint, out Vector3 hitNormal)
    {
        hitPoint = Vector3.zero;
        hitNormal = Vector3.up;
        if (_previewInstance == null || _utility?.camera == null) return false;
        if (!previewRect.Contains(guiMousePos)) return false;

        Ray ray = GuiRectToRay(previewRect, guiMousePos);
        Collider[] cols = _previewInstance.GetComponentsInChildren<Collider>();
        float best = float.MaxValue;
        bool found = false;

        for (int i = 0; i < cols.Length; i++)
        {
            Collider col = cols[i];
            if (col == null || !col.enabled) continue;
            if (!col.Raycast(ray, out RaycastHit h, 1000f) || h.distance >= best) continue;
            best = h.distance;
            hitPoint = h.point;
            hitNormal = h.normal;
            found = true;
        }

        if (!found)
            found = TryRaycastRenderers(ray, out hitPoint, out hitNormal);

        return found;
    }

    /// <summary>将预览实例上的命中点换算为源物品根节点的本地坐标。</summary>
    public Vector3 WorldToSourceLocal(Vector3 worldPoint)
    {
        if (_previewInstance == null || _sourceItem == null) return worldPoint;
        return _previewInstance.transform.InverseTransformPoint(worldPoint);
    }

    public Vector3 WorldToSourceLocalDirection(Vector3 worldDirection)
    {
        if (_previewInstance == null || _sourceItem == null) return worldDirection;
        return _previewInstance.transform.InverseTransformDirection(worldDirection).normalized;
    }

    public Vector2 WorldToGui(Rect previewRect, Vector3 worldPoint)
    {
        Camera cam = _utility.camera;
        Vector3 sp = cam.WorldToScreenPoint(worldPoint);
        if (sp.z < 0f) return new Vector2(-10000f, -10000f);

        float u = sp.x / Mathf.Max(1f, cam.pixelWidth);
        float v = sp.y / Mathf.Max(1f, cam.pixelHeight);
        return new Vector2(
            previewRect.x + u * previewRect.width,
            previewRect.y + (1f - v) * previewRect.height);
    }

    Ray GuiRectToRay(Rect previewRect, Vector2 guiMousePos)
    {
        float u = (guiMousePos.x - previewRect.x) / Mathf.Max(1f, previewRect.width);
        float v = 1f - (guiMousePos.y - previewRect.y) / Mathf.Max(1f, previewRect.height);
        return _utility.camera.ViewportPointToRay(new Vector3(u, v, 0f));
    }

    void Rebuild(InspectableItem item)
    {
        DestroyPreviewInstance();
        _sourceItem = item;

        _previewInstance = Object.Instantiate(item.gameObject);
        _previewInstance.name = item.name + "_InspectionPreview";
        _previewInstance.hideFlags = HideFlags.HideAndDontSave;
        _previewInstance.transform.position = Vector3.zero;
        _previewInstance.transform.localScale = item.transform.lossyScale;

        foreach (MonoBehaviour mb in _previewInstance.GetComponentsInChildren<MonoBehaviour>(true))
            mb.enabled = false;

        _utility.AddSingleGO(_previewInstance);
    }

    void FrameCamera()
    {
        if (_previewInstance == null) return;

        Bounds bounds = ItemInfoWorldUI.CalculateWorldBounds(_previewInstance);
        float fov = FieldOfView;
        _utility.camera.fieldOfView = fov;

        float distance = _sourceItem != null
            ? _sourceItem.ResolveInspectionDisplayDistance(DisplayDistance)
            : DisplayDistance;
        float radius = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
        if (radius > 0.001f)
        {
            float fitDistance = radius / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            distance = Mathf.Max(distance, fitDistance * DisplayDistancePadding);
        }

        Vector3 lookTarget = bounds.center;
        _utility.camera.transform.position = lookTarget + Vector3.back * distance;
        _utility.camera.transform.rotation = Quaternion.identity;
    }

    bool TryRaycastRenderers(Ray ray, out Vector3 point, out Vector3 normal)
    {
        point = Vector3.zero;
        normal = Vector3.up;
        if (_previewInstance == null) return false;

        Renderer[] renderers = _previewInstance.GetComponentsInChildren<Renderer>();
        float best = float.MaxValue;
        bool hit = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Bounds b = renderers[i].bounds;
            if (!b.IntersectRay(ray, out float dist) || dist >= best) continue;
            best = dist;
            point = ray.GetPoint(dist);
            normal = (point - b.center).normalized;
            hit = true;
        }

        return hit;
    }

    void DestroyPreviewInstance()
    {
        if (_previewInstance != null)
        {
            Object.DestroyImmediate(_previewInstance);
            _previewInstance = null;
        }
        _sourceItem = null;
    }
}
