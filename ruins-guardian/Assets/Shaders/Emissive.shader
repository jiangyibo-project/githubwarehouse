Shader "Ruins/Emissive"
{
    Properties
    {
        _Color ("主色", Color) = (0.27, 0.88, 0.78, 1)
        [HDR] _Emission ("自发光（HDR）", Color) = (1, 1, 1, 1)
        _EmissionIntensity ("自发光强度", Float) = 3
        _FresnelPower ("菲涅尔锐度", Range(0.5, 8)) = 2.5
        _FresnelBoost ("菲涅尔增益", Range(0, 4)) = 1.2
        _PulseSpeed ("脉动速度", Float) = 1.2
        _PulseAmount ("脉动幅度", Range(0, 1)) = 0.3
        _ShadowColor ("冷色阴影", Color) = (0.18, 0.23, 0.36, 1)
        _AmbientStrength ("环境光强度", Range(0, 2)) = 1
        _Band1 ("暗部阈值", Range(0, 1)) = 0.3
        _Band2 ("亮部阈值", Range(0, 1)) = 0.62
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

        fixed4 _Color;
        half4 _Emission;   // HDR 值可能远大于 1，用 half 而不是 fixed
        float _EmissionIntensity;
        float _FresnelPower;
        float _FresnelBoost;
        float _PulseSpeed;
        float _PulseAmount;
        fixed4 _ShadowColor;
        float _AmbientStrength;
        float _Band1;
        float _Band2;

        struct appdata_emissive
        {
            float4 vertex : POSITION;
            float3 normal : NORMAL;
        };

        struct v2f_emissive
        {
            float4 pos : SV_POSITION;
            float3 worldNormal : TEXCOORD0;
            float3 worldPos : TEXCOORD1;
            RUINS_SHADOW_COORDS(2)
            UNITY_FOG_COORDS(3)
        };

        inline half RuinsEmissiveBand(half ndl, half t1, half t2)
        {
            half band = 0.16h;
            band += 0.34h * step(t1, ndl);
            band += 0.50h * step(t2, ndl);
            return band;
        }

        // 自发光本体：HDR 输出 + 菲涅尔边缘 + 正弦脉动，可触发 Bloom
        inline half3 RuinsEmissionTerm(half3 nrm, half3 viewDir)
        {
            half fresnel = pow(saturate(1.0h - saturate(dot(nrm, viewDir))), _FresnelPower);
            half3 emissiveColor = _Emission.rgb * max(_EmissionIntensity, 0.0h);
            half pulse = 1.0h + _PulseAmount * sin(_Time.y * _PulseSpeed * 6.2831853h);
            return emissiveColor * pulse * (1.0h + fresnel * _FresnelBoost);
        }

        v2f_emissive vertEmissive(appdata_emissive v)
        {
            v2f_emissive o;
            UNITY_INITIALIZE_OUTPUT(v2f_emissive, o);
            o.pos = UnityObjectToClipPos(v.vertex);
            o.worldNormal = UnityObjectToWorldNormal(v.normal);
            o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
            RUINS_TRANSFER_SHADOW(o)
            UNITY_TRANSFER_FOG(o, o.pos);
            return o;
        }

        // 防御性守卫：AutoLight.cginc 第 12-14 行在没有任何光源关键字时会强制 define DIRECTIONAL，
        // 所以正常情况下恒成立；保留它只是为了让 UNITY_LIGHT_ATTENUATION 的使用点在异常配置下也不会变成未定义标识符。
        #if defined(DIRECTIONAL) || defined(POINT) || defined(SPOT) || defined(POINT_COOKIE) || defined(DIRECTIONAL_COOKIE)
        fixed4 fragEmissive(v2f_emissive i) : SV_Target
        {
            float3 nrm = normalize(i.worldNormal);
            float3 viewDir = normalize(UnityWorldSpaceViewDir(i.worldPos));
            float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);

            half ndl = saturate(dot(nrm, lightDir) * 0.5h + 0.5h);
            half band = RuinsEmissiveBand(ndl, _Band1, _Band2);
            UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);

            half3 ambient = ShadeSH9(half4(nrm, 1));
            half3 albedo = _Color.rgb;
            half3 lit = albedo * (ambient * _AmbientStrength + _ShadowColor.rgb * 0.35h)
                      + albedo * _LightColor0.rgb * (band * atten);

            half3 col = lit + RuinsEmissionTerm(nrm, viewDir);

            fixed4 outColor = fixed4(col, 1);
            UNITY_APPLY_FOG(i.fogCoord, outColor);
            return outColor;
        }

        fixed4 fragEmissiveAdd(v2f_emissive i) : SV_Target
        {
            float3 nrm = normalize(i.worldNormal);
            float3 lightDir = normalize(_WorldSpaceLightPos0.xyz - i.worldPos * _WorldSpaceLightPos0.w);
            half ndl = saturate(dot(nrm, lightDir) * 0.5h + 0.5h);
            half band = RuinsEmissiveBand(ndl, _Band1, _Band2);
            UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
            half3 col = _Color.rgb * _LightColor0.rgb * (band * atten);
            return fixed4(col, 1);
        }

        #endif

        struct v2f_emissive_shadow
        {
            V2F_SHADOW_CASTER;
        };

        v2f_emissive_shadow vertEmissiveShadow(appdata_emissive v)
        {
            v2f_emissive_shadow o;
            UNITY_INITIALIZE_OUTPUT(v2f_emissive_shadow, o);
            TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
            return o;
        }

        float4 fragEmissiveShadow(v2f_emissive_shadow i) : SV_Target
        {
            SHADOW_CASTER_FRAGMENT(i)
        }
        ENDCG

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vertEmissive
            #pragma fragment fragEmissive
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
            #pragma vertex vertEmissive
            #pragma fragment fragEmissiveAdd
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
            #pragma vertex vertEmissiveShadow
            #pragma fragment fragEmissiveShadow
            #pragma multi_compile_shadowcaster
            #pragma target 3.0
            ENDCG
        }
    }

    FallBack "Diffuse"
}
