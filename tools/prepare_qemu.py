"""从 QEMU 完整安装目录裁剪出 Raster Virtual 需要的最小运行时。

只保留 x86_64 目标所需的可执行文件、动态库与固件，
其它架构（sparc / ppc / mips / s390 …）的模拟器和固件全部剔除，
可把体积从 ~1.5 GB 压到 ~300 MB。

用法：
    python prepare_qemu.py <完整安装目录> <输出目录>
"""
import os
import shutil
import sys

# 需要保留的可执行文件
KEEP_EXE = {
    "qemu-system-x86_64.exe",
    "qemu-system-x86_64w.exe",
    "qemu-img.exe",
    "qemu-io.exe",
    "qemu-edid.exe",
    "qemu-ga.exe",
}

# 需要保留的固件 / ROM（x86 平台相关）
KEEP_FIRMWARE_PREFIX = (
    "bios",
    "vgabios",
    "efi-",
    "edk2-i386",
    "edk2-x86_64",
    "kvmvapic",
    "linuxboot",
    "multiboot",
    "pvh",
    "sgabios",
    "pxe-",
    "qboot",
    "hyperv",
)

# 明确剔除的其它架构固件前缀
DROP_FIRMWARE_PREFIX = (
    "openbios", "palcode", "s390", "u-boot", "hppa", "opensbi",
    "npcm7xx", "qemu_vga.ndrv", "slof", "skiboot", "canyonlands",
    "petalogix", "ppc_rom", "spapr", "vof", "bamboo", "qemu-nsis",
    "edk2-aarch64", "edk2-arm", "edk2-riscv", "edk2-loongarch",
)

# 整个目录直接跳过
SKIP_DIRS = {"locale", "doc", "icons", "applications", "man"}

FIRMWARE_EXT = {".bin", ".rom", ".fd", ".img", ".dtb", ".elf", ".dat", ".ndrv"}


def keep_firmware(name: str) -> bool:
    lower = name.lower()

    for p in DROP_FIRMWARE_PREFIX:
        if lower.startswith(p):
            return False

    for p in KEEP_FIRMWARE_PREFIX:
        if lower.startswith(p):
            return True

    return False


def should_copy(rel_path: str, name: str) -> bool:
    lower = name.lower()
    parts = rel_path.replace("\\", "/").split("/")

    for part in parts[:-1]:
        if part.lower() in SKIP_DIRS:
            return False

    ext = os.path.splitext(lower)[1]

    # 可执行文件：白名单
    if ext == ".exe":
        return lower in KEEP_EXE

    # 动态库全部保留，依赖关系复杂不做裁剪
    if ext == ".dll":
        return True

    # 固件按前缀判断
    if ext in FIRMWARE_EXT:
        return keep_firmware(lower)

    # 键盘映射、许可证等小文件保留
    return True


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)

    source = os.path.abspath(sys.argv[1])
    target = os.path.abspath(sys.argv[2])

    if not os.path.isdir(source):
        print(f"源目录不存在：{source}")
        sys.exit(1)

    if os.path.exists(target):
        shutil.rmtree(target)
    os.makedirs(target, exist_ok=True)

    copied = 0
    skipped = 0
    total_bytes = 0

    for root, dirs, files in os.walk(source):
        dirs[:] = [d for d in dirs if d.lower() not in SKIP_DIRS]

        for name in files:
            src_file = os.path.join(root, name)
            rel = os.path.relpath(src_file, source)

            if not should_copy(rel, name):
                skipped += 1
                continue

            dst_file = os.path.join(target, rel)
            os.makedirs(os.path.dirname(dst_file), exist_ok=True)
            shutil.copy2(src_file, dst_file)

            copied += 1
            total_bytes += os.path.getsize(src_file)

    print(f"已复制 {copied} 个文件，跳过 {skipped} 个")
    print(f"运行时体积：{total_bytes / 1024 / 1024:.1f} MB")
    print(f"输出目录：{target}")

    probe = os.path.join(target, "qemu-system-x86_64.exe")
    if os.path.exists(probe):
        print("校验通过：qemu-system-x86_64.exe 存在")
    else:
        print("警告：未找到 qemu-system-x86_64.exe，请检查源目录结构")
        sys.exit(2)


if __name__ == "__main__":
    main()
