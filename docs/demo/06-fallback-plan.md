# Fallback Plan — ถ้าเกิดปัญหาตอน Demo

> เก็บไว้เปิดเร็ว ๆ ถ้าระบบมีปัญหา
> ปัญหาส่วนใหญ่แก้ในเวลา 30 วินาที ถ้ารู้ก่อนล่วงหน้า

## หลัก: อย่าตื่นตระหนก

ลูกค้าจะดูความเป็นมืออาชีพจากการ **รีแอ็ค** ของคุณมากกว่าจากปัญหาเอง.
ทุกระบบมีปัญหาได้ — สำคัญคือคุณจัดการอย่างไร

### ประโยคที่ใช้ได้เสมอ

```
ขอเวลาแค่นาทีนะครับ — ระบบจริงในเครื่องลูกค้า on-prem
จะ stable กว่านี้ — ตัวนี้เป็น cloud demo ที่ shared ใช้ทั้งทีม
```

หรือ:

```
มาเปลี่ยนเล่าให้ดูแบบนี้แทน — ทุกอย่างมีใน slides ครับ
```

## ปัญหาที่อาจเกิด + วิธีแก้

### P1: 3 ลิ้งก์เปิดไม่ขึ้น HTTP 5xx

**สิ่งที่เห็น:** browser แสดงหน้า error / "This site can't be reached"

**สิ่งที่พูดต่อลูกค้า:**
```
ขอผมเช็คสถานะหลังบ้านก่อนนะครับ
```

**สิ่งที่ทำ:**
1. เปิดมือถือ — ทักหา engineer ผ่าน chat: "เซิร์ฟเวอร์ลง help"
2. ระหว่างรอ — ใช้ slide / screenshot ที่เตรียมไว้แทน

**Engineer ทำ:**
```bash
# SSH เข้า server
ssh root@drm.zcr.ai

# เช็ค container
docker ps

# ถ้า drm-server down → restart
docker compose -f /opt/drm/deploy/management/docker/docker-compose.yml restart drm-server

# รอ 15-20 วินาที — ดู healthy
docker inspect docker-drm-server-1 --format "{{.State.Health.Status}}"
```

**Recovery ปกติ: ~30-60 วินาที**

### P2: Welcome modal ไม่ขึ้น / session หาย

**สิ่งที่เห็น:** เปิด /admin/ มา แต่ไม่มี modal "Create test tenant"

**สาเหตุ:** localStorage ของเครื่อง demo ถูกเคลียร์ หรือเปิดในเครื่องอื่น

**วิธีแก้:**
1. เปิด DevTools → Application tab → Local Storage → `https://drm.zcr.ai`
2. ลบ key `drm-onboarded-welcome` (ถ้ามี)
3. Refresh

**หรือพูดต่อลูกค้า:**
```
session ของผมเคลียร์ไป — งั้นเข้าตรงไปที่ Policy templates เลยครับ
```

### P2.5: zcrDRM Agent ไม่เปิด / Right-click menu หาย / Sign-in fail

**สิ่งที่เห็น:** คลิกขวา PDF ไม่เห็น "Protect with zcrDRM" — หรือเปิด agent แล้วยังเห็นหน้า "Welcome to zcrDRM" — หรือ sign-in dialog แสดง "We couldn't find demo@zcr.ai"

**สิ่งที่พูดต่อลูกค้า:**
```
ผมจะเล่าจากฝั่ง web แทนก็ได้ครับ — agent ลงในเครื่องพนักงานจริงทำงานนิ่งกว่านี้
ของวันนี้เป็น demo cloud ที่ใช้ร่วมหลายทีม
```

**fallback flow ที่ใช้แทน Part 2:**

เปิด <https://drm.zcr.ai/me/> (Browser Tab 3) — flow เดิมก่อนมี agent:

1. Tenant ID = `dddddddd-1111-2222-3333-dddddddddddd` (จาก [09-prod-seeded-credentials.md](09-prod-seeded-credentials.md))
2. User ID = `eeeeeeee-1111-2222-3333-eeeeeeeeeeee` (demo engineer) หรือ `aaaaaaaa-1111-2222-3333-aaaaaaaaaaaa` (Somchai)
3. ลาก-วาง `Q4-Sales-Contract-ABC-XYZ.pdf`
4. Recipient: `malee@xyz.com`
5. กด Send protected file
6. Copy share URL → ใช้ใน Part 3

