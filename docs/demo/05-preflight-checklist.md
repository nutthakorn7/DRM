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

- [ ] **เปิด 3 tabs ใน browser** (Chrome แนะนำ):
  - Tab 1: <https://drm.zcr.ai/admin/> — login ด้วย demo session แล้ว
  - Tab 2: <https://drm.zcr.ai/me/> — ฟอร์มพร้อมกรอก
  - Tab 3: <https://drm.zcr.ai/share/> — หรือเปิด share URL ใหม่ใน incognito

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
