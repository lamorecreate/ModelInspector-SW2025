# 贡献指南

感谢愿意帮忙。这个项目很小，规则也很简单。

## 报告问题

用 [Bug report](.github/ISSUE_TEMPLATE/bug_report.yml) 模板，**一定要附日志**：

```
%LOCALAPPDATA%\SwAiGuide\addin.log
```

没有日志基本无法定位——SolidWorks 的报错信息非常贫乏。

## 提交代码

1. Fork 后新建分支
2. 改 `src/SwAiGuide.cs`（单文件，无外部依赖）
3. 用 `src/build.cmd` 编译，**确认没有新增警告**
4. 关闭 SolidWorks 后运行 `src/install.cmd` 注册，实测通过
5. 提 PR，说明改了什么、为什么

## 代码约定

- C# 5 语法（要能用 .NET Framework 自带的 `csc.exe` 编译，不依赖 Visual Studio）
- 单文件结构，不引入第三方库
- 所有 SolidWorks API 调用必须包 try/catch——部分 API 失败时会拖垮整个进程
- 新增检查项时，同步更新 README 的功能表和 CHANGELOG

## 几个硬性约束

这些是踩过坑得出的，请不要破坏：

| 约束 | 原因 |
|---|---|
| 不用 `HighlightTBButton` | 空实现，无视觉效果 |
| 不用 `ShowBubbleTooltip` | 会崩溃 |
| 不用 `GetButtonPosition` | 会崩溃 |
| 关闭文档用 `QuitDoc` | `CloseDoc` 会弹保存框 |
| 不提交 `SolidWorks.Interop.*.dll` | 版权属于 Dassault，且应由安装脚本从本机复制 |

## 测试

没有自动化测试（SolidWorks 加载项难以自动化）。提交前请至少验证：

- [ ] 单个零件扫描正常
- [ ] 单个装配体扫描正常
- [ ] 批量扫描 10+ 文件不卡死、不弹窗
- [ ] CSV 导出的中文在 Excel 中正常