**สิ่งที่พูดต่อตอนทำ:**
```
หน้านี้คือ web fallback สำหรับองค์กรที่ยังไม่ลง agent
ทำได้เหมือนกัน แต่พนักงานต้องเปิด website + จำ tenant/user
agent ตัวจริงคลิกขวาเสร็จเลย ไม่ต้องเปิด browser
```

**Engineer recovery (ทำหลัง demo / ถ้า demo ยังไม่ถึง Part 3):**

```powershell
# ลบ identity cache แล้ว sign in ใหม่
Remove-Item "$env:LOCALAPPDATA\zcrDRM\identity.bin" -Force -ErrorAction SilentlyContinue
Start-Process "C:\Program Files\zcrDRM\Drm.Agent.Tray.Windows.exe"
# จะขึ้น first-run dialog ใหม่ — พิมพ์ demo@zcr.ai
```

ถ้า right-click menu หาย:
```powershell
taskkill /im explorer.exe /f
explorer.exe
```

ถ้า sign-in fail (404 จาก discover) — seed บน prod หาย → re-seed ตาม recipe ใน [09-prod-seeded-credentials.md](09-prod-seeded-credentials.md)

### P3: Send button ค้าง / spinner ไม่หาย ที่ /me/

**สิ่งที่เห็น:** กด Send แล้วปุ่มเทาค้าง

**สาเหตุ:** Tenant ID หรือ User ID ไม่ valid

**สิ่งที่พูดต่อลูกค้า:**
```
ขอผมตรวจการ config นิดหน่อย
```

**วิธีแก้:**
- DevTools → Console — ดู error
- ถ้าเห็น 400/401 — Tenant ID/User ID ผิด
- แก้ในฟอร์ม settings ของ /me/ → ใส่ค่าจาก engineer prep

**Fallback:** ใช้ share URL ที่ engineer prep ไว้ก่อนแล้ว — ข้าม step "ส่งไฟล์สด"
```
ผมมี share URL ตัวอย่างที่ผมเตรียมไว้ — เปิดเลยเลยครับ
```

### P4: Verification code ไม่มาในอีเมล

**สาเหตุที่พบบ่อย:** SMTP integration ยังไม่ได้ตั้งใน demo tenant

**Engineer ต้อง prep ไว้:**
- Standby SSH session เปิดอยู่ พร้อมคำสั่งดู log
- เห็นรหัสที่จะถูกส่ง → ทักมาแชทบอกคุณ

**Engineer คำสั่ง:**
```bash
ssh root@drm.zcr.ai 'docker compose -f /opt/drm/deploy/management/docker/docker-compose.yml logs --tail=20 drm-server' \
  | grep -i "verification.*code"
```

**สิ่งที่พูดต่อลูกค้า:**
```
ระบบนี้ส่งรหัสจริงทาง email — สำหรับ demo cloud ผมใช้ test SMTP
ขอผม fetch รหัสจาก log ให้นะครับ (เผื่ออธิบาย):
ในระบบ production ของลูกค้าจะตั้งกับ Exchange/Postfix/SES ของลูกค้าจริง
```

### P5: Wrong code ตอน verify

**สิ่งที่เห็น:** ใส่รหัสแล้ว "invalid_verification_code"

**สาเหตุ:** อ่าน log ผิด หรือ verification expired (10 นาที)

**วิธีแก้:**
- กด "Send verification code" ใหม่ — ได้รหัสใหม่
- แต่ระวัง — มันจะ count เป็น failed attempt ของรอบเก่า ถ้าเข้า threshold = revoke

**สิ่งที่พูดต่อลูกค้า:**
```
ผมขอเริ่ม verification ใหม่นะครับ — verification code มีอายุ 10 นาที
นี่เป็น security feature — ลูกค้าจริงตั้ง expiry ได้ที่ admin
```

### P6: Tenants tab ไม่ทำงาน / fallback to Overview

**สิ่งที่เห็น:** กด Tenants tab แล้ว URL ขึ้น `#tab-tenants` แต่ panels ของ overview ยังโชว์

**สาเหตุ:** v1.2.x bug regression — ไม่ควรเกิดในปัจจุบัน (fix แล้วใน v1.2.2+)
ถ้าเกิดอีก = bug ใหญ่ ต้อง report ทันที

**วิธีแก้ระหว่าง demo:**
```
มาดู Overview tab ก่อนแล้วกลับมา Tenants ครับ
```
ข้าม Tenants ในรอบ demo นี้ — ใช้ tab อื่นแทน

