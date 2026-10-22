# SolidWorks 模型体检 (Model Inspector)

# **注意：此项目由 DeepSeek Harness 生成，任何人如需使用，需先全部审阅代码后使用。使用本项目则默认使用者已完全审阅且知晓该项目运行机制和风险，且同意由本项目带来的一切风险与可能带来的损失由使用者自行承担**

一个 SolidWorks 2025 加载项，用来**批量检查模型问题**。

它不是"教你操作"的工具，而是"替你检查"的工具——一次检查一整个文件夹，最后给你一张表。

![主界面](docs/images/main-window.png)

---

## 它解决什么问题

SolidWorks 自带的「什么错」对话框很好用，但有个硬限制：**一次只能看一个文件**。

假设你有一个文件夹，里面躺着几十个零件和装配体。你想知道：

- 哪些模型有特征重建错误？
- 哪些装配体的零件链接断了（文件被移动/改名/删除）？
- 哪些装配体里有被压缩或轻化的零部件？
- 哪些零件是多实体、容易踩坑的？

原生做法是：打开一个 → 点「什么错」→ 记下来 → 关掉 → 再开下一个。三十个文件就是三十遍。

这个插件把这件事变成一个按钮。

## 功能

| 功能 | 说明 |
|---|---|
| 扫描当前文档 | 特征重建错误、断链文件、压缩/轻化零部件、多实体零件 |
| 批量扫描文件夹 | 递归扫描所有 `.sldprt` / `.sldasm`，逐个打开检查后自动关闭 |
| 进度条 + 可中断 | 显示「3/13」，随时可以停 |
| 导出 CSV | UTF-8 带 BOM，Excel 打开中文不乱码 |
| 复制报告 | 一键复制到剪贴板 |
| 前台感知置顶 | 在 SolidWorks 里时浮于其上，切到别的程序自动让开 |

**扫描不会修改你的文件。** 以只读方式打开，关闭时丢弃所有改动，全程不写盘。

## 系统要求

- Windows 10 / 11
- **SolidWorks 2025（版本 33.x）** — 其他版本不保证可用
- .NET Framework 4.8（Win10 1903 以上系统自带）
- 安装时需要管理员权限（写注册表）

## 安装

1. 到 [Releases](../../releases) 下载最新 zip
2. **如果 zip 是从网络下载的，先右键 → 属性 → 勾选「解除锁定」→ 确定，再解压**
   （不解除锁定会报 `0x80131515`，因为 Windows 把 DLL 标记为"来自网络"，.NET 会拒绝加载）
3. 完全关闭 SolidWorks
4. 右键 `install.cmd` → 以管理员身份运行
5. 启动 SolidWorks → 工具 → 插件 → 勾选 **Model Inspector**

详细图文教程见 [docs/TUTORIAL.md](docs/TUTORIAL.md)。

## 性能参考

在 13 个文件的案例目录上（含 2 个装配体）实测：

| 指标 | 值 |
|---|---|
| 总耗时 | 58 秒 |
| 普通零件 | 2–5 秒/个 |
| 复杂零件（40+ 特征） | 10–12 秒/个 |

慢是因为必须真的打开文件——特征信息只有打开后才读得到。

## 已知限制

- 仅支持 SolidWorks 2025
- 批量模式不递归检查子装配体内部的特征错误
- 不做干涉检查（需要几何计算，速度会掉一个数量级）
- 无法避免扫描时 SolidWorks 窗口跳到前台（这是通过 SW 打开文件的固有行为，插件已做到每扫完一个自动切回你的原文档）

## 从源码构建

需要 .NET Framework 4.8 自带的 `csc.exe`（Windows 自带，无需安装 Visual Studio）：

```bat
cd src
build.cmd
```

`build.cmd` 会从本机 SolidWorks 安装目录复制 Interop 程序集到输出目录。

编译产物需通过 `install.cmd` 注册后才能被 SolidWorks 加载。

## 技术笔记

开发过程中实测了几个 SolidWorks API 的实际行为，可能对写 SW 插件的同学有用：

### 1. `ISldWorks::HighlightTBButton` 是空实现

调用不报错、返回正常，但**没有任何视觉效果**。网上流传的"让按钮闪烁"方案大多指向它，实测在 SW 2025 上无效。

### 2. `ISldWorks::ShowBubbleTooltip` 会直接崩溃

按官方示例的参数调用（`ShowBubbleTooltip cmdID, CStr(cmdID), 0, "", ""`），
在按钮不可见时会导致 SolidWorks 进程崩溃（`0xE06D7363`，KERNELBASE）。

### 3. `ISldWorks::GetButtonPosition` 同样会崩溃

对部分命令 ID 调用会直接终止 SolidWorks 进程。本项目中已完全移除该 API 的使用。

### 4. 宏拿不到 `CommandManager`

`GetCommandManager(0)` 在 VBA 宏里返回 `Nothing`，必须由加载项传入 `ConnectToSW` 的 cookie 才能获得。
这意味着**命令面板的枚举与切换只能在加载项内完成**。

### 5. 关闭文档要用 `QuitDoc` 而不是 `CloseDoc`

`CloseDoc` 在文档被标记为已修改时会弹出"是否保存"对话框，批量扫描时会卡住。
`QuitDoc` 丢弃改动直接关闭，无弹窗。

### 结论

截至 SW 2025，**没有稳定可用的原生"按钮高亮"能力**。
任何依赖视觉引导的方案都应当自绘（分层置顶窗口），而不是调用上述 API。

## 许可证

[MIT](LICENSE)

## 免责声明

本项目与 Dassault Systèmes 无关，非官方工具。
SolidWorks 是 Dassault Systèmes SolidWorks Corporation 的商标。
使用本工具造成的任何数据问题由使用者自行承担——**建议在任何批量操作前先备份**。

---

## English

**SolidWorks Model Inspector** — a batch health-check add-in for SolidWorks 2025.

SolidWorks' built-in "What's Wrong" dialog only works on one open document at a time.
This add-in scans an entire folder of parts and assemblies, reporting rebuild errors,
broken references, suppressed/lightweight components and multi-body parts — then exports a CSV.

- Requires SolidWorks 2025 (v33.x), Windows 10/11, .NET Framework 4.8
- Read-only: never modifies your files
- Install: run `install.cmd` as administrator with SolidWorks closed

See [docs/TUTORIAL.md](docs/TUTORIAL.md) (Chinese) for details.
