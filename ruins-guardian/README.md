# Unity 第二次实验作业

## 项目主题

**Ruins Guardian（遗迹守卫）：交互界面、角色动画与有限状态机 AI**。本工程把第 5 章 UGUI 交互界面、第 6 章 Animator 参数过渡与第 7 章有限状态机 AI 整合到一个完整的第三人称动作游戏里：玩家潜入暮色中的遗迹庭院，穿过断柱回廊与干涸蓄水池，在守卫、哨兵与精英的围堵下收集 6 颗遗迹宝石，并用 3 块符文石碑开启北侧石门。

工程已从"原始体拼装的粗糙演示"升级为 V2 版本：程序化 PBR 贴图与 5 个自写风格化 shader、后处理与暖冷对撞光照、程序化低多边形人形角色、三段连击与顿帧屏震的战斗手感、程序化合成的三层 BGM 与 21 种音效。全部资产零外部素材，由编辑器脚本一键生成。

## 玩法目标

- 收集散落在遗迹中的 **6 颗遗迹宝石**（含石门后的 1 颗）即可通过试炼（胜利）；每拾取一颗回复 15 点生命。
- 血量归零则试炼失败；倒地后按 **R** 可消耗复活符文原地复活一次。
- 击败守卫可以清路，但宝石不要求清怪；宝石总计 6 颗，第 3 颗会触发 2 名哨兵增援的波次。
- 北侧石门锁死，需先激活 **3 块符文石碑**（E 键），集齐后石门整体下沉开启，门后是第 6 颗宝石与 1 名精英守卫。

## 操作方式

| 按键 | 功能 |
| --- | --- |
| **W A S D** | 移动（方向相对当前相机） |
| **Shift** | 冲刺（仍为冲刺，不是闪避） |
| **鼠标左键** | 挥剑攻击，连续点击触发三段连击 |
| **空格** | 跳跃：跳高约 0.9 米，可越过矮墙、池沿与碎石台 |
| **Q** | 闪避：0.22 秒无敌帧 + 残影，冷却 0.9 秒 |
| **Tab** | 锁定 / 取消锁定最近的敌人 |
| **E**（或 F） | 靠近符文石碑时激活符文 |
| **R** | 倒地后使用复活符文原地复活（限一次） |
| **Esc** | 暂停 / 继续，弹出卡片式暂停面板 |
| **鼠标移动 / Z、C** | 直接旋转视角（FPS 式，无需按住右键）；Z / C 键盘辅助 |
| **鼠标滚轮** | 缩放视角 |

界面左上为 HUD（血条、宝石计数、目标条、连击数、闪避冷却环、准心、锁定框），右上为**可折叠的音频控制面板**（播放 / 暂停 BGM、静音、音量滑块、AI 总开关、复位全部守卫）。

## 环境与文件

- **Unity 2022.3.62f3**，**内置渲染管线 Built-in RP** + **Post Processing Stack v2**；脚本使用内置 `UnityEngine.UI` 与旧版输入 API，不依赖任何第三方插件，不下载任何素材。
- 场景、材质、贴图、角色、Animator Controller、UI、音频**全部由编辑器脚本程序化生成**；菜单 `Tools > Ruins Guardian > 生成演示场景` 一键重建整个游戏。

### 编辑器端：生成器与工具（`Assets/Editor`）

| 文件 | 职责 |
| --- | --- |
| `BuildDemoScene.cs` | 生成器本体：入口 `Build()`、共享基元（CreateBox / CreateCylinder / CreateMaterial）、Animator Controller 与混合树、后处理配置 |
| `BuildDemoScene.World.cs` | 三层庭院、断柱回廊、外围断墙角塔、远景剪影塔楼、火盆与体积光锥、粒子 |
| `BuildDemoScene.Characters.cs` | 调用 MeshKit 拼出玩家与守卫的分肢体人形，装配材质、碰撞体、披风 |
| `BuildDemoScene.Lighting.cs` | 暖主光 + 冷补光 + 三层环境光 + 指数雾 + 色彩分级参数 |
| `BuildDemoScene.Ui.cs` | 全部 UGUI：HUD、开始 / 暂停 / 结算面板、音频面板、按钮动效 |
| `BuildDemoScene.Level.cs` | 宝石、符文石碑与石门、3 种敌人、波次增援、治疗瓶、复活符文 |
| `BuildDemoScene.Audio.cs` | 挂载 AudioDirector 与程序化音频源 |
| `MeshKit.cs` | 程序化低多边形人形建模套件（分段身体、头盔、披风、长剑、战锤），保留动画器需要的节点层级 |
| `TextureKit.cs` | 程序化 PBR 贴图：石材 albedo / normal / roughness、草卡、噪声 |
| `ShaderKit.cs` | 材质工厂（唯一入口）：Stone / Metal / Character / Emissive / Foliage，返回已保存的 `.mat` 资源 |
| `CaptureShots.cs`、`PlayCapture.cs` | 截图工具：菜单 `Tools > Ruins Guardian > 截图` 输出到 `Logs/shots` |
| `PlayModeSmokeTest.cs` | 菜单 `Tools > Ruins Guardian > 运行冒烟测试`，自动模拟受伤、击杀、拾取与胜利流程 |

