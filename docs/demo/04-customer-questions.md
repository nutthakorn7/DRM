# คำถามลูกค้าที่จะถาม + คำตอบมั่นใจ

> ใช้ตอน demo / Q&A หลัง demo
> ไม่ต้องท่อง — อ่านได้เลย พูดตามนี้ก็ตอบได้

## หมวด: Security

### Q: ปลอดภัยขนาดไหน?
**A:** "เราใช้มาตรฐานเดียวกับธนาคาร — AES-256 ต่อไฟล์, RSA-2048 wrap key, FIPS 140-2 ready ทำงานตามมาตรฐาน NIST. ทุกไฟล์ถูกเข้ารหัสที่เครื่องผู้ส่งก่อนถูกอัปโหลด — server ไม่เห็นข้อมูลที่ไม่ได้เข้ารหัสเลย"

### Q: ถ้า server ถูก hack ข้อมูลรั่วไหม?
**A:** "ไม่ — เพราะ private key ของแต่ละ tenant แยกกัน เก็บที่ตำแหน่งคนละที่กับ encrypted files. แม้ attacker จะได้ database ทั้งก้อนไป ก็จะได้แค่ ciphertext ที่ถอดรหัสไม่ได้ถ้าไม่มี key wrapping master key"

### Q: ที่ FinalCode ทำได้ — เราทำได้หมดไหม?
**A:** "ทำได้หมดที่เป็น core: encrypt, audit, revoke, watermark, access count limit, brute-force protection, expiry, offline lease. ส่วนที่เราไม่ทำคือ iOS/Android app เพราะ on-prem first ไม่เน้น mobile — แต่ web viewer เปิดได้ทุกอุปกรณ์อยู่แล้ว"

### Q: ผ่าน ISO 27001 / PDPA ไหม?
**A:** "ระบบเรา by design สนับสนุนการรองรับ ISO 27001 และ PDPA — มี tamper-proof audit chain ที่ export เป็น CSV ได้, SIEM webhook สำหรับ log aggregation, GDPR erase สำหรับลบข้อมูลผู้ใช้, retention policy ตั้งระยะเก็บได้ ตัวรับ certify เป็น process ที่ลูกค้าเอาไปกับ auditor ของตัวเอง — ระบบให้ tooling ครบ"

### Q: ป้องกัน screen capture / Print Screen ได้จริงเหรอ?
**A:** "ในระบบของเรา ใช่ — Windows viewer ใช้ SetWindowDisplayAffinity ที่ block Snipping Tool, Print Screen, OBS, Teams screen-share, Zoom screen-share หมด **ภาพที่ capture ออกมาจะเป็น black rectangle** ระดับเดียวกับ Netflix DRM. ที่บล็อกไม่ได้คือกล้องโทรศัพท์ที่ถ่ายจอ — แต่ watermark ที่ฝังในจอจะติดไปด้วย รู้ว่าใครเอาออก"

## หมวด: Deployment / Operations

### Q: ติดตั้งใช้เวลาเท่าไหร่?
**A:** "**5 นาที** ครับ. คือชื่อ tagline ของเรา 'Self-hosted DRM, ready in 5 minutes.' ใช้ Docker Compose — มีแค่ 3 services: web server, Postgres, Caddy reverse proxy พร้อม Let's Encrypt TLS อัตโนมัติ. คำสั่งเดียว `docker compose up -d` จบ"

### Q: ต้องมี cloud subscription ไหม?
**A:** "**ไม่ต้องเลย** — on-prem first. ลงในเครื่อง bare metal, VPS, AWS EC2 ที่ไหนก็ได้. ไม่มี subscription ต่อ user, ไม่มี data hostage ต่อ vendor. ที่ลูกค้าจ่ายคือ hardware + license ของเรา จบ"

### Q: ถ้า server ตายล่ะ?
**A:** "Docker volume เก็บ Postgres data ในรูปแบบ standard — backup ด้วย pg_dump ปกติ. การ restore = restore volume + start container ใหม่. ส่วนนึงของ deploy package ของเรามี runbook ครับ"

### Q: รองรับกี่ user / กี่ tenant?
**A:** "ทดสอบในการ pilot ที่ระดับหมื่น users และหลายร้อย tenants ต่อ instance. Postgres scale ได้ตามที่กำหนดอยู่แล้ว ถ้าโตกว่านั้น scale-out ด้วย read replicas + caching layer ได้"

### Q: monitoring / alerting?
**A:** "ทุก event ออก SIEM webhook ได้ — Splunk, Elastic, Datadog ใส่ webhook URL จบ. healthcheck endpoint `/healthz` ใช้กับ uptime monitor ภายนอก (Pingdom, Better Uptime, Uptime Kuma) ได้ทันที"

## หมวด: Pricing / Business

### Q: ราคาเท่าไหร่?
**A:** "ราคาขึ้นอยู่กับขนาด deployment — แนะนำให้นัดคุยรายละเอียดหลัง demo ครับ ถ้ามี requirements เบื้องต้นแล้วผมจัด proposal ให้ภายในอาทิตย์นี้"
> [ถ้ารู้ราคาแล้ว — เปลี่ยนเป็นข้อความจริง อย่าเดาตอน demo]

### Q: ทดลองใช้ได้ไหม?
**A:** "**ได้ครับ** — ระบบ on-prem ติดตั้ง Docker ในเครื่องคุณ 5 นาที ผมส่ง Docker compose file + .env template ให้ ภายในวันนี้ครับ ใช้ฟรีในช่วง pilot ก่อนตัดสินใจ"

