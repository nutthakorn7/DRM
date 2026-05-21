# Demo Script — สิ่งที่คุณพูดและคลิก (10-15 นาที)

> **คุณ** = ผู้พรีเซนต์ (เจ้าของ / sales)
> **อ่านได้เลย** ถ้าตื่นเต้น — ทุกประโยคใช้ได้จริง
> **เครื่องที่ใช้:** เครื่องที่ engineer prep ไว้แล้ว มี Tenant + Sample file พร้อม

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

## ส่วนที่ 2: เปิด /me/ — "พนักงานส่งไฟล์" (3-4 นาที)

**[เปิด tab ใหม่ → https://drm.zcr.ai/me/]**

```
ตอนนี้เปลี่ยนมุมมอง — เป็นพนักงานสามัญที่ต้องส่งไฟล์ลับ
หน้านี้คือ drm.zcr.ai/me/ — ออกแบบให้ง่ายที่สุด
ไม่ต้องเรียนวิธีใช้
```

**[ชี้ที่หน้าจอ — header "Send a protected file"]**

```
ก่อนเริ่ม จะสังเกตว่า:
- มี zcrDRM wordmark เดียวกัน
- มี "Sign-in needed" pill — บอกว่ายังไม่ login
- topbar เรียบ มีแค่ Send, Open shared file, Personalize
- **ไม่มี Admin link** — เพราะพนักงานคนนี้ไม่ใช่ admin
  (ระบบรู้ว่าใครเป็นอะไร ไม่ทำให้เห็น link ที่ไม่ใช่หน้าที่ของเรา)
```

**[คลิกที่ "You are signed in as (not configured)" details]**

```
ใส่ Tenant ID และ User ID ของพนักงาน — แค่ครั้งแรกครั้งเดียว
ครั้งต่อไปเครื่องจำได้ ไม่ต้องพิมพ์ใหม่
```

**[พิมพ์ Tenant ID, User ID ที่ engineer prep ไว้ — หรือถ้า engineer save session แล้ว ค่าจะ auto-fill]**

**[ลาก-วางไฟล์ Q4-Sales-Contract-ABC-XYZ.pdf ลง drop zone]**

```
ผมจะลาก-วางสัญญาตัวอย่างลงตรงนี้ — เห็นไหมครับ ไม่มี upload form
แค่ลาก ก็พอ
```

**[พิมพ์ recipient email: malee@xyz.com]**

```
ใส่อีเมลผู้รับ — Malee คือคนจาก XYZ Co.
ผมจะกด "Advanced options" ก่อน
```

**[คลิก Advanced options → ชี้ "Allow print" checkbox]**

```
ตรงนี้ตั้งได้ทันทีว่า print ได้ไหม กี่วันหมดอายุ
ผมจะปล่อยเป็น default — ใช้กฎจาก Confidential Contract template
```

**[กดปุ่ม "Send protected file"]**

```
กดส่ง — ระบบจะ:
1. เข้ารหัสไฟล์ด้วย AES-256 ที่เครื่องนี้ทันที
2. ส่งไปเก็บที่ server
3. ส่งอีเมลแจ้ง Malee พร้อมลิ้งก์
4. ส่งลิ้งก์กลับมาให้พนักงานเก็บไว้
```

**[result panel ขึ้น — มี share URL + Copy link button]**

```
เสร็จแล้ว — ระบบสร้าง share URL กลับมา
พนักงานคัดลอกไปแชร์ทางที่ไหนก็ได้ — email, LINE, Slack — ไม่ใช่ความลับ
**เพราะลิ้งก์อย่างเดียวเปิดไฟล์ไม่ได้** — Malee ต้อง verify email ของตัวเองก่อน
```

**[กดปุ่ม Copy link]**

```
ผมคัดลอกแล้ว — จะเปิดเหมือนเป็น Malee ดูในหน้าถัดไป
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

2. **พนักงานส่งง่าย** — ลาก-วาง ไม่ต้องเรียนวิธีใช้
   ระบบจัดการ encryption ให้เอง

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
