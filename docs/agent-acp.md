# ACP Agent 连接

本分支恢复上游的 ACP Agent 子系统，只保留 Claude Code、Codex CLI、OpenCode 三个 Harness。

## 架构

- `AgentCoordinator` 为每个 Harness 维护一个鱼鱼专用 ACP 会话，按工作区隔离。
- ACP 进程通过 `session/prompt` 接收事件；普通回答不会进入气泡，只有 `say` / `say_next` 工具会显示文字。
- 内部工具由 `ControlServer` 提供，通过本机当前用户限定的 socket 连接。
- ACP 启动的内部 MCP 桥复用主程序：`bigbluefish --mcp-bridge --pipe <随机名>`。桥接模式不初始化 Avalonia、不创建单实例、不访问数据目录。
- 会话记录保存在 `agent/sessions/<工作区路径哈希>/<harness>.json`，只保存会话编号、程序路径和工作目录。
- 技能 `big-blue-fish/SKILL.md` 与人设 `persona.md` 安装在工作区内，可由主人编辑。

## Harness

| 名称 | 命令 | ACP 入口 |
| --- | --- | --- |
| Claude Code | `claude` | npm 固定版本 `@agentclientprotocol/claude-agent-acp@0.99.0` |
| Codex CLI | `codex` | npm 固定版本 `@agentclientprotocol/codex-acp@2.5.0` |
| OpenCode | `opencode` | `opencode acp` |

Claude/Codex 需要 Node.js / npm；OpenCode 使用自带 ACP。适配器安装在 `agent/adapters/`，不会升级或卸载原有 CLI。

## 工具

`get_settings`、`update_settings`、`list_actions`、`capture_foreground`、`capture_screen`、`play_action`、`interrupt_action`、`say`、`say_next`、`get_operation_guide`、`get_wakeups`、`schedule_wakeup`、`cancel_wakeup`、`get_weather`、`memory_list`、`memory_remember`、`memory_forget`、`reminder_create`、`reminder_list`、`reminder_cancel`。

交互事件通过 MCP 资源 `bigbluefish://interactions` 提供，最近 128 条。点击/拖动是否主动发起 Agent 轮次由设置页的“点击时让 Agent 回应”“拖动结束时让 Agent 回应”控制，对应 `AgentRespondToClick`、`AgentRespondToDrag`，默认关闭。关闭时事件仍会记录，但不会主动叫醒 Agent。

天气使用 Open-Meteo 和固定地区坐标；设置页提供城市搜索、经纬度和“天气变化时让 Agent 提醒”。只有明显天气变化且超过 30 分钟冷却时才发送 `weather_change`。

记忆保存在 `agent/memories.json`，每轮附加有上限的快照；提醒保存在 `agent/reminders.json`，支持一次性和每日时间，由运行中的桌宠投递气泡，也可以设置 `agent_speech` 触发 Agent 组织语言。

## 本分支边界

- 不包含本地 4B 模型、OCR、新闻或 `request_local_speech`。
- 日常关心文字始终由 Agent 编写；关闭 `AgentDailySpeech` 后不做自动日常关心。
- 通用提醒由运行中的桌宠计时；桌宠未运行时不弹气泡，错过的一次性提醒在下次启动时补触发一次。
- 截图使用 X11；Wayland 原生窗口可能无法获取前台像素，会返回明确错误。
- 聊天输入框不保存历史记录，也不回放对话正文；上下文由 Agent 自己保存。
