// UV-mapped lit surface that actually uses the vertex data the generators produce.
//
// Everything in this game is generated, and MeshBuilder writes two things into every
// vertex that URP's own Lit shader has nowhere to put:
//
//   * RGB — the per-vertex tint the factories bake in. Wear on a character's shell,
//     mineral staining on a prop, the darker underside of a deck. Without a shader
//     that reads it, a whole layer of authored variation is computed and thrown away,
//     and every steel fitting in the building is exactly the same colour.
//
//   * Alpha — the curvature occlusion baked by MeshBuilder.BakeVertexOcclusion. On a
//     character that is the darkening in the joint creases, the eye sockets and the
//     gaps between shell plates, which is most of what stops a generated model from
//     looking like injection-moulded plastic.
//
// It also carries the same derivative-based detail normal as the cave shader, so a
// rusted steel plate has relief under a moving lamp rather than being a flat decal.
//
// This is deliberately not a Shader Graph. A .shadergraph is a large binary-ish JSON
// blob that cannot be reviewed in a diff and cannot be edited without opening Unity,
// which is the whole thing this project is arranged to avoid.
Shader "Grotto/SurfaceLit"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor]   _BaseColor("Base Colour", Color) = (1,1,1,1)

        _Metallic("Metallic", Range(0,1)) = 0
        _Smoothness("Smoothness", Range(0,1)) = 0.3

        _VertexColorStrength("Vertex Colour Strength", Range(0,1)) = 1
        _OcclusionStrength("Baked Occlusion Strength", Range(0,1)) = 1

        _DetailStrength("Detail Relief", Range(0,3)) = 0.8

        _EmissionColor("Emission", Color) = (0,0,0,0)

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

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;
            half4  _EmissionColor;
            half   _Metallic;
            half   _Smoothness;
            half   _VertexColorStrength;
            half   _OcclusionStrength;
            half   _DetailStrength;
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
            #pragma vertex SurfaceVertex
            #pragma fragment SurfaceFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                float2 lightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float4 color      : TEXCOORD3;
                float  fogCoord   : TEXCOORD4;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 5);
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings SurfaceVertex(Attributes input)
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
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                output.fogCoord = ComputeFogFactor(positions.positionCS.z);

                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
                OUTPUT_SH(output.normalWS, output.vertexSH);

                return output;
            }

            // Derivative bump mapping: the luminance of the albedo is treated as a
            // height field and its screen-space gradient projected onto the surface.
            // Same technique as the cave shader; see CaveTriplanar.shader for the long
            // version of why this beats shipping a normal map we would have to commit
            // as a binary.
            float3 PerturbNormal(float3 normalWS, float3 positionWS, half3 sampled)
            {
                float height = dot(sampled, half3(0.299, 0.587, 0.114));

                float dhdx = ddx(height);
                float dhdy = ddy(height);

                float3 dpdx = ddx(positionWS);
                float3 dpdy = ddy(positionWS);

                float3 r1 = cross(dpdy, normalWS);
                float3 r2 = cross(normalWS, dpdx);
                float determinant = dot(dpdx, r1);

                float3 gradient = (r1 * dhdx + r2 * dhdy) / max(abs(determinant), 1e-6);

                return normalize(normalWS - gradient * _DetailStrength * 0.05);
            }

            half4 SurfaceFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 sampled = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);

                half3 albedo = sampled.rgb * _BaseColor.rgb;
                albedo *= lerp(half3(1, 1, 1), input.color.rgb, _VertexColorStrength);

                half occlusion = lerp(1.0h, input.color.a, _OcclusionStrength);

                float3 geometricNormalWS = normalize(input.normalWS);
                float3 normalWS = PerturbNormal(geometricNormalWS, input.positionWS, sampled.rgb);

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
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.occlusion = occlusion;
                surfaceData.alpha = 1.0h;
                surfaceData.emission = _EmissionColor.rgb;
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
            #pragma multi_compile_instancing

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
            #pragma multi_compile_instancing

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
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
