PV 轻看 1.5.8
===========
轻便快捷的 Windows 图片、视频、音乐与 FBX 查看器。原生界面，无浏览器、后台服务或自动启动项。播放内核仅在播放视频、音乐及查看额外图片格式时加载；FBX 读取模块仅在打开模型时加载。

使用
  双击文件或拖入窗口；Ctrl+O 打开。
  图片：滚轮缩放、鼠标拖动、0 适应窗口、1 原始大小、R 顺时针旋转、左右键切换。
  图片（包括 PNG）：完整显示时固定居中；放大超出窗口后可拖动，只在超出窗口的方向平移，到图片边缘即停止。
  缩小或调整窗口大小时重新限制位置，不会把图片拖出新的空白区域。
  窗口右下角：SVG 适应窗口按钮、比例下拉框、缩小镜、缩放滑条、放大镜、SVG 全屏按钮。
  缩放最大 800%；小图片适应窗口时也遵守此上限。
  比例下拉框中的 100% 对应原始像素（原 1:1 功能）；旋转可用 R 或右上角菜单。
  上一个、下一个按钮位于画面左右两侧的中部，仅有一个可查看文件时禁用。
  视频：空格播放/暂停、左右键快退/快进 5 秒、[ 和 ] 调整 0.25–4 倍速、Backspace 恢复 1 倍速、上下键音量、M 静音。
  播放/暂停使用圆形 SVG 按钮，随播放状态切换；静音按钮显示扬声器或带叉的扬声器 SVG 图标。
  音量条可连续拖动，也可点击任意位置设置；拖高音量会取消静音，静音时保留原音量。
  音乐：沿用视频的暂停、进度、音量与倍速控制；音乐界面显示歌名、歌手和专辑，无标签时显示文件名。
       两侧按钮 / Ctrl+左右键 / PageUp / PageDown 切换同文件夹里的音乐，按文件名自然排序。
       播放完当前歌曲后停止，按空格重新播放；切换下一首需手动操作。菜单“单曲循环”可重复播放当前歌曲。
  FBX：左键拖动旋转；右键、中键或 Shift+左键拖动平移；滚轮或 + / - 缩放。
       带动画的模型打开后自动循环播放；底部下拉列表切换动画片段，选择“默认姿态”停止动画。
       空格播放/暂停；拖动进度条跳转；[ 和 ] 调整 0.25–4 倍速；Backspace 恢复 1 倍速。
       Shift+左右键前后微调 0.1 秒；右上角菜单“循环播放动画”可关闭循环。
       方向键旋转；0 / R 复位；1 / 2 / 3 正面、侧面、顶面；W 切换线框；G 切换地面网格。
       地面网格随当前视野延伸，红色 X 轴、蓝色 Z 轴显示正负两个方向。
       右下角坐标球显示 X（红）、Y（绿）、Z（蓝）及其反方向，并随模型视角转动。
  Ctrl+左右键 / PageUp / PageDown：切换文件。F / F11 / 双击：全屏。Esc 退出全屏。
  所有调整只用于查看，不修改原文件。原文件不会保持锁定。

格式
  图片：JPG/JPEG/JFIF、PNG、BMP、GIF（动画）、TIFF、ICO、WebP、AVIF、HEIC/HEIF。
  视频：MP4、MKV、MOV、AVI、WebM、WMV、M4V、MPEG、TS/MTS/M2TS、FLV、3GP、OGV、VOB。
  音乐：MP3、WAV、FLAC、M4A/M4B、AAC、OGG/OGA、Opus、WMA、AIFF/AIF、APE、WavPack（WV）、MKA。
  模型：二进制与 ASCII FBX；显示默认姿态，保留节点变换、多个网格、基础材质颜色、顶点颜色及基础色贴图。
       读取嵌入贴图，以及 FBX 相对路径、同目录、同名 .fbm 文件夹和 textures 子目录中的贴图。
       常见贴图格式包括 PNG、JPG、BMP、TGA、PSD、GIF 首帧等。缺失或无法解码的贴图会在底部计数，模型仍可查看。
       支持骨骼蒙皮、节点关键帧、动画层和 blend shape 变形；没有动画的模型显示默认姿态。
       动画在后台逐帧计算并复用顶点缓冲区，暂停后停止计算，最小化时停止刷新。
       当前不还原复杂着色器、动态材质、法线贴图或完整 PBR 效果，不播放外部点缓存，也不编辑或导出模型。
       单个模型最多 400 万个三角形；单张贴图最多 8192 像素边长且不超过 1600 万像素，总贴图解码内存最多 256 MB。
  部分 HEIC/HEIF 文件的读取取决于文件编码与播放内核的支持情况；不支持时会清晰显示错误。

构建与安装
  开源仓库：https://github.com/AI-Invoker/pv
  新环境构建：先运行 prepare-dependencies.ps1，再运行 build.ps1；详细步骤见 README.md。
  源码：F:\design\pv\src
  构建：powershell -NoProfile -ExecutionPolicy Bypass -File F:\design\pv\build.ps1
  安装：powershell -NoProfile -ExecutionPolicy Bypass -File F:\design\pv\install.ps1
  程序：F:\Program Files\PV\PV.exe
  运行依赖：Windows 自带 .NET Framework 4.8、OpenGL；libmpv-2.dll 与约 0.6 MB 的 pv-fbx.dll。
  FBX 源码：native\pv_fbx.c；读取库 ufbx 0.23.1 与 stb_image 的源码和许可证在 vendor 中。
  界面 SVG 图标源码：assets\ui；构建时嵌入程序，按显示比例绘制。
  首次重编译 FBX 模块需要 Zig 0.14.1 Windows x64 编译器。当前已放入 work\toolchain，无需安装系统开发环境。
  编译器下载：https://ziglang.org/download/0.14.1/zig-x86_64-windows-0.14.1.zip
  编译器 SHA-256：554f5378228923ffd558eac35e21af020c73789d87afeabf4bfd16f2e6feed2c
  自定义编译器路径：powershell -NoProfile -File F:\design\pv\build-native.ps1 -Compiler <zig.exe 的完整路径>
  菜单“默认应用设置”打开 Windows 默认应用设置。真正的默认选择由系统设置界面完成。
  安装更新仅注册可用的打开方式；可右击音乐或 FBX → 打开方式 → PV 轻看。

配置
  仅在 %LOCALAPPDATA%\PV\settings.ini 保存音量与倍速。
  --register：注册打开方式；--associations <json>：读取实际关联。
  --set-defaults：设置普通文件关联；Windows 已保存的用户默认选择优先，需在系统设置中更改。
  --diagnostics <json>：读取当前窗口的解码尺寸、倍速、进度和内存。
