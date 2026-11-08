# 定音 MyPin for Windows — 交接文档

面向接手这个仓库的开发者。需求看 [REQUIREMENTS.md](REQUIREMENTS.md)，实现细节看 [SPEC.md](SPEC.md)，
本文只讲**怎么读懂代码、怎么构建、怎么验证、哪里容易踩坑、后面还能做什么**。

---

## 1. 一句话架构

一个 WinForms 托盘程序（`TrayApp : ApplicationContext`）在后台盯着 Core Audio 的设备插拔事件与默认
设备变更事件，按「用户排序 + 屏蔽集合 + 自动规则」算出应该用哪个输入设备，如果和当前默认不一致就
通过未公开接口 `IPolicyConfig::SetDefaultEndpoint` 改掉；同时把状态推给主窗口（`MainForm`）、
托盘图标、HUD 和角落浮标。

```
                 ┌──────────────────────────────────────────┐
  Windows 音频   │  AudioSystem (AudioDevices.cs)           │
  服务 (COM)  ←──┤   · GetInputs()    枚举采集端点+分类      │
                 │   · GetDefaultInputId()                  │
                 │   · SetDefaultInput()  → IPolicyConfig   │
                 └───────────────┬──────────────────────────┘
                                 │ 事件：设备插拔 / 默认变更
                                 ▼
                 ┌──────────────────────────────────────────┐
                 │  TrayApp (ApplicationContext)            │
                 │   · Evaluate()      看护循环（唯一决策点）│
                 │   · OrderDevices()  排序                  │
                 │   · ToggleBlock()   屏蔽集合              │
                 │   · RecordSwitch()  抢设备退避            │
                 │   · ApplyTrayIcon / UpdateTrayText        │
                 └──┬──────────┬──────────┬─────────────┬───┘
                    │ 快照      │          │             │
                    ▼          ▼          ▼             ▼
              MainForm     NotifyIcon    HudForm      PillForm
             (主窗口)      (托盘菜单)   (切换提示)   (角落设备名)
```

**线程模型**：全部在 UI 线程。COM 回调（`IMMNotificationClient`）在系统线程上触发，通过
`SynchronizationContext`/`Control.BeginInvoke` 转回 UI 线程后再 `Evaluate()`。看护逻辑本身也用
一个 `Timer` 兜底轮询，防止回调丢失。

---

## 2. 代码地图

| 文件 | 职责 | 改动时的注意事项 |
| --- | --- | --- |
| `src/Program.cs` | 入口；单实例 Mutex；`--write-icon` 构建钩子 | 单实例名 `Local\MyPinWin.SingleInstance` 是会话级 |
| `src/CoreAudio.cs` | 全部 COM 互操作：接口定义、`PROPERTYKEY`/`PROPVARIANT`、`IPolicyConfig` | vtable 顺序不能动，见 SPEC §3 |
| `src/AudioDevices.cs` | 端点枚举、分类（Bluetooth/Virtual/USB/BuiltIn/Other）、默认设备读写、`PickAutomatic` | `PickAutomatic` 是 `static`，纯函数，便于测试 |
| `src/TrayApp.cs` | 应用上下文：**所有业务决策都在这里** | 新增功能优先塞进 `Evaluate()` 而不是各自为政 |
| `src/MainForm.cs` | 主窗口：自绘背景卡片、布局、快照渲染 | 布局全在 `ApplyLayout()` 里，别在构造函数里散着写坐标 |
| `src/DeviceListBox.cs` | 优先级列表：行内 ▲▼、屏蔽按钮、悬停高亮 | 子区域点击要在 `OnMouseDown` 里 `return`，不能调 `base` |
| `src/DarkCheck.cs` | 深色自绘复选框 | 系统勾选框在深色底会变白块，所以必须自绘 |
| `src/Theme.cs` | 深色调色板 + `State(MicState)` 状态色 | 改配色只改这里 |
| `src/Glyphs.cs` | 程序化绘制麦克风图标（三态）、生成 .ico、`Ui.ScaleOf` | 图标全是 `Graphics` 画出来的，没有位图资源 |
| `src/HudForm.cs` | 右上角淡入淡出提示 | 固定主屏 |
| `src/PillForm.cs` | 任务栏角落设备名浮标 | 固定主屏 |
| `src/Settings.cs` | JSON 持久化（`DataContractJsonSerializer`）+ 开机启动注册表 | 反序列化时 `[OnDeserializing]` 填默认值 |
| `src/Strings.cs` | 中英双语，`L.Zh` 决定语言 | 新增文案必须两种语言都写 |
| `src/Diag.cs` | 独立控制台诊断程序（不参与主程序编译） | `build.ps1` 里单独的 source 列表 |
| `installer/Installer.cs` | 自包含 per-user 安装器 / 卸载器 | 主程序 exe 被嵌成资源 `MyPinPayload.exe` |
| `build.ps1` | 编译主程序 + 诊断工具 | 两遍编译，见 §3 |
| `build-installer.ps1` | 编译安装包，把 `dist\MyPin.exe` 嵌进去 | 依赖 `dist\MyPin.exe` 已存在 |

