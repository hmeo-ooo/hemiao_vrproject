Shader "Hemiao/ItemOutlineHull"
{
    // 反向法线挤出 (Inverted Hull) 描边。
    // 通过 Cull Front + 沿世界法线扩张顶点，渲染出贴身的等宽轮廓。
    // _OutlineWidth 单位为世界空间米。
    Properties
    {
        [HDR]_OutlineColor ("Outline Color", Color) = (1, 1, 1, 1)
        _OutlineWidth     ("Outline Width (meters)", Float) = 0.005
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry+10"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ItemOutlineHull"
            Tags { "LightMode" = "UniversalForward" }

            Cull Front
            ZWrite On
            ZTest LEqual
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float  _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // 世界空间挤出，保证不同缩放下描边宽度一致（以米计）。
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(input.normalOS);

                // 防御退化：法线接近零时回退到 (0,1,0)，避免顶点塌陷。
                float  lenSq = dot(normalWS, normalWS);
                normalWS = (lenSq > 1e-8) ? normalWS * rsqrt(lenSq) : float3(0, 1, 0);

                positionWS += normalWS * max(_OutlineWidth, 0.0);
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return _OutlineColor;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
