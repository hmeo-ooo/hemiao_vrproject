#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 将运行时 UI Canvas 烘焙进场景，使 Hierarchy 在 Edit 模式下可见、可编辑。
/// 打开场景时若发现未烘焙的 Session UI，会自动补齐；已烘焙的会校正父节点与 scale。
/// </summary>
[InitializeOnLoad]
public static class SessionUiBaker
{
    const string MenuPath = "Tools/UI/Bake Session UI Canvases Into Scene";

    static SessionUiBaker()
    {
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorApplication.delayCall += BakeOpenScenesIfNeeded;
    }

    static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        EditorApplication.delayCall += BakeOpenScenesIfNeeded;
    }

    [MenuItem(MenuPath)]
    static void BakeMenu()
    {
        BakeOpenScenesIfNeeded(forceLog: true);
    }

    static void BakeOpenScenesIfNeeded()
    {
        BakeOpenScenesIfNeeded(forceLog: false);
    }

    static void BakeOpenScenesIfNeeded(bool forceLog)
    {
        if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        int baked = BakeAll();
        if (baked > 0)
        {
            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log($"[SessionUiBaker] 已处理 {baked} 个 UI Canvas（挂到 LevelSession / Player，scale=1）。请保存场景。");
        }
        else if (forceLog)
        {
            Debug.Log("[SessionUiBaker] 无需修改：相关 UI 已正确挂载。");
        }
    }

    static int BakeAll()
    {
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Bake Session UI Canvases");
        int undoGroup = Undo.GetCurrentGroup();
        int baked = 0;

        foreach (var start in Object.FindObjectsByType<StartScreenUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            bool missing = start.panelRoot == null;
            Undo.RecordObject(start, "Bake StartScreenUI");
            start.EnsureUiBuilt();
            if (missing || NeedsReparent(start.transform, start.panelRoot))
            {
                EditorUtility.SetDirty(start);
                baked++;
            }
        }

        foreach (var hub in Object.FindObjectsByType<LevelHubUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            bool missing = hub.panelRoot == null;
            Undo.RecordObject(hub, "Bake LevelHubUI");
            hub.EnsureUiBuilt();
            if (missing || NeedsReparent(hub.transform, hub.panelRoot))
            {
                EditorUtility.SetDirty(hub);
                baked++;
            }
        }

        foreach (var tutorial in Object.FindObjectsByType<LevelTutorialUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            bool missing = tutorial.panelRoot == null;
            Undo.RecordObject(tutorial, "Bake LevelTutorialUI");
            tutorial.EnsureUiBuilt();
            if (missing || NeedsReparent(tutorial.transform, tutorial.panelRoot))
            {
                EditorUtility.SetDirty(tutorial);
                baked++;
            }
        }

        foreach (var pause in Object.FindObjectsByType<PauseMenuUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            bool missing = pause.panelRoot == null;
            Undo.RecordObject(pause, "Bake PauseMenuUI");
            pause.EnsureUiBuilt();
            if (missing || NeedsReparent(pause.transform, pause.panelRoot))
            {
                EditorUtility.SetDirty(pause);
                baked++;
            }
        }

        foreach (var backstory in Object.FindObjectsByType<BackstoryController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!backstory.buildOverlayUi && backstory.panelRoot == null) continue;
            bool missing = backstory.panelRoot == null;
            Undo.RecordObject(backstory, "Bake BackstoryController");
            backstory.EnsureOverlayUiBuilt();
            if (missing || NeedsReparent(backstory.transform, backstory.panelRoot))
            {
                EditorUtility.SetDirty(backstory);
                baked++;
            }
        }

        foreach (var interaction in Object.FindObjectsByType<CharacterInteraction>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var itemInfo = interaction.GetComponent<ItemInfoWorldUI>();
            if (itemInfo == null)
                itemInfo = Undo.AddComponent<ItemInfoWorldUI>(interaction.gameObject);

            bool missing = itemInfo.canvas == null || itemInfo.panelRect == null;
            Undo.RecordObject(itemInfo, "Bake ItemInfoWorldUI");
            itemInfo.EnsureUiBuilt();
            if (missing || (itemInfo.canvas != null && itemInfo.canvas.transform.parent != itemInfo.transform))
            {
                EditorUtility.SetDirty(itemInfo);
                baked++;
            }
        }

        Undo.CollapseUndoOperations(undoGroup);
        return baked;
    }

    static bool NeedsReparent(Transform owner, GameObject panelRoot)
    {
        if (owner == null || panelRoot == null) return false;
        Canvas canvas = panelRoot.GetComponentInParent<Canvas>();
        if (canvas == null) return true;
        return canvas.transform.parent != owner || canvas.transform.localScale != Vector3.one;
    }
}
#endif
