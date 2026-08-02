import os, time, subprocess, sys

path = r"E:\PC\Raster Virtual\tools\qemu-setup.exe"
log = []
def L(m): log.append(str(m)); print(m, flush=True)

# 1) size growth
s1 = os.path.getsize(path)
time.sleep(5)
s2 = os.path.getsize(path)
L(f"size t0={s1} t1={s2} growing={s2!=s1}")

# 2) lock test: try exclusive open
for mode in ("rb", "r+b"):
    try:
        f = open(path, mode)
        f.close()
        L(f"open {mode}: OK")
    except Exception as e:
        L(f"open {mode}: FAIL {type(e).__name__}: {e}")

# 3) try copy
cpy = r"E:\PC\Raster Virtual\tools\qemu-setup-copy.exe"
try:
    with open(path,"rb") as a, open(cpy,"wb") as b:
        b.write(a.read())
    L(f"copy OK size={os.path.getsize(cpy)}")
except Exception as e:
    L(f"copy FAIL: {type(e).__name__}: {e}")

# 4) list windows processes (tasklist) to spot downloaders
try:
    out = subprocess.run(["tasklist.exe","/FO","CSV"], capture_output=True, text=True, timeout=30)
    for line in out.stdout.splitlines():
        low = line.lower()
        if any(k in low for k in ("qemu","download","curl","wget","bitsadmin","chrome","edge","brave","firefox","idm","motrix","aria2")):
            L("PROC: "+line)
except Exception as e:
    L(f"tasklist FAIL: {e}")

# 5) attempt run via subprocess (will show real error if still locked)
dest = r"E:\PC\Raster Virtual\runtime\_extract"
os.makedirs(dest, exist_ok=True)
try:
    r = subprocess.run([path,"/S",f"/D={dest}"], capture_output=True, text=True, timeout=60)
    L(f"run exit={r.returncode} out={r.stdout[:200]} err={r.stderr[:200]}")
except Exception as e:
    L(f"run FAIL: {type(e).__name__}: {e}")

with open(r"E:\PC\Raster Virtual\tools\diag_py.log","w") as f:
    f.write("\n".join(log))
