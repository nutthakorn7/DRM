# Demo Script — สิ่งที่คุณพูดและคลิก (10-15 นาที)

> **คุณ** = ผู้พรีเซนต์ (เจ้าของ / sales)
> **อ่านได้เลย** ถ้าตื่นเต้น — ทุกประโยคใช้ได้จริง
> **เครื่องที่ใช้:** Windows laptop ที่ engineer prep ไว้ — **zcrDRM Agent ลงและ sign in ด้วย `demo@zcr.ai` แล้ว** + Sample PDF บน Desktop + เปิด `/admin/` กับ `/share/` รออยู่บนเบราว์เซอร์
>
> **สำคัญ:** demo ส่วนที่ 2 ใช้ตัว Windows Agent (right-click → Protect) แทนหน้า /me/ — agent คือ "หน้าตาจริง" ของระบบที่พนักงานลูกค้าจะใช้ทุกวัน /me/ เป็น fallback ถ้า agent มีปัญหา (ดู [06-fallback-plan.md](06-fallback-plan.md))

---

## ก่อนเริ่ม (1 นาที)

```
สวัสดีครับ ขอบคุณที่สละเวลาให้
วันนี้ผมจะแสดงระบบ DRM ของเราชื่อ zcrDRM ที่ลูกค้าใช้
ปกป้องไฟล์ลับเช่นสัญญา, รายงานการเงิน, ข้อมูล customer

3 อย่างที่ระบบเราทำได้ ที่ทำให้ต่างจากที่อื่น:
1. Encrypt — ทุกไฟล์เข้ารหัส AES-256
2. Audit — รู้ทุกครั้งที่มีคนเปิดไฟล์
3. Revoke — ดึงสิทธิ์คืนได้ตลอด แม้ส่งไปแล้ว

ใช้เวลา 10-15 นาที พอ — ขออนุญาตเริ่มเลยนะครับ
```

---

## ส่วนที่ 1: เปิด /admin/ — "ผู้ดูแลตั้งกฎ" (3-4 นาที)

