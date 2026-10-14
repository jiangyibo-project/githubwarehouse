# GalCode - 对话模拟系统

一个基于 Tkinter 的桌面 GalGame 风格对话模拟程序，内置离线对话数据库，也可接入 DeepSeek 大模型进行在线对话，包含角色切换、关系度系统、对话历史等功能。

## 运行环境

- Python 3.10 及以上
- 依赖库：requests（Tkinter 为 Python 自带）

## 运行方法

1. 安装依赖：`pip install -r requirements.txt`
2. 复制 `.env.example` 为 `.env`，填入你自己的 DeepSeek API Key；不填写也可以运行，程序会自动使用离线模式。
3. 启动程序：`python galcode.py`

## 文件说明

- `galcode.py`：程序主文件，包含界面、角色逻辑和 API 调用
- `galcode.spec`：PyInstaller 打包配置，可用 `pyinstaller galcode.spec` 打包成 exe
- `.env.example`：环境变量模板，真实的 `.env` 不会被提交

## 安全提示

API Key 属于敏感信息，请勿将其写入代码或提交到公开仓库。
