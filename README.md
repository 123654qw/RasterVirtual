# Raster Virtual

**Raster Virtual** 是一款桌面级虚拟机管理器，基于 **QEMU** 作为虚拟化后端，提供简洁的深色主题界面，让你像管理普通应用一样创建、运行和销毁虚拟机。软件以 ISO 安装映像引导，完成操作系统的安装与日常使用。

> 设计基调：炭黑底色 + 暖橙强调色，禁用蓝紫配色，符合现代深色桌面的阅读习惯。

---

## ✨ 功能特性

| 模块 | 说明 |
| --- | --- |
| 虚拟机列表 | 主界面统一管理所有虚拟机，支持开机 / 关机 / 暂停 / 恢复 / 删除 / 设置 |
| 新建向导 | 5 步向导：基本信息 → 处理器与内存 → 虚拟硬盘 → 安装介质 → 确认；按客户机系统自动套用经过验证的硬件预设 |
| 安装介质 | 挂载系统 ISO 引导安装，支持附加"驱动光盘"（如 Virtio 驱动） |
| 虚拟硬盘 | 创建 / 附加 / 扩容 / 查看属性 / 移除；支持 qcow2、vmdk、vhdx、vdi、raw 格式 |
| CPU / 内存 | 按主机物理资源上限分配，避免把主机拖垮 |
| 固件 | UEFI（OVMF / edk2）或传统 BIOS，自动选择对应启动方式 |
| 显示 | 可切换显卡型号（Std / Cirrus / Virtio / QXL），支持截图 |
| 网络 | NAT（默认）与桥接两种模式，支持端口转发（Port Forward） |
| 音频 | 可配置声卡与后端 |
| 共享文件夹 | 通过内置机制把宿主机目录共享进虚拟机 |
| 快照 | 运行时（在线）与关机（离线）两种快照；可创建 / 恢复 / 删除 |
| 电源控制 | 启动、软关机（ACPI）、强制停止、暂停 / 恢复 |
| 硬件加速 | 自动检测 WHPX（Windows Hypervisor Platform）；不可用时回退到纯软件 TCG |
| 首选项 | 查看 QEMU 运行时状态与版本、指定 QEMU 目录、配置虚拟机存放根目录、退出行为 |

---

## 📋 系统要求

- **操作系统**：Windows 10 / 11（64 位）
- **处理器**：支持硬件虚拟化（Intel VT-x 或 AMD-V）
- **强烈建议开启 WHPX**：
  - 控制面板 → 程序 → 启用或关闭 Windows 功能 → 勾选 **Windows 虚拟机监控程序平台** 与 **虚拟机平台**
  - 或在 PowerShell（管理员）执行：
    ```powershell
    Enable-WindowsOptionalFeature -Online -FeatureName VirtualMachinePlatform
    Enable-WindowsOptionalFeature -Online -FeatureName HypervisorPlatform
    ```
  - 开启后需**重启一次**
- **未开启加速也能用**：Raster Virtual 会自动回退到 TCG（纯软件模拟），但性能明显下降，仅适合轻量系统或安装阶段

> ⚠️ 若主机已启用 Hyper-V / 其他虚拟机平台（如某些杀软沙箱、WSL2 的后端），WHPX 通常可直接复用，无需额外操作。

---

## 🚀 快速开始

1. 解压 / 安装后运行 `RasterVirtual.exe`。
2. 首次启动会在 **首选项 → 运行时** 中自动检测内置 QEMU（绿色圆点表示可用）。
3. 点击 **新建虚拟机**，按向导完成创建。
4. 在向导第 4 步 **安装介质** 选择系统 ISO，并建议勾选"从光驱启动优先"。
5. 创建完成后选中虚拟机，点击 **启动**。虚拟机会从 ISO 引导，按提示完成系统安装。

> 内置 QEMU 位于程序目录的 `runtime\qemu`，无需单独安装。若你想使用自己的 QEMU，可在首选项中手动指定目录。

---

## 💿 挂载 ISO 安装系统（详细步骤）

1. 打开 **新建虚拟机** 向导。
2. **第 1 步 基本信息**：填写虚拟机名称，选择客户机类型（Windows / Linux / BSD / DOS 等）。
3. **第 2 步 处理器与内存**：拖动滑块分配资源，界面会提示主机可用上限。
4. **第 3 步 虚拟硬盘**：
   - 新建一块 qcow2 硬盘（推荐，支持快照与稀疏存储），或
   - 使用已有虚拟磁盘文件，或
   - 仅从光盘运行（不建硬盘）。
5. **第 4 步 安装介质**：
   - 点击 **浏览** 选择系统 ISO（如 Windows / Linux 安装盘）。
   - 如需 Virtio 等驱动，可在"附加驱动光盘"中再选一个 ISO。
   - 勾选 **从光驱启动优先**，确保开机先进入安装界面。
6. **第 5 步 确认**：核对摘要后点击 **创建虚拟机**。
7. 回到主界面，选中虚拟机并 **启动**。随后在安装界面中把系统装到第 3 步创建的硬盘上即可。

---

## 🌐 网络

- **NAT（默认）**：虚拟机通过宿主机的网络地址转换访问外网，适合绝大多数场景。
- **桥接**：虚拟机直接接入宿主机所在的物理网络，获得与宿主机同网段的独立 IP（需宿主机有可用网卡）。
- **端口转发**：在虚拟机设置 → 网络 中配置，把宿主机的某个 TCP/UDP 端口映射到虚拟机内部服务（如 SSH 22、RDP 3389）。

