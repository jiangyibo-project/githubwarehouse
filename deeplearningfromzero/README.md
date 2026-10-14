# 深度学习从零开始：纯 numpy 实现两层神经网络

本实验**不使用任何深度学习框架**（PyTorch / TensorFlow / Keras 都不用），只用 `numpy` 从零实现一个两层全连接神经网络，在 scikit-learn 内置的手写数字数据集（8×8 像素，共 1797 张，数字 0~9）上完成训练与识别。

已在干净 Python 环境中完整执行验证，测试集识别精度约 **92.2%**。

## 实验内容

- 网络结构：输入层 64 → 隐藏层 50（sigmoid 激活）→ 输出层 10（softmax）
- 手工实现：sigmoid 及其导数、softmax、交叉熵损失、前向传播、反向传播解析梯度
- 数值梯度检查：用中心差分的数值梯度验证反向传播公式写对了
- 小批量随机梯度下降（mini-batch SGD）训练，固定随机种子保证结果可复现
- 可视化：网络结构图、激活函数曲线、损失曲线、学习曲线、混淆矩阵、认错样本、第一层权重图，并把训练过程合成 GIF 动画

## 运行环境

- Python 3.10 及以上（在 3.12 上验证通过）
- 依赖库见 `requirements.txt`：numpy、scikit-learn、matplotlib、pillow、ipython

## 如何运行

1.（建议先创建虚拟环境）安装依赖：

   ```
   pip install -r requirements.txt
   ```

2. 方式一：启动 Jupyter 交互运行

   ```
   jupyter notebook deeplearningfromzero.ipynb
   ```

   打开后在菜单选 `Kernel → Restart & Run All`，即可从头执行全部单元。

3. 方式二：命令行一次性执行全部单元（不打开浏览器）：

   ```
   jupyter nbconvert --to notebook --execute --inplace deeplearningfromzero.ipynb
   ```

## 文件说明

- `deeplearningfromzero.ipynb`：实验主体，包含全部代码、公式讲解和可视化输出
- `build_notebook.py`：生成上面 notebook 的脚本（用代码方式拼装单元格），运行 `python build_notebook.py` 即可在脚本所在目录重新生成 notebook；运行它需要额外安装 `nbformat`
- `train_animation.gif`：训练过程动画（notebook 执行时会自动重新生成并覆盖，保存在当前工作目录）

## 学习建议

建议按 notebook 单元格顺序阅读，每一节都先讲"为什么需要它"再给代码。如果想动手扩展，可以尝试：改隐藏层神经元数量、改学习率、把 sigmoid 换成 ReLU、增加训练轮数，观察精度和损失曲线的变化。