**[คลิกที่บุ๊กมาร์ก https://drm.zcr.ai/admin/]**

```
นี่คือหน้าหลังบ้านของระบบครับ ลูกค้าจะเห็นแบบนี้ที่ drm.zcr.ai/admin/
จะสังเกตว่าหน้าตาเรียบ ไม่รก — เพราะเราออกแบบให้ฝ่าย IT ใช้งานได้ทันที
ไม่ต้องอบรม
```

**[ชี้ที่ Brand wordmark]**

```
ด้านบนซ้าย zcrDRM — นี่คือ wordmark ของเรา
ใต้นั้นมี drm.zcr.ai เป็น domain จริงๆ ที่ลูกค้าจะใช้
ขวาสุดเขียน "Not connected" สีส้ม — เพราะตอนนี้ยังไม่ได้ลงชื่อเข้า
```

**[ชี้ที่ Hero pillars — Encrypt / Audit / Revoke + Trust badges]**

```
ตรงนี้ครับ 3 หัวใจของเรา — Encrypt, Audit, Revoke

Encrypt = AES-256 ต่อไฟล์ + RSA-2048 wrap key
Audit = บันทึกทุกการเปิด ทุกอุปกรณ์ tamper-proof
Revoke = ดึงสิทธิ์ได้ตลอดเวลา จากที่ไหนก็ได้

ด้านล่างเป็น tech badges:
AES-256, RSA-2048, FIPS 140-2 ready, PostgreSQL, Docker, On-prem first
ทั้งหมดนี้คือมาตรฐานความปลอดภัยระดับ enterprise
```

**[เลื่อนลงไปที่ Getting started — 5 steps]**

```
ลูกค้าใหม่จะเห็น 5 ขั้นตอน — เปิดบัญชี, ตั้ง admin, สร้าง user, สร้าง policy, ส่งไฟล์
ทั้งหมดเสร็จได้ใน **5 นาที** ตามที่ tagline บอก
นี่คือจุดที่ต่างจาก FinalCode/Vera/Seclore ที่ใช้เวลา deploy หลายอาทิตย์
```

**[คลิก tab "Policy"]**

```
ตอนนี้ผมจะพาไปดู Policy templates — เป็นแม่แบบที่ admin ตั้งไว้ครั้งเดียว แล้วใช้ซ้ำได้
```

**[ชี้ที่ template "Confidential Contract" ที่ engineer prep ไว้]**

```
อันนี้คือ template ที่เราตั้งไว้ตัวอย่างชื่อ "Confidential Contract"
มันบอกว่า:
- View อย่างเดียว ไม่ให้ print, copy, edit
- เปิดได้ 3 ครั้งต่อคน — ครบแล้วเปิดไม่ได้
- ออฟไลน์ได้ 60 นาที
- มี watermark ของชื่อคนเปิด + เวลา + คำว่า "ABC CONFIDENTIAL"

template เดียวนี้คุมได้ทั้งบริษัท — admin ตั้งครั้งเดียวเสร็จ
```

**[คลิก tab "Files" หรือ tab อื่นเพื่อแสดง breadth]**

```
นอกจาก Policy ระบบยังมีอีก 5 tab — Overview, Identity (users/groups/devices), Files,
Integrations (Box, Outlook, SIEM webhooks), และ Tenants (multi-tenant management)
ครบทุกอย่างที่ enterprise security team ต้องการ
```

---

## ส่วนที่ 2: Windows Agent — "พนักงานคลิกขวา → Protect" (3-4 นาที)

**[ปิด /admin/ ไปก่อน, สลับไปที่ Windows Desktop ของเครื่อง demo]**

```
ทีนี้ — สิ่งที่ผู้บริหารและฝ่าย IT ต้องการรู้คือ:
"แล้วพนักงานทั่วไปของบริษัทเราจะใช้ระบบนี้อย่างไร?"

คำตอบของ zcrDRM ง่ายมาก: **ไม่ต้องเปิด website เลย**
```

**[ชี้ที่ Desktop — มี PDF ตัวอย่าง Q4-Sales-Contract-ABC-XYZ.pdf วางอยู่]**

```
ฝ่าย IT ติดตั้ง zcrDRM Agent เป็น MSI ครั้งเดียวตอน setup laptop
หลังจากนั้นพนักงานเปิด laptop วันแรก — ระบบถามแค่อีเมลที่ทำงาน
แล้วก็จบ — ไม่มี GUID ที่ต้อง copy-paste, ไม่มี portal ที่ต้องล็อกอิน
```

**[คลิกขวาที่ไฟล์ PDF → context menu ขึ้นมา → ชี้ที่ "Protect with zcrDRM"]**

```
เห็นไหมครับ — เมนูคลิกขวาของ Windows มีคำว่า "Protect with zcrDRM"
ใต้นั้น 3 ตัวเลือก:
- Quick send (recommended)  — สำหรับส่งให้คนนอกบริษัท
- Protect (advanced)         — เปิด policy editor ตั้งค่าเอง
- Transparent protect        — ปกป้องไฟล์โดยไม่เปลี่ยนนามสกุล

ผมจะเลือก Quick send
```

**[คลิก Quick send (recommended) → tray window เด้งขึ้น พร้อมไฟล์ pre-loaded]**

```
หน้าต่าง zcrDRM Agent เด้งขึ้น — สังเกตที่ title bar
**"zcrDRM Agent — Demo Engineer (demo@zcr.ai)"**
ระบบรู้ว่าใครเปิดอยู่ ไม่มี login form

ไฟล์ Q4-Sales-Contract-ABC-XYZ.pdf อยู่ในช่องส่งแล้ว — ระบบเอามาให้
```

**[ชี้ที่ Tenant ID + Policy template ที่ pre-fill อยู่]**

```
และดูตรงนี้ — Tenant ID, Policy template "Confidential Contract"
ระบบกรอกให้แล้ว ทั้งหมด เพราะตอน sign in ด้วย demo@zcr.ai
ระบบไปถามที่ server แล้วว่า:
- คนคนนี้เป็นของ tenant ไหน
- tenant นี้ตั้ง default template ไว้ตัวไหน
แล้วเก็บไว้เครื่องนี้ — ครั้งต่อไปเปิดมาทำงานต่อได้เลย
```

**[พิมพ์ recipient email: malee@xyz.com]**

```
อย่างเดียวที่พนักงานต้องพิมพ์เองคือ — อีเมลผู้รับ
Malee จาก XYZ Co.
```

**[กดปุ่ม "Send protected file"]**

```
กดส่ง — ระบบทำที่เครื่องนี้:
1. เข้ารหัสไฟล์ด้วย AES-256 ที่ laptop ตัวเองทันที — ไฟล์ไม่ขึ้น cloud
2. เขียนไฟล์ .drmx ลงข้าง source — เห็นบน Desktop ทันที
3. ส่ง wrapped key + metadata ไปที่ server เพื่อ register
4. คืน share URL มา + เปิดอีเมลให้พร้อมส่ง
```

**[status panel ขึ้น — "✅ Wrote Q4-Sales-Contract-ABC-XYZ.pdf.drmx. Share URL copied + email composer opened" + default mail client เด้งขึ้นมาพร้อม recipient/subject/body กรอกแล้ว]**

```
สังเกตหน้าจอ — สองอย่างเกิดขึ้นพร้อมกัน:

1. **บน Desktop** มีไฟล์ใหม่: Q4-Sales-Contract-ABC-XYZ.pdf.drmx
   ไฟล์เข้ารหัสที่ Malee จะเปิดได้ (หลัง verify email)

2. **default mail client เปิดเอง** — to: malee@xyz.com,
   subject + body กรอกครบ พร้อม share URL ฝังในเนื้อหา

พนักงานทำ **1 อย่าง** เท่านั้น: ลาก .drmx จาก Desktop ใส่เป็น attachment แล้ว Send
```

**[ลาก .drmx จาก Desktop ใส่เป็น attachment → กดส่ง → ปิดหน้าต่าง agent]**

```
สังเกตว่าตั้งแต่ต้นจนเสร็จ:
- พนักงานเปิด website **0 ครั้ง**
- พนักงานพิมพ์ GUID **0 ครั้ง**
- พนักงานต้องเลือก policy **0 ครั้ง** (ใช้ default ของ tenant)
- พนักงานก็อปปี้ share URL ใส่อีเมลเอง **0 ครั้ง** (ระบบเปิดอีเมลให้แล้ว)
- click ทั้งหมด **3 ครั้ง** — Right-click → Quick send → ใส่อีเมล → ส่ง → drag .drmx → Send mail

นี่คือ "easy to use ที่สุด" ที่เราอยากให้ลูกค้าได้
ฝ่าย IT ตั้งค่าครั้งเดียว, พนักงานใช้ทุกวันโดยไม่รู้ว่ามีระบบความปลอดภัยอยู่
```

---

## ส่วนที่ 3: เปิด /share/ — "ลูกค้ารับและเปิดอ่าน" (4-5 นาที)

**[เปิด tab ใหม่ → แปะ share URL ที่คัดลอก หรือใช้ incognito]**

```
ตอนนี้ผมเป็น Malee ที่ XYZ Co. ได้รับลิ้งก์มาจาก Somchai
เปิดเข้ามา — มาที่หน้า drm.zcr.ai/share/
```

**[หน้า /share/ เปิดมา — มี wordmark + "Open shared file" + 2-step verification]**

```
สังเกตว่าหน้าตา consistent กับหน้าก่อน — zcrDRM wordmark เดิม
มันบอกว่า: Verify your guest access before opening this protected document session
- Step 1: Share access — ใส่อีเมลให้ระบบส่งรหัส
- Step 2: Identity check — ใส่รหัสกลับมา

**Malee ไม่ต้องลงโปรแกรมอะไรเลย — เปิดเบราว์เซอร์ก็พอ**
นี่คือจุดที่ดีกว่า DRM แบบเดิมที่ต้องลง agent client
```

**[ใส่ email malee@xyz.com → กด "Send verification code"]**

```
กดส่งรหัสครับ — ระบบจะส่งรหัส 6 หลักไปที่อีเมล Malee
Verification ID ของรอบนี้ขึ้นมาให้
```

**[ถ้า email integration ไม่ทำงาน — engineer ส่งรหัสมาก่อน demo จาก server log]**

```
รหัสที่ Malee ได้รับ คือ XXXXXX — ใส่ตรงนี้
```

**[ใส่รหัส verify → กด "Open viewer session"]**

```
verify ผ่าน — ขึ้น viewer session ขึ้นมา
**Malee เห็นเอกสารแล้ว — ในเบราว์เซอร์ ไม่ต้อง download ไม่ต้องลง app**
```

**[ชี้ที่ watermark บนเอกสาร]**

```
สังเกตที่หัวมุม — มี watermark ของ "Malee · time · ABC CONFIDENTIAL"
ถ้า Malee ลองถ่ายรูปจอด้วยมือถือ — watermark ติดในรูปเลย
รู้ว่าใครเอาออก
```

**[ที่เครื่อง Windows ของ engineer — ถ้ามี — แสดงเปิดไฟล์ใน WPF viewer + กด Print Screen]**

```
ถ้าเปิดด้วย viewer Windows ของเรา — กด Print Screen หรือใช้ Snipping Tool
**ได้แค่จอดำ** — เพราะระบบบล็อกการ capture ของ Windows OS
ระดับเดียวกับ Netflix DRM
```

**[กลับมาที่หน้า /admin/ → tab Overview → Audit events subtab → Refresh]**

```
ระหว่างที่ Malee เปิด — ทุกอย่างถูกบันทึก
มาดูที่ admin console — tab Audit events
```

**[ชี้ที่ entries: access_allowed สำหรับ Malee, รวมเวลา device IP]**

```
เห็นไหมครับ — ทุกการเปิด, ทุกครั้ง, ทุกอุปกรณ์, ทุก IP
สำหรับ compliance — export CSV ได้, ส่งเข้า SIEM ได้, ลงทะเบียน ISO/PDPA ได้
```

---

## ส่วนปิด: Revoke (1-2 นาที)

**[กลับไปที่ admin → Files tab → หา file ที่ส่งไป → กด Revoke]**

```
สมมติว่าสัปดาห์หน้า ความสัมพันธ์ทางธุรกิจกับ XYZ จบลง
ไม่ต้องการให้ Malee เปิดดูสัญญาได้อีกแล้ว
admin กดแค่ปุ่มเดียว — Revoke
```

**[กดปุ่ม Revoke]**

```
ทันที — ไฟล์ตาย แม้ Malee จะมี share URL อยู่ในมือ
มาลองดูจากฝั่ง Malee
```

**[เปิด /share/ tab — กดปุ่มลองอะไรสักอย่าง หรือ refresh viewer]**

```
Malee เห็น "File revoked" — เปิดไม่ได้แล้ว
นี่คือ Revoke — กดจากที่ไหนก็ได้ ใช้ได้ทันที
```

---

## สรุปปิด (1-2 นาที)

```
สรุปวันนี้:

1. **Admin ตั้งกฎ** — Policy templates, watermark, ระยะเวลา
   เห็นภาพรวมและตามรอยทุกอย่างจากที่เดียว

2. **พนักงานคลิกขวา → Protect** — ผ่าน zcrDRM Agent
   ลง MSI ครั้งเดียวที่ laptop, sign in ด้วย work email ครั้งเดียว
   หลังจากนั้นพนักงานไม่ต้องเปิด website ไม่ต้องจำ GUID
   2 click ส่งไฟล์ลับให้ใครก็ได้

3. **ลูกค้าเปิดง่าย** — แค่เบราว์เซอร์ ไม่ต้องลงโปรแกรม
   แต่ผู้ใช้ทุกคนถูกตามรอย และ revoke ได้ตลอด

ที่ต่างจาก FinalCode/Vera/Seclore:
- **On-premise** — ข้อมูลลูกค้าไม่ออกจาก network ของบริษัท
- **Docker deploy** — ลงใน 5 นาที ไม่ต้อง services 3 ทีมมาช่วย
- **Self-hosted** — ไม่มี SaaS lock-in, ไม่จ่ายต่อ user-month
- **Engineer-friendly** — ทุกอย่างมี REST API, มี SIEM webhook, มี Postgres

ราคา — เราตัดทุกอย่างที่ไม่จำเป็นออก — ลูกค้าจ่าย hardware + license ที่ใช้
ไม่จ่าย consulting deploy fee, ไม่จ่าย cloud subscription

มีคำถามอะไรเชิญถามครับ
```

---

## ❌ Tips สิ่งที่ห้ามทำตอน demo

- **อย่ากดเข้าไป tab Tenants** ถ้าไม่ใช่จำเป็น — มี subtab เยอะ ลูกค้าอาจ overwhelmed
- **อย่าเข้าไปแก้ตอนกำลังพูด** — ค่อยพูดให้จบประโยค แล้วค่อยคลิก
- **ห้ามใช้ tenant production จริง** — engineer prep ของ demo แยกไว้
- **ห้าม print screen ตัว WPF viewer** ในระหว่าง demo — จะเป็น black rectangle, ลูกค้าตกใจ
  (อันนั้นคือ feature แต่ดูเหมือนเสีย ถ้าอธิบายไม่ทัน)
- **อย่าเปิด console DevTools** ตอน demo — ลูกค้าจะคิดว่ามีปัญหา

## ✅ Tips สิ่งที่ทำให้ demo ลื่นไหล

- **เปิด 3 tabs ไว้ล่วงหน้า** — /admin/ /me/ /share/ พร้อม session save แล้ว
- **ขนาด zoom 110-125%** ใน browser — ลูกค้านั่งห่างเห็นได้
- **ปิด notification** ทุกอย่างก่อน demo — Slack, Mail, Calendar
- **เปิด full screen** browser ขณะ demo (F11)
- **มี engineer standby** ผ่าน chat — ถ้ามีปัญหา ส่งข้อความถาม
