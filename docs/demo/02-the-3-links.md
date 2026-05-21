# 3 surfaces ที่ engineer ต้อง test ก่อน demo

> **เปลี่ยนแปลง 2026-05-21:** demo ใหม่ใช้ **Windows Agent** เป็นหน้าตาฝั่งพนักงาน ไม่ใช่ /me/ แล้ว — /me/ ยังทำงานได้ แต่กลายเป็น fallback ถ้า agent มีปัญหา

ส่งข้อความนี้ให้ engineer ทาง Slack/LINE/อีเมล:

---

```
สวัสดีครับ
อาทิตย์หน้ามี demo ลูกค้า ผมต้องการให้คุณช่วย test 3 surface นี้และเตรียมข้อมูลให้พร้อมก่อน demo

ทั้ง 3 surface เป็นจุดที่ผู้ใช้ของลูกค้าจะสัมผัส:

1) https://drm.zcr.ai/admin/  (web)
   - หน้าหลังบ้านของผู้ดูแลระบบ — ตั้งนโยบาย ดู audit log ดูทุกอย่าง
   - มีกี่ tab? Overview/Identity/Policy/Files/Integrations/Tenants รวม 6 tab
   - test ครบ: คลิกได้ทุก tab โหลดได้ไม่มี console error
   - ข้อมูล demo seed แล้วบน prod ไม่ต้องสร้างใหม่ ดู docs/demo/09-prod-seeded-credentials.md

2) zcrDRM Agent บน Windows  (native MSI)
   - หน้าตาที่พนักงานลูกค้าจะใช้ทุกวัน
   - ลง MSI ครั้งเดียว — sign in ด้วย work email — เสร็จ
   - หลังจากนั้น right-click ไฟล์ไหนก็ได้ใน Explorer → "Protect with zcrDRM" → Quick send → ใส่อีเมลผู้รับ
   - ดู docs/demo/08-engineer-windows-msi-setup.md สำหรับวิธีติดตั้งและ smoke

3) https://drm.zcr.ai/share/  (web)
   - หน้าผู้รับเปิดอ่าน — ใส่อีเมลรับรหัส 6 หลัก verify แล้วเปิดอ่านได้
   - ลูกค้าไม่ต้องลงโปรแกรมอะไรเลย

(ของเดิม https://drm.zcr.ai/me/ ยังเปิดได้และใช้งานได้ครบ —
ตอน demo ใช้เป็น fallback ถ้า agent install หรือ sign-in มีปัญหา
ดู docs/demo/06-fallback-plan.md)

ที่ต้องเตรียมก่อน demo:
- ดู docs/demo/09-prod-seeded-credentials.md — seed บน prod แล้ว แค่ verify ครบ
- ลง zcrDRM Agent MSI บน laptop ที่จะใช้ demo (docs/demo/08-engineer-windows-msi-setup.md)
- sign in ด้วย demo@zcr.ai (ค่าใน 09)
- เตรียม sample PDF บน Desktop ของ laptop demo
- เปิด /admin/ กับ /share/ ค้างไว้บน browser ด้วย

วันที่ demo: __________
เวลา: __________
ลูกค้า: __________

คำถามอะไร ทักได้ครับ
```

---

## รายละเอียดแต่ละลิ้งก์ (เผื่อ engineer ถาม)

### Link 1: `https://drm.zcr.ai/admin/` — Admin Console

**ใครใช้:** IT admin / Security officer / Compliance officer ที่บริษัทลูกค้า

**ทำอะไรได้:**
- จัดการ users, groups, devices
- สร้าง policy templates (กำหนดสิทธิ์การเปิด, อายุ, watermark)
- ดู audit log ทุกการเปิดไฟล์ ทุกเครื่อง
- เปิด/ปิดผู้ใช้, ยกเลิกไฟล์, ดึงสิทธิ์คืน
- ตั้งค่า integrations: Box, Outlook, SIEM, Folder watcher
- จัดการ tenants (multi-tenant SaaS mode)

