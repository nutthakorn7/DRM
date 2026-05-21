# Windows Agent — ติดตั้งบน demo machine

> **สำหรับ engineer ทำบน Windows VM/laptop ที่จะใช้ demo**
> **เวลาใช้ ~10-15 นาที** (ถ้า .NET 10 runtime ลงไว้แล้ว: 5 นาที)

## สิ่งที่ทำได้หลังลง

หลังลง agent บน Windows demo machine:

1. **Right-click ไฟล์ → "Protect with DRM"** — ปกป้องไฟล์ผ่าน context menu
2. **Double-click ไฟล์ .drmx** — เปิดใน zcrDRM viewer ที่มี Screen Capture Protection (C3)
3. **Tray icon** — แสดง agent status, รัน background
4. **Watermark live overlay** — ทุก protected file ที่เปิดเห็น watermark
5. **Screen capture blocked** — Snipping Tool / Print Screen / OBS / Teams screen share = black rectangle

## สถานะวันนี้ (โปร่งใส)

| Item | สถานะ |
|------|-------|
| **Code v1.6.1** | ✅ พร้อม |
| **Build จาก Mac dev** | ✅ ทำได้ผ่าน `EnableWindowsTargeting=true` |
| **Runnable Tray + Viewer .exe** | ✅ มี (1.7 MB zip) |
| **MSI installer** | ❌ ยังไม่มี — install เป็น manual extract + PowerShell script |
| **Code-signed binaries** | ❌ ยังไม่ sign — Windows Defender อาจขึ้น warning ตอนรัน |
| **ทดสอบรันบน Windows จริง** | ⚠️ ยังไม่ได้ทำใน session นี้ — ต้องลองที่ Windows VM ก่อน demo |

**สรุปสำหรับ demo:** Code พร้อม artifacts พร้อม แต่ engineer ต้องทดลองรันบน Windows VM อย่างน้อย 1 ครั้งก่อน demo เพื่อยืนยันว่าไม่มี runtime issue

## ขั้นตอนการติดตั้ง

### Step 1 — เตรียม Windows machine

ต้องการ:
- **Windows 10 build 19041+** หรือ **Windows 11** (สำหรับ `WDA_EXCLUDEFROMCAPTURE`)
- ถ้าเป็น Windows 10 build เก่ากว่า — Screen Capture Protection จะ fallback เป็น `WDA_MONITOR` (ทำงานน้อยกว่าหน่อย แต่ยังบล็อก Print Screen)
- พื้นที่ disk: ~50 MB (ไม่นับ .NET runtime)
- Admin permission (เพื่อรัน PowerShell script)

### Step 2 — ลง .NET 10 Desktop Runtime

zcrDRM agent ต้องการ **.NET 10.0 Desktop Runtime (x64)**:

1. ไปที่ <https://dotnet.microsoft.com/download/dotnet/10.0>
2. หา section "Run desktop apps" → download `Windows x64 Installer`
3. รัน installer (Next, Next, Finish)
4. เปิด PowerShell → ทดสอบ:
   ```powershell
   dotnet --list-runtimes
   # ต้องเห็น Microsoft.WindowsDesktop.App 10.0.x
   ```

ถ้าไม่ลง runtime → ตอนรัน .exe จะขึ้น error "To run this application, you must install .NET"

### Step 3 — Copy artifacts ไป Windows machine

จากเครื่อง dev (Mac/Linux ที่มี source):

```bash
# Build agent + viewer ใหม่ล่าสุดจาก master
cd /path/to/DRM
~/.dotnet/dotnet publish src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj \
  -c Release -r win-x64 --self-contained false
~/.dotnet/dotnet publish src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj \
  -c Release -r win-x64 --self-contained false

# Package ทั้งคู่ไว้ใน folder เดียว
mkdir -p artifacts/zcrdrm-agent-v1.6.1
cp -r src/Drm.Agent.Tray.Windows/bin/Release/net10.0-windows/win-x64/publish/* artifacts/zcrdrm-agent-v1.6.1/
cp -r src/Drm.Viewer.Windows/bin/Release/net10.0-windows/win-x64/publish/* artifacts/zcrdrm-agent-v1.6.1/
cp deploy/desktop/register-shell-integration.ps1 artifacts/zcrdrm-agent-v1.6.1/

# Zip
cd artifacts && zip -qr zcrdrm-agent-v1.6.1-win-x64.zip zcrdrm-agent-v1.6.1/
```

