#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""无头自测：验证 download_file / extract_zip 核心逻辑可用（不创建 Tk 窗口）。

- 对真实下载 URL 做「限量下载 + 取消」测试，验证流式下载与取消生效。
- 用合成 zip 验证 extract_zip 的解压与进度回调正确。
"""
import os
import sys
import io
import zipfile
import threading

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import downloader

URL = downloader.DOWNLOAD_URL
PASS = 0
FAIL = 0


def check(name, cond):
    global PASS, FAIL
    if cond:
        PASS += 1
        print(f"  ✓ {name}")
    else:
        FAIL += 1
        print(f"  ✗ {name}")


def test_download_cancel():
    print("[测试] 限量下载 + 取消")
    tmp = os.path.join(os.path.dirname(__file__), "_selftest_dl")
    os.makedirs(tmp, exist_ok=True)
    dest = os.path.join(tmp, "part.zip")
    cancel = threading.Event()
    states = []

    def cb(done, total):
        states.append((done, total))
        if done >= 2 * 1024 * 1024:  # 下满 2MB 就取消
            cancel.set()

    ok = downloader.download_file(URL, dest, cb, cancel)
    check("下载函数因取消返回 False", ok is False)
    check("产生了部分文件", os.path.exists(dest))
    size = os.path.getsize(dest)
    check(f"已下载约 2MB（实际 {size/1024/1024:.1f}MB）", size >= 1.5 * 1024 * 1024)
    check("进度回调被触发", len(states) > 0)
    # 清理
    try:
        os.remove(dest)
        os.rmdir(tmp)
    except OSError:
        pass


def test_extract():
    print("[测试] 合成 zip 解压")
    tmp = os.path.join(os.path.dirname(__file__), "_selftest_ex")
    os.makedirs(tmp, exist_ok=True)
    zpath = os.path.join(tmp, "sample.zip")
    out = os.path.join(tmp, "out")

    # 构造合成 zip
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w") as zf:
        zf.writestr("hello.txt", "hi")
        zf.writestr("sub/world.txt", "yo")
    with open(zpath, "wb") as f:
        f.write(buf.getvalue())

    counts = []
    downloader.extract_zip(zpath, out, lambda d, t: counts.append((d, t)))
    check("hello.txt 已解压", os.path.exists(os.path.join(out, "hello.txt")))
    check("sub/world.txt 已解压", os.path.exists(os.path.join(out, "sub", "world.txt")))
    check("进度回调次数 = 条目数(2)", len(counts) == 2)
    check("最后一次进度 done==total", counts[-1] == (2, 2))

    # 清理
    import shutil
    shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    print("=== RasterVirtual-Download 核心逻辑自测 ===")
    test_download_cancel()
    test_extract()
    print(f"\n结果：通过 {PASS} / 失败 {FAIL}")
    sys.exit(1 if FAIL else 0)
