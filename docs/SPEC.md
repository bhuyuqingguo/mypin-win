# 技术规格（Specification）

本文档描述 MicPin for Windows 的实现规格：数据结构、算法、状态机、持久化、界面与 DPI 规则。
实现必须与本文一致；改动行为时请同步更新本文件。

---

## 1. 术语

| 术语 | 含义 |
| --- | --- |
| 端点 / Endpoint | Windows 音频的采集设备（`eCapture` 数据流） |
| 角色 / Role | Core Audio 的三个默认设备角色：`eConsole`(0)、`eMultimedia`(1)、`eCommunications`(2) |
| 目标设备 / Target | 当前策略希望成为系统默认输入的那个端点 |
| 锁定 / Pinned | 用户显式指定某个端点，不再走自动选择 |
| 屏蔽 / Blocked | 会话级“临时不使用该设备”，不落盘 |
| 优先级顺序 | 用户维护的设备 id 列表，自动模式按此顺序取第一个可用的 USB 设备 |
| 看护 / Enforce | 监听变更并强制把系统默认输入改回目标设备 |

---

## 2. 架构与线程模型

```
┌─────────────────────────────── 进程（单实例） ───────────────────────────────┐
│ Program.Main                                                                │
│   └─ TrayApp : ApplicationContext      ← UI 线程，所有状态只在这里修改        │
│        ├─ AudioSystem                  ← Core Audio 封装（COM，STA）          │
│        │    ├─ GetInputs()             枚举采集端点 + 属性读取 + 分类          │
│        │    ├─ SetDefaultInput(id)     通过 IPolicyConfig 写三个角色          │
│        │    └─ StartMonitoring()       IMMNotificationClient → DevicesChanged │
│        ├─ Timer: debounce 350ms        事件去抖后触发 Evaluate                 │
│        ├─ Timer: safety 3000ms         兜底轮询                               │
│        ├─ NotifyIcon + ContextMenuStrip 托盘与右键菜单                        │
│        ├─ HudForm / PillForm           无焦点浮层                             │
│        └─ MainForm                     设置窗口（按需创建，关闭即隐藏）        │
└─────────────────────────────────────────────────────────────────────────────┘
```

约定：

- **单线程状态**：设备列表、屏蔽集合、设置对象只在 UI 线程读写。
- COM 回调（`IMMNotificationClient`）在系统线程发生，只能 `BeginInvoke` 回到 UI 线程，不能直接改状态。
- `Evaluate` 是唯一的“策略执行”入口：计算目标 → 必要时调用 `SetDefaultEndpoint` → 刷新图标/浮标/窗口快照。
- 所有 UI 刷新都走 `AppSnapshot`（值拷贝），窗口不得反向读取 `TrayApp` 的内部状态。

---

## 3. Core Audio 互操作规格（`CoreAudio.cs`）

### 3.1 常量

| 名称 | 值 |
| --- | --- |
| `CLSID_MMDeviceEnumerator` | `BCDE0395-E52F-467C-8E3D-C4579291692E` |
| `CLSID_PolicyConfigClient` | `870AF99C-171D-4F9E-AF0D-E63DF40C2BC9` |
| `IID_IPolicyConfig` | `F8679F50-850A-41CF-9C72-430F290290C8` |
| `PKEY_FriendlyName` | `A45C254E-DF1C-4EFD-8020-67D146A850E0`, pid `14` |
| `PKEY_DeviceDesc` | `A45C254E-DF1C-4EFD-8020-67D146A850E0`, pid `2` |
| `PKEY_Device_EnumeratorName` | `A45C254E-DF1C-4EFD-8020-67D146A850E0`, pid `24` |
| 设备状态 `DEVICE_STATE_ACTIVE` | `0x1` |
| `STGM_READ` | `0` |

### 3.2 结构体

- `PropertyKey`：`Guid FormatId; int PropertyId;`（必须 `public`，否则接口可见性报 CS0051）。
- `PropVariant`：`short ValueType; short Reserved1..3; IntPtr Pointer; IntPtr Pointer2;`
  x86 = 16 字节、x64 = 24 字节，字段顺序不能改。

### 3.3 接口

所有方法使用 `[PreserveSig] int` 返回 HRESULT，避免异常穿越 COM 边界。

`IPolicyConfig` 的 vtable 顺序必须与系统对象完全一致，`SetDefaultEndpoint` 位于**第 11 个方法**（0 基索引 10）。
顺序错位会导致 `AccessViolationException` 而不是可诊断的 HRESULT。