**Pre-built zip มีในเครื่องเรา (Mac dev) ที่:**
`artifacts/zcrdrm-agent-v1.6.1-win-x64.zip` (~1.7 MB)

ส่งทาง:
- USB drive
- LINE OA / Slack file share (ขนาดเล็ก ส่งได้)
- Cloud storage (Google Drive / OneDrive)
- SCP จาก Mac → Windows (ถ้ามี SSH server บน Windows)

### Step 4 — Extract บน Windows machine

```powershell
# สมมติ zip อยู่ที่ Downloads
cd $env:USERPROFILE\Downloads

# Extract
Expand-Archive -Path zcrdrm-agent-v1.6.1-win-x64.zip -DestinationPath C:\

# จะได้ folder C:\zcrdrm-agent-v1.6.1\
# มีไฟล์ทั้งหมด ~44 ไฟล์ ทั้ง .exe และ dependency dll

# ย้าย/rename ไป Program Files (เลือกไม่ทำก็ได้ — ใช้จาก C:\ ตรงๆ ก็ได้)
Move-Item C:\zcrdrm-agent-v1.6.1 "C:\Program Files\EnterpriseDRM"
```

### Step 5 — Register shell integration

PowerShell as admin (ขวาคลิก → Run as Administrator):

```powershell
cd "C:\Program Files\EnterpriseDRM"

.\register-shell-integration.ps1 `
  -TrayPath "C:\Program Files\EnterpriseDRM\Drm.Agent.Tray.Windows.exe" `
  -ViewerPath "C:\Program Files\EnterpriseDRM\Drm.Viewer.Windows.exe"
```

ขั้นนี้ทำ:
- Register `.drmx` file extension → double-click เปิดใน viewer
- Add "Protect with DRM" ใน right-click context menu สำหรับ files

ถ้าต้องการเอาออกหลัง demo:
```powershell
.\register-shell-integration.ps1 -Unregister
```

### Step 6 — Configure agent server URL

เปิด PowerShell:

```powershell
# Agent อ่าน config จาก environment variable
[Environment]::SetEnvironmentVariable("DRM_SERVER_URL", "https://drm.zcr.ai", "User")
[Environment]::SetEnvironmentVariable("DRM_TENANT_ID", "<demo_tenant_id ที่ engineer prep>", "User")
[Environment]::SetEnvironmentVariable("DRM_USER_ID", "<demo user — Somchai>", "User")

# Restart PowerShell หรือ logout/login ให้ env var มีผล
```

หรือสร้าง `appsettings.json` ใน folder agent ก็ได้ (ถ้า agent ออกแบบรองรับ).
**Engineer ต้องดู code ของ Drm.Agent.Core เพื่อยืนยันว่ารับ config จากไหน — ผมยังไม่ verify ตรงนี้**

### Step 7 — รัน Tray agent

Double-click `Drm.Agent.Tray.Windows.exe` หรือ:

```powershell
Start-Process "C:\Program Files\EnterpriseDRM\Drm.Agent.Tray.Windows.exe"
```

จะเห็น tray icon มุมขวาล่าง — agent กำลังรัน

### Step 8 — ทดสอบ end-to-end (จุดที่ต้องลอง ก่อน demo จริง)

1. **Right-click test:** สร้าง file ใดก็ได้ (test.pdf) → right-click → ควรเห็น "Protect with DRM"
2. **Protect a file:** กด "Protect with DRM" → tray app เปิด protect dialog → เลือก policy → submit
3. **Result:** file `test.pdf.drmx` ปรากฏ (encrypted)
4. **Open .drmx:** double-click → viewer เปิด, ใส่ credentials, ดูเอกสาร + watermark
5. **Screen capture test:**
   - Press **Print Screen** → tray status text "Screen capture blocked"
   - Open **Snipping Tool** → drag over viewer window → image ที่ได้ = black rectangle
   - Open **OBS Studio** display capture → viewer area = black
   - Open **Microsoft Teams** call → share screen → ผู้ฟังเห็น viewer = black

### Step 9 — Tear down หลัง demo (ถ้าใช้ VM ที่ใช้ซ้ำ)

