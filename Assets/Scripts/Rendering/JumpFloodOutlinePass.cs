using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Hemiao.Rendering
{
    internal class JumpFloodOutlinePass : ScriptableRenderPass
    {
        const string k_ProfilerTag = "Jump Flood Outline";
        const int k_MaxStepIterations = 12;
        const int k_PassColor = 0;
        const int k_PassInsideSeed = 1;
        const int k_PassSilhouette = 2;

        readonly Material m_MaskMaterial;
        readonly Material m_BlitMaterial;
        readonly MaterialPropertyBlock m_BlitMpb = new MaterialPropertyBlock();

        public float OutlineWidth = 6f;
        public float EdgeSoftness = 1.2f;
        public Color GlobalTint = Color.white;
        public bool DownsampleHalf = false;
        public bool DepthOcclusion = true;
        public float DepthOcclusionBias = 0.0002f;
        public bool DebugLog;

        RTHandle m_ColorRT;
        RTHandle m_SilhouetteRT;
        RTHandle m_SeedRT;
        RTHandle m_SeedPing;
        RTHandle m_SeedPong;

        static readonly int s_BlitTexId = Shader.PropertyToID("_BlitTexture");
        static readonly int s_BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
        static readonly int s_TexSizeId = Shader.PropertyToID("_JFOTextureSize");
        static readonly int s_MaskTexId = Shader.PropertyToID("_JFOOutlineMaskTex");
        static readonly int s_InsideTexId = Shader.PropertyToID("_JFOInsideMaskTex");
        static readonly int s_StepSizeId = Shader.PropertyToID("_StepSize");
        static readonly int s_OutlineW = Shader.PropertyToID("_OutlineWidth");
        static readonly int s_EdgeSoft = Shader.PropertyToID("_EdgeSoftness");
        static readonly int s_OutlineTint = Shader.PropertyToID("_OutlineTint");
        static readonly int s_OutlineColorId = Shader.PropertyToID("_OutlineColor");
        static readonly int s_DepthOccId = Shader.PropertyToID("_DepthOcclusion");
        static readonly int s_DepthBiasId = Shader.PropertyToID("_DepthOcclusionBias");

        static readonly Vector4 s_FullScreenBias = new Vector4(1f, 1f, 0f, 0f);

        public JumpFloodOutlinePass(Material maskMat, Material blitMat)
        {
            m_MaskMaterial = maskMat;
            m_BlitMaterial = blitMat;
            profilingSampler = new ProfilingSampler(k_ProfilerTag);
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public void Dispose()
        {
            m_ColorRT?.Release();
            m_SilhouetteRT?.Release();
            m_SeedRT?.Release();
            m_SeedPing?.Release();
            m_SeedPong?.Release();
            m_ColorRT = m_SilhouetteRT = m_SeedRT = m_SeedPing = m_SeedPong = null;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            int w = Mathf.Max(1, desc.width);
            int h = Mathf.Max(1, desc.height);
            if (DownsampleHalf) { w = Mathf.Max(1, w / 2); h = Mathf.Max(1, h / 2); }

            var rtDesc = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 0, 0)
            {
                msaaSamples = 1, useMipMap = false, autoGenerateMips = false, sRGB = false,
            };
            RenderingUtils.ReAllocateIfNeeded(ref m_ColorRT, rtDesc, FilterMode.Point, TextureWrapMode.Clamp, name: "_JFOColor");
            RenderingUtils.ReAllocateIfNeeded(ref m_SilhouetteRT, rtDesc, FilterMode.Point, TextureWrapMode.Clamp, name: "_JFOSilhouette");
            RenderingUtils.ReAllocateIfNeeded(ref m_SeedRT, rtDesc, FilterMode.Point, TextureWrapMode.Clamp, name: "_JFOSeed");

            var seedDesc = new RenderTextureDescriptor(w, h, RenderTextureFormat.RGHalf, 0, 0)
            {
                msaaSamples = 1, useMipMap = false, autoGenerateMips = false, sRGB = false,
            };
            RenderingUtils.ReAllocateIfNeeded(ref m_SeedPing, seedDesc, FilterMode.Point, TextureWrapMode.Clamp, name: "_JFOPing");
            RenderingUtils.ReAllocateIfNeeded(ref m_SeedPong, seedDesc, FilterMode.Point, TextureWrapMode.Clamp, name: "_JFOPong");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (m_MaskMaterial == null || m_BlitMaterial == null) return;
            if (m_ColorRT == null || m_SilhouetteRT == null || m_SeedRT == null) return;

            var camType = renderingData.cameraData.cameraType;
            if (camType == CameraType.Preview || camType == CameraType.Reflection) return;
            if (Application.isPlaying && camType != CameraType.Game) return;

            var entries = JumpFloodOutlineRegistry.GetSnapshot();
            if (entries.Count == 0)
            {
                if (DebugLog)
                    Debug.Log("[JumpFloodOutline] 跳过：注册数=0");
                return;
            }

            var renderer = renderingData.cameraData.renderer;
            var cameraColor = renderer.cameraColorTargetHandle;
            if (cameraColor?.rt == null)
            {
                if (DebugLog)
                    Debug.Log("[JumpFloodOutline] 跳过：相机颜色 RT 无效");
                return;
            }

            var cam = renderingData.cameraData.camera;
            var cmd = CommandBufferPool.Get(k_ProfilerTag);

            using (new ProfilingScope(cmd, profilingSampler))
            {
                int draws = DrawMasks(cmd, cam, entries);
                if (draws == 0)
                {
                    if (DebugLog)
                        Debug.Log($"[JumpFloodOutline] 跳过：Mask 绘制=0 注册={entries.Count}");
                    context.ExecuteCommandBuffer(cmd);
                    CommandBufferPool.Release(cmd);
                    return;
                }

                int maxDim = Mathf.Max(m_ColorRT.rt.width, m_ColorRT.rt.height);
                int jfaPasses = Mathf.Min(k_MaxStepIterations,
                    Mathf.CeilToInt(Mathf.Log(Mathf.Max(OutlineWidth, 1f), 2f)) + 1);

                RTHandle jfaResult = RunJumpFlood(cmd, jfaPasses, maxDim);
                Composite(cmd, jfaResult, cameraColor);

                if (DebugLog)
                {
                    Debug.Log(
                        $"[JumpFloodOutline] OK 相机={cam.name} 注册={entries.Count} 绘制={draws} " +
                        $"尺寸={m_ColorRT.rt.width}x{m_ColorRT.rt.height} 宽={OutlineWidth}px");
                }
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        int DrawMasks(CommandBuffer cmd, Camera cam, List<JumpFloodOutlineRegistry.Entry> entries)
        {
            cmd.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);
            cmd.SetViewport(new Rect(0, 0, m_ColorRT.rt.width, m_ColorRT.rt.height));

            // 全部 ZTest Always，避免 MSAA 深度不匹配导致 mask 画不出来
            m_MaskMaterial.SetInt(Shader.PropertyToID("_ZTestMode"), (int)CompareFunction.Always);

            CoreUtils.SetRenderTarget(cmd, m_SilhouetteRT, ClearFlag.Color, Color.clear);
            int n = DrawAll(cmd, entries, k_PassSilhouette);

            CoreUtils.SetRenderTarget(cmd, m_SeedRT, ClearFlag.Color, Color.clear);
            n = Mathf.Max(n, DrawAll(cmd, entries, k_PassInsideSeed));

            CoreUtils.SetRenderTarget(cmd, m_ColorRT, ClearFlag.Color, Color.clear);
            n = Mathf.Max(n, DrawAll(cmd, entries, k_PassColor));

            return n;
        }

        int DrawAll(CommandBuffer cmd, List<JumpFloodOutlineRegistry.Entry> entries, int pass)
        {
            int n = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                var rd = entries[i].Renderer;
                if (rd == null || !rd.enabled || !rd.gameObject.activeInHierarchy) continue;

                m_MaskMaterial.SetColor(s_OutlineColorId, entries[i].Color);

                if (rd is MeshRenderer mr)
                {
                    var mf = mr.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var mesh = mf.sharedMesh;
                    var mtx = mr.localToWorldMatrix;
                    int subs = Mathf.Max(1, mesh.subMeshCount);
                    for (int s = 0; s < subs; s++) { cmd.DrawMesh(mesh, mtx, m_MaskMaterial, s, pass); n++; }
                }
                else if (rd is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                {
                    int subs = Mathf.Max(1, smr.sharedMesh.subMeshCount);
                    for (int s = 0; s < subs; s++) { cmd.DrawRenderer(smr, m_MaskMaterial, s, pass); n++; }
                }
                else
                {
                    cmd.DrawRenderer(rd, m_MaskMaterial, 0, pass);
                    n++;
                }
            }
            return n;
        }

        RTHandle RunJumpFlood(CommandBuffer cmd, int passes, int maxDim)
        {
            Blit(cmd, m_SeedRT, m_SeedPing, 0, mpb =>
            {
                SetTexSize(mpb, m_SeedRT);
            });

            RTHandle src = m_SeedPing, dst = m_SeedPong;
            for (int i = 0; i < passes; i++)
            {
                int step = Mathf.Min(1 << (passes - 1 - i), maxDim);
                Blit(cmd, src, dst, 1, mpb =>
                {
                    mpb.SetFloat(s_StepSizeId, step);
                    SetTexSize(mpb, src);
                });
                (src, dst) = (dst, src);
            }
            return src;
        }

        void Composite(CommandBuffer cmd, RTHandle jfa, RTHandle target)
        {
            Blit(cmd, jfa, target, 2, mpb =>
            {
                mpb.SetTexture(s_MaskTexId, m_ColorRT);
                mpb.SetTexture(s_InsideTexId, m_SilhouetteRT);
                mpb.SetFloat(s_OutlineW, OutlineWidth);
                mpb.SetFloat(s_EdgeSoft, Mathf.Max(EdgeSoftness, 0.001f));
                mpb.SetColor(s_OutlineTint, GlobalTint);
                mpb.SetFloat(s_DepthOccId, DepthOcclusion ? 1f : 0f);
                mpb.SetFloat(s_DepthBiasId, DepthOcclusionBias);
                SetTexSize(mpb, jfa);
            }, RenderBufferLoadAction.Load);
        }

        void Blit(CommandBuffer cmd, RTHandle source, RTHandle dest, int pass,
            System.Action<MaterialPropertyBlock> setup, RenderBufferLoadAction load = RenderBufferLoadAction.DontCare)
        {
            m_BlitMpb.Clear();
            m_BlitMpb.SetTexture(s_BlitTexId, source);
            m_BlitMpb.SetVector(s_BlitScaleBiasId, s_FullScreenBias);
            setup?.Invoke(m_BlitMpb);

            cmd.SetRenderTarget(dest, load, RenderBufferStoreAction.Store);
            cmd.DrawProcedural(Matrix4x4.identity, m_BlitMaterial, pass, MeshTopology.Triangles, 3, 1, m_BlitMpb);
        }

        static void SetTexSize(MaterialPropertyBlock mpb, RTHandle rt)
        {
            if (rt?.rt == null) return;
            mpb.SetVector(s_TexSizeId, new Vector4(rt.rt.width, rt.rt.height, 0f, 0f));
        }
    }
}