### 3.4 设置默认设备

```
SetDefaultInput(id):
  policy = new PolicyConfig()
  ok = false
  foreach role in { eConsole, eMultimedia, eCommunications }:
      if policy.SetDefaultEndpoint(id, role) == S_OK: ok = true
  return ok
```

三个角色必须都设置：会议软件读 `eCommunications`，音乐/系统读 `eMultimedia`，部分应用读 `eConsole`。
只设其中一个会出现“系统设置显示已改，但开会仍用旧设备”的现象。

---

## 4. 设备分类规格（`AudioDevices.Classify`）

输入：`EnumeratorName`（枚举器名）与 `FriendlyName`，均转小写后判断。**判定顺序不可调换**：

1. **Bluetooth**：枚举器含 `bthenum` / `bthle` / `bluetooth`，或名称含 `bluetooth` / `蓝牙` / `airpods` / `hands-free`。
2. **Virtual**：枚举器等于 `swd` 或 `root`，或名称命中虚拟声卡关键词表。
   关键词表：`virtual`、`vb-audio`、`voicemeeter`、`cable output`、`blackhole`、`krisp`、
   `teams audio`、`discord`、`obs`、`stereo mix`、`what u hear`、`loopback`、
   `nvidia broadcast`、`sound mapper`、`primary sound`、`虚拟`。
3. **USB**：枚举器含 `usb`。
4. **Built-in**：枚举器含 `hdaudio`、`hdaudbus`、`intelaudio`、`soundwire`、`acpi`、`pci`。
   （`soundwire` 用于骁龙平台笔记本的内置麦，如 Cirrus Logic XU，缺了会把内置麦误判为 Other。）
5. **Other**：其余。

分类结果与展示标签：`Usb→"USB"`、`BuiltIn→内置/Built-in`、`Bluetooth→蓝牙/Bluetooth`、
`Virtual→虚拟/Virtual`、`Other→空字符串`。

> 分类只影响“自动模式能否选中该设备”。锁定模式允许锁定任何类别的设备。

---

## 5. 自动选择算法

```
PickAutomatic(orderedDevices, blocked):
  foreach d in orderedDevices:          # 已按用户优先级排好序
      if d.Kind != Usb: continue
      if blocked contains d.Id: continue
      return d
  return null
```

- 复杂度 O(n)，n 通常 ≤ 10。
- 非 USB 设备**永远不参与**自动选择，这是“不被内置/蓝牙/虚拟设备抢走”的根本保证。
- 返回 `null` 时上层必须**不修改**系统默认设备（而不是回退到任意设备）。

---

## 6. 优先级排序与临时屏蔽

### 6.1 数据模型

| 数据 | 位置 | 生命周期 |
| --- | --- | --- |
| `Settings.DeviceOrder : List<string>` | `%APPDATA%\MicPin\settings.json` | 持久化 |
| `TrayApp._blocked : HashSet<string>` | 进程内存 | 会话级，退出即清空 |

设备在内存中的排序结果不落盘，落盘的只有 id 列表。

### 6.2 排序（OrderDevices）

```
OrderDevices(liveDevices):
  ordered = []
  foreach id in settings.DeviceOrder:
      if id already in ordered: continue
      if liveDevices contains id: ordered.Add(that)
  foreach d in liveDevices:
      if d.Id not in ordered: ordered.Add(d)      # 新设备临时排到最后
  return ordered
```

### 6.3 移动（MoveDevice(id, delta)）

1. 取当前在线设备并按 `OrderDevices` 排序得到 `ordered`。
2. 找到 `id` 的下标 `i`，目标下标 `j = i + delta`；越界（首行上移 / 末行下移）直接忽略。
3. 在 `ordered` 内做一次 `Remove` + `Insert(j)`。
4. 写回设置：`newOrder = ordered 的所有 id`，然后**追加**旧顺序中不在 `newOrder` 里的 id
   （保留掉线设备的槽位）。
5. 保存设置并立即 `Evaluate`，让新顺序马上生效。

### 6.4 屏蔽（ToggleBlock / UnblockAll）

- 屏蔽集合只存在于内存，`PublishSnapshot` 时作为 `BlockedIds` 传给窗口。
- 屏蔽“当前锁定设备”时，同时清空锁定（否则会出现“已锁定但又被屏蔽”的矛盾状态）。
- 用户把某设备设为锁定时，自动把它从屏蔽集合移除（显式选择优先于临时屏蔽）。
- 「全部恢复」= 清空集合 + `Evaluate`。