### 运行时脚本（`Assets/Scripts`）

**Combat（战斗与手感）** `Combat/CombatEvents.cs`（战斗事件总线）、`Combat/PlayerDodge.cs`（Q 闪避：无敌帧、残影、冷却）、`Combat/TargetLock.cs`（Tab 锁定）、`Combat/JuiceDirector.cs`（顿帧 + trauma 屏震 + 击杀慢动作）、`Combat/ImpactFx.cs`（命中火花与灰尘环）

**UI（界面）** `UI/UiFactory.cs`（程序化 UGUI 工厂：斜切血条、宝石、面板、滑杆）、`UI/HudAnimator.cs`（血条延迟掉血、闪避冷却环、连击计数）、`UI/UiFeedback.cs`（目标条、拾取提示、受击红闪）、`UI/DamagePopup.cs`（伤害飘字，暴击变色）、`UI/CombatEventBridge.cs`（订阅 CombatEvents 转 UI 事件）、`UI/UiButtonMotion.cs`、`UI/UiPanelFold.cs`（面板淡入缩放扫光、音频面板折叠）

**Audio（音频）** `Audio/AudioDirector.cs`（三层 BGM 交叉淡化、脚步材质判定、心跳与远钟）、`Audio/MusicFactory.cs`（12 秒同相位循环 BGM）、`Audio/SfxFactory.cs`（21 种程序化音效）、`Audio/AudioSynth.cs`（合成基元）、`Audio/AudioMusicMode.cs`

**Level（关卡玩法）** `Level/RunePillar.cs`（符文石碑，E 激活）、`Level/RuneGate.cs`（石门下沉与目标条刷新）、`Level/WaveDirector.cs`（第 3 颗宝石触发增援）、`Level/ReviveRuneSystem.cs`（R 复活）、`Level/HealPickup.cs`（精英掉落的治疗瓶）、`Level/LevelGround.cs`（射线贴地行走）、`Level/HudBridge.cs`

**AI（有限状态机与敌人扩展）** `EnemyWanderAI.cs`（五状态 FSM 基类）、`AI/SentinelAI.cs`（远程哨兵）、`AI/BruteAI.cs`（精英两段连击与震地）、`AI/EnergyBolt.cs`、`AI/ShockwaveRing.cs`、`AI/BurstFx.cs`、`AI/EnemyHealthBar.cs`

**Visual（视觉表现）** `Visual/FirePit.cs`（火盆粒子火焰 + 点光闪烁）、`CapeMotion.cs`（披风顶点摆动）

**根级核心** `GameManager.cs`（菜单 / 游玩 / 暂停 / 胜利 / 失败状态机）、`Health.cs`（生命值、无敌帧、死亡）、`PlayerCombat.cs`（三段连击：前摇 / 命中窗 / 收招）、`PlayerExplorer.cs`（相机相对移动与冲刺）、`OrbitFollowCamera.cs`（环绕相机与屏震叠加）、`HUDController.cs`、`GameMenuController.cs`、`AudioPanelController.cs`（第 5 章 UGUI 音频面板）、`Collectible.cs`、`HitFlash.cs`、`TorchFlicker.cs`、`SimpleCollisionResolver.cs`、`ProceduralAudioBank.cs`

### Shader（`Assets/Shaders`）

`StylizedStone.shader`、`StylizedCharacter.shader`（含描边 Pass）、`Emissive.shader`、`Foliage.shader`、`Metal.shader`，命名空间统一为 `Ruins/`。

### 配套材料

`实验报告_江贻博.docx`（实验报告）、`视频录制脚本.md`（五段分镜）、`Docs/UPGRADE_SPEC.md`（升级规范）、`Logs/shots/`（真实渲染截图：开始界面、HUD、追击、角色近景、守卫近景、暂停面板）。

## 生成场景

1. 用 Unity Hub 打开本目录作为 Unity 2022.3 项目并等待导入（首次导入会自动编译 5 个 shader 与全部编辑器生成器）。
2. 菜单选择 `Tools > Ruins Guardian > 生成演示场景`。生成器会创建可运行场景、三层遗迹庭院、程序化 PBR 材质与 5 个风格的材质实例、分肢体人形角色、Animator Controller（含一维混合树）、后处理配置、程序化 UI 与程序化音频、3 种敌人、6 颗宝石与符文石门机关。
3. 打开 `Assets/Generated/RuinsDemo.unity` 并按 Play。
4. 可选：菜单 `Tools > Ruins Guardian > 截图` 生成新的宣传截图到 `Logs/shots`。

## Animator 参数与逻辑

| 参数 | 类型 | 用途 |
| --- | --- | --- |
| `Speed` | Float | 驱动 Idle / Walk / Run **一维混合树**（阈值 0、2、6），由实际移动速度写入 |
| `AttackTrigger` | Trigger | 进入攻击状态：玩家挥剑（三段连击各触发一次）或敌人出手 |
| `HitTrigger` | Trigger | 受伤进入受击后仰状态 |
| `IsDead` | Bool | 血量归零进入倒地状态，Any State 可转入 |

