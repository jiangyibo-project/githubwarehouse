Shader "Ruins/StylizedStone"
{
    Properties
    {
        _MainTex ("反照率贴图", 2D) = "white" {}
        _NormalMap ("法线贴图", 2D) = "bump" {}
        _DetailTex ("细节噪声", 2D) = "gray" {}
        _TintA ("色调A（石材主体）", Color) = (0.78, 0.75, 0.68, 1)
        _TintB ("色调B（苔痕/暗部）", Color) = (0.37, 0.48, 0.29, 1)
        _Tiling ("贴图平铺", Float) = 4
        _Roughness ("粗糙度", Range(0, 1)) = 0.85
        _Wetness ("湿润度", Range(0, 1)) = 0
        _DetailStrength ("细节强度", Range(0, 1)) = 0.45
        _DetailTiling ("细节相对平铺", Float) = 1.5
        _Band1 ("暗部阈值", Range(0, 1)) = 0.25
        _Band2 ("亮部阈值", Range(0, 1)) = 0.55
        _ShadowColor ("冷色阴影", Color) = (0.18, 0.23, 0.36, 1)
        _AmbientStrength ("环境光强度", Range(0, 2)) = 1
        _SpecPower ("高光锐度", Float) = 32
        _SpecIntensity ("高光强度", Range(0, 2)) = 0.25
        _VertexColorAmount ("顶点色调制量", Range(0, 1)) = 0
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

        sampler2D _MainTex;
        float4 _MainTex_ST;
        sampler2D _NormalMap;
        float4 _NormalMap_ST;
        sampler2D _DetailTex;
        float4 _DetailTex_ST;
        fixed4 _TintA;
        fixed4 _TintB;
        float _Tiling;
        float _Roughness;
        float _Wetness;
        float _DetailStrength;
        float _DetailTiling;
        float _Band1;
        float _Band2;
        fixed4 _ShadowColor;
        float _AmbientStrength;
        float _SpecPower;
        float _SpecIntensity;
        float _VertexColorAmount;

        struct appdata_stone
        {
            float4 vertex : POSITION;
            float3 normal : NORMAL;
            float4 tangent : TANGENT;
            float2 texcoord : TEXCOORD0;
            fixed4 color : COLOR;
        };

        struct v2f_stone
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 worldNormal : TEXCOORD1;
            float4 worldTangent : TEXCOORD2;   // xyz = 世界切线, w = 手性符号
            float3 worldPos : TEXCOORD3;
            fixed4 vcol : TEXCOORD4;
            RUINS_SHADOW_COORDS(5)
            UNITY_FOG_COORDS(6)
        };

        // 三阶卡通明暗：暗 / 中 / 亮
        inline half RuinsStoneBand(half ndl, half t1, half t2)
        {
            half band = 0.12h;
            band += 0.38h * step(t1, ndl);
            band += 0.50h * step(t2, ndl);
            return band;
        }

        // 世界空间切线基法线解包
        inline half3 RuinsWorldNormal(float3 wN, float4 wT, half3 tangentNormal)
        {
            float3 t = normalize(wT.xyz);
            float3 b = cross(wN, t) * wT.w;
            return normalize(half3(
                dot(t, tangentNormal),
                dot(b, tangentNormal),
                dot(wN, tangentNormal)));
        }

        v2f_stone vertStone(appdata_stone v)
        {
            v2f_stone o;
            UNITY_INITIALIZE_OUTPUT(v2f_stone, o);
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = TRANSFORM_TEX(v.texcoord, _MainTex) * _Tiling;
            o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
            o.worldNormal = UnityObjectToWorldNormal(v.normal);
            float3 wT = UnityObjectToWorldDir(v.tangent.xyz);
            o.worldTangent = float4(wT, v.tangent.w * unity_WorldTransformParams.w);
            o.vcol = v.color;
            RUINS_TRANSFER_SHADOW(o)
            UNITY_TRANSFER_FOG(o, o.pos);
            return o;
        }

        // 防御性守卫：AutoLight.cginc 第 12-14 行在没有任何光源关键字时会强制 define DIRECTIONAL，
        // 所以正常情况下恒成立；保留它只是为了让 UNITY_LIGHT_ATTENUATION 的使用点在异常配置下也不会变成未定义标识符。
        #if defined(DIRECTIONAL) || defined(POINT) || defined(SPOT) || defined(POINT_COOKIE) || defined(DIRECTIONAL_COOKIE)
        fixed4 fragStone(v2f_stone i, fixed facing : VFACE) : SV_Target
        {
            fixed3 texColor = tex2D(_MainTex, i.uv).rgb;
            fixed3 albedo = texColor * _TintA.rgb;

            // 顶点色调制：默认关闭，避免没有颜色流的网格被乘成黑色
            half3 vcMask = lerp(half3(1, 1, 1), saturate(i.vcol.rgb), _VertexColorAmount);
            albedo = albedo * vcMask;

            // 细节噪声与色调B混合，制造苔痕/脏迹
            fixed detail = tex2D(_DetailTex, i.uv * _DetailTiling).r;
            half detailMask = saturate((detail - 0.45h) * 2.0h * _DetailStrength + _DetailStrength * 0.15h);
            albedo = lerp(albedo, albedo * _TintB.rgb * 1.8h, detailMask);

            // 法线
            float3 wN = normalize(i.worldNormal);
            half3 tn = UnpackNormal(tex2D(_NormalMap, i.uv));
            float3 nrm = RuinsWorldNormal(wN, i.worldTangent, tn) * (facing > 0 ? 1.0 : -1.0);   // 双面几何/翻转法线网格：背面法线朝内，翻转后不再黑面

            // 主方向光
            float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
            half ndl = saturate(dot(nrm, lightDir) * 0.5h + 0.5h);
            half band = RuinsStoneBand(ndl, _Band1, _Band2);

            UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);

            // 冷色环境（球谐）
            half3 ambient = ShadeSH9(half4(nrm, 1));
            half3 ambTerm = albedo * (ambient * _AmbientStrength + _ShadowColor.rgb * 0.35h);

            // 暖色直接光
            half3 directTerm = albedo * _LightColor0.rgb * (band * atten);

            half3 col = ambTerm + directTerm;

            // 湿润/湿滑高光
            float3 viewDir = normalize(UnityWorldSpaceViewDir(i.worldPos));
            float3 halfDir = normalize(lightDir + viewDir);
            half specTerm = pow(saturate(dot(nrm, halfDir)), _SpecPower);
            half wet = 1.0h + _Wetness * 4.0h;
            half gloss = saturate(1.0h - _Roughness * 0.85h);
            col += _LightColor0.rgb * specTerm * _SpecIntensity * wet * gloss * atten;

            fixed4 outColor = fixed4(col, 1);
            UNITY_APPLY_FOG(i.fogCoord, outColor);
            return outColor;
        }

        fixed4 fragStoneAdd(v2f_stone i, fixed facing : VFACE) : SV_Target
        {
            fixed3 albedo = tex2D(_MainTex, i.uv).rgb * _TintA.rgb;
            half3 vcMask = lerp(half3(1, 1, 1), saturate(i.vcol.rgb), _VertexColorAmount);
            albedo = albedo * vcMask;

            fixed detail = tex2D(_DetailTex, i.uv * _DetailTiling).r;
            half detailMask = saturate((detail - 0.45h) * 2.0h * _DetailStrength + _DetailStrength * 0.15h);
            albedo = lerp(albedo, albedo * _TintB.rgb * 1.8h, detailMask);

            float3 wN = normalize(i.worldNormal);
            half3 tn = UnpackNormal(tex2D(_NormalMap, i.uv));
            float3 nrm = RuinsWorldNormal(wN, i.worldTangent, tn) * (facing > 0 ? 1.0 : -1.0);   // 双面几何/翻转法线网格：背面法线朝内，翻转后不再黑面

            // 点光/聚光：w = 0 表示方向光
            float3 lightDir = normalize(_WorldSpaceLightPos0.xyz - i.worldPos * _WorldSpaceLightPos0.w);
            half ndl = saturate(dot(nrm, lightDir) * 0.5h + 0.5h);
            half band = RuinsStoneBand(ndl, _Band1, _Band2);

            UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);

            half3 col = albedo * _LightColor0.rgb * (band * atten);

            float3 viewDir = normalize(UnityWorldSpaceViewDir(i.worldPos));
            float3 halfDir = normalize(lightDir + viewDir);
            half specTerm = pow(saturate(dot(nrm, halfDir)), _SpecPower);
            half wet = 1.0h + _Wetness * 4.0h;
            half gloss = saturate(1.0h - _Roughness * 0.85h);
            col += _LightColor0.rgb * specTerm * _SpecIntensity * wet * gloss * atten;

            return fixed4(col, 1);
        }

        #endif

        struct v2f_stone_shadow
        {
            V2F_SHADOW_CASTER;
        };

        v2f_stone_shadow vertStoneShadow(appdata_stone v)
        {
            v2f_stone_shadow o;
            UNITY_INITIALIZE_OUTPUT(v2f_stone_shadow, o);
            TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
            return o;
        }

        float4 fragStoneShadow(v2f_stone_shadow i) : SV_Target
        {
            SHADOW_CASTER_FRAGMENT(i)
        }
        ENDCG

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vertStone
            #pragma fragment fragStone
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
            #pragma vertex vertStone
            #pragma fragment fragStoneAdd
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
            #pragma vertex vertStoneShadow
            #pragma fragment fragStoneShadow
            #pragma multi_compile_shadowcaster
            #pragma target 3.0
            ENDCG
        }
    }

    FallBack "Diffuse"
}
