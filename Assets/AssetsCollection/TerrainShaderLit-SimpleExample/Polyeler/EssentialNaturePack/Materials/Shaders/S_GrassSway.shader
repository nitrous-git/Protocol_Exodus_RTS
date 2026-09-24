Shader "Custom/URP_GrassWind_Fast"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Cutoff ("Cutoff", Range(0, 1)) = 0.25
        _Speed ("Speed", float) = 0.25
        _WindDirection ("Wind Direction", Vector) = (1,0,0,1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest"
            "IgnoreProjector" = "True"
        }
        LOD 150
        Cull [_Cull]

        // ==================== Forward Pass (Blinn-Phong, no shadows) ====================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _MainTex_ST;
                half   _Cutoff;
                half   _Speed;
                float4 _WindDirection;
            CBUFFER_END

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            void ApplyWind(inout float3 positionOS)
            {
                float3 windDir    = normalize(_WindDirection.xyz);
                float  weight     = positionOS.y;
                float  windOffset = weight * sin(_Time.y * _Speed);
                positionOS.xyz   += windDir * windOffset;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                ApplyWind(IN.positionOS.xyz);

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrmInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS   = nrmInputs.normalWS;
                OUT.uv         = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 frag(Varyings IN, FRONT_FACE_TYPE isFrontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * _Color;
                clip(albedo.a - _Cutoff);

                bool frontFace = IS_FRONT_VFACE(isFrontFace, true, false);
                half3 normalWS = normalize(IN.normalWS);
                //if (!frontFace) normalWS = -normalWS;

                // Blinn-Phong lighting: one texture sample, no PBR
                Light mainLight = GetMainLight();
                half3 lightDir = normalize(mainLight.direction);
                half3 viewDir  = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                half NdotL = saturate(dot(normalWS, lightDir));
                half3 diffuse = albedo.rgb * mainLight.color * NdotL;

                // Simple ambient
                half3 ambient = SampleSH(normalWS) * albedo.rgb;

                half3 finalColor = diffuse + ambient;

                return half4(finalColor, albedo.a);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}