玩家与守卫共用同一套参数名，动画曲线按节点路径驱动 MeshKit 的分肢体层级（`Visual/Body/{Hips, Chest, Head, ArmL, ArmR, LegL, LegR}`，手臂下再分上臂 / 前臂 / 手 / WeaponPivot）。三段连击复用 `AttackTrigger`，靠"是否仍在连击窗口内"决定回到 Idle 还是直接切回攻击态，形成触发式状态切换而不是三个独立状态。

## 敌人有限状态机

基类 `EnemyWanderAI` 拆出 5 个 `protected virtual` 状态方法，`SentinelAI` 与 `BruteAI` 各自覆写即可复用整套 FSM：

- **Patrol（巡逻）**：沿巡逻点在遗迹间往返，视锥内发现玩家则进入追击。
- **Investigate（调查）**：丢失目标后前往最后看到玩家的位置，环顾一段时间；再次看到则追击，超时则返回。
- **Chase（追击）**：持续追向玩家；进入攻击距离转攻击，再次丢失目标转调查。
- **Attack（攻击）**：前摇结束后判定命中，随后进入冷却再判断下一步。
- **Return（返回）**：调查无果或玩家死亡后回到出生点，恢复巡逻。

### 三种敌人

| 敌人 | 行为特点 | 生命 |
| --- | --- | --- |
| **守卫 Guard** | 近战追击，标准 FSM 全套 | 3 |
| **远程哨兵 Sentinel** | 覆写攻击状态：拉开距离后发射 `EnergyBolt` 能量弹 | 3 |
| **精英 Brute** | 覆写攻击状态：两段连击（FirstWindup → Gap → SecondWindup → Recover）与 `ShockwaveRing` 震地波，掉落治疗瓶 | 8 |

## 世界与关卡布局

- **三层遗迹庭院**：主庭院（y = 0）／北侧高台（+1.6）／东侧干涸蓄水池（−0.6）。玩家靠 `LevelGround` 的射线贴地行走，可自然上下石阶、进出池底。
- **断柱回廊**（含 2 根倒塌柱）、外围高低断墙与角塔、远景剪影塔楼构成纵深；6 座火盆（粒子火焰 + 点光闪烁）、4 条伪造体积光锥、尘埃与余烬粒子负责空气感。
- 石门后的凹室内藏第 6 颗宝石与精英守卫，石门下沉后才由 `RuneGate` 激活该精英。

## 材质、Shader 与后处理

- **程序化 PBR 贴图**（`TextureKit`）：逐砖冷暖抖动、超大尺度冷暖漂移、苔痕压进砖缝与低洼、砖缝额外压暗；albedo / normal / roughness 三通道齐备。
- **5 个自写风格化 shader**：`Ruins/StylizedStone`（三平面细节 + 自建阴影坐标）、`Ruins/StylizedCharacter`（分块上色 + 描边 Pass）、`Ruins/Emissive`、`Ruins/Foliage`（alpha 裁切）、`Ruins/Metal`。
- **后处理**（PPSv2，`RuinsPostProcessProfile.asset`）：Bloom、ACES 色彩分级、暗角、胶片颗粒、色差、环境光遮蔽。
- **光照**：暖主光（软阴影）+ 冷补光 + 三层环境光（天冷地暖）+ 指数雾。

## 战斗手感

三段连击的每一段都有独立的**前摇 / 命中窗口 / 收招**与扇形范围、前冲位移；命中时 `JuiceDirector` 把 `Time.timeScale` 压到 **0.05** 持续 **70 ms** 再平滑回弹（重击更长），同时按 trauma² 曲线叠加屏震；闪避带 0.22 秒无敌帧与淡出的 3 重残影；另有命中火花、地面灰尘环、受击硬直与击退、死亡倒地坍缩。

## UI 与音频

- **UI**：斜切分段血条（延迟掉血两层）、宝石计数、顶部目标条、准心、闪避冷却环、连击计数、伤害飘字（暴击变色）、拾取提示、锁定框；卡片式开始 / 暂停 / 结算面板带淡入 + 缩放 + 扫光；音频面板可折叠。UI 只通过 `CombatEvents` 取战斗状态，不直接引用战斗脚本。
- **音频**：全程序化合成。三层 BGM（探索 / 紧张 / 环境）按最近敌人距离与是否被发现交叉淡化，12 秒同相位无缝循环；21 种音效含剑挥 3 变体、金属命中 5 音高、血肉命中、脚步（石 / 草材质区分）、闪避、符文、开门、UI、心跳；低血量触发心跳，远处有随机钟声。

## 截图

`Logs/shots/` 下为真实渲染截图：`play_01_menu.png`（开始界面）、`play_02_hud.png`（遗迹庭院与 HUD）、`play_03_chase.png`（追击）、`play_04_closeup.png`（角色近景）、`play_05_guard.png`（守卫近景）、`play_06_pause.png`（暂停面板）。

场景与全部资产均由程序化几何体、程序化贴图与程序化音频生成，未使用任何外部素材；替换为 Humanoid 模型时只需替换 MeshKit 产出的网格并保留同名 Animator 参数即可。
