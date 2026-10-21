# Ruins Guardian 升级规范（全员必读，v1）

工程：`D:\unityproject\虚拟现实作业2`（Unity 2022.3.62f3，**内置渲染管线 Built-in RP + Post Processing Stack v2**）。
场景、材质、角色、Animator、UI **全部由 `Assets/Editor/BuildDemoScene.cs` 程序化生成**，零外部素材。菜单 `Tools > Ruins Guardian > 生成演示场景` 一键重建整个游戏。

## 0. 现状（要解决什么）
- 角色是胶囊/立方体拼的火柴人，几乎不读作人形。
- 场景是 20×20 的方盒子，砖块贴图平铺拉伸，光照扁平，只有一盏方向光。
- 无描边、无法线、无色彩分级，画面灰黄一片。
- 战斗只有"挥一下、掉血"，没有顿帧、屏震、闪避、连击。
- HUD 是纯白文字加一条长条血条。

## 1. 目标
现代独立动作游戏的质感（参考 Hades / 暖雪 / 哈迪斯式的明暗对比与打击反馈）：
**低多边形造型 + 手绘质感贴图 + 描边 + 强方向光 + 暖冷对撞 + 夸张的动作反馈。**
不是写实。远看要有大色块与光斑，近看要有金属高光与描边。

## 2. 调色板（统一使用，禁止自创色）
| 用途 | Hex |
| --- | --- |
| 火光 / 暖主光 | #FFB066、#FF7A3C |
| 冷阴影 / 环境光 | #2E3A5C、#4A5F8C |
| 石材本体 | #C8C0AE、#8A8478 |
| 苔痕 / 植被 | #5F7A4A |
| 符文青 | #46E0C8 |
| 宝石 | #3DE0FF |
| 血红（危险/受击） | #D84A3A |

## 3. 光照
- 主方向光：暖色 (1.0, 0.72, 0.45)，intensity 1.8，仰角 25–35°，软阴影，shadowStrength 0.85。
- 环境：Gradient（天空冷 #2E3A5C、赤道中性、地面暖 #4A3A2E），intensity 0.7。
- 补光：与主光反向的冷蓝方向光，intensity 0.25，无阴影。
- 点光：火盆 / 符文石，暖色 intensity 2.0–3.0，range 6–9，必须带闪烁脚本。
- 雾：Exponential，density 0.018–0.022，颜色 (0.35, 0.33, 0.42)。

## 4. 后处理（PPSv2，扩展 `RuinsPostProcessProfile`）
Bloom(intensity 7, threshold 0.85, softKnee 0.6, diffusion 7) /
ColorGrading(ACES, 对比 +15, 饱和 +12, 高光偏暖、阴影偏冷) /
Vignette(0.32) / FilmGrain(0.22, white) / ChromaticAberration(0.06) /
AmbientOcclusion(MultiScaleVolumetric, 1.1)。

## 5. 代码契约（硬约束，违反必然编译失败）

### 5.1 生成器是 partial 类
`BuildDemoScene` 已声明为 `public static partial class`。**所有新生成逻辑写成新的 partial 文件**，可直接调用原有私有成员（`CreateBox`/`CreateCylinder`/`CreateMaterial`/`matStone` 等）与私有静态字段。
**禁止**修改 `BuildDemoScene.cs` 本体——集成（在 `Build()` 里切换新旧实现）由调度者统一完成。

### 5.2 Shader / 材质工厂（唯一入口，签名不可改）
T3 负责实现 `Assets/Editor/ShaderKit.cs` 与 `Assets/Shaders/*.shader`；**其他所有人只能通过下面签名拿材质**：

```csharp
public static class ShaderKit
{
    public static Material Stone(string name, Texture2D albedo, Texture2D normal, Color tintA, Color tintB, float tiling = 4f, float roughness = 0.85f);
    public static Material Metal(string name, Texture2D normal);
    public static Material Character(string name, Color bodyColor, Color trimColor, Color rimColor, float outlineWidth = 0.02f);
    public static Material Emissive(string name, Color color, float emission);
    public static Material Foliage(string name, Texture2D albedo, float cutoff = 0.4f);
}
```
全部返回已保存为资源的 `.mat`（`Assets/Generated/Materials/`），不得返回运行时临时材质。
Shader 名称固定为：`Ruins/StylizedStone`、`Ruins/StylizedCharacter`、`Ruins/Emissive`、`Ruins/Foliage`、`Ruins/Metal`。

### 5.3 程序化贴图（T2 实现）
```csharp
public static class TextureKit
{
    public static Texture2D StoneAlbedo(int size, Color baseColor, float mottle, int seed, float moss);
    public static Texture2D StoneNormal(int size, float strength, float tileScale, int seed);
    public static Texture2D Roughness(int size, float min, float max, int seed);
    public static Texture2D GrassCard(int size, int seed);
    public static Texture2D Noise(int size, int seed);
}
```

### 5.4 战斗事件（T4 定义，T5 订阅）
```csharp
public static class CombatEvents
{
    public static event System.Action<int> ComboChanged;        // 连击数，0 = 结束
    public static event System.Action<float> DodgeCooldown;     // 0..1 就绪度
    public static event System.Action<Vector3, int> HitLanded;  // 命中点, 伤害
    public static event System.Action<Transform> TargetChanged; // 锁定目标，null = 无
}
```
UI 只通过它们取状态，**禁止**直接引用战斗脚本里的具体类。

### 5.5 角色节点层级（T1 必须保持，动画曲线按路径驱动）
`Visual/Body/{Hips, Chest, Head, ArmL, ArmR, LegL, LegR}`，手臂下 `上臂/前臂/手/WeaponPivot`，腿下 `大腿/小腿/靴子`。根节点需带 `CapsuleCollider` 与 `HitFlash`。

## 6. 工程纪律
- **任何人都不要运行 Unity.exe**（工程锁冲突，编译验证由调度者串行执行）。写完代码即停。
- 只改**自己拥有的文件**。需要别人的改动就写进回报的"待接线"里。
- 注释一律中文；文件 UTF-8 无 BOM、LF 换行。
- 不新增第三方插件；不引入 URP/HDRP；不下载任何素材（本阶段全程序化）。
- 每个公开类型一个文件，文件名与类型名一致。
- 性能预算：单帧 draw call 目标 < 600，场景物件总数 < 1200。

## 7. 文件所有权
| 模块 | 拥有文件 |
| --- | --- |
| T1 角色 | `Assets/Editor/MeshKit.cs`、`Assets/Editor/BuildDemoScene.Characters.cs` |
| T2 世界 | `Assets/Editor/TextureKit.cs`、`Assets/Editor/BuildDemoScene.World.cs`、`Assets/Editor/BuildDemoScene.Lighting.cs` |
| T3 渲染 | `Assets/Editor/ShaderKit.cs`、`Assets/Shaders/*.shader` |
| T4 战斗 | `Assets/Scripts/Combat/*.cs`、`PlayerCombat.cs`、`Health.cs`、`OrbitFollowCamera.cs`、`GameManager.cs` |
| T5 UI | `Assets/Scripts/UI/*.cs`、`HUDController.cs`、`GameMenuController.cs`、`Assets/Editor/BuildDemoScene.Ui.cs` |
