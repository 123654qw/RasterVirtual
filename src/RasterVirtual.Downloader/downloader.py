#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""RasterVirtual-Download.exe —— Raster Virtual 客户端下载安装器。

运行流程：
    1. 用户选择安装目录
    2. 从远程下载 RasterVirtual-Client.zip
    3. 解压到所选目录
    4. 提示完成，可一键打开目录 / 启动

核心下载与解压函数（download_file / extract_zip）不依赖 UI，可独立单元测试。
"""
import os
import sys
import threading
import zipfile
import shutil
import tempfile
import urllib.request
import urllib.error

import tkinter as tk
from tkinter import ttk, filedialog, messagebox

DOWNLOAD_URL = "https://lix-uix.bj.bcebos.com/Raster%20Virtual/RasterVirtual-Client.zip"
APP_NAME = "Raster Virtual"
WINDOW_TITLE = "Raster Virtual 下载器"
EXE_NAME = "RasterVirtual.exe"


# ---------------------------------------------------------------------------
# 核心逻辑（无 UI 依赖，便于测试与复用）
# ---------------------------------------------------------------------------
def download_file(url, dest, progress_cb=None, cancel_flag=None):
    """下载 url 到 dest。

    progress_cb(downloaded, total) 在每次写盘后回调（total 为 0 表示未知大小）。
    返回 True 表示完成；False 表示被取消。
    """
    req = urllib.request.Request(
        url, headers={"User-Agent": "RasterVirtual-Downloader/1.0"}
    )
    with urllib.request.urlopen(req, timeout=30) as resp:
        total = int(resp.headers.get("Content-Length", 0) or 0)
        downloaded = 0
        with open(dest, "wb") as f:
            while True:
                if cancel_flag is not None and cancel_flag.is_set():
                    return False
                chunk = resp.read(64 * 1024)
                if not chunk:
                    break
                f.write(chunk)
                downloaded += len(chunk)
                if progress_cb is not None:
                    progress_cb(downloaded, total)
    return True


def extract_zip(zip_path, dest, progress_cb=None):
    """把 zip 解压到 dest。progress_cb(done, total) 每解压一个条目回调一次。"""
    with zipfile.ZipFile(zip_path) as zf:
        names = zf.namelist()
        total = len(names)
        for i, name in enumerate(names):
            zf.extract(name, dest)
            if progress_cb is not None:
                progress_cb(i + 1, total)


# ---------------------------------------------------------------------------
# GUI
# ---------------------------------------------------------------------------
class DownloaderApp:
    def __init__(self, root):
        self.root = root
        self.cancel_flag = None
        self.running = False

        root.title(WINDOW_TITLE)
        root.geometry("540x320")
        root.resizable(False, False)
        try:
            root.iconbitmap()  # 无图标文件时忽略
        except Exception:
            pass

        # 安装路径
        ttk.Label(root, text="选择 Raster Virtual 的安装位置：").pack(
            anchor="w", padx=20, pady=(18, 6)
        )
        path_row = ttk.Frame(root)
        path_row.pack(fill="x", padx=20)
        self.path_var = tk.StringVar(value=os.path.join(os.path.expanduser("~"), APP_NAME))
        self.path_entry = ttk.Entry(path_row, textvariable=self.path_var)
        self.path_entry.pack(side="left", fill="x", expand=True)
        ttk.Button(path_row, text="浏览…", width=10, command=self.browse).pack(
            side="left", padx=(8, 0)
        )

        # 进度条
        self.bar = ttk.Progressbar(root, maximum=100, mode="determinate")
        self.bar.pack(fill="x", padx=20, pady=(16, 4))
        self.pct_var = tk.StringVar(value="0%")
        ttk.Label(root, textvariable=self.pct_var).pack(anchor="e", padx=20)

        # 状态
        self.status_var = tk.StringVar(value="就绪。选择目录后点击“开始下载”。")
        ttk.Label(root, textvariable=self.status_var, foreground="#555555").pack(
            anchor="w", padx=20, pady=(4, 0)
        )

        # 按钮区
        self.btn_row = ttk.Frame(root)
        self.btn_row.pack(fill="x", padx=20, pady=(18, 0))
        self.start_btn = ttk.Button(
            self.btn_row, text="开始下载", width=14, command=self.start
        )
        self.start_btn.pack(side="left")
        self.cancel_btn = ttk.Button(
            self.btn_row, text="取消", width=12, state="disabled",
            command=self.cancel,
        )
        self.cancel_btn.pack(side="left", padx=(10, 0))

        # 完成后的操作按钮（默认隐藏）
        self.done_row = ttk.Frame(root)
        self.open_btn = ttk.Button(
            self.done_row, text="打开安装目录", width=16, command=self.open_folder
        )
        self.launch_btn = ttk.Button(
            self.done_row, text="启动 Raster Virtual", width=18, command=self.launch
        )
        self.install_path = None

    # ----- UI 行为 -----
    def browse(self):
        d = filedialog.askdirectory(title="选择安装目录", initialdir=self.path_var.get())
        if d:
            self.path_var.set(d)

    def set_running(self, running):
        self.running = running
        self.start_btn.configure(state="disabled" if running else "normal")
        self.cancel_btn.configure(state="normal" if running else "disabled")
        self.path_entry.configure(state="disabled" if running else "normal")

    def start(self):
        if self.running:
            return
        path = self.path_var.get().strip()
        if not path:
            messagebox.showerror("错误", "请先选择安装目录。")
            return
        try:
            os.makedirs(path, exist_ok=True)
        except Exception as e:
            messagebox.showerror("错误", f"无法创建安装目录：\n{e}")
            return
        self.install_path = path
        self.set_running(True)
        self._set_status("正在下载客户端…")
        self.cancel_flag = threading.Event()
        threading.Thread(target=self._worker, args=(path,), daemon=True).start()

    def cancel(self):
        if self.cancel_flag is not None:
            self.cancel_flag.set()
            self._set_status("已取消。")

    def _worker(self, path):
        tmp = tempfile.mkdtemp(prefix="rvdl_")
        zip_path = os.path.join(tmp, "RasterVirtual-Client.zip")
        try:
            ok = download_file(
                DOWNLOAD_URL, zip_path, self._on_dl_progress, self.cancel_flag
            )
            if not ok:
                self._finish(False, "已取消下载。")
                return
            self.root.after(0, lambda: self._set_status("正在解压客户端…"))
            self.root.after(0, self._reset_bar)
            extract_zip(zip_path, path, self._on_ex_progress)
            try:
                os.remove(zip_path)
            except OSError:
                pass

            exe = os.path.join(path, EXE_NAME)
            if not os.path.exists(exe):
                self._finish(False, "解压完成，但未找到 RasterVirtual.exe。")
                return
            self._finish(True, f"安装完成！Raster Virtual 已安装到：\n{path}")
        except urllib.error.URLError as e:
            self._finish(False, f"下载失败（网络错误）：\n{e}")
        except Exception as e:
            self._finish(False, f"安装出错：\n{e}")
        finally:
            shutil.rmtree(tmp, ignore_errors=True)

    # ----- 回调（由工作线程调用，统一切换回主线程更新 UI） -----
    def _on_dl_progress(self, done, total):
        self.root.after(0, self._update_bar, done, total)

    def _on_ex_progress(self, done, total):
        self.root.after(0, self._update_bar, done, total)

    def _update_bar(self, done, total):
        pct = (done / total * 100) if total else 0
        self.bar.configure(value=pct)
        self.pct_var.set(f"{int(pct)}%")

    def _reset_bar(self):
        self.bar.configure(value=0)
        self.pct_var.set("0%")

    def _set_status(self, msg):
        self.status_var.set(msg)

    def _finish(self, success, msg):
        self.root.after(0, self._show_finish, success, msg)

    def _show_finish(self, success, msg):
        self.set_running(False)
        self._set_status(msg)
        if success:
            self.done_row.pack(fill="x", padx=20, pady=(10, 0))
            self.open_btn.pack(side="left")
            self.launch_btn.pack(side="left", padx=(10, 0))
            messagebox.showinfo("完成", msg)
        else:
            messagebox.showerror("失败", msg)

    def open_folder(self):
        if self.install_path and os.path.isdir(self.install_path):
            os.startfile(self.install_path)

    def launch(self):
        if self.install_path:
            exe = os.path.join(self.install_path, EXE_NAME)
            if os.path.exists(exe):
                os.startfile(exe)


def main():
    root = tk.Tk()
    DownloaderApp(root)
    root.mainloop()


if __name__ == "__main__":
    main()
