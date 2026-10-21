# Ruins Guardian V2 升级记录

## 一、结论

| 项目 | 结果 |
| --- | --- |
| 编译（Assembly-CSharp / Assembly-CSharp-Editor） | **0 error**（5 个自定义 shader 全部通过 d3d11 编译） |
| Play 模式冒烟测试 | **SMOKE PASS**（受伤、击杀、拾取、胜利全流程） |
| 场景生成 | 批处理 `BuildDemoScene.Build` 全自动、幂等 |
| 截图验收 | `Logs/shots/play_01..06.png`（1600×900，Play 模式实拍） |

> 复现命令（工程根目录）：
> `Unity.exe -batchmode -projectPath <工程> -executeMethod BuildDemoScene.Build -quit`
> 再执行 `-executeMethod PlayModeSmokeTest.Start` 跑冒烟，`-executeMethod PlayCapture.Capture` 出截图。

## 二、模块与文件（V2 新增）

| 模块 | 关键文件 |
| --- | --- |
| 角色 | `Editor/MeshKit.cs`、`Editor/BuildDemoScene.Characters.cs`、`Scripts/CapeMotion.cs` |
| 世界 | `Editor/TextureKit.cs`、`Editor/BuildDemoScene.World.cs`、`Editor/BuildDemoScene.Lighting.cs`、`Scripts/Visual/FirePit.cs` |
| 渲染 | `Shaders/*.shader`（5 个）、`Editor/ShaderKit.cs` |
| 战斗手感 | `Scripts/Combat/{CombatEvents,JuiceDirector,PlayerDodge,TargetLock,ImpactFx}.cs` |
| UI | `Scripts/UI/*.cs`（6 个）、`Editor/BuildDemoScene.Ui.cs` |
| 音频 | `Scripts/Audio/*.cs`（4 个）、`Editor/BuildDemoScene.Audio.cs` |
| 关卡与 AI | `Scripts/Level/*.cs`（7 个）、`Scripts/AI/*.cs`（5 个）、`Editor/BuildDemoScene.Level.cs` |

生成器被拆成 `public static partial class BuildDemoScene` 的 6 个文件，`Build()` 只负责按顺序调用；任一模块都可单独回滚。

## 三、集成接线（`Build()` 内的调用顺序）

```
ConfigureEnvironmentV2()   ← 天空盒 / 三层环境光 / 雾 / 主光 + 冷补光
BuildWorldV2()             ← 三层地形（必须第一个跑，初始化贴图与材质缓存）
BuildDecorV2() / BuildAltarV2() / BuildAmbientV2()
BuildCharacterV2(...)      ← 玩家；再补 Health / PlayerExplorer / PlayerCombat / PlayerDodge / TargetLock
BuildGuardV2(...) ×3
BuildGems(...) → BuildGameplayV2(...)   ← 地形碰撞、符文机关、石门、增援、复活
BuildCamera(...) / BuildPostProcessObject(...)
BuildUiAndManagers(...)    ← HUD + 菜单 + 音频面板 + GameManager
BuildAudioV2(...)          ← 三层 BGM 与全部音效
AttachCombatFeel(...)      ← JuiceDirector / ImpactFx / 相机引用注入
```

## 四、过程中修掉的真实缺陷

1. **画面先"死黑"后"过曝"**：根因是三层叠加——环境光 0.7、后处理曝光、以及**贴图已烘焙石材基色而材质又乘了一次偏暗 tint**（墙面反照率只剩 0.34）。修法：tintA 提到接近白（由 tintB 承担暗部）+ 曝光/对比/饱和重新标定，最终停在"暖主光 + 冷阴影"的对撞区间。
2. **5 个 shader 全部编译失败**：`SHADOW_COORDS` 在 AutoLight.cginc 里只有 4 处条件定义，而 `multi_compile_shadowcaster` 的 `SHADOWS_DEPTH`-only 变体正好落在四者的缝隙里。改成自建 `RUINS_SHADOW_COORDS` 让 `_ShadowCoord` **恒定存在**（而非空宏兜底），结构上杜绝了同类错误。
3. **UI"金字压金底"其实不是字色错**：解析场景发现字本来就是 #14161F 深色，糊是因为金色径向光晕盖在字心 + ColorBlock 把底色压到 0.78。修法：亮金底 #FFC078 + 近黑粗体字 + 光晕 0.22→0.10，对比度 6.1:1 → 11.5:1。
4. **V2 地形原本没有任何碰撞体**（铺装是纯装饰网格）：由关卡层补了一层不可见地板，否则射线贴地会直接穿过去。
5. **角色/敌人身上的“黑色粒子”（用户实测反馈）**：其实是**五个独立成因叠在一起**，逐个查证后分别修掉——
   - ① 描边 pass 用硬边顶点法线外扩，MeshKit 是逐三角形独立顶点，相邻面法线不同 → 棱角处炸成黑色碎三角。修法：MeshKit 额外生成**位置合并 + 面积加权**的平滑法线写入 uv3（SetUVs(2) / TEXCOORD2），仅描边 pass 使用，主 pass 保持硬边。
   - ② 描边值 0.026 在 1080p 是 **28px 全宽**，20m 外角色只有约 30px 高 → 整个人被描边包成黑剪影。修法：玩家 0.008 / 守卫 0.007，并给描边加**视距衰减**（_OutlineRefDist / _OutlineMinScale）。
   - ③ 描边在 NDC 空间外扩是**各向异性**的（水平比垂直粗 1.78 倍）。修法：先转到像素空间归一化再外扩，做到像素等宽。
   - ④ 双面薄片（披风、布幡、草卡）背面法线朝内，卡通光照下 NdotL 为负 → 全黑。修法：四个 shader 的光照片元加 VFACE（fixed facing : VFACE）翻转法线。
   - ⑤ 近黑细窄件（通风槽宽 16mm）比描边 18mm 还窄，被描边吞成黑疙瘩。修法：这些件单独用 _OutlineWidth = 0 的材质。
