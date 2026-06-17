Shader "Hemiao/JumpFloodOutlineBlit"
{
    Properties
    {
        [HideInInspector] _JFOOutlineMaskTex ("Outline Mask", 2D) = "black" {}
        [HideInInspector] _JFOInsideMaskTex ("Inside Mask", 2D) = "black" {}
        [HideInInspector] _JFOTextureSize ("Texture Size", Vector) = (1920, 1080, 0, 0)
        [HideInInspector] _StepSize ("Step Size", Float) = 1
        [HideInInspector] _OutlineWidth ("Outline Width", Float) = 6
        [HideInInspector] _EdgeSoftness ("Edge Softness", Float) = 1.2
        [HideInInspector] _OutlineTint ("Outline Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _DepthOcclusion ("Depth Occlusion", Float) = 1
        [HideInInspector] _DepthOcclusionBias ("Depth Occlusion Bias", Float) = 0.0002
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    TEXTURE2D(_JFOOutlineMaskTex);
    SAMPLER(sampler_JFOOutlineMaskTex);
    TEXTURE2D(_JFOInsideMaskTex);
    SAMPLER(sampler_JFOInsideMaskTex);

    CBUFFER_START(UnityPerMaterial)
        float4 _JFOTextureSize;
        float  _StepSize;
        float  _OutlineWidth;
        float  _EdgeSoftness;
        float4 _OutlineTint;
        float  _DepthOcclusion;
        float  _DepthOcclusionBias;
    CBUFFER_END

    static const float2 kEmptySeed = float2(-1.0, -1.0);
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "JFAInit"
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half inside = SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv).r;
                if (inside > 0.5)
                    return half4(uv.x, uv.y, 0.0, 1.0);
                return half4(kEmptySeed.x, kEmptySeed.y, 0.0, 0.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "JFAStep"
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 invSize = 1.0 / max(_JFOTextureSize.xy, float2(1.0, 1.0));
                float2 texel = invSize * _StepSize;
                float2 bestSeed = kEmptySeed;
                float  bestDist = 1.0e20;

                UNITY_UNROLL
                for (int y = -1; y <= 1; ++y)
                {
                    UNITY_UNROLL
                    for (int x = -1; x <= 1; ++x)
                    {
                        float2 sampleUV = uv + float2(x, y) * texel;
                        if (any(sampleUV < 0.0) || any(sampleUV > 1.0))
                            continue;

                        float4 s = SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, sampleUV);
                        float2 seedUV = s.xy;
                        if (seedUV.x < 0.0) continue;

                        float2 diffPx = (seedUV - uv) * _JFOTextureSize.xy;
                        float  d = dot(diffPx, diffPx);
                        if (d < bestDist)
                        {
                            bestDist = d;
                            bestSeed = seedUV;
                        }
                    }
                }

                if (bestSeed.x < 0.0)
                    return half4(kEmptySeed.x, kEmptySeed.y, 0.0, 0.0);
                return half4(bestSeed.x, bestSeed.y, 0.0, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Composite"
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                // 剪影内部：不画任何东西，保留模型原色
                half inside = SAMPLE_TEXTURE2D(_JFOInsideMaskTex, sampler_PointClamp, uv).r;
                if (inside > 0.5)
                    discard;

                float4 seed = SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv);
                float2 seedUV = seed.xy;
                if (seedUV.x < 0.0)
                    discard;

                float2 diffPx = (seedUV - uv) * _JFOTextureSize.xy;
                float  distPx = length(diffPx);
                if (distPx < 0.5 || distPx > _OutlineWidth)
                    discard;

                if (_DepthOcclusion > 0.5)
                {
                    float depthAtPixel = SampleSceneDepth(uv);
                    float depthAtSeed  = SampleSceneDepth(seedUV);
                    #if UNITY_REVERSED_Z
                        if (depthAtPixel > depthAtSeed + _DepthOcclusionBias)
                            discard;
                    #else
                        if (depthAtPixel < depthAtSeed - _DepthOcclusionBias)
                            discard;
                    #endif
                }

                half4 col = SAMPLE_TEXTURE2D(_JFOOutlineMaskTex, sampler_JFOOutlineMaskTex, seedUV);
                col.rgb *= _OutlineTint.rgb;

                float soft = max(_EdgeSoftness, 0.0001);
                float alpha = saturate((_OutlineWidth - distPx) / soft);
                return half4(col.rgb, alpha * _OutlineTint.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
