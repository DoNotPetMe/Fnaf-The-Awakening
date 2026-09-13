// Triplanar-projected rock for the cave shells.
//
// The cavern meshes are noise-displaced rings whose UVs stretch badly wherever the
// surface turns steeply — which, in a cave, is everywhere. Projecting the texture
// from the three world axes and blending by the world normal sidesteps UVs entirely,
// so the rock keeps a consistent grain over overhangs, breakdown and ceiling domes.
//
// Because the projection is in world space, adjacent chambers share a continuous
// grain across their seams for free.
Shader "Grotto/CaveTriplanar"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor]   _BaseColor("Base Colour", Color) = (1,1,1,1)

        _Tiling("World Tiling (tiles per metre)", Float) = 0.35
        _BlendSharpness("Projection Sharpness", Range(1, 16)) = 4

        _Metallic("Metallic", Range(0,1)) = 0
        _Smoothness("Smoothness", Range(0,1)) = 0.12

        _DampColor("Damp Tint", Color) = (0.42, 0.48, 0.46, 1)
        _DampHeight("Damp Height (world Y)", Float) = -4
        _DampFalloff("Damp Falloff (metres)", Float) = 2.5

        _VertexColorStrength("Vertex Colour Strength", Range(0,1)) = 1

        // Declared for the shared material CBUFFER used by the shadow and depth passes.
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // One CBUFFER shared by every pass keeps the SRP Batcher compatible.
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;
            half4  _DampColor;
            float  _Tiling;
            half   _BlendSharpness;
            half   _Metallic;
            half   _Smoothness;
            float  _DampHeight;
            float  _DampFalloff;
            half   _VertexColorStrength;
            half   _Cutoff;
        CBUFFER_END

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex TriplanarVertex
            #pragma fragment TriplanarFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float2 lightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 color      : TEXCOORD2;
                float  fogCoord   : TEXCOORD3;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 4);
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings TriplanarVertex(Attributes input)
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

                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
                OUTPUT_SH(output.normalWS, output.vertexSH);

                return output;
            }

            // Samples the base map projected from all three world axes and blends by
            // the world normal, so steep faces never show UV stretching.
            half3 SampleTriplanar(float3 positionWS, float3 normalWS)
            {
                float3 blend = pow(abs(normalWS), _BlendSharpness);
                blend /= max(blend.x + blend.y + blend.z, 1e-4);

                float2 uvX = positionWS.zy * _Tiling;
                float2 uvY = positionWS.xz * _Tiling;
                float2 uvZ = positionWS.xy * _Tiling;

                half3 x = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvX).rgb;
                half3 y = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvY).rgb;
                half3 z = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvZ).rgb;

                return x * blend.x + y * blend.y + z * blend.z;
            }

            half4 TriplanarFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 normalWS = normalize(input.normalWS);
                half3 albedo = SampleTriplanar(input.positionWS, normalWS) * _BaseColor.rgb;

                // Vertex colour carries the shading the generator baked in — ceilings
                // darker than floors, mineral staining, wear on the props.
                half3 vertexTint = lerp(half3(1, 1, 1), input.color.rgb, _VertexColorStrength);
                albedo *= vertexTint;

                // Everything below the historic high-water mark is stained and wet.
                half damp = saturate((_DampHeight - input.positionWS.y) / max(_DampFalloff, 0.01));
                albedo = lerp(albedo, albedo * _DampColor.rgb, damp);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogCoord;
                inputData.vertexLighting = half3(0, 0, 0);
                inputData.bakedGI = SAMPLE_GI(input.lightmapUV, input.vertexSH, normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.metallic = _Metallic;
                // Wet rock is shinier rock.
                surfaceData.smoothness = lerp(_Smoothness, saturate(_Smoothness + 0.45), damp);
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.occlusion = 1.0h;
                surfaceData.alpha = 1.0h;
                surfaceData.emission = half3(0, 0, 0);
                surfaceData.specular = half3(0, 0, 0);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0h;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
