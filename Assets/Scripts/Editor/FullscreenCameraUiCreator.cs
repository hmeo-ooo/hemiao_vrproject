using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class FullscreenCameraUiCreator
{
    const string MenuPath = "Tools/UI/Create Fullscreen Overlay Canvas";
    const string DefaultCanvasName = "BackstoryCanvas";
    const string DefaultPanelName = "BackgroundPanel";
    const int OverlaySortingOrder = 100;

    [MenuItem(MenuPath)]
    static void CreateForActiveScene()
    {
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Create Fullscreen Overlay Canvas");
        int undoGroup = Undo.GetCurrentGroup();

        GameObject canvasGo = new GameObject(
            DefaultCanvasName,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(canvasGo, "Create Canvas");

        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = OverlaySortingOrder;

        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        RuntimeUiUtility.ConfigureOverlayCanvasScaler(scaler);

        GameObject panelGo = new GameObject(
            DefaultPanelName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        Undo.RegisterCreatedObjectUndo(panelGo, "Create Background Panel");
        panelGo.transform.SetParent(canvasGo.transform, false);

        RectTransform panelRect = panelGo.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        panelRect.pivot = new Vector2(0.5f, 0.5f);

        Image panelImage = panelGo.GetComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 1f);
        panelImage.raycastTarget = true;

        EnsureEventSystem();

        Selection.activeGameObject = canvasGo;
        EditorSceneManager.MarkSceneDirty(canvasGo.scene);
        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log($"[FullscreenCameraUiCreator] 已创建 {DefaultCanvasName}（Screen Space - Overlay）。");
    }

    static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;

        GameObject esGo = new GameObject(
            "EventSystem",
            typeof(UnityEngine.EventSystems.EventSystem),
            typeof(UnityEngine.EventSystems.StandaloneInputModule));
        Undo.RegisterCreatedObjectUndo(esGo, "Create EventSystem");
    }
}
