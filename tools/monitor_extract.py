import os, time, subprocess, sys

BASE = r"E:\PC\Raster Virtual"
SETUP = os.path.join(BASE, "tools", "qemu-setup.exe")
EXTRACT = os.path.join(BASE, "runtime", "_extract")
RUNTIME = os.path.join(BASE, "src", "RasterVirtual", "runtime", "qemu")
PREP = os.path.join(BASE, "tools", "prepare_qemu.py")
LOGP = os.path.join(BASE, "tools", "extract.log")
DONE = os.path.join(BASE, "tools", "extract_done.marker")

log = []
def L(m):
    s = f"[{time.strftime('%H:%M:%S')}] {m}"
    log.append(s)
    print(s, flush=True)

def size():
    try:
        return os.path.getsize(SETUP)
    except Exception:
        return -1

# --- Phase 1: wait for download to stop growing ---
L("phase1: waiting for download to finish...")
prev = size()
stable = 0
while stable < 3:
    time.sleep(8)
    cur = size()
    if cur == prev and cur > 0:
        stable += 1
        L(f"  size stable at {cur} ({stable}/3)")
    else:
        stable = 0
        L(f"  size {prev} -> {cur} (still downloading)")
    prev = cur
L(f"download appears complete at {prev} bytes")

# --- Phase 2: run NSIS silent extract (retry while locked) ---
os.makedirs(EXTRACT, exist_ok=True)
L(f"phase2: running NSIS silent install -> {EXTRACT}")
ok = False
for i in range(15):
    try:
        r = subprocess.run([SETUP, "/S", f"/D={EXTRACT}"], capture_output=True, text=True, timeout=120,
                           creationflags=0x08000000)  # CREATE_NO_WINDOW
        L(f"  attempt {i+1} exit={r.returncode}")
        if r.returncode == 0:
            ok = True
            break
    except PermissionError as e:
        L(f"  attempt {i+1} locked: {e}")
    except Exception as e:
        L(f"  attempt {i+1} err: {type(e).__name__}: {e}")
    time.sleep(5)

if not ok:
    L("ERROR: NSIS install failed/locked after retries")
    sys.exit(2)

# verify
target = os.path.join(EXTRACT, "qemu-system-x86_64.exe")
if not os.path.exists(target):
    L(f"ERROR: {target} not found after extract. Contents:")
    for root, dirs, files in os.walk(EXTRACT):
        for f in files[:20]:
            L(f"  {os.path.relpath(os.path.join(root,f), EXTRACT)}")
    sys.exit(3)
L(f"extract OK: {target}")

# --- Phase 3: crop runtime with prepare_qemu.py ---
os.makedirs(RUNTIME, exist_ok=True)
L(f"phase3: cropping runtime -> {RUNTIME}")
try:
    r = subprocess.run([sys.executable, PREP, EXTRACT, RUNTIME], capture_output=True, text=True, timeout=300)
    L(f"  prepare_qemu exit={r.returncode}")
    if r.stdout: L("  out: " + r.stdout[-1500:])
    if r.stderr: L("  err: " + r.stderr[-1500:])
except Exception as e:
    L(f"  prepare_qemu err: {type(e).__name__}: {e}")

final = os.path.join(RUNTIME, "qemu-system-x86_64.exe")
if os.path.exists(final):
    L(f"SUCCESS: runtime ready at {final} ({os.path.getsize(final)} bytes)")
    with open(DONE, "w") as f:
        f.write("OK\n" + final + "\n")
else:
    L(f"ERROR: cropped runtime missing {final}")

with open(LOGP, "w", encoding="utf-8") as f:
    f.write("\n".join(log))