6. **地面上的黑色小三角**：不是法线问题，是**零厚度苔藓薄片（离地 24mm）被自己的阴影贴图渗出黑边**（shadowBias 0.03 + 42° 主光，投影偏移仅 2.2cm）。修法：苔藓改成 0.018~0.026m 实心薄盒 + 关闭自身投影；草卡/爬藤同样关投影。
7. **玩家能看到地图外（漂浮岛屿感）**：根因不止“墙矮”——相机 5.9m 高、俯角 40° 时画面顶边在水平线下方 10°，真正看到的“地平线”是 84m 台地在 42m 处的**板边**。修法三层：墙体 1.2m → **6.4~8.56m**（无通底缺口）、台地 84m → **220m**、远景层加 13 座塔楼与 12 条断崖（30~90m）+ 雾密度 0.017 → **0.027**。
8. **相机穿进围墙**（墙加高后暴露）：后撤 4.68m 会撞进 8m 角塔，画面变成一片墙面。修法：相机加 **SphereCast 避障**（命中带 Health 的碰撞体则跳过，收缩 12/s、恢复 4/s，压到 2m 内自动抬高俯视）。
14. **角色卡住出不去 + 移动/攻击时模型扭曲（用户实测反馈）**：
    - 卡住：SimpleCollisionResolver 旧版把推出方向 y 清零、只迭代 2 次、无脱困兜底，角色在护栏/台阶/池沿的平行窄缝会永久卡死。修法 V3：迭代 5 次、保留 3D 分离方向、CapsuleCast 先探再走（分轴剔除）、0.5s 脱困兜底（反推 0.6m→8 方向→抬 0.4m）；关卡侧填掉 4 处 0.28~0.6m 致命窄缝、3 块石碑换址。
    - 模型：动画曲线用 localRotation.x 写入的是四元数分量而非欧拉角，移动摆臂摆腿/攻击挥臂时四肢诡异扭曲（玩家与敌人共用控制器）。修法：6 处动画绑定改 localEulerAnglesRaw.x。
    - 排查经验：批处理 PlayMode 测试在多次运行后进不去播放模式（打开场景即退出），改用编辑模式反射探针确认击杀伤害链健康（3 击击杀守卫）。
9. **画面调性偏冷偏暗、局部过曝（像素级复测发现）**：Bloom 7（正常 1~3）糊爆 + temperature 4 + lift 蓝偏导致 57%~86% 冷色。修法：Bloom 2.8/阈值 1.14、postExposure 0.44、temperature 14、saturation 30、contrast 22、fill 0.85、fog 偏青；尘埃及余烬粒子重写（尘埃 700 + 6 组火盆余烬）。复测：主视角暖色 48%、暗区 18.4%→11.8%、过曝→2.2%。
10. **长条构件木纹根因**：AddBoxWithUv 的 V 轴按厚度缩放，台基/梁贴图沿长边被拉 20 倍以上。修法：平板类改 AddFlatSlab、梁柱改世界尺寸 UV，共 18 处；高台台体换世界尺寸 UV 消除近景糊面板。
11. **角色像未上色占位体**：三阶明暗没拉起来 + rim 被暖橙覆盖。修法：Character 三阶 ramp（_RampEdge/_RampHiEdge/_Darken）+ 冷色 rim（#6E8CD8/#5270C7 按材质分档）+ 主光 2.0→1.5（防钢甲 clamp 白）+ 玩家前上聚光灯不投影只照角色（解决逆光纯色，点光会烤亮地面已弃用）。
12. **远景山脊像集装箱墙**：平顶盒 + 竖缝清晰。修法：主体+坡顶拆分、收窄 30~40%、随机斜切、块宽 16m→11m、压暗 6~15%。
13. **苔藓硬边与植被黑斑**：苔藓 3 片→5~6 片且外圈随机收窄；植被卡片缩小并四向交叉；草色提亮 9%。

