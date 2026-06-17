#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Play 模式切换时把 Inspector 切到持久资源，避免 GameObjectInspector 在 OnDisable 时
/// 仍引用已销毁的运行时对象而抛出 NullReferenceException。
/// </summary>
[InitializeOnLoad]
static class PlayModeInspectorFix
{
    const int PostEditModeRedirectPasses = 8;

    static Object _safeInspectorTarget;

    static PlayModeInspectorFix()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        switch (state)
        {
            // 进入 Play 前：把 Inspector 从场景对象移开，避免 Editor 记住将被销毁的选中项。
            case PlayModeStateChange.ExitingEditMode:
                RedirectInspectorToSafeTarget();
                break;

            // 进入 Play 后解锁，允许 Play 模式内正常调试（若需要）。
            case PlayModeStateChange.EnteredPlayMode:
                ActiveEditorTracker.sharedTracker.isLocked = false;
                break;

            // 退出 Play 前：对象尚未销毁，先切到安全目标，防止 OnDisable 读到 null。
            case PlayModeStateChange.ExitingPlayMode:
                RedirectInspectorToSafeTarget();
                break;

            // FinalizePlaymodeLayout 之后会还原旧选中，需延迟多次重定向到安全目标。
            case PlayModeStateChange.EnteredEditMode:
                RedirectInspectorToSafeTarget();
                SchedulePostEditModeRedirects(PostEditModeRedirectPasses);
                break;
        }
    }

    static void SchedulePostEditModeRedirects(int passesRemaining)
    {
        if (passesRemaining <= 0)
            return;

        EditorApplication.delayCall += () =>
        {
            RedirectInspectorToSafeTarget();
            ActiveEditorTracker.sharedTracker.isLocked = false;
            SchedulePostEditModeRedirects(passesRemaining - 1);
        };
    }

    static void RedirectInspectorToSafeTarget()
    {
        Object safe = GetSafeInspectorTarget();
        if (safe == null)
        {
            Selection.activeInstanceID = 0;
            Selection.objects = System.Array.Empty<Object>();
            return;
        }

        ActiveEditorTracker.sharedTracker.isLocked = true;
        Selection.activeObject = safe;
        Selection.objects = new[] { safe };
    }

    static Object GetSafeInspectorTarget()
    {
        if (_safeInspectorTarget != null)
            return _safeInspectorTarget;

        _safeInspectorTarget = AssetDatabase.LoadMainAssetAtPath("ProjectSettings/ProjectSettings.asset");
        if (_safeInspectorTarget != null)
            return _safeInspectorTarget;

        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");
        if (sceneGuids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(sceneGuids[0]);
            _safeInspectorTarget = AssetDatabase.LoadMainAssetAtPath(path);
        }

        return _safeInspectorTarget;
    }
}
#endif