### Q: support เป็นยังไง?
**A:** "support ระดับ enterprise ครับ — email + LINE OA + emergency phone หลัง deploy. ระหว่าง pilot ผมเป็น contact คนเดียว ตอบใน 1 ชั่วโมงในเวลาทำงาน"

### Q: ถ้ามีปัญหา bug?
**A:** "เรา patch ผ่าน Docker image update — `docker compose pull && up -d` จบ. ทุก minor version ของ v1.x มี backward compatible. ตอนนี้ live ที่ v1.6.1 ครับ"

## หมวด: Comparison vs Competitors

### Q: ต่างจาก FinalCode ของ Digital Arts ยังไง?
**A:** "FinalCode เป็น Japanese SaaS, ต้องผ่าน cloud ของเขา. **เราเป็น on-prem first — data ไม่ออกจาก network ของคุณ**. Deploy เร็วกว่า (5 นาที vs หลายวัน). ราคาไม่จ่ายต่อ user-month. และมี API ที่ integrator/SI ของคุณใช้ต่อยอดได้ทันที"

### Q: ต่างจาก Microsoft Purview ยังไง?
**A:** "Purview ต้องใช้ Microsoft 365 license และ data ผ่าน Azure. **เรารัน on-prem ในเครื่องของคุณ** — ไม่ต้อง M365, ไม่ต้องบังคับ Office. ใช้ได้กับ Google Workspace, OnlyOffice, หรือไม่มีอะไรเลยก็ได้"

### Q: ต่างจาก Vera / Seclore ยังไง?
**A:** "ทั้งสองตัวเป็น enterprise software ราคาสูง deploy time นานเป็นเดือน. **เรา deploy 5 นาที ราคาสมเหตุสมผล**. ใช้ tech stack เปิด (Postgres + Docker + .NET) ไม่ใช่ proprietary"

### Q: open source ไหม?
**A:** "Core engine เป็น proprietary ครับ — แต่ deploy stack เป็น open source ทั้งหมด (PostgreSQL, Caddy, Docker). ไม่มี vendor lock-in ใน infrastructure layer. การ migrate ออกถ้าวันหนึ่งอยากเปลี่ยน — ทำได้เพราะ data อยู่ใน Postgres standard format"

## หมวด: Technical Details

### Q: API documentation อยู่ไหน?
**A:** "ตอนนี้ API docs ยังเป็น internal — เปิดเป็น public docs site ในเดือนหน้าครับ ระหว่างนี้ผมส่ง OpenAPI spec + Postman collection ให้ดูได้ทันทีถ้าสนใจ"

### Q: integrate กับ Active Directory / Entra ID ได้ไหม?
**A:** "ได้ครับ — มี Directory sync service ที่ดึง users + groups จาก Entra ID เข้ามา. ตั้ง tenant + client ID + client secret ใน admin console จบ. SSO via SAML/OIDC อยู่ใน roadmap quarter หน้า"

### Q: integrate กับระบบที่เรามีอยู่แล้วได้ไหม?
**A:** "ระบบเราเป็น REST API ครบทุก operation — file upload, policy check, audit query, tenant management. มี webhook ออกทุก event. มี SIEM dispatcher ส่งเข้า Splunk/Elastic. มี Outlook add-in + Word add-in + Box integration พร้อมใช้"

### Q: รองรับ file format อะไรบ้าง?
**A:** "ทุก format ที่เป็น binary file ครับ — PDF, Office (docx/xlsx/pptx), images, ZIP, plain text. การ render ใน viewer ตอนนี้รองรับ PDF + Office native. format อื่น ๆ — download protected copy แล้วเปิดใน software ปกติได้ แต่ DRM enforcement จะระดับ container ไม่ใช่ application"

## หมวด: Roadmap (ถ้าถาม)

### Q: roadmap ต่อไปคืออะไร?
**A:** "Quarter นี้ — Windows MSI installer สำหรับ viewer + Office add-ins, API documentation public site. Quarter หน้า — SSO via SAML/OIDC, advanced audit dashboards, programmatic policy DSL"

### Q: มี mobile app ไหม?
**A:** "ระบบเราใช้ web viewer ที่ทำงานบน mobile browser ได้อยู่แล้ว — เปิดด้วย Safari หรือ Chrome บนมือถือ. native iOS/Android app ยังไม่ได้อยู่ใน roadmap ครับ — เพราะ scope เราเน้น **on-prem + browser-based** ที่ไม่ต้องลงอะไรเลย"

## ⚠️ คำถามที่ห้ามตอบเดา — ขอเวลา

ถ้าลูกค้าถามแล้วคุณไม่แน่ใจ — **ตอบแบบนี้:**

```
ขอผมเช็คข้อมูลที่ตรงนี้ก่อนครับ
ผมตอบ email ภายในวันนี้/พรุ่งนี้ มีข้อมูลแม่นยำกว่า

[จดคำถาม]
```

**อย่าเดา.** ตอบผิด = trust หาย. ตอบ "ขอเช็ค" = professional + ปลอดภัย

ตัวอย่างคำถามที่มักต้องเช็คก่อนตอบ:
- ราคาที่แน่ชัด (ถ้ายังไม่มี pricing tier ตั้งไว้)
- จำนวน users ที่ deploy ได้ขนาด N (load test ที่ scale นั้น)
- เปรียบเทียบ feature ละเอียดกับ competitor (อ่าน datasheet ก่อน)
- compliance certificate ที่เฉพาะ (ISO/SOC2 — ถ้ายังไม่ certified จริง)
- SLA ขั้นที่บริษัทรับประกัน (ขึ้นอยู่กับ business policy)
