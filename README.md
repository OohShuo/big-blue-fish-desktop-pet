# 蓝色大肥鱼桌宠

面向 **Ubuntu 22.04 / 24.04 amd64** 的纯动画桌宠，使用 C#、.NET 10、Avalonia X11 和系统 GStreamer。没有模型、OCR、读屏、Agent、网络服务或应用内更新。

保留全部 106 种动作，以及点击、拖动、节日与季节动作、欢迎与告别气泡、托盘、自动启动和低功耗模式。右键桌宠可打开设置或退出。

## 准备环境

构建需要 .NET 10 SDK（安装到 PATH，或解压到仓库 `.dotnet/`）及以下系统依赖：

```bash
sudo apt update
sudo apt install make python3 dpkg-dev gstreamer1.0-tools \
  gstreamer1.0-plugins-base gstreamer1.0-plugins-good gstreamer1.0-plugins-bad \
  libx11-6 libxext6 libxi6 libxcursor1 libxrandr2 libxrender1 \
  libice6 libsm6 libfontconfig1 libfreetype6 libdbus-1-3 fonts-noto-cjk
```

.NET 的 ICU 和 OpenSSL 依赖分别是 22.04 的 `libicu70 libssl3`、24.04 的 `libicu74 libssl3t64`。首次构建需要访问 NuGet 下载 UI 依赖。程序自带 .NET 运行时，用户无需安装 SDK。

## 根目录操作

```bash
make configure          # 检查依赖，生成本地 config.mk，不修改系统
make                    # 编译，生成 build/app/bigbluefish
make run                # 编译并启动
make test               # 动画资源、日历策略、设置恢复及真实视频解码验证
make pack               # 编译并生成 dist/bigbluefish_1.1.2_amd64.deb
sudo make install       # 安装已构建的程序
sudo make uninstall     # 卸载，保留用户设置
make clean              # 删除构建与打包产物
make distclean          # 同时删除 config.mk
```

`make configure DOTNET=/absolute/path/to/dotnet CONFIGURATION=Release` 可指定 SDK 和构建类型。配置写入 `config.mk`，后续仍可用 Make 命令行变量覆盖。`make build` 与默认的 `make` 相同。

可直接运行 `./build/app/bigbluefish`；需保留同目录的运行时、库和 `assets/`，可执行文件不是单文件分发包。不要通过 sudo 启动桌宠。

安装 `.deb`：

```bash
sudo apt install ./dist/bigbluefish_1.1.2_amd64.deb
bigbluefish
sudo apt remove bigbluefish
```

程序安装至 `/opt/bigbluefish`，提供 `/usr/bin/bigbluefish`、应用菜单项和图标。`make install DESTDIR=/absolute/staging/path` 可在临时目录检查安装布局，无需 root；`make uninstall DESTDIR=...` 删除同一布局。

## 桌面与设置

X11 会话直接运行；Ubuntu 默认的 Wayland 会话通过 XWayland 运行，需要安装 `xwayland`。透明区域可点击穿透，拖动时保持悬空动作。全屏自动隐藏依据 X11 窗口状态，在 Wayland 会话中只能检测 XWayland 窗口；原生 Wayland 全屏应用无法通过 X11 查询。

系统托盘需桌面提供 AppIndicator / StatusNotifier 支持，Ubuntu 默认桌面通常已启用。托盘不可用时仍可右键桌宠操作。

用户配置位于 `$XDG_DATA_HOME/bigbluefish`，默认 `~/.local/share/bigbluefish`。保存时备份到 `preferences.json.bak`；旧版配置中已移除的字段会被忽略，在下次保存时清除。自动启动项位于 `$XDG_CONFIG_HOME/autostart/bigbluefish.desktop`，可在设置中关闭；卸载保留用户配置和自动启动偏好。

普通待机和日常活动共用一个自动播放池，每段动画结束后抽取下一段，不再设置独立活动间隔。普通待机与普通日常动作的基础权重统一为 100；夜间、季节、节日、饭点和移动动作仍保留原有特殊条件与权重变化。四个禁用动作保持禁用，点击、拖动、行礼及每日刷牙保留原来的触发方式与规则。移动动作仍完成“外出、短暂休息、返回”的完整流程。

低功耗模式会在动画播完后暂停解码一段时间；点击和拖动可打断等待。系统不支持 VP9 硬件解码时，会使用软件解码。设置中的“软件解码画质”可选择低清（640×360、24 帧，默认）或高清（1280×720、60 帧）；下一个动作起生效并保存选择。高清软件解码会增加 CPU 占用，硬件解码始终使用高清素材。

## 项目结构

- `src/`：Ubuntu 界面、交互、动画策略、GStreamer 播放和配置。
- `assets/`：动画、图标与气泡字体；`fish/compat/` 为软件解码素材。
- `packaging/`：依赖检查、安装布局与 Debian 打包。
- `tests/`：无需测试框架依赖的核心验证与 Xvfb 窗口测试。
- `licenses/`、`THIRD-PARTY.md`：随程序分发的许可与第三方声明。

窗口测试另需 `xvfb x11-utils imagemagick libxtst6`，运行 `make test-ui`。CI 分别在 Ubuntu 22.04、24.04 上构建、测试、检查安装布局并打包。版本号统一在 `src/BigBlueFish.csproj` 的 `Version` 设置。

动画来源、增强工具和字体许可见 [第三方声明](THIRD-PARTY.md)。项目采用 [MIT 许可](LICENSE)。
