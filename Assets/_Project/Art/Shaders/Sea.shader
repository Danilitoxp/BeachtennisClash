// Stylised-realistic sea for mobile (URP). One pass, no depth/opaque texture, no reflection probe:
// vertex swell + two scrolling procedural normals, Fresnel between water and sky colour, Blinn sun
// glint from the main light, shallow → deep colour by distance from the shore (uv.x), distance fog.
// SRP Batcher compatible (all material properties in UnityPerMaterial).
Shader "BeachTennis/Sea"
{
    Properties
    {
        _ShallowColor ("Shallow", Color) = (0.18, 0.72, 0.72, 1)
        _DeepColor ("Deep", Color) = (0.03, 0.25, 0.42, 1)
        _SkyColor ("Sky reflection", Color) = (0.62, 0.78, 0.95, 1)
        _FoamColor ("Foam", Color) = (0.95, 0.97, 0.98, 1)
        _ShallowWidth ("Shallow width (uv)", Range(0.001, 0.5)) = 0.04
        _FoamWidth ("Foam width (uv)", Range(0, 0.05)) = 0.006
        _WaveHeight ("Swell height (m)", Range(0, 1)) = 0.12
        _WaveLength ("Swell length (m)", Range(1, 60)) = 14
        _WaveSpeed ("Swell speed", Range(0, 3)) = 0.6
        _RippleScale ("Ripple scale", Range(0.05, 4)) = 0.6
        _RippleStrength ("Ripple strength", Range(0, 1)) = 0.35
        _Glossiness ("Sun glint power", Range(8, 512)) = 160
        _GlintIntensity ("Sun glint intensity", Range(0, 4)) = 1.4
        _FresnelPower ("Fresnel power", Range(0.5, 8)) = 4
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _SkyColor;
                half4 _FoamColor;
                half _ShallowWidth;
                half _FoamWidth;
                float _WaveHeight;
                float _WaveLength;
                float _WaveSpeed;
                float _RippleScale;
                half _RippleStrength;
                half _Glossiness;
                half _GlintIntensity;
                half _FresnelPower;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Swell: two long sines along X and Z; returns height and slope (dh/dx, dh/dz).
            float3 Swell(float2 p, float t)
            {
                float k = 6.2831853 / _WaveLength;
                float a = sin(p.x * k + t * _WaveSpeed);
                float b = sin(p.y * k * 0.7 + p.x * k * 0.3 + t * _WaveSpeed * 1.3);
                float h = (a + 0.6 * b) * _WaveHeight * 0.5;
                float dx = (cos(p.x * k + t * _WaveSpeed) * k + 0.6 * cos(p.y * k * 0.7 + p.x * k * 0.3 + t * _WaveSpeed * 1.3) * k * 0.3) * _WaveHeight * 0.5;
                float dz = 0.6 * cos(p.y * k * 0.7 + p.x * k * 0.3 + t * _WaveSpeed * 1.3) * k * 0.7 * _WaveHeight * 0.5;
                return float3(h, dx, dz);
            }

            // Cheap ripple slope from a few rotated sines (no texture fetches).
            float2 Ripples(float2 p, float t)
            {
                p *= _RippleScale;
                float2 s = 0;
                s += float2(cos(p.x * 3.1 + t * 1.7), cos(p.y * 2.7 - t * 1.3)) * 0.5;
                s += float2(cos(p.x * 5.3 - p.y * 2.1 + t * 2.3), cos(p.y * 6.1 + p.x * 1.7 - t * 2.9)) * 0.3;
                s += float2(cos(p.x * 11.0 + p.y * 7.0 + t * 3.1), cos(p.y * 12.0 - p.x * 5.0 + t * 3.7)) * 0.15;
                return s;
            }

            Varyings vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = TransformObjectToWorld(input.positionOS.xyz);
                ws.y += Swell(ws.xz, _Time.y).x * saturate(input.uv.x / max(_ShallowWidth, 0.001)); // flat at the shore
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = input.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float3 swell = Swell(i.positionWS.xz, t);
                float2 ripple = Ripples(i.positionWS.xz, t) * _RippleStrength;
                float3 n = normalize(float3(-(swell.y + ripple.x), 1.0, -(swell.z + ripple.y)));

                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light sun = GetMainLight();

                half shoreT = saturate(i.uv.x / _ShallowWidth);
                half3 water = lerp(_ShallowColor.rgb, _DeepColor.rgb, shoreT);
                half fresnel = pow(1.0 - saturate(dot(n, v)), _FresnelPower);
                half3 color = lerp(water, _SkyColor.rgb, fresnel);

                // Sun glint.
                float3 h = normalize(sun.direction + v);
                half glint = pow(saturate(dot(n, h)), _Glossiness) * _GlintIntensity;
                color += sun.color * glint;
                // Scale by the sun so sunset/night presets darken the sea consistently.
                color *= lerp(0.35, 1.0, saturate(dot(float3(0, 1, 0), sun.direction) * 2.0 + 0.2)) * saturate(length(sun.color));

                // Thin foam line at the shore.
                half foam = 1.0 - smoothstep(0.0, _FoamWidth, i.uv.x + (ripple.x * 0.002));
                color = lerp(color, _FoamColor.rgb, foam * 0.85);

                color = MixFog(color, i.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
