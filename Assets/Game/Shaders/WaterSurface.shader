// The water table.
//
// A flat plane that has to read as deep, still, cold water under almost no light.
// Fresnel does the heavy lifting: at grazing angles the surface goes reflective and
// opaque, straight down it goes dark and clear, which is what makes a single quad
// read as a body of water rather than as a sheet of glass.
//
// Two scrolling noise layers at different rates perturb the normal so the surface
// moves without needing a normal map or a wave simulation. _Agitation is driven from
// C# by WaterLevelDriver, so the water visibly picks up when the pump is running.
Shader "Grotto/WaterSurface"
{
    Properties
    {
        [MainTexture] _BaseMap("Ripple Noise", 2D) = "gray" {}
        [MainColor]   _BaseColor("Shallow Colour", Color) = (0.10, 0.28, 0.30, 0.75)
        _DeepColor("Deep Colour", Color) = (0.01, 0.05, 0.07, 0.97)

        _FresnelPower("Fresnel Power", Range(0.5, 8)) = 3.5
        _Smoothness("Smoothness", Range(0,1)) = 0.94

        _RippleScale("Ripple Scale", Float) = 0.35
        _RippleStrength("Ripple Strength", Range(0, 0.5)) = 0.12
        _ScrollSpeed("Scroll Speed", Float) = 0.035
        _Agitation("Agitation", Range(0, 3)) = 1

        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex WaterVertex
            #pragma fragment WaterFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half4  _DeepColor;
                half   _FresnelPower;
                half   _Smoothness;
                float  _RippleScale;
                half   _RippleStrength;
                float  _ScrollSpeed;
                half   _Agitation;
                half   _Cutoff;
            CBUFFER_END

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 color      : TEXCOORD2;
                float  fogCoord   : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings WaterVertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = normals.normalWS;
                output.color = input.color;
                output.fogCoord = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            // Central-difference the noise to get a slope, and tilt the normal by it.
            half3 RippleNormal(float2 uv, float time)
            {
                float2 driftA = float2(time * _ScrollSpeed, time * _ScrollSpeed * 0.73);
                float2 driftB = float2(-time * _ScrollSpeed * 0.61, time * _ScrollSpeed * 0.44);

                const float2 step = float2(0.004, 0.0);

                half hL = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv - step.xy + driftA).r
                        + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, (uv - step.xy) * 1.7 + driftB).r;
                half hR = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + step.xy + driftA).r
                        + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, (uv + step.xy) * 1.7 + driftB).r;
                half hD = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv - step.yx + driftA).r
                        + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, (uv - step.yx) * 1.7 + driftB).r;
                half hU = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + step.yx + driftA).r
                        + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, (uv + step.yx) * 1.7 + driftB).r;

                half strength = _RippleStrength * _Agitation;
                return normalize(half3((hL - hR) * strength, 1.0, (hD - hU) * strength));
            }

            half4 WaterFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.positionWS.xz * _RippleScale;
                half3 rippled = RippleNormal(uv, _Time.y);

                // The plane is always horizontal, so the ripple normal can be used
                // directly in world space rather than needing a tangent basis.
                float3 normalWS = normalize(half3(rippled.x, rippled.y, rippled.z));
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                half facing = saturate(dot(normalWS, viewDirWS));
                half fresnel = pow(1.0h - facing, _FresnelPower);

                half4 water = lerp(_DeepColor, _BaseColor, fresnel);
                water.rgb *= input.color.rgb;
                water.a = saturate(lerp(_DeepColor.a, _BaseColor.a, 1.0h - fresnel));

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = viewDirWS;
                inputData.shadowCoord = float4(0, 0, 0, 0);
                inputData.fogCoord = input.fogCoord;
                inputData.vertexLighting = half3(0, 0, 0);
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = water.rgb;
                surfaceData.metallic = 0.0h;
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.occlusion = 1.0h;
                surfaceData.alpha = water.a;
                surfaceData.emission = half3(0, 0, 0);
                surfaceData.specular = half3(0, 0, 0);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = water.a;
                return color;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