### 关键函数索引

| 想改什么 | 去哪里 |
| --- | --- |
| 自动模式选哪个设备 | `AudioSystem.PickAutomatic()` + `TrayApp.OrderDevices()` |
| 什么时候重新决策 | `TrayApp.Evaluate()`，由事件 / 定时器 / 用户操作触发 |
| 「不是抢设备而是被抢」的判断 | `TrayApp.RecordSwitch()`（15 秒 6 次 → 暂停 20 秒） |
| 设备归类 | `AudioSystem.Classify()`（枚举器名 + 关键词） |
| 系统默认怎么写入 | `AudioSystem.SetDefaultInput()` → `IPolicyConfig` 三个 role |
| 界面尺寸 | `MainForm.ApplyLayout()`，高度由设备行数推出来 |
| DPI 变化 | `MainForm.WndProc` 捕获 `WM_DPICHANGED (0x02E0)` → `ApplyScale()` → `ApplyLayout()` |
| 托盘菜单项 | `TrayApp.RebuildMenu()`（在 `ContextMenuStrip.Opening` 里重建，保证勾选态最新） |

---

## 3. 构建流水线

### 3.1 为什么不用 MSBuild

目标机器与开发机都**没有 .NET SDK**，只有 Windows 自带的 `csc.exe`
（`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`）。所以直接用命令行编译器，
好处是构建零依赖、产物小（66 KB），代价是**只能写 C# 5 语法**：

- 不能用字符串插值 `$"..."` → 用 `+` 拼接；
- 不能用 `?.` / `?[]` → 显式判空；
- 不能用 `nameof` → 写字面量字符串；
- 不能用表达式体成员 `=> ...` → 写完整方法体；
- 不能用自动属性初始化器 → 在构造函数或 `[OnDeserializing]` 里赋值。

如果哪天允许引入 SDK，把这套源码迁到 `net48` 的 csproj 是直接的（文件无 NuGet 依赖）。

### 3.2 主程序：单遍编译

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

1. **主程序**：一次编译出 `dist\MyPin.exe`，用 `/win32icon:assets\logo\app-icon.ico` 直接嵌入品牌图标
   （图标是仓库里的设计稿，不再由程序运行时生成）。
2. **诊断工具**：单独用 `CoreAudio.cs + AudioDevices.cs + Strings.cs + Diag.cs` 编 `MyPinDiag.exe`
   （`/target:exe` 控制台程序）。

> 编译前如果 `MyPin.exe` 正在运行，文件会被锁住导致编译失败 —— 先 `Stop-Process -Name MyPin -Force`。
> 这一条在本地反复迭代时最容易忘。

