# githubwarehouse

个人学习项目仓库，存放课程作业与独立完成的小项目。每个项目都放在独立文件夹中，文件夹内有各自的 README，可按说明直接运行。

## 项目一览

### 1. [shape-maze](./shape-maze) — 3D 形状迷宫游戏（Unity）

用 Unity 制作的 3D 迷宫探险小游戏：随机生成迷宫，玩家利用变形系统穿过形状门，躲避陷阱和敌人，拾取道具并找到终点。

- 技术栈：Unity 2022.3 LTS、C#、Input System
- 运行方式：用 Unity Hub 以 `shape-maze` 文件夹打开工程，打开 `Assets/Scenes/SampleScene.unity` 后点击 Play
- 支持一键打包 Windows exe（菜单 Build → Windows x64 Release）

### 2. [deeplearningfromzero](./deeplearningfromzero) — 纯 numpy 从零实现神经网络

不使用任何深度学习框架，只用 numpy 手写两层全连接神经网络，在手写数字数据集上完成识别，测试集精度约 92.2%。包含数值梯度校验、反向传播、训练过程动画和完整可视化。

- 技术栈：Python、numpy、scikit-learn、matplotlib
- 运行方式：`pip install -r requirements.txt` 后 `jupyter notebook deeplearningfromzero.ipynb`

### 3. [galcode](./galcode) — GalGame 风格对话模拟系统（Python）

Tkinter 桌面程序，可接入 DeepSeek 大模型在线对话，也可使用内置本地对话库离线运行，含角色切换、关系度系统和对话记录。

- 技术栈：Python、Tkinter、requests
- 运行方式：`pip install -r requirements.txt` 后 `python galcode.py`；不配置 API Key 时自动使用离线模式

### 4. [ruins-guardian](./ruins-guardian) — 遗迹守卫（Unity 第三人称动作）

程序化生成的第三人称动作游戏：玩家潜入遗迹庭院，收集 6 颗遗迹宝石、激活 3 块符文石碑开启北侧石门，在守卫、哨兵与精英的围堵下完成试炼。场景、材质、低多边形角色、动画、战斗手感、UI 与音频全部由编辑器脚本一键生成，零外部素材。

- 技术栈：Unity 2022.3 LTS、C#、Built-in RP、UGUI、Post Processing Stack v2
- 运行方式：用 Unity Hub 以 `ruins-guardian` 文件夹打开工程，打开 `Assets/Generated/RuinsDemo.unity` 后点击 Play
- 操作：WASD 移动、Shift 冲刺、空格跳跃、Q 闪避、左键攻击、Tab 锁定、E 激活符文、鼠标直接转视角
- 场景可随时用菜单 `Tools > Ruins Guardian > 生成演示场景` 一键重建

## 仓库目录结构

```
githubwarehouse/
├── shape-maze/              # Unity 3D 迷宫游戏
│   ├── Assets/              # 游戏场景、C# 脚本、编辑器扩展
│   ├── Packages/            # Unity 包依赖
│   └── ProjectSettings/     # Unity 工程配置
├── deeplearningfromzero/    # 深度学习从零开始实验
│   ├── deeplearningfromzero.ipynb
│   ├── build_notebook.py
│   └── train_animation.gif
├── ruins-guardian/          # 遗迹守卫（Unity 第三人称动作）
│   ├── Assets/              # 场景、C# 脚本、编辑器扩展
│   ├── Packages/            # Unity 包依赖
│   └── ProjectSettings/     # Unity 工程配置
├── galcode/                 # GalCode 对话模拟系统
│   ├── galcode.py
│   └── galcode.spec
└── README.md                # 本文件
```

## 通用说明

- Python 两个项目已在干净环境中按各自 README 的步骤实际运行验证；Unity 项目未在本机完整编译，按说明用对应版本编辑器打开即可
- 虚拟环境、构建产物和本地缓存均已通过 `.gitignore` 排除，clone 后请按各项目说明自行安装依赖
- 密钥等敏感信息通过 `.env` 文件本地配置，仓库中只提供 `.env.example` 模板，请勿将真实密钥提交到仓库
