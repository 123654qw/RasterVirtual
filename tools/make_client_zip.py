import zipfile, os

PUBLISH = r"E:/PC/Raster Virtual/publish"
OUT = r"E:/PC/Raster Virtual/RasterVirtual-Client.zip"

# 仅打包客户端运行必需组件：exe、WPF 原生 DLL、Assets、runtime/qemu
# publish 目录本就只含构建产物，这里再做一层源码/开发文件保护
EXCLUDE_EXT = {".cs", ".csproj", ".sln", ".xaml", ".user", ".suo", ".tmp", ".pdb"}
EXCLUDE_DIRS = {"obj", "bin", ".git", ".workbuddy"}

count = 0
total = 0
with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED) as z:
    for root, dirs, files in os.walk(PUBLISH):
        dirs[:] = [d for d in dirs if d.lower() not in EXCLUDE_DIRS]
        for f in files:
            if os.path.splitext(f)[1].lower() in EXCLUDE_EXT:
                continue
            fp = os.path.join(root, f)
            arc = os.path.relpath(fp, PUBLISH)
            z.write(fp, arc)
            count += 1
            total += os.path.getsize(fp)

print(f"已打包 {count} 个文件，原始大小 {total/1024/1024:.1f} MB")
print(f"ZIP 输出: {OUT}")
print(f"ZIP 大小: {os.path.getsize(OUT)/1024/1024:.1f} MB")