### 3.3 安装包

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-installer.ps1
```

- 若 `dist\MyPin.exe` 不存在会先调 `build.ps1`；
- 把 `dist\MyPin.exe` 作为**托管资源**编进安装器：`/resource:dist\MyPin.exe,MyPinPayload.exe`；
- 产物 `dist\MyPinSetup.exe`（约 122 KB），同样带 DPI 清单与品牌图标。

安装器行为（`installer/Installer.cs`）：

| 动作 | 位置 |
| --- | --- |
| 程序 | `%LOCALAPPDATA%\Programs\MyPin\MyPin.exe` |
| 卸载器 | `%LOCALAPPDATA%\Programs\MyPin\Uninstall.exe`（安装器自身副本） |
| 开始菜单 | `%APPDATA%\Microsoft\Windows\Start Menu\Programs\MyPin.lnk` |
| 桌面快捷方式 | `%USERPROFILE%\Desktop\MyPin.lnk`（默认创建，可取消） |
| 登录启动 | `HKCU\...\Run` 的 `MyPin` 值 + 改写 `settings.json` 的 `launchAtLogin` |
| 卸载登记 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\MyPin` |

**per-user，不需要管理员**，因此不触发 UAC。

几个实现细节别踩：

- 快捷方式用 `WScript.Shell` **晚绑定**创建（`Type.GetTypeFromProgID` + `InvokeMember`），
  避免给安装器加 COM 引用。
- 「登录时启动」必须**同时**改 `settings.json`。因为主程序每次启动都会用自己的设置覆盖 Run 键，
  只写注册表会被下一次启动抹掉。改 JSON 用的是正则替换 `"launchAtLogin"\s*:\s*(true|false)`，
  字段不存在则插入到末尾 `}` 前。
- 卸载时自删目录用 `cmd.exe /c ping 127.0.0.1 -n 3 > nul & rmdir /s /q "<InstallDir>"`
  —— `ping` 纯粹当 `sleep` 用，等进程退出后再删。
