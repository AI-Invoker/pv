# PV 轻看

Windows 图片、视频、音乐与 FBX 查看器。使用原生 WinForms 界面，提供打开即看的操作方式，没有后台服务或开机自启。

**当前版本：1.3.1 · Windows 10/11 x64 · MIT**

PV is a native Windows viewer for images, videos, music, and static FBX models, with playback speed controls and SVG buttons. The source is licensed under MIT.

## 功能

- **图片**：缩放、适应窗口、原始大小、旋转、GIF 动画；最大放大 800%。完整显示时居中固定，放大超出窗口后可拖动，到图片边缘即停止。
- **视频**：播放、暂停、进度跳转、0.25–4 倍速、音量和静音。
- **音乐**：歌名、歌手、专辑信息；播放控制与视频共用，同文件夹内自然排序，自动播放下一首和单曲循环。
- **FBX**：默认姿态、基础材质与贴图、旋转和平移、线框、延伸地面网格、XYZ 坐标球。
- **界面**：SVG 播放/暂停、扬声器/静音、适应窗口、放大镜和全屏按钮；音量连续拖动，文件切换按钮在画面左右两侧。
- **文件打开**：双击关联文件、拖入窗口或 `Ctrl+O`；同一实例接收新文件。

普通 JPG、PNG 等图片使用系统图像解码，额外图片格式、视频和音乐按需使用 libmpv；FBX 模块仅在打开模型时加载。

播放内核占安装目录的绝大部分空间；主程序和 FBX 模块体积较小。启动时间和运行内存尚未做专项性能测试。

## 支持格式

| 类型 | 格式 |
| --- | --- |
| 图片 | JPG/JPEG/JFIF、PNG、BMP、GIF、TIFF、ICO、WebP、AVIF、HEIC/HEIF |
| 视频 | MP4、MKV、MOV、AVI、WebM、WMV、M4V、MPEG、TS/MTS/M2TS、FLV、3GP、OGV、VOB |
| 音乐 | MP3、WAV、FLAC、M4A/M4B、AAC、OGG/OGA、Opus、WMA、AIFF/AIF、APE、WavPack、MKA |
| 模型 | 二进制和 ASCII FBX |

FBX 当前显示静态默认姿态，不播放骨骼动画，不还原复杂着色器或完整 PBR 材质，也不编辑、导出模型。单个模型最多 400 万个三角形，单张贴图最大 8192 像素。

## 从源码构建

需要 Windows x64、.NET Framework 4.8 和 PowerShell 5.1 或更新版本。C# 使用 Windows 中的 .NET Framework 编译器，不要求安装 Visual Studio 或 .NET SDK。

```powershell
git clone https://github.com/AI-Invoker/pv.git
cd pv
powershell -NoProfile -ExecutionPolicy Bypass -File .\prepare-dependencies.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

`prepare-dependencies.ps1` 下载固定版本的 libmpv 和 Zig 0.14.1，校验 SHA-256 后解压到仓库本地目录，不安装系统软件。首次构建依赖联网；缓存完整后可离线重新构建。

Windows 自带的 `tar.exe` 用于解压 mpv 的 7z 文件；如果系统版本不支持，可安装 [7-Zip](https://www.7-zip.org/) 后重试。依赖版本、下载地址和校验值见 [dependencies.json](dependencies.json)。ufbx 和 stb_image 的源码与许可证已包含在 `vendor` 中。

如已有 Zig 编译器，可跳过下载并显式指定：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\prepare-dependencies.ps1 -SkipCompiler
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Compiler "C:\tools\zig\zig.exe"
```

构建产物在 `dist`，可直接运行 `dist\PV.exe`。需要保留同目录的 DLL 和许可文件，目前没有独立的单文件安装程序。

### 播放检查

安装 FFmpeg 并将其加入 PATH 后，可以生成短音频样本并验证播放内核：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\check-audio.ps1
```

检查包含 10 种音频格式、中文标签、暂停、跳转、倍速、音量、静音、循环与错误反馈，不打开窗口、不输出声音。它不替代界面、实际音频输出或显卡兼容性测试。

## 安装和卸载

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

首次默认安装到 `%LOCALAPPDATA%\Programs\PV`，更新时沿用已登记的安装位置。可指定自己的 `PV` 目录，例如：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -Destination "F:\Program Files\PV"
```

安装会创建快捷方式、登记“打开方式”和卸载入口。默认软件在 Windows“默认应用”中选择，也可右击文件 → 打开方式 → PV 轻看。更新前需要关闭正在运行的 PV。

通过 Windows 已安装应用卸载，或运行安装目录中的 `uninstall.ps1`。个人音量和倍速偏好保存在 `%LOCALAPPDATA%\PV\settings.ini`，卸载时保留。

## 快捷键

| 操作 | 按键 |
| --- | --- |
| 打开文件 | `Ctrl+O` |
| 全屏 / 退出全屏 | `F` / `F11` / 双击；`Esc` 退出 |
| 切换文件 | `Ctrl+←/→`、`PageUp/PageDown` |
| 图片缩放 / 适应窗口 / 原始大小 / 旋转 | 滚轮、`0`、`1`、`R` |
| 播放 / 暂停 | `空格` |
| 快退 / 快进 | `←/→`，每次 5 秒 |
| 音量 / 静音 | `↑/↓`、`M` |
| 调整倍速 / 恢复正常速度 | `[` / `]`、`Backspace` |
| FBX 旋转 / 平移 / 缩放 | 左键拖动；右键、中键或 Shift+左键拖动；滚轮 |
| FBX 复位 / 正面 / 侧面 / 顶面 | `0` / `R`、`1`、`2`、`3` |
| FBX 线框 / 地面网格 | `W`、`G` |

## 源码结构

- `src`：WinForms 界面、图片交互、libmpv 播放与 OpenGL 模型显示。
- `native/pv_fbx.c`：FBX 读取与贴图解码桥接模块。
- `assets`：程序图标、清单、配置和 SVG 控件图标。
- `vendor/ufbx`、`vendor/stb`：固定版本的第三方源码与许可证。
- `dependencies.json`、`prepare-dependencies.ps1`：可校验的依赖准备流程。
- `build.ps1`、`build-native.ps1`：C# 程序与原生 FBX 模块构建。

`work`、`dist`、下载的 libmpv、编译器和本地调试文件不提交到 Git。

## 开源许可

PV 自身源码采用 **MIT 许可**，见 [LICENSE](LICENSE)。允许使用、修改、商用和再发布，也允许将自己的修改闭源发布；保留 MIT 要求的版权和许可声明即可。

默认依赖采用 LGPL 构建的 libmpv，通过可替换的 DLL 动态调用；第三方库继续遵守各自的许可。ufbx 与 stb_image 使用各自提供的 MIT 许可选项。依赖来源、版本与许可说明见 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)。

欢迎通过 GitHub Issues 报告问题，或提交 Pull Request 改进。
