Shader "Hemiao/JumpFloodOutlineMask"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTestMode ("ZTest", Float) = 4
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        // Pass 0：描边取色（仅可见表面）
        Pass
        {
            Name "ColorMask"
            ZWrite Off
            ZTest [_ZTestMode]
            Cull Off
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragColor
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half4 fragColor(Varyings input) : SV_Target
            {
                return half4(_OutlineColor.rgb, 1.0);
            }
            ENDHLSL
        }

        // Pass 1：可见表面种子（JFA 从此向外扩散）
        Pass
        {
            Name "InsideSeed"
            ZWrite Off
            ZTest [_ZTestMode]
            Cull Off
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragInside
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half4 fragInside(Varyings input) : SV_Target
            {
                return half4(1.0, 0.0, 0.0, 1.0);
            }
            ENDHLSL
        }

        // Pass 2：背面剪影（填满 2D 轮廓，合成时剔除内部）
        Pass
        {
            Name "InsideSilhouette"
            ZWrite Off
            ZTest Always
            Cull Front
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragSilhouette
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half4 fragSilhouette(Varyings input) : SV_Target
            {
                return half4(1.0, 0.0, 0.0, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