- 静默参数：`MyPinSetup.exe --install`（默认开登录启动与桌面快捷方式）、`--uninstall`。
- **从 1.0 升级**：安装前会 `KillRunningCopies()` 并清掉旧名 `MicPin` 的安装目录、快捷方式、
  `Run` 值、卸载登记项；`settings.json` 若只存在于 `%APPDATA%\MicPin\` 会搬到 `%APPDATA%\MyPin\`
  （主程序 `Settings.Load()` 里也有一份同样的迁移，两边都跑不会互相覆盖）。

---

## 4. 运行与验证

### 4.1 手动验证清单

| 场景 | 期望 |
| --- | --- |
| 拔掉所有 USB 麦克风 | 状态「自动 - 未检测到 USB 麦克风」，系统默认**不动** |
| 插入 USB 麦克风 | 2 秒内成为系统默认输入，右上角出现 HUD，托盘图标变实心 |
| 托盘菜单锁定某设备 | 图标实心；拔掉该设备后系统默认保持不变（不退回内置麦） |
| 在界面里把 A 拖到 B 前面 | 自动模式切到 A；重启程序顺序仍在 |
| 屏蔽 A | A 行变灰显示「已屏蔽」，自动模式跳过 A 选 B；「全部恢复」还原 |
| 用 Zoom 手动切输入 | 程序会切回来；若 15 秒内反复 6 次，暂停 20 秒（图标打叉） |
| 系统缩放到 200% | 界面清晰无栅格，窗口尺寸翻倍但不裁切 |
| 中英文系统 | 文案跟随系统语言 |

### 4.2 诊断工具

```powershell
.\dist\MyPinDiag.exe                 # 端点列表 + 三种 role 的默认设备 + 自动模式结果 + PolicyConfig 可用性
.\dist\MyPinDiag.exe --set "CABLE"   # 按 id 或名称子串把默认输入切到匹配设备
```

`PolicyConfig available: True` 且 `Round-trip SetDefaultEndpoint: OK` 说明写入链路通；
如果这里是 False，先怀疑 `IPolicyConfig` 的 IID/vtable（见 SPEC §3），而不是业务逻辑。

### 4.3 界面抓图（高 DPI 下怎么核对布局）

`PrintWindow` 抓图时，抓图进程自己必须是 DPI-aware，否则拿到的是被系统缩放过的位图：

```powershell
# 抓图脚本要点
[Win32]::SetProcessDpiAwarenessContext(-4)   # PerMonitorV2
# 位图尺寸用 GetWindowRect 的物理像素，不要用 ClientSize
[Win32]::PrintWindow($hwnd, $hdc, 2)         # flag=2 抓完整窗口（含非客户区）
```

临时验证 harness（不在仓库里）：一个只做 `new TrayApp(); app.ShowWindow();` 的预览入口，
配合 `--block` 参数通过反射拿到 `_snapshot` 再调 `ToggleBlock()`，可以稳定复现「屏蔽态」截图。

### 4.4 已知会被踩到的坑

| 现象 | 原因 / 处理 |
| --- | --- |
| 编译报文件被占用 | `MyPin.exe` 在跑，先 `Stop-Process -Name MyPin -Force` |
| 新装的版本双击没反应 | 便携版已在跑，单实例 Mutex 生效（预期行为） |
| PowerShell 启动的 exe 在命令结束后消失 | 用 `explorer.exe "<path>"` 启动可持久运行 |
| `Start-Process -ArgumentList ""` 报错 | 空参数要单独分支，不要传空串 |
| 内置麦克风被当成 `Other` | 本机枚举器是 `SOUNDWIRE`；分类里已经含 `soundwire`/`pci`，换机器要补关键词 |
| 列表点设备行不锁定 | 检查 `_list.SelectedIndexChanged += OnSelectionChanged;` 是否被删掉（踩过一次） |
| 深色底复选框变白块 | 用 `DarkCheck` 自绘，别用系统 `CheckBox` |
| 1px 描边出现双像素/糊边 | `PenAlignment.Inset` + 分隔线用 `y + 0.5f` |

---

## 5. 已知限制

1. 只改「系统默认输入设备」，不改应用内部私有设置（例如某软件自己记住了设备）。
2. 设备分类基于枚举器名称与关键词，未知枚举器会落到 `Other`，不会被自动选中。
3. 无代码签名：首次运行会有 SmartScreen 提示。
4. 单实例作用域是当前登录会话（`Local\`），多用户同时登录互不影响，同一用户不能双开。
5. HUD 与角落浮标固定主屏，多显示器不做跟随。
6. 「临时屏蔽」是会话级（内存里），退出即恢复；这是刻意的设计，避免用户忘了关。
7. 未做代码签名与自动更新。

---

## 6. 后续路线（按性价比排序）

1. **数字签名**：消除 SmartScreen 提示，安装体验的最后一公里。
2. **单文件打包进 .NET Framework 4.8 的离线安装检测**：目前假设目标机自带 4.8（Win11 成立）。
3. **多显示器 HUD**：跟随当前活动屏。
4. **屏蔽持久化选项**：给「屏蔽」加一个「记住」勾选。
5. **按应用区分设备**：Windows 没有系统级 API，需要 hook 或虚拟声卡方案，成本高。
6. **单元测试**：`PickAutomatic` / `OrderDevices` / 分类是纯逻辑，可以抽出来跑测试，
   现在只能靠 `MyPinDiag.exe` 手工验证。

---

## 7. 传到 GitHub

仓库已经可以直接 `git init`（`.gitignore` 已排除 `dist/`）。

```powershell
cd MyPinWin
git init
git add .
git commit -m "定音 MyPin: tray mic guardian with priority ordering and temporary mute"
git branch -M main
git remote add origin https://github.com/<your-account>/mypin-win.git
git push -u origin main
```

上传前建议确认：

- [ ] `dist/` 没有被提交（构建产物不入库）；
- [ ] `docs/images/` 下的截图已提交（README 引用了它们）；
- [x] `LICENSE` 已是 MIT；
- [ ] 安装包 `dist\MyPinSetup.exe` 如果需要分发，走 GitHub Releases 而不是提交进仓库。