---

## 📸 快照

- **运行时快照（在线）**：虚拟机正在运行时创建，基于 QMP 实时保存当前状态，恢复后进程、内存、磁盘回到拍摄时刻。
- **关机快照（离线）**：虚拟机关机后创建，基于 `qemu-img snapshot` 对磁盘打点，适合在重大变更前存档。
- 操作入口：选中虚拟机 → **快照** 窗口，支持创建 / 恢复 / 删除，并可用标签命名便于区分。

---

## 📁 共享文件夹

在虚拟机设置 → **音频与共享** 中指定一个宿主机目录，Raster Virtual 会将其以虚拟磁盘形式挂载进客户机，方便在宿主机与虚拟机之间互传文件。

---

## ⚙️ 硬件加速说明

Raster Virtual 通过 `AccelDetector` 在启动时检测可用的加速方式：

- **WHPX（推荐）**：Windows 自带虚拟化平台，性能接近原生，启动快。启用方式见上文"系统要求"。
- **TCG（回退）**：纯软件模拟，无需任何硬件特性，但速度慢，仅建议用于安装阶段或无法开启虚拟化的情况。
- 加速模式可在新建向导 / 虚拟机设置中设为 **自动 / 强制 WHPX / 强制 TCG**。

**常见排查**：
- 启动报 "WHPX" 相关错误 → 确认已在 Windows 功能中开启 WHPX 并重启；确认 BIOS 中开启了 VT-x / SVM。
- 与其他开启 Hyper-V 的软件冲突 → 同一台机器上 WHPX 与 Hyper-V 可共存，但与某些第三方虚拟机监控可能冲突，必要时改用 TCG。

---

## 📦 内置 QEMU 运行时

Raster Virtual **开箱内置** QEMU，从程序目录的 `runtime\qemu` 加载，无需用户单独安装。

- 该目录由官方 QEMU Windows 安装包裁剪而来，仅保留 `qemu-system-x86_64`、`qemu-img` 及 x86 平台所需的固件（OVMF / BIOS / VGA BIOS 等）与动态库，体积约 300 MB。
- 裁剪脚本见 `tools/prepare_qemu.py`，可用于用更新的 QEMU 重新生成运行时：
  ```bash
  python tools/prepare_qemu.py "<QEMU 安装目录>" "src/RasterVirtual/runtime/qemu"
  ```
- 若想使用其它版本 QEMU：把对应 QEMU 的 Windows 安装包按上述方式裁剪后，覆盖 `runtime\qemu`，或在 **首选项** 中手动指定其目录。

查找顺序（详见 `Services/QemuLocator.cs`）：
1. 首选项中手动指定的目录
2. 程序目录 `runtime\qemu`（内置）
3. 程序目录 `qemu`
4. 系统 `PATH`
5. 常见安装路径与注册表

---

## 🛠️ 开发与构建

- **技术栈**：.NET 9 (C#) + WPF，MVVM 架构，QEMU 作为后端。
- **编译（Debug）**：
  ```bash
  dotnet build -c Debug
  ```
- **发布为单文件自包含 exe（Release）**：
  ```bash
  dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
  ```
  发布后请将 `src/RasterVirtual/runtime/qemu` 目录一并复制到生成的 exe 同目录下，以保证内置 QEMU 可用。
- 程序图标由 `tools/make_icon.py` 生成（纯标准库，无第三方依赖）。

---

## 📂 目录结构

```
Raster Virtual/
├─ src/RasterVirtual/        # 主程序（.NET 9 WPF）
│  ├─ Models/                # 数据模型（VirtualMachine / 各配置 / 枚举）
│  ├─ Services/              # QEMU 定位、参数构建、QMP 控制、磁盘、仓库
│  ├─ ViewModels/            # MainViewModel / VmItemViewModel
│  ├─ Views/                 # 主窗口、新建向导、设置、快照、首选项
│  ├─ Themes/Dark.xaml       # 深色主题资源
│  └─ runtime/qemu/          # 内置 QEMU 运行时（发布时随 exe 分发）
├─ tools/                    # prepare_qemu.py / make_icon.py 等辅助脚本
└─ README.md
```

---

## ❓ 常见问题

**Q：开机黑屏 / 卡在 SeaBIOS？**
A：确认 ISO 已正确挂载且启动顺序为"光驱优先"；检查是否分配了足够内存。

**Q：性能很卡？**
A：确认已开启 WHPX 硬件加速（首选项 → 运行时 应显示绿色）；未开启时会走 TCG 软件模拟。

**Q：客户机里找不到硬盘 / 网卡？**
A：Windows 等系统可能需要先加载 Virtio 驱动——在第 4 步用"附加驱动光盘"挂上驱动 ISO，安装时手动加载。

**Q：如何彻底删除一台虚拟机及其磁盘？**
A：在主界面删除时会将虚拟机目录移入回收站，可在回收站中找回；磁盘文件一并处理，不会立即破坏。

**Q：程序报错崩溃？**
A：未捕获的异常会写入 `%AppData%\RasterVirtual\crash.log`，可据此反馈问题。

---

## 📜 许可

本项目仅供学习与个人使用。内置 QEMU 遵循其自身的开源许可（GPL）。
