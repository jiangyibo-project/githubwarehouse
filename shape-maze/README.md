# Shape Maze 形状迷宫

一个用 Unity 制作的 3D 迷宫探险小游戏。迷宫每次随机生成，玩家需要在陷阱、移动尖刺和敌人的干扰下，利用变形系统穿过形状门，拾取道具并找到迷宫终点。

## 运行环境

- **Unity 编辑器版本：2022.3 LTS**（本工程使用 `2022.3.62f3c1` 保存，安装同版本或更新的 2022.3 LTS 均可打开；打开 `ProjectSettings/ProjectVersion.txt` 可查看精确版本号）
- 主要依赖包：Input System 1.14.2、TextMesh Pro 3.0.7（版本已锁定在 `Packages/packages-lock.json`，首次打开时 Unity 会自动拉取，无需手动安装）
- 操作系统：Windows 10 / 11（开发与打包目标为 Windows x64）

## 如何在编辑器中运行

1. 安装 [Unity Hub](https://unity.com/cn/download)，在 Hub 中安装一个 **Unity 2022.3 LTS** 版本，安装时勾选 "Windows Build Support (IL2CPP)"
2. Unity Hub 中点击「添加 / Add → 从磁盘添加」，选择本 `shape-maze` 文件夹（注意是包含 Assets、Packages、ProjectSettings 的这一级）
3. 首次打开工程时 Unity 会自动导入资源和包，需要等待几分钟
4. 在 Project 窗口中双击打开 `Assets/Scenes/SampleScene.unity`，点击顶部的 **Play（播放）按钮**即可开始游戏

`Library/`、`Temp/` 等目录是 Unity 自动生成的本地缓存，不包含在仓库中，首次打开会自动重建，属于正常现象。

## 操作方式（默认按键，可在游戏内自行改键）

- `W / A / S / D`：前后左右移动
- `空格`：跳跃
- `F`：变形（切换形状以通过对应的形状门）
- `E`：使用道具
- `1`：在脚下放置标记（防止迷路）
- `Q`：限时俯视视角
- `M`：透视模式（看穿假墙）
- `J`：射击
- `Tab`：显示 / 锁定鼠标
- `Esc`：返回主菜单

## 游戏玩法

- 迷宫由 `MazeGenerator` 随机生成，找到发光的终点门即可通关
- 部分通道是 `FakeWall` 假墙，部分门需要用 `F` 变成特定形状才能通过
- 小心移动尖刺和巡逻敌人，掉落迷宫会自动重置
- 拾取道具可以回复生命或获得射击能力

## 如何打包成 Windows 可执行文件

- 方式一（推荐）：工程已内置一键打包脚本，在 Unity 顶部菜单选择 **Build → Windows x64 Release (1.0)**，产物输出到 `shape-maze/Builds/Windows/ShapeMaze-1.0.exe`
- 方式二：`File → Build Settings`，确认 Scenes 列表中有 SampleScene，平台选 Windows，点击 Build

## 目录结构

- `Assets/Scenes/`：游戏场景
- `Assets/scripts/`：全部游戏逻辑 C# 脚本（玩家控制、迷宫生成、敌人、道具、变形系统等）
- `Assets/Editor/`：编辑器一键打包脚本
- `Packages/`：Unity 包依赖清单
- `ProjectSettings/`：Unity 工程配置（输入、画质、物理、音频等）