---

## 7. 看护状态机

### 7.1 判定表

| 自动锁定 | 锁定设备 | 目标设备 | 图标状态 | 行为 |
| --- | --- | --- | --- | --- |
| 关 | — | 无 | Paused（打叉） | 不修改系统默认 |
| 开 | 空 | `PickAutomatic` 结果 | 有 → Locked，无 → Idle | 有目标则强制切换 |
| 开 | 有 | 该设备（在线） | Locked | 强制切换 |
| 开 | 有 | 无（离线） | Idle | **不替换**，保持系统默认 |

状态文案（中/英）见 `Strings.cs`。

### 7.2 触发来源

| 来源 | 触发条件 | 处理 |
| --- | --- | --- |
| 设备事件 | 插拔 / 状态变化 / 默认设备变化 | 去抖 350ms 后 `Evaluate(false)` |
| 兜底轮询 | 每 3000ms | `Evaluate(false)` |
| 用户操作 | 锁定、排序、屏蔽、开关、立即重新检测 | `Evaluate(forceHud)` |

### 7.3 防抢设备（退避）

```
RecordSwitch(now):
  记录 now，剔除 15 秒以前的记录
  if 记录数 > 6:
      清空记录
      backoffUntil = now + 20s
```

`backoffUntil` 期间**不执行** `SetDefaultEndpoint`，但仍刷新界面状态。用于避免与其他音频程序互相拉扯。

### 7.4 HUD 触发

- 发生真实切换且 `ShowHud` 为真 → 弹 HUD。
- 用户点“立即重新检测” → 无论是否切换都弹 HUD（反馈）。

---

## 8. 持久化规格

文件：`%APPDATA%\MicPin\settings.json`（UTF-8 无 BOM，DataContract JSON）。

```json
{
  "pinnedDeviceId": "{0.0.1.00000000}.{guid}",
  "pinnedDeviceName": "NEOM USB",
  "autoLock": true,
  "showHud": true,
  "showName": false,
  "launchAtLogin": false,
  "deviceOrder": ["{id-A}", "{id-B}"]
}
```

| 字段 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `pinnedDeviceId` | string / null | null | 锁定设备 id；null = 自动模式 |
| `pinnedDeviceName` | string / null | null | 仅用于离线时展示 |
| `autoLock` | bool | true | 关 = 暂停看护 |
| `showHud` | bool | true | 切换时弹 HUD |
| `showName` | bool | false | 任务栏角落浮标 |
| `launchAtLogin` | bool | false | 写入 HKCU Run |
| `deviceOrder` | string[] | [] | 优先级顺序，高优先在前 |

规则：

- 读取失败（文件损坏 / 权限）→ 静默使用默认值，不弹错误。
- 写入失败 → 静默忽略（只影响下次启动的体验）。
- 新字段必须通过 `[OnDeserializing]` 给默认值，保证旧配置文件可加载。
- 屏蔽状态**不**写入此文件。

