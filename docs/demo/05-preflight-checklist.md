# Pre-flight Checklist — 15 นาทีก่อน Demo

> **ทำตามนี้ก่อนลูกค้าเข้าห้อง 15 นาที**
> ใช้เวลาน้อย ป้องกันความผิดพลาด 95%

## Engineer ทำ (10 นาที)

- [ ] **Healthcheck ขึ้น**
  ```bash
  curl -s https://drm.zcr.ai/healthz
  ```
  คาดผล: `{"status":"ok"}`

- [ ] **3 ลิ้งก์โหลดได้**
  ```bash
  for url in https://drm.zcr.ai/admin/ https://drm.zcr.ai/me/ https://drm.zcr.ai/share/; do
    echo "$url → $(curl -s -o /dev/null -w '%{http_code}' $url)"
  done
  ```
  คาดผล: ทั้ง 3 ขึ้น HTTP 200

- [ ] **Agent discover ตอบ identity ของ demo@zcr.ai**
  ```bash
  curl -s "https://drm.zcr.ai/api/agent/discover?email=demo@zcr.ai" | head -c 300
  ```
  คาดผล: JSON ที่มี `tenantId`, `userId`, `defaultPolicyTemplateId` ครบ
  (ดู [09-prod-seeded-credentials.md](09-prod-seeded-credentials.md) — ถ้า 404 ต้อง re-seed)

- [ ] **zcrDRM Agent บน demo laptop เปิดอยู่ + sign in แล้ว**
  - Start Menu → zcrDRM Agent → MainWindow ขึ้น
  - Title bar อ่านว่า `zcrDRM Agent — Demo Engineer (demo@zcr.ai)` (ไม่ใช่ "Welcome to zcrDRM")
  - Tenant ID + User ID + Policy Template GUIDs pre-fill อยู่ในฟอร์ม
  - ถ้าเห็นหน้า "Welcome to zcrDRM" → คือ first-run ยังไม่เสร็จ → ใส่ `demo@zcr.ai` → Sign in → ทดสอบใหม่
  - ถ้า dialog "We couldn't find..." → seed บน prod หาย → กลับไปทำ discover smoke ข้างบน

- [ ] **Sample PDF บน Desktop ของ demo laptop**
  - `Q4-Sales-Contract-ABC-XYZ.pdf` (~ 2 MB)
  - ทดสอบ right-click → ต้องเห็น "Protect with zcrDRM" → flyout มี 3 รายการ
  - ถ้าไม่เห็นเมนู → restart Explorer (`taskkill /im explorer.exe /f && explorer.exe`)

- [ ] **Default mail client ตั้งไว้บน demo laptop** (Stage 13 — Quick Send เปิด mailto: ให้)
  - Settings → Apps → Default apps → Mail → ต้องมีค่า (Outlook / Thunderbird / built-in Mail ก็ได้)
  - ถ้าเป็นช่อง blank → mailto: จะเงียบหาย ตอน demo ลูกค้าเห็นแค่ status text ไม่เห็นอีเมลเด้ง
  - ทดสอบ: เปิด PowerShell แล้ว `Start-Process "mailto:test@test.com?subject=preflight"` → mail client ต้องเด้งขึ้น

- [ ] **Stage 13 Quick Send smoke — เครื่อง demo laptop ส่งจริงผ่านสำเร็จ**
  - Right-click test PDF → Protect with zcrDRM → Quick send
  - ใส่อีเมลจริงที่ engineer เช็คได้ → Send protected file
  - ต้องเห็นทั้งสามอย่าง: status `✅ Wrote ... .drmx` + `.drmx` บน Desktop + mail composer เปิดเอง
  - เปิด share URL ใน incognito → ใส่อีเมล → ใส่รหัส 6 หลัก → ต้อง land ที่หน้า file พร้อมปุ่ม Download
  - ถ้า status ขึ้น `Share-link failed: HTTP 400` — เช็ค `ClientApiKeyBox` ว่าเป็น `DEMO_ADMIN_KEY`