**โครงสร้าง UI:**
- Header: zcrDRM wordmark + connection status pill
- Left rail (sidebar): Admin console / Send a file / Open shared file (cross-links)
- 6 main tabs: Overview, Identity, Policy, Files, Integrations, Tenants
- Each tab has 3-10 subtabs

**Engineer ต้อง test:**
- ทั้ง 6 tab คลิกได้ — `document.body.dataset.activeTab` update ถูกต้อง
- Console errors = 0
- Welcome modal โหลดมาแบบสะอาด ปุ่ม "Create test tenant" ทำงาน
- Brand: zcrDRM wordmark + drm.zcr.ai monospace chip ใต้ wordmark

### Link 2: `https://drm.zcr.ai/me/` — Send a Protected File

**ใครใช้:** พนักงานคนใดก็ได้ที่ต้องส่งไฟล์ลับ (ไม่ใช่ admin)

**ทำอะไรได้:**
- ลาก-วางไฟล์ลง drop zone
- พิมพ์อีเมลผู้รับ
- เลือก expiry + permission (ขั้น advanced)
- กดปุ่ม → ได้ share URL กลับมา
- ส่ง share URL ทางอีเมล/Slack/LINE ให้ผู้รับ

**โครงสร้าง UI:**
- Header: zcrDRM wordmark + Persona badge ("Sign-in needed")
- Topbar nav: Send / Open shared file / Personalize
- Main: 1 form, 1 ปุ่ม

**Engineer ต้อง test:**
- ฟอร์มมี input: Tenant ID, User ID, drop zone, recipient email
- "Advanced options" expand ได้ — มี expiry + allow_print
- กด Send สำเร็จ → result panel ขึ้น มี share URL + Copy link + Send another file
- **"Admin →" link ในหัวต้องไม่โผล่** (v1.6.1 fix — บั๊ก CSS แก้ไปแล้ว)

### Link 3: `https://drm.zcr.ai/share/` — Open Shared File

**ใครใช้:** ผู้รับไฟล์ (อาจเป็นคนนอกบริษัทเลย ไม่ต้องเป็นพนักงาน)

**ทำอะไรได้:**
- คลิก share URL ที่ส่งมา → มาถึงหน้านี้
- ใส่ email ของตัวเอง → ระบบส่งรหัส 6 หลักให้ทาง email
- ใส่รหัส → ผ่าน verification → เปิดอ่านไฟล์ได้ (ในเบราว์เซอร์ ไม่ต้องลงโปรแกรม)
- ทุกครั้งที่เปิด = 1 access count
- ถ้ายืนยันรหัสผิดเกินกำหนด → ลิ้งก์ถูก revoke อัตโนมัติ (brute-force protection)

**โครงสร้าง UI:**
- Header: zcrDRM wordmark + lock SVG + "External viewer" badge
- 2-step wizard:
  - Step 1: Share access (start verification)
  - Step 2: Identity check (confirm code)
- Right panel: Viewer session (Document preview)

**Engineer ต้อง test:**
- Share URL ที่ engineer สร้างใน /me/ เปิดมาที่ /share/ ได้ พร้อม prefill tenant + token
- กรอก guest email → "Send verification code" → ได้ verification ID
- รหัสมาทาง email หรือ fetchจาก server log
- ใส่รหัส → unlock → เห็นเอกสาร
- ทดสอบ brute-force: ใส่รหัสผิด 3 ครั้ง (threshold ที่ engineer ตั้งใน 01-engineer-prep) → ลิ้งก์ถูก revoke

## ทำไมต้อง 3 ลิ้งก์นี้

เพราะมันคือ **3 บทบาทในเรื่องเล่า DRM:**

| ใคร | ทำอะไร | ลิ้งก์ |
|------|--------|--------|
| **IT admin / ผู้ดูแล** | ตั้งกฎและตามรอย | `/admin/` |
| **พนักงาน** | ส่งไฟล์ลับให้คนนอก | `/me/` |
| **ผู้รับ** | เปิดอ่านโดยไม่ต้องลงโปรแกรม | `/share/` |

Demo ทั้งสามจึงครอบคลุม **end-to-end story** ของลูกค้า — admin → send → receive — จบในตัวเอง