### P7: Print Screen ตอนเปิด WPF viewer แล้วลูกค้าเห็น black rectangle

**นี่ไม่ใช่ปัญหา — เป็น FEATURE ของ C3!**

**สิ่งที่พูด:**
```
ตรงนี้คือ Screen Capture Protection ที่เราเพิ่งเปิดตัวใน v1.6.0 ครับ
Windows OS เห็นว่า viewer นี้ถูกป้องกัน — Snipping Tool, Print Screen, OBS, Teams screen share
ทั้งหมดจะแสดงเป็น black rectangle
ระดับเดียวกับ Netflix DRM
```

จงปั้นมันเป็นจุดเด่นไป — ไม่ใช่ปัญหา

### P8: Browser console ขึ้น error สีแดง

**สิ่งที่เห็น:** ลูกค้าเห็น DevTools → ตกใจ

**ทำทันที:**
```
F12 ปิด console
หรือ Ctrl+Shift+I ปิด DevTools
```

**สิ่งที่พูดถ้ามี follow-up:**
```
ขอโทษครับ ผมเปิดผิด — สำหรับ developer ดู debug
ระบบใช้งานจริงไม่เห็นนี้
```

### P9: Wi-Fi ลูกค้าช้า — โหลด /admin/ ช้า 5+ วินาที

**ลูกค้าจะรอ — แต่หน้าเก่ายัง cached อยู่**

**สิ่งที่พูด:**
```
ลูกค้าจริง deploy on-prem ในเครื่องของคุณ — โหลดไม่กี่ ms
demo cloud ของผมอยู่ที่ Singapore — มีลาดบางทีจาก network
```

### P10: ลูกค้าถามอะไรที่ตอบไม่ได้

อย่าเดา — อ่าน [04-customer-questions.md](04-customer-questions.md) ส่วน "ห้ามเดา"

```
ขอผมเช็คข้อมูลที่ตรงนี้ก่อนครับ
ผม follow-up ใน email ภายในวันนี้นะครับ
```

จดคำถามไว้ — ตอบหลัง demo ดีกว่าตอบผิด

## 🔥 ถ้าทุกอย่างพังหมด

**Worst case:** ระบบลง 5 นาทีแล้วยังขึ้นไม่ได้ ลูกค้ารออยู่

**Plan B: เปลี่ยนเป็น screenshot tour**

1. **เปิด screenshot ที่เตรียมไว้** (ขอ engineer screenshot ก่อนแล้วเก็บใน laptop)
   - Screenshot ของ /admin/ overview
   - Screenshot ของ /me/ form
   - Screenshot ของ /share/ verification

2. **พูดต่อลูกค้า:**
   ```
   มี network issue กับ cloud demo นิดหน่อย — ผมขอเปลี่ยนเป็น
   walk through ผ่าน screenshot ครับ ระบบทำงานเหมือนกัน
   เพราะลูกค้าจริงจะ deploy on-prem อยู่แล้ว — เห็นภาพชัดเจน
   ```

3. **เดินเรื่องด้วย screenshot ตามลำดับ:**
   - /admin/ → ชี้ Hero pillars, Policy template
   - /me/ → ชี้ drop zone, recipient field
   - /share/ → ชี้ verification flow

4. **End:**
   ```
   ขอ schedule follow-up demo live เมื่อระบบ ready นะครับ
   หรือถ้าลูกค้าสนใจ — ผมส่ง Docker compose ให้ deploy ในเครื่องคุณเอง
   ใช้เวลา 5 นาที จริง ๆ ตอน installation
   ```

## Post-demo

ไม่ว่ามีปัญหาหรือไม่:

1. **ส่ง follow-up email** ภายใน 24 ชั่วโมง:
   - ขอบคุณ
   - สรุปจุดที่ลูกค้าสนใจ
   - ตอบคำถามที่ค้าง
   - แนบลิ้งก์ datasheet (ถ้ามี)
   - ขอ next step (schedule pilot? proposal?)

2. **Engineer ลบ test data** ของ demo session:
   ```bash
   # ลบ tenant ที่ใช้ demo ออก (ถ้าไม่ต้องเก็บ)
   # ผ่าน admin console → Tenants tab → suspend → delete
   ```

3. **เขียน lessons learned** สำหรับ demo ครั้งหน้า — ที่ไหนสะดุด, อะไรลื่นไหล
