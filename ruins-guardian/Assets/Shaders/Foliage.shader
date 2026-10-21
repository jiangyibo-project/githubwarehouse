Shader "Ruins/Foliage"
{
    Properties
    {
        _MainTex ("植被贴图（含 Alpha 裁切）", 2D) = "white" {}
        _Color ("色调", Color) = (0.37, 0.48, 0.29, 1)
        _Cutoff ("Alpha 裁切阈值", Range(0, 1)) = 0.4
        _WindStrength ("风力强度", Range(0, 2)) = 0.35
        _WindSpeed ("风速度", Float) = 1.6
        _WindFrequency ("风空间频率", Float) = 1.5
        _WindDirection ("风向(XZ)", Vector) = (1, 0.35, 0, 0)
        _ShadowColor ("冷色阴影", Color) = (0.18, 0.23, 0.36, 1)
        _AmbientStrength ("环境光强度", Range(0, 2)) = 1
        _Band1 ("暗部阈值", Range(0, 1)) = 0.3
        _Band2 ("亮部阈值", Range(0, 1)) = 0.62
        _TintVariance ("明暗随机量", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "AlphaTest" }
        LOD 250
        Cull Off

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
        fixed4 _Color;
        float _Cutoff;
        float _WindStrength;
        float _WindSpeed;
        float _WindFrequency;
        float4 _WindDirection;
        fixed4 _ShadowColor;
        float _AmbientStrength;
        float _Band1;
        float _Band2;
        float _TintVariance;

        struct appdata_foliage
        {
            float4 vertex : POSITION;
            float3 normal : NORMAL;
            float2 texcoord : TEXCOORD0;
            fixed4 color : COLOR;
        };

        struct v2f_foliage
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 worldNormal : TEXCOORD1;
            float3 worldPos : TEXCOORD2;
            fixed4 vcol : TEXCOORD3;
            RUINS_SHADOW_COORDS(4)
            UNITY_FOG_COORDS(5)
        };

        inline half RuinsFoliageBand(half ndl, half t1, half t2)
        {
            half band = 0.18h;
            band += 0.34h * step(t1, ndl);
            band += 0.48h * step(t2, ndl);
            return band;
        }

        // 顶点风摆：按 UV 的 v 分量（叶尖权重）在世界 XZ 平面摆动
        inline float3 RuinsWindOffset(float3 worldPos, float leafWeight)
        {
            float t = _Time.y * _WindSpeed;
            float wave = sin(t + worldPos.x * _WindFrequency + worldPos.z * _WindFrequency * 0.7)
                       + 0.5 * sin(t * 1.7 + worldPos.z * _WindFrequency * 1.3 + worldPos.x * 0.5);
            float bend = saturate(leafWeight);
            bend = bend * bend;
            float2 dir = normalize(float2(_WindDirection.x, _WindDirection.y) + float2(1e-4, 0));
            return float3(dir.x, 0, dir.y) * (wave * _WindStrength * bend);
        }

        v2f_foliage vertFoliage(appdata_foliage v)
        {
            v2f_foliage o;
            UNITY_INITIALIZE_OUTPUT(v2f_foliage, o);

            float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
            worldPos += RuinsWindOffset(worldPos, v.texcoord.y);

            o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1));
            o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
            o.worldNormal = UnityObjectToWorldNormal(v.normal);
            o.worldPos = worldPos;
            o.vcol = v.color;

            // 顶点风摆后位置已改变，这里用顶点色做轻微的透光随机
            RUINS_TRANSFER_SHADOW(o)
            UNITY_TRANSFER_FOG(o, o.pos);
            return o;
        }

        // 防御性守卫：AutoLight.cginc 第 12-14 行在没有任何光源关键字时会强制 define DIRECTIONAL，
        // 所以正常情况下恒成立；保留它只是为了让 UNITY_LIGHT_ATTENUATION 的使用点在异常配置下也不会变成未定义标识符。
        #if defined(DIRECTIONAL) || defined(POINT) || defined(SPOT) || defined(POINT_COOKIE) || defined(DIRECTIONAL_COOKIE)
        fixed4 fragFoliage(v2f_foliage i, fixed facing : VFACE) : SV_Target
        {
            fixed4 texColor = tex2D(_MainTex, i.uv);
            clip(texColor.a - _Cutoff);

            float3 nrm = normalize(i.worldNormal) * (facing > 0 ? 1.0 : -1.0);   // Cull Off 双面卡片：背面法线朝内，翻转后背面不再全黑
            float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
            half ndl = saturate(dot(nrm, lightDir) * 0.5h + 0.5h);
            half band = RuinsFoliageBand(ndl, _Band1, _Band2);

            UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);

            // 用顶点色的亮度做每一株的明暗差异，避免整片草地一个颜色
            half variance = 1.0h + (saturate(i.vcol.r + i.vcol.g + i.vcol.b) - 0.5h) * 2.0h * _TintVariance;
            half3 albedo = texColor.rgb * _Color.rgb * variance;

            half3 ambient = ShadeSH9(half4(nrm, 1));
            half3 col = albedo * (ambient * _AmbientStrength + _ShadowColor.rgb * 0.4h)
                      + albedo * _LightColor0.rgb * (band * atten);

            fixed4 outColor = fixed4(col, texColor.a);
            UNITY_APPLY_FOG(i.fogCoord, outColor);
            return outColor;
        }

        fixed4 fragFoliageAdd(v2f_foliage i, fixed facing : VFACE) : SV_Target
        {
            fixed4 texColor = tex2D(_MainTex, i.uv);
            clip(texColor.a - _Cutoff);

            float3 nrm = normalize(i.worldNormal) * (facing > 0 ? 1.0 : -1.0);   // Cull Off 双面卡片：背面法线朝内，翻转后背面不再全黑
            float3 lightDir = normalize(_WorldSpaceLightPos0.xyz - i.worldPos * _WorldSpaceLightPos0.w);
            half ndl = saturate(dot(nrm, lightDir) * 0.5h + 0.5h);
            half band = RuinsFoliageBand(ndl, _Band1, _Band2);
            UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
            half3 col = texColor.rgb * _Color.rgb * _LightColor0.rgb * (band * atten);
            return fixed4(col, 1);
        }

        #endif

        struct v2f_foliage_shadow
        {
            V2F_SHADOW_CASTER;
            float2 uv : TEXCOORD1;   // 不能占用 TEXCOORD0：点光 cube shadow 变体用它存距离
        };

        // 投影不叠加风摆：草的阴影本就柔和，避免点光 cube shadow 的距离分量出错
        v2f_foliage_shadow vertFoliageShadow(appdata_foliage v)
        {
            v2f_foliage_shadow o;
            UNITY_INITIALIZE_OUTPUT(v2f_foliage_shadow, o);
            o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
            TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
            return o;
        }

        float4 fragFoliageShadow(v2f_foliage_shadow i) : SV_Target
        {
            fixed4 texColor = tex2D(_MainTex, i.uv);
            clip(texColor.a - _Cutoff);
            SHADOW_CASTER_FRAGMENT(i)
        }
        ENDCG

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Cull Off
            CGPROGRAM
            #pragma vertex vertFoliage
            #pragma fragment fragFoliage
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
            Cull Off
            CGPROGRAM
            #pragma vertex vertFoliage
            #pragma fragment fragFoliageAdd
            #pragma multi_compile_fwdadd
            #pragma target 3.0
            ENDCG
        }

        Pass
        {
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            Cull Off
            CGPROGRAM
            #pragma vertex vertFoliageShadow
            #pragma fragment fragFoliageShadow
            #pragma multi_compile_shadowcaster
            #pragma target 3.0
            ENDCG
        }
    }

    FallBack "Diffuse"
}
