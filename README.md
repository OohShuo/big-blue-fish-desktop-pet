# 蓝色大肥鱼桌宠

面向 **Ubuntu 22.04 / 24.04 amd64** 的桌宠，使用 C#、.NET 10、Avalonia X11 和系统 GStreamer。不带本地模型、OCR、读屏或应用内更新；可以通过 ACP 连接本机已安装的 Claude Code、Codex CLI 或 OpenCode，支持长对话、工作区、截图、动作和日常关心预约。

保留全部 106 种动作，以及点击、拖动、节日与季节动作、欢迎与告别气泡、托盘、自动启动和低功耗模式。右键桌宠可打开设置或退出。

## 准备环境

构建需要 .NET 10 SDK（安装到 PATH，或解压到仓库 `.dotnet/`）及以下系统依赖：

```bash
sudo apt update
sudo apt install make python3 dpkg-dev gstreamer1.0-tools \
  gstreamer1.0-plugins-base gstreamer1.0-plugins-good gstreamer1.0-plugins-bad \
  libx11-6 libxext6 libxcomposite1 libxi6 libxcursor1 libxrandr2 libxrender1 \
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

## 趣味鼠标互动

设置中可关闭“开启趣味鼠标互动”，关闭后仍保留原有点击和拖动。

| 手势 | 反馈 |
| --- | --- |
| 拖动时快速来回甩动数次 | 悬空时提示头晕，松手后生气 |
| 不按鼠标，在头部缓慢来回移动 | 开心地回应摸头 |
| 两秒内连续点击四次；继续点击至七次 | 先惊讶，再生气 |
| 按住两秒且不拖动 | 害羞回应，松手不会再算一次普通点击 |
| 拎起后保持静止约四秒 | 请求放下来，每次拎起只提示一次 |
| 不按鼠标，在尾巴附近快速小幅来回移动 | 挠痒笑声与动画 |
| 鼠标快速进入身体附近 | 偶尔吓一跳，有较长冷却 |
| 被甩或连续戳到生气后，二十秒内慢慢摸头 | 安抚并恢复亲近回应 |

手势有冷却时间；头部和尾巴使用随桌宠大小缩放的近似区域。快速搬动一次不会算甩动。交互反馈结束后恢复自动播放，退出行礼不会被手势打断。这些交互只处理本地鼠标位置和按键，不需要模型或屏幕截图。

## 项目结构

- `src/`：Ubuntu 界面、交互、动画策略、GStreamer 播放和配置。
- `assets/`：动画、图标与气泡字体；`fish/compat/` 为软件解码素材。
- `packaging/`：依赖检查、安装布局与 Debian 打包。
- `tests/`：无需测试框架依赖的核心验证与 Xvfb 窗口测试。
- `licenses/`、`THIRD-PARTY.md`：随程序分发的许可与第三方声明。

窗口测试另需 `xvfb x11-utils imagemagick libxtst6 xauth`，运行 `make test-ui`；`make test` 验证八种手势的识别；`make test-interactions` 使用独立 Xvfb、真实播放器和模拟鼠标事件验证动画完整播放、互动延后切换与拖放。CI 分别在 Ubuntu 22.04、24.04 上构建、测试、检查安装布局并打包。版本号统一在 `src/BigBlueFish.csproj` 的 `Version` 设置。

动画来源、增强工具和字体许可见 [第三方声明](THIRD-PARTY.md)。项目采用 [MIT 许可](LICENSE)。

互动触发后会等当前动画完整播放，再切换到下一段；连续触发只保留最新的待播反应，气泡与动画同步出现。拖动窗口立即生效，放下后不补播过期的悬空动作。

## 连接本机 Agent

设置页“连接 Agent”可以选择本机已安装的 **Claude Code**、**Codex CLI** 或 **OpenCode**。首次开启会显示权限说明：Agent 可以读取和修改桌宠设置、获取前台或全屏截图、接收输入和交互事件。只连接你信任的 Agent。

- Claude Code 和 Codex 需要 Node.js / npm 安装固定版本的 ACP 适配器；OpenCode 使用自带的 `opencode acp`。
- 连接后双击鱼鱼打开悬浮输入框，`Ctrl+Enter` 发送，`Esc` 收起；断线时保留草稿，连接恢复后可继续。输入框不保存聊天历史，也不回放对话正文。
- Agent 的普通回答留在自己的会话中；只有调用 `say` / `say_next` 才会进入鱼鱼气泡。`say` 约每秒 28 个字符逐字显示，最多 4000 字。
- 每个 Harness 有独立的鱼鱼会话，按工作区隔离保存。支持 `session/resume` 或 `session/load` 的 Agent 会在重连/重启后恢复原会话；“开始新对话”会建立新会话。
- 工作区默认是“文档/蓝色大肥鱼工作区”，所有 Harness 共享文件；可在设置中新建、打开、查看或清空。
- 技能 `big-blue-fish/SKILL.md` 与人设 `persona.md` 安装在当前工作区；设置中可以编辑人设，空文件表示使用 Agent 自己的设定。
- 日常关心由 Agent 编写。普通模式按设置的时间间隔发送 `daily_care`；开启“接管触发与动作”后，Agent 通过 `schedule_wakeup` 安排 `wakeup`，鱼鱼保持待机，动作由 Agent 指定。
- 内部工具包括设置、截图、动作、说话、技能指南、预约、天气、长期记忆和通用提醒，共 20 项。点击和拖动事件保存在 `bigbluefish://interactions` 资源中；设置页的“点击时让 Agent 回应”“拖动结束时让 Agent 回应”两个开关控制是否额外发起 Agent 轮次，默认关闭。
- 天气使用固定地区：设置地区名称、经纬度或搜索城市后，Agent 可以调用 `get_weather`；开启“天气变化时让 Agent 提醒”后，只有天气类型、气温、降水概率或风速明显变化时才额外发起 Agent 轮次。数据来自 Open-Meteo，不需要 API Key，但地区坐标会发送给 Open-Meteo。
- 长期记忆通过 `memory_list` / `memory_remember` / `memory_forget` 管理，保存在桌宠数据目录；每轮事件会附带一段有上限的记忆快照，设置页可以查看和删除记忆。
- 通用提醒通过 `reminder_create` / `reminder_list` / `reminder_cancel` 管理，支持一次性和每日时间，不依赖 Puppet 模式。提醒由运行中的桌宠计时；到点显示气泡，或设置 `agent_speech=true` 让 Agent 组织语言。桌宠未运行时不会弹气泡，错过的一次性提醒会在下次启动时补触发一次。
- 当前 Ubuntu 版本不包含本地模型、OCR、新闻或 `request_local_speech`；日常关心的文字始终由 Agent 编写。

`make test-agent` 验证发布程序的无界面 MCP 桥接模式；`make test` 包含 ACP 会话、工作区、预约、设置和 MCP 协议检查。先运行 `make build` 再运行 `make test-agent`。

Agent 自己保存会话和模型请求；输入、截图或工具结果可能经它配置的模型服务发送到云端。桌宠本身不保存 API Key。
