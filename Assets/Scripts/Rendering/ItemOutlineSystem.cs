using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hemiao.Rendering
{
    /// <summary>
    /// 物品描边系统总入口。负责：
    /// 1. 加载共享描边材质 <see cref="SharedMaterial"/>；
    /// 2. 场景加载 / 周期性扫描时为所有 <see cref="ItemInformation"/> /
    ///    <see cref="Screwdriver"/> / <see cref="Knife"/> 根节点自动挂上
    ///    <see cref="ItemOutlineHull"/>；
    /// 3. 暴露 <see cref="SetHeld(GameObject)"/> / <see cref="ClearHeld"/>，
    ///    供 <c>CharacterInteraction</c> 在抓取 / 释放时切换高亮颜色。
    /// </summary>
    public static class ItemOutlineSystem
    {
        const string k_MaterialResourcePath = "ItemOutlineHull";
#if UNITY_EDITOR
        const string k_MaterialAssetPath = "Assets/Materials/ItemOutlineHull.mat";
#endif

        public static Color DefaultColor = Color.white;
        public static Color HeldColor    = new Color(1f, 0.85f, 0.1f, 1f);
        public static float DefaultWidthMeters = 0.005f;

        /// <summary>"能拆解"物品的加粗描边宽度（世界空间米）。</summary>
        public static float DecomposableWidthMeters = 0.012f;

        /// <summary>未挂 OutlineHull 的新物体多久补扫一次（秒）。</summary>
        public static float ScanInterval = 1.0f;

        const string k_ScrewTag = "Screw";

        static Material s_sharedMaterial;
        static bool s_materialResolved;

        static GameObject s_currentHeld;
        static RuntimePump s_pump;

        public static Material SharedMaterial
        {
            get
            {
                if (s_materialResolved) return s_sharedMaterial;
                s_sharedMaterial = ResolveSharedMaterial();
                s_materialResolved = true;
                if (s_sharedMaterial == null)
                    Debug.LogWarning("[ItemOutlineSystem] 共享材质未找到。请确认 Assets/Materials/ItemOutlineHull.mat 存在，或放到 Resources/ItemOutlineHull.mat。");
                return s_sharedMaterial;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            EnsurePump();
            SceneManager.sceneLoaded += OnSceneLoaded;
            ScanScene();
        }

        static void EnsurePump()
        {
            if (s_pump != null) return;
            var go = new GameObject("[ItemOutlineSystem]");
            go.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(go);
            s_pump = go.AddComponent<RuntimePump>();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ScanScene();
        }

        /// <summary>全场景扫描一次，为所有可交互物补挂 <see cref="ItemOutlineHull"/>。</summary>
        public static void ScanScene()
        {
            EnsureOnAll<ItemInformation>(static info => GetItemRoot(info));
            EnsureOnAll<Screwdriver>(static s => s != null ? s.gameObject : null);
            EnsureOnAll<Knife>(static k => k != null ? k.gameObject : null);

            RefreshAll();

            if (s_currentHeld != null)
                PaintHeld(s_currentHeld);
        }

        /// <summary>外部（例如 <c>ItemSpawner</c>）可在生成新物体后立刻调用。</summary>
        public static void Register(GameObject root)
        {
            if (root == null) return;
            if (root.GetComponent<ItemOutlineHull>() == null)
                root.AddComponent<ItemOutlineHull>();
        }

        public static void SetHeld(GameObject held)
        {
            if (s_currentHeld == held) return;
            if (s_currentHeld != null)
                PaintDefault(s_currentHeld);

            s_currentHeld = held;
            if (held != null)
                PaintHeld(held);
        }

        public static void ClearHeld()
        {
            SetHeld(null);
        }

        /// <summary>把当前的颜色/宽度配置同步到场景中所有 <see cref="ItemOutlineHull"/>。</summary>
        public static void RefreshAll()
        {
            var hulls = Object.FindObjectsOfType<ItemOutlineHull>(true);
            for (int i = 0; i < hulls.Length; i++)
            {
                if (hulls[i] == null) continue;
                hulls[i].Color = (hulls[i].gameObject == s_currentHeld) ? HeldColor : DefaultColor;
                hulls[i].RefreshWidth();
            }
        }

        /// <summary>
        /// 判断给定根节点是否属于"能拆解"的物品：
        /// 1) 复杂度为 <see cref="ItemInformation.ItemComplexity.Composite"/>；或
        /// 2) 自身或子节点带 <see cref="Cuttable"/> 组件；或
        /// 3) 自身或子节点带 Tag <c>"Screw"</c>。
        /// </summary>
        public static bool IsDecomposable(GameObject root)
        {
            if (root == null) return false;

            var info = root.GetComponent<ItemInformation>();
            if (info != null && info.complexity == ItemInformation.ItemComplexity.Composite)
                return true;

            if (root.GetComponentInChildren<Cuttable>(true) != null)
                return true;

            return HasScrewTagInChildren(root.transform);
        }

        static bool HasScrewTagInChildren(Transform t)
        {
            if (t == null) return false;
            if (t.CompareTag(k_ScrewTag)) return true;
            for (int i = 0; i < t.childCount; i++)
                if (HasScrewTagInChildren(t.GetChild(i))) return true;
            return false;
        }

        static void PaintHeld(GameObject go)
        {
            var hull = go.GetComponent<ItemOutlineHull>();
            if (hull == null) hull = go.AddComponent<ItemOutlineHull>();
            hull.Color = HeldColor;
        }

        static void PaintDefault(GameObject go)
        {
            if (go == null) return;
            var hull = go.GetComponent<ItemOutlineHull>();
            if (hull != null) hull.Color = DefaultColor;
        }

        static void EnsureOnAll<T>(System.Func<T, GameObject> rootSelector) where T : Component
        {
            T[] all = Object.FindObjectsOfType<T>(true);
            for (int i = 0; i < all.Length; i++)
            {
                GameObject root = rootSelector(all[i]);
                if (root == null) continue;
                if (root.GetComponent<ItemOutlineHull>() == null)
                    root.AddComponent<ItemOutlineHull>();
            }
        }

        /// <summary>同 <c>CharacterInteraction.GetItemRoot</c>：物品 + 可拆解父级合并为同一根。</summary>
        static GameObject GetItemRoot(ItemInformation info)
        {
            if (info == null) return null;
            var inspectable = info.GetComponentInParent<InspectableItem>();
            if (inspectable != null) return inspectable.gameObject;
            return info.gameObject;
        }

        static Material ResolveSharedMaterial()
        {
            var fromResources = Resources.Load<Material>(k_MaterialResourcePath);
            if (fromResources != null) return fromResources;
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(k_MaterialAssetPath);
#else
            return null;
#endif
        }

        class RuntimePump : MonoBehaviour
        {
            float _accum;

            void Update()
            {
                _accum += Time.unscaledDeltaTime;
                if (_accum < ScanInterval) return;
                _accum = 0f;
                ScanScene();
            }
        }
    }
}
