// The surveillance monitor.
//
// Sits on the RawImage that displays a camera's RenderTexture and turns a clean
// modern render into a 1979 multiplexer feed: interlace scanlines, a vertical roll
// the sync never quite holds, chroma separation, snow, and a bloomed vignette from
// a tube that has been on since the Carter administration.
//
// _Signal is the gameplay hook. At 1 the picture is clear; as a camera's condition
// degrades the C# side drives it toward 0 and the feed dissolves into snow, so the
// same material expresses both "old equipment" and "this camera is failing".
Shader "Grotto/MonitorFeed"
{
    Properties
    {
        [PerRendererData] _MainTex("Feed", 2D) = "black" {}
        _Color("Tint", Color) = (1,1,1,1)

        _Signal("Signal Quality", Range(0,1)) = 1
        _StaticAmount("Static Amount", Range(0,1)) = 0.06
        _Flicker("Flicker", Range(0,1)) = 0.15

        _ScanlineCount("Scanline Count", Float) = 320
        _ScanlineStrength("Scanline Strength", Range(0,1)) = 0.35

        _RollSpeed("Roll Speed", Float) = 0.08
        _RollStrength("Roll Strength", Range(0,0.2)) = 0.015

        _Aberration("Chroma Separation", Range(0,0.02)) = 0.0025
        _Vignette("Vignette", Range(0,2)) = 0.85
        _Desaturate("Desaturate", Range(0,1)) = 0.55
        _Brightness("Brightness", Range(0,2)) = 1.05
        _Contrast("Contrast", Range(0,3)) = 1.25
        _PhosphorTint("Phosphor Tint", Color) = (0.82, 0.95, 0.88, 1)

        // Standard UI plumbing so this behaves inside a Canvas and respects masks.
        _StencilComp("Stencil Comparison", Float) = 8
        _Stencil("Stencil ID", Float) = 0
        _StencilOp("Stencil Operation", Float) = 0
        _StencilWriteMask("Stencil Write Mask", Float) = 255
        _StencilReadMask("Stencil Read Mask", Float) = 255
        _ColorMask("Colour Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "MonitorFeed"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            float _Signal;
            float _StaticAmount;
            float _Flicker;
            float _ScanlineCount;
            float _ScanlineStrength;
            float _RollSpeed;
            float _RollStrength;
            float _Aberration;
            float _Vignette;
            float _Desaturate;
            float _Brightness;
            float _Contrast;
            fixed4 _PhosphorTint;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.texcoord;
                float time = _Time.y;

                // Vertical roll — the sync slipping, worse on a weak signal.
                float roll = frac(time * _RollSpeed);
                float rollBand = smoothstep(0.0, 0.06, abs(frac(uv.y + roll) - 0.5) - 0.44);
                uv.y += rollBand * _RollStrength * (2.0 - _Signal);

                // Horizontal tearing on individual scanlines when the signal is poor.
                float tearNoise = Hash21(float2(floor(uv.y * _ScanlineCount), floor(time * 12.0)));
                float tear = step(0.985 + _Signal * 0.014, tearNoise);
                uv.x += tear * (tearNoise - 0.5) * 0.08 * (1.0 - _Signal);

                // Chroma separation: sample the channels at slightly different offsets.
                float separation = _Aberration * (2.0 - _Signal);
                fixed4 feed;
                feed.r = tex2D(_MainTex, uv + float2(separation, 0)).r;
                feed.g = tex2D(_MainTex, uv).g;
                feed.b = tex2D(_MainTex, uv - float2(separation, 0)).b;
                feed.a = 1.0;
                feed += _TextureSampleAdd;

                // Snow. Always a little; on a dead camera it is the whole picture.
                float snow = Hash21(uv * float2(640.0, 480.0) + frac(time) * 91.7);
                float snowMix = saturate(_StaticAmount + (1.0 - _Signal) * 1.15);
                feed.rgb = lerp(feed.rgb, snow.xxx, snowMix);

                // Interlace.
                float scanline = sin(uv.y * _ScanlineCount * 3.14159265);
                feed.rgb *= 1.0 - _ScanlineStrength * (scanline * 0.5 + 0.5);

                // Grade: desaturate toward the phosphor, then lift contrast.
                float luma = dot(feed.rgb, float3(0.299, 0.587, 0.114));
                feed.rgb = lerp(feed.rgb, luma.xxx * _PhosphorTint.rgb, _Desaturate);
                feed.rgb = saturate((feed.rgb - 0.5) * _Contrast + 0.5) * _Brightness;

                // Mains hum on the tube brightness.
                float flicker = 1.0 - _Flicker * (0.5 + 0.5 * sin(time * 37.0)) * (1.0 - _Signal * 0.6);
                feed.rgb *= flicker;

                // Vignette from the tube's curvature.
                float2 centred = i.texcoord - 0.5;
                float vignette = 1.0 - dot(centred, centred) * _Vignette;
                feed.rgb *= saturate(vignette);

                fixed4 result = feed * i.color;

                #ifdef UNITY_UI_CLIP_RECT
                result.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a - 0.001);
                #endif

                return result;
            }
            ENDCG
        }
    }
}