开机启动：`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，值名 `MicPin`，值 `"<exe 路径>"`。
每次启动时按 `launchAtLogin` 校正（存在/不存在）。

---

## 9. 单实例与启动参数

| 项 | 规格 |
| --- | --- |
| 互斥体 | `Local\MicPinWin.SingleInstance`（`Local\` = 当前登录会话） |
| 重复启动 | 第二个进程立即退出，返回 0 |
| `--write-icon <path>` | 生成多尺寸 ICO 后退出（构建流程使用） |

---

## 10. 界面规格

### 10.1 窗口

- 逻辑像素宽度固定 **460**，高度自适应。
- 边框 `FixedSingle`，不最大化，`ShowInTaskbar = true`，启动居中。
- 关闭窗口 = `Hide()`（`CloseReason.UserClosing` 时取消关闭）。

高度公式（逻辑像素，`rows` = 列表行数，1..5）：

```
listHeight   = rows * 40
optionsTitleY= 220 + listHeight + 24
checkTop     = optionsTitleY + 20
buttonTop    = checkTop + 134
clientHeight = buttonTop + 34 + 18
```

### 10.2 布局表（逻辑像素，原点为客户区左上角）

| 元素 | 位置/尺寸 | 说明 |
| --- | --- | --- |
| 麦克风图标 | (18, 16) 34×34 | 程序化绘制，Accent 色 |
| 标题 `MicPin` | (62, 14) | 15pt Bold |
| 副标题 | (63, 44) | 8.5pt，次要色 |
| 状态卡片 | (16, 78) 428×84 | 圆角 10，Surface 填充 + Border 描边 |
| 「当前输入」 | (32, 88) | 8.5pt 次要色 |
| 设备名 | (32, 104) 336×24 | 12pt Bold，超长省略 |
| 状态行 | (32, 130) 352×18 | 8.5pt |
| 状态圆点 | (414, 91) 10×10 | 绿=锁定 / 琥珀=待机 / 灰=暂停 |
| 「麦克风优先级」 | (18, 180) | 9pt Bold |
| 「全部恢复」 | (352, 172) 92×24 | 无屏蔽时禁用 |
| 提示行 | (18, 200) 400×16 | 8pt 弱化色 |
| 列表外框 | (16, 220) 428×(listHeight+4) | 圆角 10，1px 描边 |
| 设备列表 | (18, 222) 424×listHeight | 圆角裁剪，行高 40 |
| 「选项」 | (18, optionsTitleY) | 9pt Bold |
| 四个开关 | (16, checkTop + 30·k) 428×26 | k = 0..3 |
| 「立即重新检测」 | (16, buttonTop) 160×34 | 主按钮（Accent） |
| 「关闭」 | (352, buttonTop) 92×34 | 次要按钮 |

### 10.3 列表行

第 0 行固定为「自动（按下面顺序使用）」，右侧弱化提示「优先任意 USB 麦克风」；其后为设备行。

设备行从左到右：

| 区域 | 规格 |
| --- | --- |
| 单选圆点 | 圆心 x = 15，半径 7；选中 = Accent 实心 + Accent 环 |
| 设备名 | 左 38，单行垂直居中，超长省略 |
| 类型标签 | 右对齐到 ▲ 左侧 6px，高 19，圆角胶囊，ChipBack 底 |
| ▲ 上移 | 20×20，命中区扩展到整行高 |
| ▼ 下移 | 20×20，命中区扩展到整行高 |
| 屏蔽胶囊 | 右 8 起，54×21，圆角胶囊；未屏蔽=描边、已屏蔽=Warn 配色 |

行样式：选中 `RowSelected`、悬停 `RowHover`、其余 `SurfaceAlt`；行间 1px 分隔线（`y + 0.5` 对齐）；
被屏蔽行文字用 `TextMuted`。

### 10.4 交互规则

- 点击行内空白/单选圆点 → 选中并切换锁定（点已锁定设备 = 解除锁定，回到自动）。
- 点击 ▲▼ → 移动优先级，**不**改变当前选中项。
- 点击屏蔽胶囊 → 切换屏蔽，**不**改变当前选中项。
- 子区域点击通过 `OnMouseDown` 提前返回，不调用 `base.OnMouseDown`，因此不会触发选中。
- 悬停到可点击子区域时光标变手型。
- 复选框支持鼠标点击与空格键。

### 10.5 托盘

- 图标三态：Locked（实心麦克风）/ Idle（空心）/ Paused（打叉），16~20px 按系统小图标尺寸绘制。
- 左键单击 / 双击 → 打开窗口；右键 → 菜单。
- tooltip：`MicPin - <当前设备名>`。
- 菜单在 `Opening` 事件里重建，保证设备列表实时。

---

## 11. 主题与绘制规范（`Theme.cs`）

| 用途 | 颜色 |
| --- | --- |
| 窗口底 | `#15181D` |
| 卡片 / 列表面 | `#1D222A` |
| 列表底 | `#191E26` |
| 描边 | `#2C333E` |
| 主文字 | `#E7EBF2` |
| 次要文字 | `#9AA4B2` |
| 弱化文字 | `#7A8495` |
| 强调色 | `#5496FF`（悬停 `#6AA6FF`，按下 `#4282EB`） |
| 选中行 | `#223349` |
| 悬停行 | `#20262F` |
| 分隔线 | `#232A33` |
| 标签胶囊 | 底 `#2A313C`，字 `#9AA4B2` |
| 屏蔽胶囊 | 底 `#3A2E1E`，边 `#6E5429`，字 `#E8B36B` |
| 状态点 | 锁定 `#3DD68C`，待机 `#F0B45A`，暂停 `#7A8495` |

绘制约定：

- 标题栏通过 `DwmSetWindowAttribute(hwnd, 20 | 19, TRUE)` 启用沉浸式深色；
  失败静默忽略（旧系统保留默认框）。
