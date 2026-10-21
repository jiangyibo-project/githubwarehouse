Shader "Ruins/Metal"
{
    Properties
    {
        _Color ("金属色", Color) = (0.29, 0.37, 0.55, 1)
        _NormalMap ("法线贴图", 2D) = "bump" {}
        _NormalStrength ("法线强度", Range(0, 2)) = 1
        _Tiling ("贴图平铺", Float) = 2
        _Smoothness ("光滑度", Range(0, 1)) = 0.7
        _SpecPower ("高光锐度", Float) = 48
        _SpecIntensity ("高光强度", Range(0, 4)) = 1.6
        _ReflectionStrength ("环境反射强度", Range(0, 2)) = 0.9
        _DiffuseStrength ("漫反射强度", Range(0, 1)) = 0.35
        _FresnelColor ("边缘光色", Color) = (0.29, 0.37, 0.55, 1)
        _FresnelPower ("边缘光锐度", Range(0.5, 8)) = 3
        _ShadowColor ("冷色阴影", Color) = (0.18, 0.23, 0.36, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 300

        CGINCLUDE
        #include "UnityCG.cginc"
        #include "Lighting.cginc"
        #include "AutoLight.cginc"

        // 阴影坐标自己声明：AutoLight 的 SHADOW_COORDS / TRANSFER_SHADOW 是条件宏
        //（只在 SHADOWS_SCREEN（第 17-53 行）、SHADOWS_DEPTH+SPOT（第 238 行）、SHADOWS_CUBE（第 247 行）下定义），
        // 而 CGINCLUDE 会被注入每一个 Pass——ShadowCaster 的 SHADOWS_DEPTH 单独变体、以及描边 Pass 都不满足上述条件。
        // 这里直接声明 AutoLight 内部宏读取的 _ShadowCoord 成员，任何 Pass/变体都能编译。
        #define RUINS_SHADOW_COORDS(idx1) unityShadowCoord4 _ShadowCoord : TEXCOORD##idx1;
        #if defined(SHADOWS_SCREEN)
        #   define RUINS_TRANSFER_SHADOW(a) a._ShadowCoord = ComputeScreenPos(a.pos);
        #else
        #   define RUINS_TRANSFER_SHADOW(a)
        #endif

        sampler2D _NormalMap;
        float4 _NormalMap_ST;
        fixed4 _Color;
        float _NormalStrength;
        float _Tiling;
        float _Smoothness;
        float _SpecPower;
        float _SpecIntensity;
        float _ReflectionStrength;
        float _DiffuseStrength;
        fixed4 _FresnelColor;
        float _FresnelPower;
        fixed4 _ShadowColor;

        struct appdata_metal
        {
            float4 vertex : POSITION;
            float3 normal : NORMAL;
            float4 tangent : TANGENT;
            float2 texcoord : TEXCOORD0;
        };

        struct v2f_metal
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 worldNormal : TEXCOORD1;
            float4 worldTangent : TEXCOORD2;   // xyz = 世界切线, w = 手性符号
            float3 worldPos : TEXCOORD3;
            RUINS_SHADOW_COORDS(4)
            UNITY_FOG_COORDS(5)
        };

        inline half3 RuinsMetalNormal(float3 wN, float4 wT, half3 tangentNormal)
        {
            float3 t = normalize(wT.xyz);
            float3 b = cross(wN, t) * wT.w;
            return normalize(half3(
                dot(t, tangentNormal),
                dot(b, tangentNormal),
                dot(wN, tangentNormal)));
        }

        // 简单立方体采样（不做 box projection，反射探针按普通 cubemap 读取）
        inline half3 RuinsSpecCube(float3 reflectDir, half smoothness)
        {
            half lod = (1.0h - saturate(smoothness)) * 7.0h;
            half4 raw = UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, reflectDir, lod);
            return DecodeHDR(raw, unity_SpecCube0_HDR);
        }

        v2f_metal vertMetal(appdata_metal v)
        {
            v2f_metal o;
            UNITY_INITIALIZE_OUTPUT(v2f_metal, o);
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = TRANSFORM_TEX(v.texcoord, _NormalMap) * _Tiling;
            o.worldNormal = UnityObjectToWorldNormal(v.normal);
            float3 wT = UnityObjectToWorldDir(v.tangent.xyz);
            o.worldTangent = float4(wT, v.tangent.w * unity_WorldTransformParams.w);
            o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
            RUINS_TRANSFER_SHADOW(o)
            UNITY_TRANSFER_FOG(o, o.pos);
            return o;
        }

        // 防御性守卫：AutoLight.cginc 第 12-14 行在没有任何光源关键字时会强制 define DIRECTIONAL，
        // 所以正常情况下恒成立；保留它只是为了让 UNITY_LIGHT_ATTENUATION 的使用点在异常配置下也不会变成未定义标识符。
        #if defined(DIRECTIONAL) || defined(POINT) || defined(SPOT) || defined(POINT_COOKIE) || defined(DIRECTIONAL_COOKIE)
        fixed4 fragMetal(v2f_metal i, fixed facing : VFACE) : SV_Target
        {
            float3 wN = normalize(i.worldNormal);
            half3 tn = UnpackNormal(tex2D(_NormalMap, i.uv));
            tn = lerp(half3(0, 0, 1), tn, _NormalStrength);
            float3 nrm = RuinsMetalNormal(wN, i.worldTangent, tn) * (facing > 0 ? 1.0 : -1.0);   // 双面几何/翻转法线网格：法线翻转后再取反射方向

            float3 viewDir = normalize(UnityWorldSpaceViewDir(i.worldPos));
            float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);

            UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);

            half3 albedo = _Color.rgb;
            half3 ambient = ShadeSH9(half4(nrm, 1));

            // 漫反射压得很低，靠高光与环境反射出金属感
            half3 col = albedo * (ambient + _ShadowColor.rgb * 0.5h) * _DiffuseStrength;

            float3 halfDir = normalize(lightDir + viewDir);
            half specTerm = pow(saturate(dot(nrm, halfDir)), max(_SpecPower, 1.0h));
            col += _LightColor0.rgb * specTerm * _SpecIntensity * saturate(_Smoothness + 0.2h) * atten;

            // 环境反射（unity_SpecCube0）
            float3 reflectDir = reflect(-viewDir, nrm);
            half3 envRefl = RuinsSpecCube(reflectDir, _Smoothness);
            half fresnel = pow(saturate(1.0h - saturate(dot(nrm, viewDir))), 5.0h);
            col += envRefl * albedo * _ReflectionStrength * (0.35h + 0.65h * fresnel);

            // 菲涅尔边缘
            half rim = pow(saturate(1.0h - saturate(dot(nrm, viewDir))), _FresnelPower);
            col += _FresnelColor.rgb * rim * 0.35h;

            fixed4 outColor = fixed4(col, 1);
            UNITY_APPLY_FOG(i.fogCoord, outColor);
            return outColor;
        }

        fixed4 fragMetalAdd(v2f_metal i, fixed facing : VFACE) : SV_Target
        {
            float3 wN = normalize(i.worldNormal);
            half3 tn = UnpackNormal(tex2D(_NormalMap, i.uv));
            tn = lerp(half3(0, 0, 1), tn, _NormalStrength);
            float3 nrm = RuinsMetalNormal(wN, i.worldTangent, tn) * (facing > 0 ? 1.0 : -1.0);   // 双面几何/翻转法线网格：法线翻转后再取反射方向

            float3 viewDir = normalize(UnityWorldSpaceViewDir(i.worldPos));
            float3 lightDir = normalize(_WorldSpaceLightPos0.xyz - i.worldPos * _WorldSpaceLightPos0.w);
            UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);

            float3 halfDir = normalize(lightDir + viewDir);
            half specTerm = pow(saturate(dot(nrm, halfDir)), max(_SpecPower, 1.0h));
            half ndl = saturate(dot(nrm, lightDir));
            half3 col = _Color.rgb * _LightColor0.rgb * (ndl * _DiffuseStrength + specTerm * _SpecIntensity) * atten;
            return fixed4(col, 1);
        }

        #endif

        struct v2f_metal_shadow
        {
            V2F_SHADOW_CASTER;
        };

        v2f_metal_shadow vertMetalShadow(appdata_metal v)
        {
            v2f_metal_shadow o;
            UNITY_INITIALIZE_OUTPUT(v2f_metal_shadow, o);
            TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
            return o;
        }

        float4 fragMetalShadow(v2f_metal_shadow i) : SV_Target
        {
            SHADOW_CASTER_FRAGMENT(i)
        }
        ENDCG

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vertMetal
            #pragma fragment fragMetal
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma target 3.0
            ENDCG
        }

        Pass
        {
            Tags { "LightMode" = "ForwardAdd" }
            Blend One One
            ZWrite Off
            CGPROGRAM
            #pragma vertex vertMetal
            #pragma fragment fragMetalAdd
            #pragma multi_compile_fwdadd
            #pragma target 3.0
            ENDCG
        }

        Pass
        {
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            CGPROGRAM
            #pragma vertex vertMetalShadow
            #pragma fragment fragMetalShadow
            #pragma multi_compile_shadowcaster
            #pragma target 3.0
            ENDCG
        }
    }

    FallBack "Diffuse"
}
