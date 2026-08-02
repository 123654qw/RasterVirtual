$log = "E:\PC\Raster Virtual\tools\diag.log"
$exe = "E:\PC\Raster Virtual\tools\qemu-setup.exe"
$sb = [Text.StringBuilder]::new()
function Log($m){ [void]$sb.AppendLine($m) }

$s1 = (Get-Item $exe).Length
Start-Sleep -Seconds 4
$s2 = (Get-Item $exe).Length
Log("size t0=$s1 t1=$s2 growing=$(($s2 -ne $s1))")

# try copy
$copy = "E:\PC\Raster Virtual\tools\qemu-setup-copy.exe"
try { Copy-Item $exe $copy -Force; Log("copy OK size=$((Get-Item $copy).Length)") }
catch { Log("copy FAILED: $($_.Exception.Message)") }

# list processes that might lock
$procs = Get-Process | Where-Object { $_.Name -match "qemu|download|curl|wget|bits|edge|chrome|brave|firefox" }
Log("suspect procs:")
foreach($p in $procs){ Log("  $($p.Id) $($p.Name) $($p.Path)") }

# try run copy if exists
if (Test-Path $copy) {
  $dest = "E:\PC\Raster Virtual\runtime\_extract"
  if (Test-Path $dest){ Remove-Item $dest -Recurse -Force }
  New-Item -ItemType Directory -Path $dest | Out-Null
  try {
    $pr = Start-Process -FilePath $copy -ArgumentList "/S","/D=$dest" -Wait -NoNewWindow -PassThru
    Log("run copy exitcode=$($pr.ExitCode)")
    Log("extract contents:")
    Get-ChildItem $dest | ForEach-Object { Log("  $($_.Name)") }
  } catch { Log("run copy FAILED: $($_.Exception.Message)") }
}

Set-Content -Path $log -Value $sb.ToString()
Log("done")
Set-Content -Path $log -Value $sb.ToString()
