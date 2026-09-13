// Full-screen condition overlay for the control room.
//
// One material expresses everything the player's own state does to what they see:
// bad-air hallucination, the adrenaline spike from a scare, and the desaturated
// tunnel of a blackout.
//
// It deliberately does not distort the scene behind it. A Canvas overlay cannot
// sample the frame buffer without a grab pass or a renderer feature, and both cost
// more than this effect is worth. Instead it composites its own creeping structures —
// drifting cellular veins, per-pixel grain, a breathing vignette — which reads as
// something wrong with the *viewer* rather than with the room, and that is the more
// useful lie.
Shader "Grotto/StationOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite", 2D) = "white" {}
        _Color("Tint", Color) = (1,1,1,1)

        _Hallucination("Hallucination", Range(0,1)) = 0
        _Scare("Scare", Range(0,1)) = 0
        _Blackout("Blackout", Range(0,1)) = 0

        _GrainAmount("Grain", Range(0,1)) = 0.25
        _VeinScale("Vein Scale", Float) = 7
        _VeinSpeed("Vein Speed", Float) = 0.12
        _VignetteStrength("Vignette", Range(0,3)) = 1.3
        _PulseSpeed("Pulse Speed", Float) = 1.4

        _HallucinationColor("Hallucination Colour", Color) = (0.35, 0.62, 0.45, 1)
        _ScareColor("Scare Colour", Color) = (0.75, 0.10, 0.12, 1)

        _StencilComp("Stencil Comparison", Float) = 8
        _Stencil("Stencil ID", Float) = 0
        _StencilOp("Stencil Operation", Float) = 0
        _StencilWriteMask("Stencil Write Mask", Float) = 255
        _StencilReadMask("Stencil Read Mask", Float) = 255
        _ColorMask("Colour Mask", Float) = 15
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
            Name "StationOverlay"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

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

            fixed4 _Color;
            float4 _ClipRect;

            float _Hallucination;
            float _Scare;
            float _Blackout;
            float _GrainAmount;
            float _VeinScale;
            float _VeinSpeed;
            float _VignetteStrength;
            float _PulseSpeed;
            fixed4 _HallucinationColor;
            fixed4 _ScareColor;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.texcoord;
                float time = _Time.y;

                float2 centred = uv - 0.5;
                float radial = length(centred);

                fixed3 rgb = 0;
                float alpha = 0;

                // ---- Hallucination: veins creeping in from the edges --------------
                if (_Hallucination > 0.001)
                {
                    float2 drift = float2(time * _VeinSpeed, -time * _VeinSpeed * 0.7);
                    float veins = ValueNoise(uv * _VeinScale + drift);
                    veins = abs(veins - 0.5) * 2.0;
                    veins = 1.0 - smoothstep(0.0, 0.35, veins);

                    // They only take hold in the periphery, which is where the eye
                    // is least able to argue with them.
                    float periphery = smoothstep(0.16, 0.55, radial);
                    float amount = veins * periphery * _Hallucination;

                    rgb += _HallucinationColor.rgb * amount;
                    alpha = max(alpha, amount * 0.85);
                }

                // ---- Scare: a hard red pulse that decays ---------------------------
                if (_Scare > 0.001)
                {
                    float pulse = 0.55 + 0.45 * sin(time * _PulseSpeed * 8.0);
                    float edge = smoothstep(0.08, 0.62, radial);
                    float amount = _Scare * edge * pulse;

                    rgb += _ScareColor.rgb * amount;
                    alpha = max(alpha, amount * 0.8);
                }

                // ---- Blackout: everything closes to a tunnel ----------------------
                if (_Blackout > 0.001)
                {
                    float tunnel = smoothstep(0.05, 0.5, radial) * _Blackout;
                    rgb = lerp(rgb, fixed3(0, 0, 0), tunnel);
                    alpha = max(alpha, tunnel);
                }

                // ---- Grain, scaled by how bad things are --------------------------
                float pressure = max(max(_Hallucination, _Scare), _Blackout);
                if (_GrainAmount > 0.001 && pressure > 0.001)
                {
                    float grain = Hash21(uv * float2(1920, 1080) + frac(time) * 137.0) - 0.5;
                    float amount = _GrainAmount * pressure;
                    rgb += grain * amount;
                    alpha = max(alpha, abs(grain) * amount);
                }

                // ---- Breathing vignette ------------------------------------------
                float breath = 1.0 + 0.12 * sin(time * _PulseSpeed);
                float vignette = smoothstep(0.25, 0.72, radial) * _VignetteStrength * pressure * breath;
                alpha = max(alpha, saturate(vignette) * 0.7);

                fixed4 result = fixed4(saturate(rgb), saturate(alpha)) * i.color;

                #ifdef UNITY_UI_CLIP_RECT
                result.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif

                return result;
            }
            ENDCG
        }
    }
}
