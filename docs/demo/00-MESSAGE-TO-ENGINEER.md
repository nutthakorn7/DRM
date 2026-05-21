# ข้อความส่งหา Engineer — คัดลอกได้เลย

> **คัดลอกข้อความด้านล่างทั้งหมด → ส่งใน LINE/Slack/Email ให้ engineer**
> ส่งวันนี้เพื่อให้เขามีเวลาเตรียมก่อน demo อาทิตย์หน้า

---

```
สวัสดีครับ

อาทิตย์หน้ามี demo zcrDRM ลูกค้า ขอให้คุณช่วยเตรียมระบบให้พร้อม

** 3 ลิ้งก์หลักที่ใช้ demo **

1) https://drm.zcr.ai/admin/    — หน้าผู้ดูแลระบบ
2) https://drm.zcr.ai/me/       — หน้าพนักงานส่งไฟล์
3) https://drm.zcr.ai/share/    — หน้าผู้รับเปิดไฟล์

** สิ่งที่ขอให้คุณทำ **

อ่าน docs/demo/01-engineer-prep.md ใน repo
https://github.com/nutthakorn7/DRM/blob/master/docs/demo/01-engineer-prep.md

สรุปสั้น ๆ:

1. test ว่า healthcheck + 3 ลิ้งก์ขึ้น HTTP 200
2. สร้าง demo tenant ใหม่ผ่าน "Create test tenant" ที่ /admin/
3. สร้าง 2 test users — Somchai (somchai@abc.co.th), Malee (malee@xyz.com)
4. สร้าง policy template ชื่อ "Confidential Contract" ที่:
   - permissions: View only
   - max_opens: 3 (เปิดได้ 3 ครั้ง/คน)
   - watermark: "{user} · {time} · ABC CONFIDENTIAL"
   - offline_lease: 60 นาที
   - allow_print: false
5. ตั้ง brute-force protection threshold=3 (default 10 ลดให้ demo เห็นเร็ว)
   ผ่าน API: PUT /api/admin/brute-force-policy
6. เตรียม sample PDF — ชื่อ "Q4-Sales-Contract-ABC-XYZ.pdf" ขนาด < 5 MB
   ลาก-วางลง /me/ ทดสอบส่งให้ malee@xyz.com
7. ตรวจว่า verification code ที่ /share/ มาทาง email ได้หรือไม่
   ถ้าไม่มา — เตรียม SSH session ดู server log สำหรับ standby ช่วยอ่านรหัส

** ส่งกลับ ผมต้องการ **

หลัง prep เสร็จ ส่งข้อมูลนี้:

- Tenant ID: ____________
- Admin Key: ____________ (ใส่ password manager — ไม่ส่งในแชท)
- Sample share URL ที่ทดสอบเปิดได้: ____________
- เวลาที่คุณ standby ช่วย demo (ถ้ามีปัญหา): ____________

** วันที่ demo **

วันที่: ____________
เวลา: ____________
ลูกค้า: ____________

** เอกสารอื่น ๆ ใน docs/demo/ **

00-MESSAGE-TO-ENGINEER.md  ← ตัวนี้
01-engineer-prep.md         ← prep steps ละเอียด
02-the-3-links.md           ← คำอธิบายแต่ละลิ้งก์
03-demo-script.md           ← สคริปต์ที่ผมจะพูดตอน demo
04-customer-questions.md    ← คำถามลูกค้าที่คาดว่าจะถาม
05-preflight-checklist.md   ← 15 นาทีก่อน demo ทำอะไร
06-fallback-plan.md         ← ถ้ามีปัญหาตอน demo

ทั้งหมดอยู่ใน repo master branch — pull ก่อนเริ่มทำ

** ระหว่างเตรียม มีคำถามอะไร ทักได้เลย **

ขอบคุณครับ
```

---

## Bonus: อะไรที่คุณอาจอยากเพิ่ม

แทรกที่ท้ายข้อความก่อนส่ง ถ้าต้องการ:

```
** Notes สำคัญที่ engineer ควรรู้ **

- ระบบเป็น zcrDRM v1.6.1 — FinalCode-compete แต่ on-prem first
- มี 3 product pillars: Encrypt / Audit / Revoke
- ลูกค้าที่ demo คือ [ระบุ industry] เน้นเรื่อง [ระบุ pain point]
- ราคา [ตั้งให้ engineer รู้ก่อน — ลูกค้าอาจถาม]

** ห้ามทำ **

- อย่าใช้ tenant production จริงสำหรับ demo
- อย่าส่ง admin key ในแชท — ใช้ password manager
- อย่าแชร์ verification code ออกนอก demo channel
```

## คำแนะนำการส่ง

1. **เวลาที่ส่ง:** ส่งวันนี้ ให้ engineer มีเวลา 3-5 วันเตรียม
2. **Channel:** ใช้ที่เขาตอบเร็วที่สุด (LINE OA / Slack DM / Email)
3. **Follow-up:** ทักถามอีก 2 วันก่อน demo ว่า "เตรียมเสร็จหรือยัง"
4. **มี TLDR ที่ต้นข้อความ:** "ขอช่วย prep ระบบสำหรับ demo อาทิตย์หน้า — รายละเอียดอ่านที่ link"
