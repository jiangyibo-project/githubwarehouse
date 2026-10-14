"""
GalCode - 对话模拟系统
=======================

一个 GalGame 风格的桌面对话程序：
- 界面使用 Python 自带的 Tkinter 实现，无需额外 GUI 框架；
- 在线模式接入 DeepSeek 大模型（OpenAI 兼容接口），由 AI 扮演角色与玩家对话；
- 离线模式使用内置的本地对话数据库（随机抽取话题、动作与回应），无需网络和密钥；
- 包含角色切换、关系度系统、对话轮次记录、新话题 / 求助 / 分享等互动功能。

API Key 从环境变量 DEEPSEEK_API_KEY 或程序同目录的 .env 文件中读取，
禁止把密钥硬编码在本文件中。直接运行：python galcode.py
"""

import tkinter as tk
from tkinter import ttk, scrolledtext, messagebox
import requests
import json
import os
from datetime import datetime
import threading
import re
import random


class GalCodeApp:
    """GalCode 主应用类，负责界面构建、对话逻辑和 API 调用。"""

    def __init__(self, root):
        """初始化应用：加载角色设定、API 配置、本地对话库并构建界面。

        参数 root：Tkinter 根窗口（tk.Tk 实例）。
        """
        self.root = root
        self.root.title("GalCode - 对话模拟系统")
        self.root.geometry("850x800")
        self.root.configure(bg='#ffffff')

        # 角色设定
        self.characters = {
            "wenjing": {
                "name": "林文静",
                "description": "文静好学的同学，喜欢阅读和音乐",
                "color": "#4a90e2",
                "greetings": [
                    "你好，今天天气不错，要一起学习吗？📚",
                    "嗨，你来了。我正在看这本书，很有趣。",
                    "你好呀，我刚好有个问题想请教你。"
                ]
            },
            "zhiyuan": {
                "name": "王志远",
                "description": "开朗活跃的同学，擅长运动和编程",
                "color": "#2ecc71",
                "greetings": [
                    "嘿！今天感觉怎么样？要一起讨论项目吗？💻",
                    "你好，我有个新想法想和你聊聊。",
                    "正好你来了，我们可以继续昨天的讨论。"
                ]
            }
        }

        # 当前角色
        self.current_character = "wenjing"

        # API配置（密钥从环境变量或同目录 .env 文件读取，禁止写死在代码里）
        self.api_key = os.environ.get("DEEPSEEK_API_KEY", "")
        if not self.api_key:
            _env_file = os.path.join(os.path.dirname(os.path.abspath(__file__)), ".env")
            if os.path.exists(_env_file):
                with open(_env_file, encoding="utf-8") as _f:
                    for _line in _f:
                        _line = _line.strip()
                        if _line.startswith("DEEPSEEK_API_KEY="):
                            self.api_key = _line.split("=", 1)[1].strip().strip('"').strip("'")
        self.api_url = "https://api.deepseek.com/v1/chat/completions"
        self.model = "deepseek-chat"

        # 本地对话数据库
        self.dialogue_db = self.create_dialogue_database()

        # 对话历史
        self.conversation_history = []

        # 关系度系统
        self.relationship = 50  # 初始50，范围0-100
        self.max_relationship = 100
        self.min_relationship = 0

        # 当前对话轮次
        self.turn_count = 0

        # 是否使用本地模式
        self.use_local_mode = False
        self.force_local_mode = False  # 用户强制使用离线模式

        # 初始化UI
        self.setup_ui()

        # 开始对话
        self.root.after(100, self.start_conversation)

    def create_dialogue_database(self):
        """创建对话数据库"""
        return {
            "greetings": [
                "你好，今天过得怎么样？",
                "很高兴见到你，最近在忙什么？",
                "你来了，正好我有事情想和你商量。"
            ],
            "topics": [
                {
                    "name": "学习交流",
                    "responses": [
                        "最近我在学习Python，感觉挺有意思的。",
                        "你有看过那本关于算法的书吗？我觉得很有启发。",
                        "我们上次讨论的项目进展如何了？"
                    ]
                },
                {
                    "name": "日常分享",
                    "responses": [
                        "今天看到一件很有趣的事情...",
                        "我最近发现了一个很好的学习方法。",
                        "周末你有什么计划吗？"
                    ]
                },
                {
                    "name": "问题讨论",
                    "responses": [
                        "关于这个问题，我有一些不同的想法。",
                        "你觉得我们该如何解决这个难题？",
                        "我遇到了一个技术问题，想听听你的意见。"
                    ]
                },
                {
                    "name": "兴趣交流",
                    "responses": [
                        "你平时有什么兴趣爱好吗？",
                        "最近有看什么有趣的电影或书籍吗？",
                        "我最近开始学习摄影，感觉很有趣。"
                    ]
                }
            ],
            "actions": [
                "认真地看着你",
                "思考了一下",
                "翻开笔记本",
                "微笑说道",
                "点点头",
                "拿出手机"
            ],
            "follow_ups": [
                "你有什么想法吗？",
                "你觉得怎么样？",
                "我想听听你的意见。",
                "我们可以一起研究一下。"
            ]
        }

    def generate_local_response(self, context=""):
        """生成本地响应"""
        character = self.characters[self.current_character]

        if not context or self.turn_count == 0:
            # 开场问候
            greeting = random.choice(character["greetings"])
            return f"{character['name']}：{greeting}"
        else:
            # 随机选择话题和回应
            topic = random.choice(self.dialogue_db["topics"])
            response = random.choice(topic["responses"])
            action = random.choice(self.dialogue_db["actions"])
            follow_up = random.choice(self.dialogue_db["follow_ups"])

            return f"{character['name']}：{action}，「{response} {follow_up}」"

    def generate_local_options(self):
        """生成本地选项"""
        options = []

        # 对话的不同风格选项
        option_types = [
            {"type": "积极", "base_score": 8, "texts": [
                "热情地回应对方的分享",
                "提出建设性的建议",
                "分享自己的相关经验"
            ]},
            {"type": "理性", "base_score": 6, "texts": [
                "分析问题的关键点",
                "提出客观的看法",
                "引用相关数据或事实"
            ]},
            {"type": "友善", "base_score": 5, "texts": [
                "表达理解和关心",
                "鼓励对方继续分享",
                "分享轻松的话题"
            ]},
            {"type": "直接", "base_score": 4, "texts": [
                "直接表达自己的看法",
                "提出明确的问题",
                "简洁地回应"
            ]}
        ]

        for i in range(3):
            # 随机选择一种类型
            option_type = random.choice(option_types)
            text_template = random.choice(option_type["texts"])

            # 根据关系度微调分数
            score_modifier = random.randint(-2, 2)
            score = max(1, min(10, option_type["base_score"] + score_modifier))

            # 生成具体的选项文本
            if option_type["type"] == "积极":
                action = random.choice(["热情地", "兴奋地", "积极地"])
                text = f"{action}：「{text_template}」"
            elif option_type["type"] == "理性":
                action = random.choice(["分析道", "思考后说", "理性地"])
                text = f"{action}：「{text_template}」"
            elif option_type["type"] == "友善":
                action = random.choice(["友善地", "温和地", "关心地"])
                text = f"{action}：「{text_template}」"
            else:
                action = random.choice(["直接地", "简洁地", "明确地"])
                text = f"{action}：「{text_template}」"

            options.append({
                "text": f"[{option_type['type']}] {text}",
                "score": score,
                "type": option_type["type"]
            })

        return options

    def setup_ui(self):
        """设置用户界面"""
        # 标题
        title_label = tk.Label(
            self.root,
            text="GalCode - 智能对话系统",
            font=("微软雅黑", 20, "bold"),
            bg='#4a90e2',
            fg='white',
            height=2,
            width=60
        )
        title_label.pack(fill=tk.X, padx=10, pady=10)

        # 模式选择区域
        mode_frame = tk.Frame(self.root, bg='#f8f9fa')
        mode_frame.pack(fill=tk.X, padx=20, pady=5)

        # 模式选择标签
        mode_label = tk.Label(
            mode_frame,
            text="选择对话模式:",
            font=("微软雅黑", 10, "bold"),
            bg='#f8f9fa',
            fg='#2c3e50'
        )
        mode_label.pack(side=tk.LEFT, padx=(0, 10))

        # 在线模式单选按钮
        self.online_mode_var = tk.BooleanVar(value=True)  # 默认在线模式

        online_radio = tk.Radiobutton(
            mode_frame,
            text="在线模式 (使用AI)",
            font=("微软雅黑", 10),
            bg='#f8f9fa',
            variable=self.online_mode_var,
            value=True,
            command=self.select_online_mode
        )
        online_radio.pack(side=tk.LEFT, padx=5)

        # 离线模式单选按钮
        offline_radio = tk.Radiobutton(
            mode_frame,
            text="离线模式 (本地对话)",
            font=("微软雅黑", 10),
            bg='#f8f9fa',
            variable=self.online_mode_var,
            value=False,
            command=self.select_offline_mode
        )
        offline_radio.pack(side=tk.LEFT, padx=5)

        # API状态标签
        self.api_status_label = tk.Label(
            mode_frame,
            text="",
            font=("微软雅黑", 9),
            bg='#f8f9fa',
            fg='#27ae60'
        )
        self.api_status_label.pack(side=tk.RIGHT, padx=10)

        # 角色信息区域
        character_frame = tk.Frame(self.root, bg='#f8f9fa')
        character_frame.pack(fill=tk.X, padx=20, pady=5)

        self.character_label = tk.Label(
            character_frame,
            text="当前对话角色：林文静",
            font=("微软雅黑", 12, "bold"),
            bg='#f8f9fa',
            fg='#2c3e50'
        )
        self.character_label.pack(side=tk.LEFT, padx=10)

        # 角色切换按钮
        switch_char_btn = tk.Button(
            character_frame,
            text="切换角色",
            font=("微软雅黑", 10),
            bg='#95a5a6',
            fg='white',
            command=self.switch_character
        )
        switch_char_btn.pack(side=tk.RIGHT, padx=10)

        # 关系度条框架
        relationship_frame = tk.Frame(self.root, bg='#ffffff')
        relationship_frame.pack(fill=tk.X, padx=20, pady=10)

        # 关系度标签
        self.relationship_label = tk.Label(
            relationship_frame,
            text=f"关系度: {self.relationship}/100",
            font=("微软雅黑", 12),
            bg='#ffffff',
            fg='#2c3e50'
        )
        self.relationship_label.pack(anchor=tk.W, pady=5)

        # 关系度进度条
        style = ttk.Style()
        style.theme_use('clam')
        style.configure("Relationship.Horizontal.TProgressbar",
                        background='#3498db',
                        troughcolor='#ecf0f1',
                        bordercolor='#bdc3c7',
                        lightcolor='#3498db',
                        darkcolor='#2980b9')

        self.relationship_bar = ttk.Progressbar(
            relationship_frame,
            style="Relationship.Horizontal.TProgressbar",
            length=400,
            mode='determinate',
            maximum=100
        )
        self.relationship_bar.pack(fill=tk.X, pady=5)
        self.update_relationship_display()

        # 对话显示区域
        self.conversation_frame = tk.Frame(self.root, bg='#ffffff')
        self.conversation_frame.pack(padx=20, pady=10, fill=tk.BOTH, expand=True)

        # 添加装饰边框
        conversation_border = tk.Frame(self.conversation_frame, bg='#e0e0e0')
        conversation_border.pack(fill=tk.BOTH, expand=True, padx=1, pady=1)

        self.conversation_text = scrolledtext.ScrolledText(
            conversation_border,
            wrap=tk.WORD,
            width=70,
            height=15,
            font=("微软雅黑", 11),
            bg='#f8f9fa',
            fg='#2c3e50',
            relief=tk.FLAT,
            borderwidth=0
        )
        self.conversation_text.pack(fill=tk.BOTH, expand=True, padx=2, pady=2)

        # 添加一些装饰文本标签
        tk.Label(
            conversation_border,
            text="💬 对话记录",
            font=("微软雅黑", 9),
            bg='#e0e0e0',
            fg='#7f8c8d'
        ).pack(side=tk.BOTTOM, fill=tk.X)

        # 分数和对话轮次显示
        info_frame = tk.Frame(self.root, bg='#ffffff')
        info_frame.pack(padx=20, pady=5)

        self.turn_label = tk.Label(
            info_frame,
            text="对话轮次: 0",
            font=("微软雅黑", 11),
            bg='#ffffff',
            fg='#7f8c8d'
        )
        self.turn_label.pack(side=tk.LEFT, padx=10)

        # 选项按钮框架
        self.options_frame = tk.Frame(self.root, bg='#ffffff')
        self.options_frame.pack(padx=20, pady=10, fill=tk.BOTH, expand=True)

        # 选项标题
        options_title = tk.Label(
            self.options_frame,
            text="📝 选择你的回应（影响关系度）：",
            font=("微软雅黑", 11, "bold"),
            bg='#ffffff',
            fg='#2c3e50'
        )
        options_title.pack(anchor=tk.W, pady=5)

        # 创建三个选项按钮
        self.option_buttons = []
        button_colors = ['#e3f2fd', '#e8f5e9', '#f3e5f5']

        for i in range(3):
            btn_frame = tk.Frame(self.options_frame, bg='#ffffff')
            btn_frame.pack(pady=8, fill=tk.X)

            # 选项编号装饰
            num_label = tk.Label(
                btn_frame,
                text=f"选项 {i + 1}",
                font=("微软雅黑", 10, "bold"),
                bg='#95a5a6',
                fg='white',
                width=8
            )
            num_label.pack(side=tk.LEFT, padx=(0, 10))

            btn = tk.Button(
                btn_frame,
                text=f"选项 {i + 1}",
                font=("微软雅黑", 11),
                bg=button_colors[i],
                fg='#2c3e50',
                height=2,
                width=45,
                wraplength=550,
                state=tk.DISABLED,
                relief=tk.RAISED,
                borderwidth=2,
                command=lambda idx=i: self.select_option(idx)
            )
            btn.pack(side=tk.LEFT, fill=tk.X, expand=True)
            self.option_buttons.append(btn)

        # 状态标签
        self.status_label = tk.Label(
            self.root,
            text="💭 正在连接对话系统...",
            font=("微软雅黑", 10),
            bg='#ffffff',
            fg='#7f8c8d'
        )
        self.status_label.pack(pady=5)

        # 控制按钮框架
        control_frame = tk.Frame(self.root, bg='#ffffff')
        control_frame.pack(pady=10)

        help_btn = tk.Button(
            control_frame,
            text="📚 寻求帮助",
            font=("微软雅黑", 11),
            bg='#3498db',
            fg='white',
            width=12,
            command=self.ask_for_help
        )
        help_btn.pack(side=tk.LEFT, padx=5)

        share_btn = tk.Button(
            control_frame,
            text="📤 分享内容",
            font=("微软雅黑", 11),
            bg='#2ecc71',
            fg='white',
            width=12,
            command=self.share_content
        )
        share_btn.pack(side=tk.LEFT, padx=5)

        new_topic_btn = tk.Button(
            control_frame,
            text="🔄 新话题",
            font=("微软雅黑", 11),
            bg='#9b59b6',
            fg='white',
            width=12,
            command=self.new_topic
        )
        new_topic_btn.pack(side=tk.LEFT, padx=5)

        reset_btn = tk.Button(
            control_frame,
            text="🔄 重新开始",
            font=("微软雅黑", 11),
            bg='#e74c3c',
            fg='white',
            width=12,
            command=self.reset_conversation
        )
        reset_btn.pack(side=tk.LEFT, padx=5)

        test_api_btn = tk.Button(
            control_frame,
            text="🔗 测试连接",
            font=("微软雅黑", 11),
            bg='#f39c12',
            fg='white',
            width=12,
            command=self.test_api_connection
        )
        test_api_btn.pack(side=tk.LEFT, padx=5)

        exit_btn = tk.Button(
            control_frame,
            text="退出",
            font=("微软雅黑", 11),
            bg='#95a5a6',
            fg='white',
            width=12,
            command=self.root.quit
        )
        exit_btn.pack(side=tk.LEFT, padx=5)

    def select_online_mode(self):
        """选择在线模式"""
        self.force_local_mode = False
        self.api_status_label.config(text="🔗 在线模式已选择")
        self.display_system_message("已切换到在线模式，将尝试连接AI服务")

        # 测试API连接
        self.test_api_connection()

    def select_offline_mode(self):
        """选择离线模式"""
        self.force_local_mode = True
        self.use_local_mode = True
        self.api_status_label.config(text="📱 离线模式已启用")
        self.display_system_message("已切换到离线模式，使用本地对话数据库")

        # 如果在对话中，重新生成本地选项
        if hasattr(self, 'current_options'):
            self._switch_to_local_mode()

    def _switch_to_local_mode(self):
        """切换到本地模式"""
        self.use_local_mode = True
        if self.conversation_history:
            # 生成本地响应
            local_response = self.generate_local_response("模式切换")
            self.conversation_history.append({"role": "assistant", "content": local_response})
            self.display_message("对方", local_response)

            # 生成本地选项
            options = self.generate_local_options()
            self.update_option_buttons(options)
            self.status_label.config(text="💭 请选择一个回应...")

    def switch_character(self):
        """切换角色"""
        if self.current_character == "wenjing":
            self.current_character = "zhiyuan"
            char_name = "王志远"
            char_color = "#2ecc71"
        else:
            self.current_character = "wenjing"
            char_name = "林文静"
            char_color = "#4a90e2"

        self.character_label.config(text=f"当前对话角色：{char_name}")
        self.display_system_message(f"已切换对话角色：{char_name}")

        # 开始新的对话
        self.start_conversation()

    def update_relationship_display(self):
        """更新关系度显示"""
        self.relationship_label.config(text=f"关系度: {self.relationship}/100")
        self.relationship_bar['value'] = self.relationship

        # 根据关系度改变颜色和文本
        if self.relationship >= 80:
            status = "🤝 信任伙伴"
            color = "#27ae60"
        elif self.relationship >= 60:
            status = "👥 良好合作"
            color = "#3498db"
        elif self.relationship >= 40:
            status = "🙂 普通朋友"
            color = "#f39c12"
        elif self.relationship >= 20:
            status = "😐 认识阶段"
            color = "#e67e22"
        else:
            status = "😕 初步接触"
            color = "#95a5a6"

        self.relationship_label.config(fg=color)

    def update_relationship(self, change):
        """更新关系度"""
        old_relationship = self.relationship
        self.relationship += change

        # 限制在0-100范围内
        self.relationship = max(self.min_relationship, min(self.max_relationship, self.relationship))

        # 更新显示
        self.update_relationship_display()

        # 显示关系度变化
        if change > 0:
            self.display_system_message(f"关系度 +{change}！📈 （{old_relationship} → {self.relationship}）")
        elif change < 0:
            self.display_system_message(f"关系度 {change} 📉 （{old_relationship} → {self.relationship}）")

    def start_conversation(self):
        """开始新的对话"""
        self.conversation_history = []
        self.turn_count = 0
        self.update_relationship_display()

        # 清空对话区域
        self.conversation_text.delete(1.0, tk.END)

        # 显示角色介绍
        character = self.characters[self.current_character]
        self.display_system_message(f"👋 开始与{character['name']}对话 - {character['description']}")

        # 根据模式选择开始对话
        if self.force_local_mode:
            # 用户选择了离线模式
            self.use_local_mode = True
            self.api_status_label.config(text="📱 离线模式运行中")

            # 使用本地模式开始对话
            local_response = self.generate_local_response()
            self.conversation_history.append({"role": "assistant", "content": local_response})
            self.display_message("对方", local_response)

            # 生成本地选项
            options = self.generate_local_options()
            self.update_option_buttons(options)
            self.status_label.config(text="💭 请选择一个回应...")
        else:
            # 尝试使用在线模式
            self.test_api_connection()

    def test_api_connection(self):
        """测试API连接"""
        if self.force_local_mode:
            # 如果用户强制使用离线模式，跳过API测试
            self.display_system_message("离线模式已启用，跳过API测试")
            return

        self.status_label.config(text="🔗 正在连接对话系统...")
        self.api_status_label.config(text="🔄 测试连接中...")

        # 在新线程中测试API
        thread = threading.Thread(target=self._test_api)
        thread.daemon = True
        thread.start()

    def _test_api(self):
        """实际测试API连接"""
        try:
            headers = {
                "Content-Type": "application/json",
                "Authorization": f"Bearer {self.api_key}"
            }

            data = {
                "model": self.model,
                "messages": [{"role": "user", "content": "Hello"}],
                "max_tokens": 10
            }

            response = requests.post(self.api_url, headers=headers, json=data, timeout=10)

            if response.status_code == 200:
                self.root.after(0, self._api_test_success)
            else:
                error_msg = f"API连接失败 (状态码: {response.status_code})"
                self.root.after(0, self._api_test_failed, error_msg)

        except Exception as e:
            error_msg = f"连接失败: {str(e)}"
            self.root.after(0, self._api_test_failed, error_msg)

    def _api_test_success(self):
        """API测试成功"""
        self.use_local_mode = False
        self.api_status_label.config(text="✅ 在线连接正常")
        self.display_system_message("✅ 连接成功！开始对话~")

        # 生成开场白
        character = self.characters[self.current_character]
        prompt = f"你是一个{character['description']}，请用自然、友好的语气开始对话。"
        self.generate_ai_content(prompt)

    def _api_test_failed(self, error_msg):
        """API测试失败"""
        # 如果用户选择了在线模式但连接失败，询问是否切换到离线模式
        if not self.force_local_mode:
            answer = messagebox.askyesno(
                "连接失败",
                f"无法连接到AI服务：{error_msg}\n\n是否切换到离线模式？"
            )

            if answer:
                self.force_local_mode = True
                self.use_local_mode = True
                self.online_mode_var.set(False)  # 更新单选按钮状态
                self.api_status_label.config(text="📱 已切换到离线模式")
                self.display_system_message("⚠️ 连接失败，已自动切换到离线模式")

                # 使用本地模式开始对话
                local_response = self.generate_local_response()
                self.conversation_history.append({"role": "assistant", "content": local_response})
                self.display_message("对方", local_response)

                # 生成本地选项
                options = self.generate_local_options()
                self.update_option_buttons(options)
                self.status_label.config(text="💭 请选择一个回应...")
            else:
                self.api_status_label.config(text="❌ 连接失败")
                self.display_system_message("❌ 连接失败，请检查网络或选择离线模式")
        else:
            # 已经是离线模式
            self.api_status_label.config(text="📱 离线模式运行中")
            self.display_system_message("📱 离线模式运行中")

    def new_topic(self):
        """开始新话题"""
        self.turn_count += 1

        if self.use_local_mode:
            # 本地模式
            local_response = self.generate_local_response("新话题")
            self.conversation_history.append({"role": "assistant", "content": local_response})
            self.display_message("对方", local_response)

            # 生成本地选项
            options = self.generate_local_options()
            self.update_option_buttons(options)
        else:
            # API模式
            character = self.characters[self.current_character]
            prompt = f"作为{character['name']}（{character['description']}），请开启一个新的话题，用自然友好的语气。"
            self.generate_ai_content(prompt)

        self.status_label.config(text="💭 对方正在思考如何回应...")

    def ask_for_help(self):
        """寻求帮助"""
        topics = ["学习问题", "技术难题", "项目建议", "生活建议"]
        topic = random.choice(topics)

        # 随机增加关系度
        relationship_change = random.randint(3, 10)
        self.update_relationship(relationship_change)

        # 显示求助效果
        character = self.characters[self.current_character]
        reactions = [
            f"（认真思考）关于{topic}，我觉得可以这样...",
            f"嗯，这个问题挺有意思的，我的建议是...",
            f"我之前遇到过类似的情况，我的经验是..."
        ]
        reaction = random.choice(reactions)

        self.display_system_message(f"你向{character['name']}咨询了{topic}")
        self.display_message("对方", f"{character['name']}：{reaction}")

        # 更新对话历史
        self.conversation_history.append({"role": "assistant", "content": reaction})

    def share_content(self):
        """分享内容"""
        contents = ["一篇有趣的文章", "一个有用的工具", "一个学习资源", "一段有趣的经历"]
        content = random.choice(contents)

        # 检查关系度是否足够
        if self.relationship >= 30:
            # 成功分享
            relationship_change = random.randint(2, 8)
            self.update_relationship(relationship_change)

            character = self.characters[self.current_character]
            responses = [
                f"（感兴趣地）谢谢分享！这个{content}看起来很有用。",
                f"嗯，这个{content}不错，我有一些补充的想法...",
                f"（点头）这个{content}让我想到了..."
            ]
            response = random.choice(responses)

            self.display_system_message(f"你分享了{content}给{character['name']}")
            self.display_message("对方", f"{character['name']}：{response}")
        else:
            # 关系度不足
            character = self.characters[self.current_character]
            responses = [
                f"（礼貌地）谢谢分享，我会看看的。",
                f"好的，我先记下来了。",
                f"嗯，收到。"
            ]
            response = random.choice(responses)

            self.display_system_message(f"你分享了{content}给{character['name']}")
            self.display_message("对方", f"{character['name']}：{response}")

    def generate_ai_content(self, prompt, is_option_generation=False):
        """调用DeepSeek API生成内容"""
        # 检查是否应该使用本地模式
        if self.force_local_mode or self.use_local_mode:
            if is_option_generation:
                # 生成本地选项
                options = self.generate_local_options()
                self.update_option_buttons(options)
                self.status_label.config(text="💭 请选择一个回应...")
            else:
                # 生成本地响应
                local_response = self.generate_local_response(prompt)
                self.conversation_history.append({"role": "assistant", "content": local_response})
                self.display_message("对方", local_response)

                # 更新对话轮次
                self.turn_count += 1
                self.turn_label.config(text=f"对话轮次: {self.turn_count}")

                # 生成选项
                options = self.generate_local_options()
                self.update_option_buttons(options)
                self.status_label.config(text="💭 请选择一个回应...")
            return

        # 更新状态
        if is_option_generation:
            self.status_label.config(text="💭 正在生成回应选项...")
        else:
            self.status_label.config(text="💭 对方正在思考如何回应...")

        # 禁用所有按钮避免重复点击
        for btn in self.option_buttons:
            btn.config(state=tk.DISABLED)

        # 创建新线程处理API调用
        thread = threading.Thread(target=self._call_deepseek_api, args=(prompt, is_option_generation))
        thread.daemon = True
        thread.start()

    def _call_deepseek_api(self, prompt, is_option_generation):
        """实际调用DeepSeek API的函数"""
        try:
            # 构建请求头
            headers = {
                "Content-Type": "application/json",
                "Authorization": f"Bearer {self.api_key}"
            }

            # 构建消息历史
            messages = []

            # 根据当前角色设置系统提示
            character = self.characters[self.current_character]

            if is_option_generation:
                system_prompt = f"""你是一个对话系统选项生成器。请根据对话历史，为玩家生成三个对话选项。
                每个选项应该：
                1. 是自然、友好的对话回应
                2. 有不同的风格（积极/理性/友善/直接等）
                3. 包含一个括号内的分数（1-10分），表示这个回应的合适程度

                角色设定：{character['name']} - {character['description']}

                格式：
                选项1：[风格] 对话内容（分数）
                选项2：[风格] 对话内容（分数）
                选项3：[风格] 对话内容（分数）"""
                messages.append({"role": "system", "content": system_prompt})
            else:
                # 普通对话的系统提示
                messages.append({"role": "system", "content":
                    f"你是一个对话系统角色，名叫{character['name']}，性格：{character['description']}。"
                    f"请用自然、友好的语气回应，保持对话流畅和有意义。"})

            # 添加对话历史
            for msg in self.conversation_history[-4:]:
                messages.append(msg)

            # 添加当前提示
            messages.append({"role": "user", "content": prompt})

            # 构建请求数据
            data = {
                "model": self.model,
                "messages": messages,
                "max_tokens": 500,
                "temperature": 0.7
            }

            # 发送请求
            response = requests.post(self.api_url, headers=headers, json=data, timeout=30)

            if response.status_code == 200:
                result = response.json()
                ai_response = result["choices"][0]["message"]["content"]

                self.root.after(0, self._handle_ai_response, ai_response, is_option_generation)
            else:
                error_msg = f"API请求失败: {response.status_code}"
                self.root.after(0, self._handle_api_error, error_msg)

        except Exception as e:
            error_msg = f"API调用错误: {str(e)}"
            self.root.after(0, self._handle_api_error, error_msg)

    def _handle_ai_response(self, response, is_option_generation):
        """处理AI响应"""
        character = self.characters[self.current_character]

        if is_option_generation:
            # 解析选项
            options = self.parse_options(response)
            self.update_option_buttons(options)
            self.status_label.config(text="💭 请选择一个回应...")
        else:
            # 添加角色名到回复中
            formatted_response = f"{character['name']}：{response}"

            # 添加AI回复到对话历史
            self.conversation_history.append({"role": "assistant", "content": formatted_response})

            # 显示AI回复
            self.display_message("对方", formatted_response)

            # 更新对话轮次
            self.turn_count += 1
            self.turn_label.config(text=f"对话轮次: {self.turn_count}")

            # 生成三个选项供用户选择
            prompt = f"基于刚才的对话，请生成三个玩家可能的回复选项。"
            self.generate_ai_content(prompt, is_option_generation=True)

    def _handle_api_error(self, error_msg):
        """处理API错误 - 自动切换到本地模式"""
        if not self.force_local_mode:
            # 询问是否切换到离线模式
            answer = messagebox.askyesno(
                "API错误",
                f"AI服务调用失败：{error_msg}\n\n是否切换到离线模式继续对话？"
            )

            if answer:
                self.force_local_mode = True
                self.use_local_mode = True
                self.online_mode_var.set(False)  # 更新单选按钮状态
                self.api_status_label.config(text="📱 已切换到离线模式")
                self.display_system_message("⚠️ AI服务错误，已切换到离线模式")

                # 使用本地生成器继续对话
                self._switch_to_local_mode()
            else:
                self.api_status_label.config(text="❌ API错误")
                self.display_system_message(f"❌ AI服务错误：{error_msg}")
        else:
            # 已经是离线模式
            self._switch_to_local_mode()

    def parse_options(self, response):
        """从AI响应中解析选项"""
        options = []
        lines = response.strip().split('\n')

        for line in lines:
            line = line.strip()
            if not line:
                continue

            # 多种格式匹配
            patterns = [
                r'选项\s*[1-3][：:]\s*\[(.+?)\]\s*(.+?)\s*[（(](\d+)[）)]',
                r'[1-3][\.、]\s*\[(.+?)\]\s*(.+?)\s*[（(](\d+)[）)]',
                r'选项\s*[1-3][：:]\s*(.+?)\s*[（(](\d+)[）)]'
            ]

            for pattern in patterns:
                match = re.search(pattern, line)
                if match:
                    if len(match.groups()) == 3:
                        option_type = match.group(1)
                        text = match.group(2).strip()
                    else:
                        option_type = "普通"
                        text = match.group(1).strip()

                    try:
                        score = int(match.group(3) if len(match.groups()) == 3 else match.group(2))
                        score = max(1, min(10, score))
                    except:
                        score = 5

                    options.append({
                        "text": f"[{option_type}] {text}",
                        "score": score,
                        "type": option_type
                    })
                    break

        # 如果解析不到3个选项，使用本地生成
        if len(options) < 3:
            local_options = self.generate_local_options()
            for i in range(3 - len(options)):
                options.append(local_options[i])

        return options[:3]

    def update_option_buttons(self, options):
        """更新选项按钮"""
        button_colors = ['#e3f2fd', '#e8f5e9', '#f3e5f5']

        for i, (btn, option) in enumerate(zip(self.option_buttons, options)):
            # 根据分数改变颜色深浅
            base_color = button_colors[i]
            if option["score"] >= 8:
                score_color = "#27ae60"  # 绿色 - 高分
                score_text = "✅ 推荐选择"
            elif option["score"] >= 5:
                score_color = "#3498db"  # 蓝色 - 中等分数
                score_text = "👍 合适选择"
            else:
                score_color = "#e74c3c"  # 红色 - 低分
                score_text = "⚠️ 谨慎选择"

            btn_text = f"选择 {i + 1}: {option['text']}\n({score_text}: +{option['score']}关系度)"
            btn.config(
                text=btn_text,
                state=tk.NORMAL,
                bg=base_color,
                fg='#2c3e50'
            )

        # 存储当前选项
        self.current_options = options

    def select_option(self, option_index):
        """用户选择了一个选项"""
        if option_index < 0 or option_index >= len(self.current_options):
            return

        selected_option = self.current_options[option_index]

        # 更新关系度
        self.update_relationship(selected_option["score"])

        # 添加用户选择到对话历史
        user_response = f"你：{selected_option['text']}"
        self.conversation_history.append({"role": "user", "content": user_response})

        # 显示用户选择
        self.display_message("你", selected_option["text"])

        # 禁用选项按钮
        for btn in self.option_buttons:
            btn.config(state=tk.DISABLED)

        # 根据模式生成回复
        if self.use_local_mode:
            # 本地模式
            self.status_label.config(text="💭 对方正在思考如何回应...")
            self.root.after(500, self._generate_local_response_after_selection, selected_option["text"])
        else:
            # API模式
            self.status_label.config(text="💭 对方正在思考如何回应...")
            character = self.characters[self.current_character]
            prompt = f"玩家选择了这个回应：'{selected_option['text']}'。请以{character['name']}的身份，用自然的语气回应。"
            self.generate_ai_content(prompt)

    def _generate_local_response_after_selection(self, user_input):
        """在选择选项后生成本地回复"""
        character = self.characters[self.current_character]

        # 根据选项类型生成不同的回应
        option_type = "积极" if "积极" in user_input else "普通"

        if option_type == "积极":
            responses = [
                f"（赞同地）你说得对，我也这么认为。",
                f"（点头）这个想法很好，我们可以继续深入讨论。",
                f"嗯，我理解你的观点，这让我想到了..."
            ]
        else:
            responses = [
                f"（思考状）让我想想...",
                f"嗯，我明白你的意思。",
                f"好的，那我们换个角度看看。"
            ]

        response = random.choice(responses)
        full_response = f"{character['name']}：{response}"

        # 添加动作和继续话题
        actions = [
            "翻开笔记",
            "思考了一下",
            "看着你",
            "点点头"
        ]
        action = random.choice(actions)

        follow_ups = [
            "关于这个问题，",
            "另外，我还想说的是，",
            "其实我们还可以考虑，"
        ]
        follow_up = random.choice(follow_ups)

        topics = [
            "你有什么具体的计划吗？",
            "你觉得下一步该怎么做？",
            "我们还需要注意哪些方面？"
        ]
        topic = random.choice(topics)

        final_response = f"{full_response}\n{character['name']} {action}，「{follow_up} {topic}」"

        self.conversation_history.append({"role": "assistant", "content": final_response})
        self.display_message("对方", final_response)

        # 更新对话轮次
        self.turn_count += 1
        self.turn_label.config(text=f"对话轮次: {self.turn_count}")

        # 生成新的选项
        options = self.generate_local_options()
        self.update_option_buttons(options)
        self.status_label.config(text="💭 请选择一个回应...")

    def display_message(self, sender, message):
        """在对话区域显示消息"""
        timestamp = datetime.now().strftime("%H:%M:%S")

        # 设置不同发送者的颜色
        if sender == "对方":
            prefix = f"[{timestamp}] 💬 {sender}: "
            tag = "assistant"
        else:
            prefix = f"[{timestamp}] 👤 {sender}: "
            tag = "user"

        # 插入消息
        self.conversation_text.insert(tk.END, prefix)
        self.conversation_text.insert(tk.END, message + "\n\n", tag)

        # 滚动到最后
        self.conversation_text.see(tk.END)

    def display_system_message(self, message):
        """显示系统消息"""
        timestamp = datetime.now().strftime("%H:%M:%S")
        self.conversation_text.insert(tk.END, f"[{timestamp}] ⚙️ 系统: {message}\n\n", "system")
        self.conversation_text.see(tk.END)

    def reset_conversation(self):
        """重置对话"""
        answer = messagebox.askyesno("确认", "确定要重新开始对话吗？当前进度将丢失。")
        if answer:
            # 清除对话区域
            self.conversation_text.delete(1.0, tk.END)

            # 禁用选项按钮
            for btn in self.option_buttons:
                btn.config(state=tk.DISABLED, text=f"选择 {self.option_buttons.index(btn) + 1}")

            self.status_label.config(text="🔄 正在重新开始对话...")

            # 重置关系度
            self.relationship = 50
            self.update_relationship_display()

            # 开始新对话
            self.root.after(500, self.start_conversation)


def main():
    """程序入口：创建主窗口并进入 Tkinter 事件循环。"""
    # 创建主窗口
    root = tk.Tk()

    # 创建应用实例
    app = GalCodeApp(root)

    # 运行主循环
    root.mainloop()


if __name__ == "__main__":
    main()