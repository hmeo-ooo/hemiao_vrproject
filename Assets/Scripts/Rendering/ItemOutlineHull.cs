using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hemiao.Rendering
{
    /// <summary>
    /// 反向法线挤出 (Inverted Hull) 描边组件。
    /// 启用时为目标物体的每个 MeshRenderer / SkinnedMeshRenderer 创建一个
    /// 与之共享 Mesh 的"_outline_*"子 GameObject，使用 <see cref="ItemOutlineSystem.SharedMaterial"/>
    /// 渲染描边。颜色 / 宽度通过 MaterialPropertyBlock 注入，原渲染器零修改。
    /// </summary>
    [DisallowMultipleComponent]
    public class ItemOutlineHull : MonoBehaviour
    {
        const string k_ChildPrefix = "_outline_";

        static readonly int s_ColorId = Shader.PropertyToID("_OutlineColor");
        static readonly int s_WidthId = Shader.PropertyToID("_OutlineWidth");

        Color _color = Color.white;
        bool  _applied;
        bool  _initialized;
        bool  _isDecomposable;

        readonly List<Renderer> _outlineRenderers = new List<Renderer>(8);
        MaterialPropertyBlock _mpb;

        public Color Color
        {
            get => _color;
            set { if (_color == value) return; _color = value; PushBlock(); }
        }

        /// <summary>由 <see cref="ItemOutlineSystem.IsDecomposable"/> 自动判定，能拆解 → 用更粗的描边。</summary>
        public bool IsDecomposable => _isDecomposable;

        /// <summary>根据当前系统配置 + 自身可拆解标记换算出的有效描边宽度。</summary>
        public float EffectiveWidth =>
            _isDecomposable ? ItemOutlineSystem.DecomposableWidthMeters : ItemOutlineSystem.DefaultWidthMeters;

        /// <summary>外部（系统）在修改默认/加粗宽度后调用，把新宽度推到 MaterialPropertyBlock。</summary>
        public void RefreshWidth() => PushBlock();

        void OnEnable()
        {
            EnsureInitialized();
            Apply();
        }

        void OnDisable()
        {
            Revert();
        }

        void OnDestroy()
        {
            Revert();
        }

        void EnsureInitialized()
        {
            if (_initialized) return;
            _color = ItemOutlineSystem.DefaultColor;
            _isDecomposable = ItemOutlineSystem.IsDecomposable(gameObject);
            _initialized = true;
        }

        void Apply()
        {
            if (_applied) return;

            Material mat = ItemOutlineSystem.SharedMaterial;
            if (mat == null) return;

            Renderer[] all = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (!IsOutlineable(r)) continue;

                Renderer created = CreateOutlineRendererFor(r, mat);
                if (created != null) _outlineRenderers.Add(created);
            }

            _applied = true;
            PushBlock();
        }

        void Revert()
        {
            for (int i = 0; i < _outlineRenderers.Count; i++)
            {
                Renderer r = _outlineRenderers[i];
                if (r == null) continue;
                if (Application.isPlaying) Destroy(r.gameObject);
                else DestroyImmediate(r.gameObject);
            }
            _outlineRenderers.Clear();
            _applied = false;
        }

        void PushBlock()
        {
            if (!_applied) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            _mpb.Clear();
            _mpb.SetColor(s_ColorId, _color);
            _mpb.SetFloat(s_WidthId, Mathf.Max(0f, EffectiveWidth));

            for (int i = 0; i < _outlineRenderers.Count; i++)
            {
                Renderer r = _outlineRenderers[i];
                if (r != null) r.SetPropertyBlock(_mpb);
            }
        }

        static bool IsOutlineable(Renderer r)
        {
            if (r == null) return false;
            if (r is ParticleSystemRenderer) return false;
            if (r is TrailRenderer) return false;
            if (r is LineRenderer) return false;
            if (r is BillboardRenderer) return false;
            if (r.gameObject.name.StartsWith(k_ChildPrefix)) return false;
            return true;
        }

        static Renderer CreateOutlineRendererFor(Renderer source, Material outlineMat)
        {
            switch (source)
            {
                case MeshRenderer mr:
                {
                    MeshFilter mf = mr.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) return null;

                    GameObject go = new GameObject(k_ChildPrefix + source.name);
                    go.transform.SetParent(source.transform, false);
                    go.layer = source.gameObject.layer;
                    go.hideFlags = HideFlags.DontSave;

                    MeshFilter outMf = go.AddComponent<MeshFilter>();
                    outMf.sharedMesh = mf.sharedMesh;

                    MeshRenderer outMr = go.AddComponent<MeshRenderer>();
                    ApplyOutlineRendererSettings(outMr, mf.sharedMesh.subMeshCount, outlineMat);
                    return outMr;
                }
                case SkinnedMeshRenderer smr:
                {
                    if (smr.sharedMesh == null) return null;

                    GameObject go = new GameObject(k_ChildPrefix + source.name);
                    go.transform.SetParent(source.transform, false);
                    go.layer = source.gameObject.layer;
                    go.hideFlags = HideFlags.DontSave;

                    SkinnedMeshRenderer outSmr = go.AddComponent<SkinnedMeshRenderer>();
                    outSmr.sharedMesh = smr.sharedMesh;
                    outSmr.bones = smr.bones;
                    outSmr.rootBone = smr.rootBone;
                    outSmr.updateWhenOffscreen = smr.updateWhenOffscreen;
                    outSmr.quality = smr.quality;
                    ApplyOutlineRendererSettings(outSmr, smr.sharedMesh.subMeshCount, outlineMat);
                    return outSmr;
                }
                default:
                    return null;
            }
        }

        static void ApplyOutlineRendererSettings(Renderer r, int subMeshCount, Material mat)
        {
            int n = Mathf.Max(1, subMeshCount);
            Material[] mats = new Material[n];
            for (int i = 0; i < n; i++) mats[i] = mat;
            r.sharedMaterials = mats;

            r.shadowCastingMode    = ShadowCastingMode.Off;
            r.receiveShadows       = false;
            r.lightProbeUsage      = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            r.allowOcclusionWhenDynamic = false;
        }
    }
}
