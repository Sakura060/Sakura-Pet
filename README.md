# 绘梨衣桌宠 Sakura 🐉

> 「世界很温柔。」 — 上杉绘梨衣

基于江南《龙族》上杉绘梨衣的 Windows 桌面宠物。C# WPF + SkiaSharp 程序化渲染，
不用一张帧动画也能"活"：呼吸、眨眼、散步、摸头会害羞、雨天飘雨滴、深夜压暗氛围。

> ⚠️ **非官方粉丝项目**：本作是基于江南《龙族》上杉绘梨衣的二次创作，与小说 / 游戏官方无关。
> 仅供个人学习与交流，请勿商用或再分发角色相关素材。音色模型权重不随本仓库分发。

**当前版本：v4.0**（发布产物见 GitHub Releases 的 `Sakura-v4.0-Release.zip`，解压后双击 `Sakura-v4.0.exe`，无需安装 .NET）

---

## 功能地图

| 层 | 能力 |
|----|------|
| 人设 | "哒"口癖 · 便签本交流（≤20字）· 正义的铁拳 · 可乐饼/棉花糖/波板糖 · 旋转木马 · 言灵「审判」 · 称呼 Sakura |
| 互动 | 单/双/三击 · **摸头**（点头顶）· 拖拽=**牵手**（甩3次才生气）· 悬停偷笑 · 🍬 投喂 · 🍅 专注陪伴（25/45/60/自定义，安静陪坐+完成夸奖）· 久坐关怀 · 小黄鸭猜拳 · 12 种闲逛动作 |
| 情感 | 好感 0-100 五档称呼 · 亲密/依赖/理解三维 · 日记（温柔便签版）· 本地向量记忆 + 记忆回响 |
| 仪式 | 她的生日（7/11，樱花雨+原声）· **你的生日** · 春节/七夕（农历查表2026-2035）/圣诞/情人节等 · 7/30/100 天纪念日 · 时段/星期问候 · 雨夜"哥哥的歌" |
| 声音 | 三级链路：**本地音色模型（GPT-SoVITS）→ Edge TTS**；托盘 🎧 循环播放配音合集；生日原声；全局音量 |
| 环境 | 天气（含雨/雪氛围粒子）· 桌面感知（写代码/游戏/音乐）· 全屏自动缩角 · 深夜压暗 · 多显示器 |
| 系统 | AI 聊天注入"此刻状态"（时段/天气/好感/专注）· 开机自启 · 窗口透明度 · 数据备份/恢复 · 滚动日志 + 全局异常兜底 · 单实例 · DPAPI 加密配置 |

**台词全部外置**：`assets/quotes.json`（同名 key 整组替换），皮肤 ZIP 导入，眨眼帧自动适配。

---

## 仓库结构

```
.
├── src\
│   ├── EriiPet.Core\        纯逻辑（服务/模型，可单测）
│   ├── EriiPet.Animation\   Skia 渲染（Layers\ 图层化）
│   └── EriiPet.App\         WPF 窗口/托盘/设置
├── assets\                  sprites(10表情×6皮肤+眨眼帧) quotes.json sounds
├── smoke\                   固化冒烟测试（改代码后必跑）
├── tools\BlinkTool\         眨眼帧生成器
└── EriiPet.sln
```

## 常用命令

```bash
# 构建运行
dotnet build EriiPet.sln -c Release
dotnet run --project src/EriiPet.App

# 冒烟测试（35 项，改逻辑后必跑；双击 smoke\运行冒烟测试.bat 亦可）
dotnet run --project smoke -c Release

# 发布（产物 → ..\Sakura-vX.Y\）
dotnet publish src/EriiPet.App -c Release -r win-x64 ^
  -p:PublishSingleFile=true -p:SelfContained=true ^
  -p:PublishReadyToRun=true -o "..\Sakura-v4.0"
```

> ⚠️ 构建坑：`dotnet build EriiPet.sln` 走 x64 平台，输出在 `bin\x64\Release\`；
> 单项目 build 在 `bin\Release\`。引用 dll 时注意区分。

---

## 本地语音模型（GPT-SoVITS）

支持接入本机运行的 GPT-SoVITS 推理服务（`api_v2`，默认 `http://127.0.0.1:9880`）：

1. 本机起好 GPT-SoVITS 推理服务（`runtime\python.exe api_v2.py -a 127.0.0.1 -p 9880`）
2. 桌宠 → 托盘 → ⚙️ 设置 → 「🗣️ 本地语音模型」
3. 填 **服务地址** / **参考音频路径** / **参考音频原文** → 点「▶ 试听」→ 保存

服务不在时自动回退 Edge TTS，无需手动切换；合成结果按台词内容缓存，重复台词秒出。

> 音色模型权重**不随本仓库分发**。想自定义音色，请自行用 GPT-SoVITS 训练。

## 数据与隐私

全部数据在 `%LocalAppData%\EriiPet\`（日志在 `logs\`），不上传任何数据；
本地语音合成无网络请求。备份用设置里的「💾 数据备份」。

---

*世界很温柔。*