- 系统主题的复选框在深色底上会呈白块，必须自绘（`DarkCheck`）。
- 所有 1px 描边使用 `PenAlignment.Inset`；水平分隔线绘制在 `y + 0.5`，避免半像素发虚。
- 单选圆点、胶囊、对勾使用 `SmoothingMode.AntiAlias`；文字统一 `TextRenderer`（GDI，CJK 回退好）。
- HUD / 浮标为深色卡片 + 白色文字，`WS_EX_NOACTIVATE`（不抢焦点），浮标额外 `WS_EX_TRANSPARENT`（点击穿透）。

---

## 12. DPI 规格

| 项 | 规格 |
| --- | --- |
| 清单声明 | `dpiAware = true/pm` + `dpiAwareness = PerMonitorV2, PerMonitor` |
| 缩放系数来源 | `GetDpiForWindow(hwnd) / 96`，回退 `Graphics.DpiX / 96` |
| 重新布局 | `WndProc` 捕获 `WM_DPICHANGED (0x02E0)` → 重算 `_scale` → `ApplyLayout()` |
| 坐标体系 | 所有尺寸用**逻辑像素**书写，绘制前乘 `_scale` 并四舍五入 |
| 字体 | 使用磅值（point），GDI 按当前 DPI 自动换算，**不要**再乘 `_scale` |
| 行高 | `ItemHeight = round(40 * scale)`，DPI 变化后重新设置 |
| 圆角区域 | `Region` 在 `ApplyLayout` 中按新尺寸重建，旧 Region 释放 |

禁止事项：

- 不要用 `AutoScaleMode.Font` 与手写 `_scale` 混用（会双重缩放）。
- 不要在清单里只写 `dpiAware=true`（会被系统位图拉伸，出现糊边）。

---

## 13. 本地化规格（`Strings.cs`）

- 判定：`CultureInfo.CurrentUICulture.Name` 以 `zh` 开头（含父级回溯）。
- 形式：`Pick(english, chinese)`。
- 覆盖范围：托盘菜单、状态行、窗口标签/按钮/提示、设备类型标签、HUD 标题。
- 设备名与类型枚举值来自系统，不做翻译。

---

## 14. 图标

- 设计网格 16×16：胶囊 6×4（x=6, y=1.4）+ 4×3.6（x=4, y=3.6）；
  U 形 `DrawArc(pen, 3.6, 5, 8.8, 8.8, 0, 180)`；竖杆 + 底座；暂停态叠加 45° 斜线。
- 托盘图标：按 `SystemInformation.SmallIconSize` 渲染，`GetHicon` → `Clone` → `DestroyIcon`。
- exe 图标：`WriteIconFile` 手工写 ICO（PNG 负载），尺寸 16/20/24/32/40/48/64/128/256。
- 构建必须两遍：第一遍无图标编译 → 运行 `--write-icon` 生成 ico → 第二遍 `/win32icon` 重编译。

---

## 15. 诊断工具规格（`MicPinDiag.exe`）

输出内容：

1. 每个活动采集端点：id、`Enumerator`、分类、名称。
2. `eConsole` / `eMultimedia` / `eCommunications` 的当前默认设备 id。
3. `PickAutomatic` 的结果。
4. `PolicyConfig available` 与一次 no-op `SetDefaultEndpoint` 往返自检。
5. `--set <id 或名称子串>`：把匹配到的第一个端点设为默认，用于验证写入链路。

用途：区分“枚举/分类错”与“写入失败”两类问题。

---

## 16. 错误处理

| 场景 | 行为 |
| --- | --- |
| 音频子系统创建失败 | `_audio = null`，界面显示“音频设备不可用”，其余功能可用 |
| `SetDefaultEndpoint` 失败 | 本次不切换，界面按实际默认设备显示，3 秒后再试 |
| 设备属性读取失败 | 名称回退为“未命名设备”，分类回退 Other |
| 设置文件损坏 | 使用默认值，静默覆盖写回 |
| 注册表写入失败 | 忽略（开关仍显示用户意图） |
| COM 回调跨线程 | `BeginInvoke` 失败时忽略（关机竞态） |

---

## 17. 自动化测试建议（当前无测试工程）

可纯逻辑测试的部分（无 COM 依赖）：

- `AudioSystem.Classify(name, enumerator)` 的分类边界。
- `AudioSystem.PickAutomatic(ordered, blocked)` 的选择与屏蔽跳过。
- `OrderDevices` / `MoveDevice` 的顺序与“掉线槽位保留”。
- `Settings` 的 JSON 往返与缺字段默认值。

建议后续抽出 `MicPin.Tests`（可在有 SDK 的机器上运行）覆盖以上四点。
