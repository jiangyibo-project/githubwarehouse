Shader "Ruins/StylizedCharacter"
{
    Properties
    {
        _Color ("主体色", Color) = (0.78, 0.75, 0.68, 1)
        _TrimColor ("饰边色", Color) = (0.32, 0.37, 0.55, 1)
        _RimColor ("边缘光色（冷色）", Color) = (0.43, 0.55, 0.85, 1)
        _RimPower ("边缘光锐度", Range(0.5, 8)) = 3.2
        _RimIntensity ("边缘光强度", Range(0, 4)) = 0.85
        _ShadowColor ("冷色阴影", Color) = (0.18, 0.23, 0.36, 1)
        _AmbientStrength ("环境光强度", Range(0, 2)) = 1
        _RampEdge ("暗→中分界", Range(0.2, 1)) = 0.45
        _RampHiEdge ("中→亮分界", Range(0.25, 1)) = 0.62
        _Darken ("暗部深浅", Range(0.1, 1)) = 0.45
        _TrimStrength ("饰边混入量", Range(0, 1)) = 0.35
        _Flash ("受击闪白", Range(0, 1)) = 0
        _OutlineColor ("描边颜色", Color) = (0.06, 0.07, 0.11, 1)
        _OutlineWidth ("描边宽度（屏幕比例）", Range(0, 0.2)) = 0.02
        _OutlineRefDist ("描边参考距离", Float) = 6
        _OutlineMinScale ("描边远处最小比例", Range(0.05, 1)) = 0.22
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

        fixed4 _Color;
        fixed4 _TrimColor;
        fixed4 _RimColor;
        float _RimPower;
        float _RimIntensity;
        fixed4 _ShadowColor;
        float _AmbientStrength;
        float _RampEdge;
        float _RampHiEdge;
        float _Darken;
        float _TrimStrength;
        float _Flash;
        fixed4 _OutlineColor;
        float _OutlineWidth;
        float _OutlineRefDist;
        float _OutlineMinScale;
        float _VertexColorAmount;

        struct appdata_character
        {
            float4 vertex : POSITION;
            float3 normal : NORMAL;
            float2 texcoord : TEXCOORD0;
            fixed4 color : COLOR;
        };

        struct v2f_character
        {
            float4 pos : SV_POSITION;
            float3 worldNormal : TEXCOORD0;
            float3 worldPos : TEXCOORD1;
            fixed4 vcol : TEXCOORD2;
            RUINS_SHADOW_COORDS(3)
            UNITY_FOG_COORDS(4)
        };

        // 三阶卡通明暗（Hades 式）：半兰伯特 → 两级 smoothstep 离散成 暗(_Darken) / 中(1.0) / 亮(1.3)，
        // 过渡只用几个像素宽的 smoothstep，既保持硬分界又不会有锯齿。
        // 同时输出：中间调掩码（供饰边色用）与暗部权重（供暗档压冷用）。
        inline half RuinsCharRamp(half ndl, out half midMask, out half lowBand)
        {
            half hiEdge = max(_RampHiEdge, 0.25h);
            half loEdge = clamp(_RampEdge, 0.2h, hiEdge - 0.05h);   // 防止把两条边设反后 smoothstep 未定义
            half rampLow = smoothstep(0.18h, loEdge, ndl);          // 暗 → 中
            half rampHi = smoothstep(hiEdge - 0.05h, hiEdge, ndl);  // 中 → 亮
            midMask = rampLow * (1.0h - rampHi);                    // 只在中间档为 1
            lowBand = 1.0h - rampLow;                               // 暗部权重
            half midLevel = lerp(_Darken, 1.0h, 0.6h);              // 中档固定在暗/亮之间的 60%（_Darken=0.45 → 0.78）
            half level = lerp(_Darken, midLevel, rampLow);          // 暗 → 中
            return lerp(level, 1.0h, rampHi);                       // 中 → 亮；不越过主光本色，避免高反照率钢甲过曝成白块
        }

        // 暗档压冷：_ShadowColor 提亮 1.6 倍后朝白色混 35%（避免暗部糊成纯黑块），中/亮档保持主光本色
        inline half3 RuinsCharBandTint(half rampLow)
        {
            half3 cool = lerp(_ShadowColor.rgb * 1.6h, half3(1, 1, 1), 0.35h);
            return lerp(cool, half3(1, 1, 1), rampLow);
        }

        v2f_character vertCharacter(appdata_character v)
        {
            v2f_character o;
            UNITY_INITIALIZE_OUTPUT(v2f_character, o);
            o.pos = UnityObjectToClipPos(v.vertex);
            o.worldNormal = UnityObjectToWorldNormal(v.normal);
            o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
            o.vcol = v.color;
            RUINS_TRANSFER_SHADOW(o)
            UNITY_TRANSFER_FOG(o, o.pos);
            return o;
        }

        // 防御性守卫：AutoLight.cginc 第 12-14 行在没有任何光源关键字时会强制 define DIRECTIONAL，
        // 所以正常情况下恒成立；保留它只是为了让 UNITY_LIGHT_ATTENUATION 的使用点在异常配置下也不会变成未定义标识符。
        #if defined(DIRECTIONAL) || defined(POINT) || defined(SPOT) || defined(POINT_COOKIE) || defined(DIRECTIONAL_COOKIE)
        fixed4 fragCharacter(v2f_character i, fixed facing : VFACE) : SV_Target
        {
            float3 nrm = normalize(i.worldNormal) * (facing > 0 ? 1.0 : -1.0);   // 双面几何：背面顶点法线朝内，翻转后 NdotL 才正确
            float3 viewDir = normalize(UnityWorldSpaceViewDir(i.worldPos));
            float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);

            // 三阶卡通明暗：暗档 _Darken(0.45，带冷色) / 中档 0.78 / 亮档 1.0，分界清楚
            half ndl = saturate(dot(nrm, lightDir) * 0.5h + 0.5h);
            half midMask;
            half lowBand;
            half level = RuinsCharRamp(ndl, midMask, lowBand);
            half3 bandTint = RuinsCharBandTint(1.0h - lowBand);

            // 饰边色：中间档自然带一点，另有顶点色时可指定饰边区域
            half trimVertex = saturate(i.vcol.r + i.vcol.g + i.vcol.b) * _VertexColorAmount;
            half trimMask = saturate(trimVertex + midMask * _TrimStrength * 0.5h);
            half3 albedo = lerp(_Color.rgb, _TrimColor.rgb, trimMask);

            UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);

            half3 ambient = ShadeSH9(half4(nrm, 1));
            half3 ambTerm = albedo * (ambient * _AmbientStrength + _ShadowColor.rgb * 0.35h);
            half3 directTerm = albedo * _LightColor0.rgb * (level * atten) * bandTint;
            half3 col = ambTerm + directTerm;

            // 冷色边缘光（菲涅尔）：只在轮廓外缘出现，补足剪影分离
            half fresnel = pow(saturate(1.0h - saturate(dot(nrm, viewDir))), _RimPower);
            col += _RimColor.rgb * (fresnel * _RimIntensity);

            // 受击闪白（0..1 混白）
            col = lerp(col, half3(1, 1, 1), saturate(_Flash));

            fixed4 outColor = fixed4(col, 1);
            UNITY_APPLY_FOG(i.fogCoord, outColor);
            return outColor;
        }

        fixed4 fragCharacterAdd(v2f_character i, fixed facing : VFACE) : SV_Target
        {
            float3 nrm = normalize(i.worldNormal) * (facing > 0 ? 1.0 : -1.0);   // 双面几何：背面顶点法线朝内，翻转后 NdotL 才正确
            float3 viewDir = normalize(UnityWorldSpaceViewDir(i.worldPos));
            float3 lightDir = normalize(_WorldSpaceLightPos0.xyz - i.worldPos * _WorldSpaceLightPos0.w);

            // 与主光同一套三阶 ramp，加点光（火盆）时角色不会变成一个亮块
            half ndl = saturate(dot(nrm, lightDir) * 0.5h + 0.5h);
            half midMask;
            half lowBand;
            half level = RuinsCharRamp(ndl, midMask, lowBand);
            half3 bandTint = RuinsCharBandTint(1.0h - lowBand);

            UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
            half3 col = _Color.rgb * _LightColor0.rgb * (level * atten) * bandTint;

            // 加点光时保留一半强度的冷色边缘光，剪影不散
            half fresnel = pow(saturate(1.0h - saturate(dot(nrm, viewDir))), _RimPower);
            col += _RimColor.rgb * (fresnel * _RimIntensity * 0.5h * atten);

            col *= saturate(1.0h - _Flash);
            return fixed4(col, 1);
        }

        #endif

        struct v2f_character_shadow
        {
            V2F_SHADOW_CASTER;
        };

        v2f_character_shadow vertCharacterShadow(appdata_character v)
        {
            v2f_character_shadow o;
            UNITY_INITIALIZE_OUTPUT(v2f_character_shadow, o);
            TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
            return o;
        }

        float4 fragCharacterShadow(v2f_character_shadow i) : SV_Target
        {
            SHADOW_CASTER_FRAGMENT(i)
        }

        // 描边：屏幕空间等宽外扩，与相机距离无关
        // 外扩方向必须用"平滑法线"（MeshKit 的网格是逐三角形独立顶点、硬法线，
        // 直接用几何法线外扩会让相邻面彼此分离，露出黑色背面 —— 也就是"身上有黑色碎片"）。
        // Unity 顶点槽位约定：mesh.uv→TEXCOORD0、uv2→TEXCOORD1、uv3→TEXCOORD2、uv4→TEXCOORD3，
        // 所以 T1 写进 uv3 的平滑法线在这里是 TEXCOORD2；同时读 TEXCOORD3 兜底（T1 若写到 uv4 也能用）。
        struct appdata_outline
        {
            float4 vertex : POSITION;
            float3 normal : NORMAL;
            float3 smoothNormalUV3 : TEXCOORD2;   // mesh.uv3
            float3 smoothNormalUV4 : TEXCOORD3;   // mesh.uv4（兜底通道）
        };

        struct v2f_outline
        {
            float4 pos : SV_POSITION;
        };

        // 平滑法线：uv3 优先 → uv4 → 几何法线。旧网格/编辑器预览没有 uv3 时自动回退，不会黑屏也不会编译失败。
        inline float3 RuinsSmoothNormal(float3 uv3, float3 uv4, float3 geometricNormal)
        {
            float3 n = uv3;
            if (length(n) < 0.01) n = uv4;
            if (length(n) < 0.01) return geometricNormal;

            // 兼容只写了 Vector2 的 uv3（Mesh.uv3 属性只能存两个分量，带 z 必须用 SetUVs(2, List<Vector3>)）。
            // 单位法线满足 x²+y²+z²=1，z 的符号取该面几何法线的半球，避免头顶/肩部等朝上朝下的面外扩方向反掉。
            if (abs(n.z) < 1e-4 && length(n.xy) > 0.5)
            {
                float z = sqrt(saturate(1.0 - dot(n.xy, n.xy)));
                n.z = (geometricNormal.z < 0.0) ? -z : z;
            }

            float len = length(n);
            if (len < 0.01) return geometricNormal;
            return n / len;
        }

        v2f_outline vertOutline(appdata_outline v)
        {
            v2f_outline o;
            float4 clipPos = UnityObjectToClipPos(v.vertex);

            float3 n = RuinsSmoothNormal(v.smoothNormalUV3, v.smoothNormalUV4, v.normal);
            float3 viewNormal = mul((float3x3)UNITY_MATRIX_IT_MV, n);
            float2 proj = float2(viewNormal.x * UNITY_MATRIX_P[0][0], viewNormal.y * UNITY_MATRIX_P[1][1]);
            float len = max(length(proj), 1e-4);

            // 屏幕等宽描边在远景会把小角色包成黑块，这里按视距收窄：
            // 参考距离内满宽（近景细线轮廓），超出后按 refDist/dist 收窄，下限 _OutlineMinScale 防止抖动。
            float viewDist = length(mul(UNITY_MATRIX_MV, v.vertex).xyz);
            float widthScale = saturate(_OutlineRefDist / max(viewDist, 0.001));
            widthScale = lerp(_OutlineMinScale, 1.0, widthScale);

            // 像素空间等宽：NDC 逐轴归一会让水平描边比垂直粗 (宽/高) 倍，这里先转到像素空间再归一化方向，
            // 于是 _OutlineWidth 的含义固定为"半屏高的比例"（与美术的像素换算一致），水平和垂直同宽。
            // clipPos.w 仍保留，所以远近描边等宽（再由 widthScale 按视距收窄）。
            float2 dirPx = normalize((proj / len) * (_ScreenParams.xy * 0.5));
            float widthPx = _OutlineWidth * _ScreenParams.y * 0.5 * widthScale;
            clipPos.xy += dirPx * (widthPx * 2.0 / _ScreenParams.xy) * clipPos.w;
            o.pos = clipPos;
            return o;
        }

        fixed4 fragOutline(v2f_outline i) : SV_Target
        {
            return fixed4(_OutlineColor.rgb, 1);
        }
        ENDCG

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vertCharacter
            #pragma fragment fragCharacter
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
            #pragma vertex vertCharacter
            #pragma fragment fragCharacterAdd
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
            #pragma vertex vertCharacterShadow
            #pragma fragment fragCharacterShadow
            #pragma multi_compile_shadowcaster
            #pragma target 3.0
            ENDCG
        }

        Pass
        {
            Name "OUTLINE"
            Tags { "LightMode" = "Always" }
            Cull Front
            ZWrite On
            ZTest LEqual
            Offset 1, 1
            CGPROGRAM
            #pragma vertex vertOutline
            #pragma fragment fragOutline
            #pragma target 3.0
            ENDCG
        }
    }

    FallBack "Diffuse"
}