15. **最右侧柱子处卡住（用户实测）**：右半场还残留 4 类 0.30~0.42m 平行窄缝/包围凹角（东墙扶壁↔东南角塔、祭坛顶台↔四角符文柱、东火盆↔东北塔内角、石门扶壁上方 0.35m 挤压空腔）。修法：新增可见倒角封堵体——塔基残石 东南/西南/西北、祭坛柱础×4、塔基碎石、侧翼扶壁加高到与踏面底面严丝合缝；场景重生成，BoxCollider 总量 186。
16. **主界面 + 清晰字体**：新增运行期主界面（深色底 + 遗迹守卫标题 + 开始游戏/退出游戏，覆盖旧开始面板）；字体统一走 OS 系统字体（微软雅黑优先，回退黑体/等线/宋体），字号 ×1.1，亮色文字补黑色 Outline。全部为运行期对象，不依赖生成脚本。

17. **蓄水池池底内角卡死（用户截图复报）**：池底 y=-0.6 的西北内角，西池沿内表面 x=5.16 + 北池沿 z=4.04，从池底看是 1.02m 高的实心直角（远超 0.36m 台阶上限），两轴先探再走全挡 → 前后都出不去。修法：4 处 45° 倒角石墩（池角斜切 西北/东北/东南/西南，0.95×1.02×0.26，与两侧池沿咬合 0.15~0.20）+ 2 处池边矮台端头倒角；全场探针把「站得住但八向全挡」的死角从 2 格（西回廊 z=3.10）清到 0，补了一个回廊落石。
18. **卡死自救最后一层**：新增「无穿透卡死」检测——主角有移动输入、连续 1.2s 水平位移 <5cm 且至少 2 个方向被高墙挡住（单面贴墙不误触）→ 先试反推/绕圈/抬高；再 0.5s 无效就回退到最近安全点（每 0.6s 记录，回退前查路径隔墙与候选点是否嵌墙）。配合原有「穿透 >2cm」路径，任意死角最迟约 1.7s 自救，永不完全卡死。

19. **蓄水池改为整体平地（用户要求）**：两次卡死都在池内，池底 y=-0.60、池沿从池底看 1.02m 高，改缝不如直接填平。池底抬到 y=0（与庭院齐平）、池沿矮沿降到 0.26m（可直接跨过）、删除内壁/下沉台阶/下水石台/池边台座等 11 个碰撞体，池底石碑与光柱整体抬到 y=0；地面高度自检：下沉格 0、全场八向全挡死角 0。
20. **新增跳跃 + 闪避改键**：空格跳跃（跳高约 0.92m，可越过池沿/矮台），原空格闪避改到 Q；HUD 键位提示、主界面提示、底部操作说明同步更新。跳跃复用 LevelGround 的重力/落地逻辑，不影响原有贴地与台阶。自救再加强：安全点只记录八向开阔位；回退失败或 0.5s 内再次卡住直接回出生点。

21. **FPS 式转向 + 手感**：相机不再要求按住右键拖拽，鼠标移动直接旋转视角（灵敏度水平 3.2 度/单位、纵向 0.85 倍，留了反转 Y 开关；Z/C 键盘仍可辅助），游戏中自动锁定光标、菜单/暂停释放；底部提示与相机的过时文案同步为「鼠标移动旋转视角」。滚轮缩放、平滑跟随、锁敌拉近与屏震手感保留。

## 五、已知限制与后续建议

- ~~高台台面不可达~~ **已解决**：正面护栏在两条石阶顶端各开 1.25m 缺口（净通行 1.19m），并补上台面碰撞补齐块（只加碰撞不加几何），末级台阶 → 踏面只抬 0.385m，全路径同高无断层。
- ~~相机 y 下限 0.4m~~ **已解决**：下限改为相对式 `target.position.y + 0.4`（字段 `minHeightAboveTarget`），平地行为不变，池底跟随不再被夹。
- ~~E 键与相机 Q/E 转视角重名~~ **已解决**：相机旋转键迁到 `Z` / `C`（可序列化字段 `rotateLeftKey`/`rotateRightKey`），`E` 专用于符文激活，`F` 仍作备用。
- **Brute 的"霸体"是 0.25s 无敌**（Health 没有硬直抗性接口）：代价是该窗口内也不掉血。
- **色彩空间仍是 Gamma**：切 Linear 会让 ACES/Bloom 层次更好，但必须同时给 TextureKit 的法线贴图加 sRGB 编码补丁（否则平法线会被解成斜 16°）并重标定全部曝光参数，属中高风险改动，本阶段未做。
- **UI 约 170 个 draw call**（每元素独立贴图），后续可上 SpriteAtlas 合批。
