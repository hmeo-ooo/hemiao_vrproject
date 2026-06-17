using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Hemiao.Rendering
{
    /// <summary>
    /// 把 <see cref="JumpFloodOutlinePass"/> 注入 URP Renderer。
    /// </summary>
    public class JumpFloodOutlineFeature : ScriptableRendererFeature
    {
        /// <summary>物品描边系统总开关。当前已停用。</summary>
        public const bool SystemEnabled = false;

        [System.Serializable]
        public class Settings
        {
            [Tooltip("AfterRenderingTransparents：在透明物体之后合成，相机 RT 最稳定。")]
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;

            [Tooltip("描边像素宽度（屏幕空间）。")]
            [Range(1f, 32f)] public float outlineWidthPx = 6f;

            [Tooltip("描边边缘软过渡像素数。")]
            [Range(0f, 6f)] public float edgeSoftnessPx = 1.2f;

            [ColorUsage(true, true)] public Color globalTint = Color.white;

            [Tooltip("半分辨率运行 Mask/JFA，更快但略糊。")]
            public bool downsampleHalf = false;

            [Tooltip("被其他物体挡住的区域不画描边（依赖场景深度）。")]
            public bool depthOcclusion = true;

            [Tooltip("深度遮挡容差，描边仍穿模时可略增大。")]
            [Range(0f, 0.01f)] public float depthOcclusionBias = 0.0002f;

            public Shader maskShader;
            public Shader blitShader;

            [Tooltip("勾选后在 Console 打印每帧 Pass 是否执行、注册数量等（仅调试用）。")]
            public bool debugLog;
        }

        public Settings settings = new Settings();

        Material m_MaskMaterial;
        Material m_BlitMaterial;
        JumpFloodOutlinePass m_Pass;
        static bool s_WarnedPassMissing;

        public override void Create()
        {
            RecreatePass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!SystemEnabled)
                return;

            if (m_Pass == null)
                RecreatePass();

            if (m_Pass == null)
            {
                WarnPassMissingOnce();
                return;
            }

            m_Pass.renderPassEvent = settings.renderPassEvent;
            m_Pass.OutlineWidth = settings.outlineWidthPx;
            m_Pass.EdgeSoftness = settings.edgeSoftnessPx;
            m_Pass.GlobalTint = settings.globalTint;
            m_Pass.DownsampleHalf = settings.downsampleHalf;
            m_Pass.DepthOcclusion = settings.depthOcclusion;
            m_Pass.DepthOcclusionBias = settings.depthOcclusionBias;
            m_Pass.DebugLog = settings.debugLog;

            renderer.EnqueuePass(m_Pass);
        }

        void RecreatePass()
        {
            EnsureMaterials();
            if (m_MaskMaterial == null || m_BlitMaterial == null)
            {
                m_Pass = null;
                return;
            }

            if (m_Pass == null)
                m_Pass = new JumpFloodOutlinePass(m_MaskMaterial, m_BlitMaterial);
        }

        void WarnPassMissingOnce()
        {
            if (s_WarnedPassMissing) return;
            s_WarnedPassMissing = true;
            Debug.LogError(
                "[JumpFloodOutlineFeature] 描边 Pass 未创建：Mask/Blit Shader 或材质缺失。" +
                "请在 URP Renderer Feature 上指定 JumpFloodOutlineMask / JumpFloodOutlineBlit，" +
                "并检查 Console 是否有 Shader 编译错误。");
        }

        protected override void Dispose(bool disposing)
        {
            m_Pass?.Dispose();
            m_Pass = null;

            if (m_MaskMaterial != null) CoreUtils.Destroy(m_MaskMaterial);
            if (m_BlitMaterial != null) CoreUtils.Destroy(m_BlitMaterial);
            m_MaskMaterial = null;
            m_BlitMaterial = null;
        }

        void EnsureMaterials()
        {
            if (m_MaskMaterial == null)
            {
                Shader sh = ResolveShader(
                    settings.maskShader,
                    "Hemiao/JumpFloodOutlineMask",
                    "Assets/Shaders/JumpFloodOutlineMask.shader");
                if (sh != null) m_MaskMaterial = CoreUtils.CreateEngineMaterial(sh);
            }

            if (m_BlitMaterial == null)
            {
                Shader sh = ResolveShader(
                    settings.blitShader,
                    "Hemiao/JumpFloodOutlineBlit",
                    "Assets/Shaders/JumpFloodOutlineBlit.shader");
                if (sh != null) m_BlitMaterial = CoreUtils.CreateEngineMaterial(sh);
            }
        }

        static Shader ResolveShader(Shader assigned, string shaderName, string assetPath)
        {
            if (assigned != null) return assigned;

            Shader found = Shader.Find(shaderName);
            if (found != null) return found;

#if UNITY_EDITOR
            found = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
            if (found != null) return found;
#endif
            return null;
        }
    }
}