```powershell
# Unregister shell integration
cd "C:\Program Files\EnterpriseDRM"
.\register-shell-integration.ps1 -Unregister

# Stop tray agent
Stop-Process -Name "Drm.Agent.Tray.Windows" -Force

# Optional: ลบ env vars
[Environment]::SetEnvironmentVariable("DRM_SERVER_URL", $null, "User")
[Environment]::SetEnvironmentVariable("DRM_TENANT_ID", $null, "User")
[Environment]::SetEnvironmentVariable("DRM_USER_ID", $null, "User")

# Optional: ลบ folder
Remove-Item -Recurse "C:\Program Files\EnterpriseDRM"
```

## ปัญหาที่อาจเจอบน Windows

### "Windows protected your PC" SmartScreen warning

**สาเหตุ:** Binaries ยังไม่ code-sign

**ทางออก:**
- คลิก **"More info"** → **"Run anyway"**
- หรือ unblock ก่อนรัน:
  ```powershell
  Get-ChildItem "C:\Program Files\EnterpriseDRM\*" | Unblock-File
  ```

**สำหรับ demo:** ทำ Step นี้ก่อน demo เพื่อไม่ให้ลูกค้าเห็น warning popup

### ".NET runtime not found"

**สาเหตุ:** ลืม Step 2

**ทางออก:** ลง .NET 10 Desktop Runtime ตาม Step 2

### "Right-click ไม่เห็น Protect with DRM"

**สาเหตุ:** Step 5 register script ไม่สำเร็จ หรือ explorer ไม่ refresh

**ทางออก:**
1. Restart Windows Explorer:
   ```powershell
   Stop-Process -Name explorer -Force
   Start-Process explorer
   ```
2. หรือลอง register script อีกครั้ง

### "Screen capture protection ไม่ทำงาน — ภาพไม่เป็น black"

**สาเหตุ:** Windows 10 build เก่ากว่า 19041 หรือ remote desktop session

**ทางออก:**
- ทดสอบบน Windows 11 หรือ Win10 22H2+
- ห้าม demo ผ่าน Remote Desktop / RDP — เพราะ SetWindowDisplayAffinity ปิดใน RDP บางกรณี
- ใช้ physical Windows machine หรือ HyperV VM แสดงตรงๆ

### "Viewer ไม่เปิด .drmx"

**สาเหตุ:** Shell integration ไม่ผ่าน

**ทางออก:** ลองเปิด viewer ตรงๆ:
```powershell
& "C:\Program Files\EnterpriseDRM\Drm.Viewer.Windows.exe" --open "test.pdf.drmx"
```

ถ้าเปิดได้แบบนี้ — shell integration ผิด รัน register script ใหม่
ถ้าเปิดไม่ได้ — viewer code มีปัญหา ส่ง error message ให้ผม debug

## สำหรับ demo คุณต้องเตรียมล่วงหน้า

อย่างน้อย **3 วันก่อน demo** ลองทำ Step 1-8 บน Windows VM/laptop จริง

ถ้าทำได้ครบ + screen-capture test ผ่าน:
✅ Demo พร้อม

ถ้าติดขัด:
- ทักผมเลย จะ debug ให้
- Plan B: ใช้แค่ web-based demo (/me/, /share/) ไม่ต้องลง agent
  เพราะ /share/ web viewer ก็แสดงเอกสารกับ watermark ได้แล้ว
  เพียงแต่ไม่มี screen-capture protection ระดับ OS

## Summary checklist

- [ ] Windows 10 (build 19041+) หรือ Windows 11 ready
- [ ] .NET 10 Desktop Runtime installed
- [ ] zcrdrm-agent-v1.6.1-win-x64.zip extracted ไป `C:\Program Files\EnterpriseDRM\`
- [ ] register-shell-integration.ps1 รันผ่าน (admin)
- [ ] Environment vars DRM_SERVER_URL, DRM_TENANT_ID, DRM_USER_ID set
- [ ] Tray agent รันได้ มี tray icon
- [ ] Right-click "Protect with DRM" เห็น
- [ ] Protect a file สร้าง .drmx ได้
- [ ] Open .drmx เปิด viewer ได้ มี watermark
- [ ] Print Screen / Snipping Tool / Teams = black rectangle
- [ ] Unblock-File ทำแล้วเพื่อไม่ให้ขึ้น SmartScreen warning ตอน demo