- [ ] **Demo tenant + template ยังอยู่**
  ```bash
  curl -s -H "X-DRM-Admin-Key: $DEMO_ADMIN_KEY" \
    "https://drm.zcr.ai/api/admin/policy-templates?tenantId=$DEMO_TENANT" | head -3
  ```
  คาดผล: template "Confidential Contract" ขึ้นมา

- [ ] **Brute-force policy ตั้งไว้ที่ threshold=3** (เผื่อ demo brute-force)
  ```bash
  curl -s -H "X-DRM-Admin-Key: $DEMO_ADMIN_KEY" \
    "https://drm.zcr.ai/api/admin/brute-force-policy?tenantId=$DEMO_TENANT"
  ```
  คาดผล: `"threshold":3,"windowMinutes":30,"enabled":true`

- [ ] **Sample share URL สด** (ทดลองส่งไฟล์ใหม่ — เผื่อ link เก่าหมดอายุ)
  ส่งไฟล์ใหม่ที่ /me/ → เก็บ share URL ใหม่
  Share URL ใหม่: `_______________________________________`

- [ ] **Verification code มาทาง email หรือ standby ดูจาก server log**
  ถ้า email ไม่ทำงาน — เปิด terminal ค้างไว้:
  ```bash
  ssh root@drm.zcr.ai 'docker compose -f /opt/drm/deploy/management/docker/docker-compose.yml logs -f drm-server' \
    | grep -i "verification.*code"
  ```

- [ ] **มี chat channel เปิดกับเจ้าของ** (LINE / Slack / Teams) — เผื่อต้องส่งรหัสด่วน

## เจ้าของทำ (5 นาที)

- [ ] **เตรียม windows ทั้งหมดให้พร้อม:**
  - Browser Tab 1: <https://drm.zcr.ai/admin/> — login ด้วย demo session แล้ว
  - Browser Tab 2: <https://drm.zcr.ai/share/> — เปิด share URL ใหม่ใน incognito (สำหรับ Part 3)
  - **zcrDRM Agent main window: ปิดไว้ก่อน** — จะเปิดผ่านคลิกขวาตอน demo จริง
  - File Explorer: เปิดที่ Desktop, มี Q4-Sales-Contract-ABC-XYZ.pdf เห็นชัด
  - Browser Tab 3 (fallback only): <https://drm.zcr.ai/me/> — ถ้าจำเป็นต้อง fallback

- [ ] **ปิด notification ทั้งหมด:**
  - macOS: System Settings → Notifications → Focus mode
  - Windows: Settings → System → Focus assist → Alarms only
  - ปิด Slack/Teams/LINE/Mail

- [ ] **Zoom browser 110-125%** (ลูกค้านั่งห่างเห็นได้)

- [ ] **Full screen browser** (F11) — ลด distraction

- [ ] **Bookmark bar ปิด** ถ้ามี — กดอะไรไม่ใช่จะตกใจ

- [ ] **มีไฟล์ตัวอย่างพร้อม** บน Desktop:
  - `Q4-Sales-Contract-ABC-XYZ.pdf` (~ 2 MB, PDF จริง)
  - ลากลง drop zone ได้ทันที

- [ ] **อ่าน [03-demo-script.md](03-demo-script.md) อีกรอบ** — โดยเฉพาะ "Tips สิ่งที่ห้ามทำ"

- [ ] **น้ำดื่ม** บนโต๊ะ — demo 10-15 นาที พูดเยอะ

- [ ] **มือถือเงียบ** — เปิด silent mode

## 🚨 ถ้ามีปัญหาตอน demo

อ่าน [06-fallback-plan.md](06-fallback-plan.md) ไว้ก่อนเริ่ม

## เริ่ม demo

```
สวัสดีครับ ขอบคุณที่สละเวลา...
```

→ [03-demo-script.md](03-demo-script.md)
