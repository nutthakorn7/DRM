# Demo Prep — zcrDRM Customer Demo

> **กำหนดการ:** Demo ลูกค้า (ใส่วันที่จริง)
> **Product:** zcrDRM — Self-hosted DRM, ready in 5 minutes
> **Live URL:** https://drm.zcr.ai

This package has **everything** needed to run a clean customer demo.

## Who reads what

| You are... | Read these in order |
|------------|---------------------|
| **Owner / Sales person** (จะพรีเซนต์ลูกค้า) | [03-demo-script.md](03-demo-script.md) → [04-customer-questions.md](04-customer-questions.md) |
| **Engineer** (เตรียมระบบก่อน demo) | [01-engineer-prep.md](01-engineer-prep.md) → [02-the-3-links.md](02-the-3-links.md) → [08-engineer-windows-msi-setup.md](08-engineer-windows-msi-setup.md) (Windows agent on demo laptop) |
| **Both** (15 นาทีก่อน demo) | [05-preflight-checklist.md](05-preflight-checklist.md) |
| **Backup plan** (ถ้าเกิดปัญหาตอน demo) | [06-fallback-plan.md](06-fallback-plan.md) |

## 3 ลิ้งก์ที่ใช้ demo

ส่งให้ engineer test ก่อนเลย:

1. **<https://drm.zcr.ai/admin/>** — admin console (ผู้ดูแลเห็นทุกอย่าง)
2. **<https://drm.zcr.ai/me/>** — ส่งไฟล์ (พนักงานใช้)
3. **<https://drm.zcr.ai/share/>** — เปิดไฟล์ที่รับมา (ลูกค้าที่รับไฟล์ใช้)

แต่ละลิ้งก์มีคำอธิบายละเอียดใน [02-the-3-links.md](02-the-3-links.md)

## Demo story สั้นๆ ที่คุณจะเล่า

> "บริษัทคุณ ABC Co. ต้องส่งสัญญาความลับให้ลูกค้า XYZ
> วันนี้ผมจะแสดง 3 อย่าง:
> 1. **ฝ่ายไอทีตั้งค่านโยบาย** (admin) — เปิดได้กี่ครั้ง, ระยะเวลา, watermark
> 2. **พนักงานส่งไฟล์** (me) — ลาก-วาง แล้วกดส่ง
> 3. **ลูกค้าเปิดอ่าน** (share) — ไม่ต้องลงโปรแกรมอะไร, แค่ verify email
>
> ทั้งหมดเป็น on-premise — ข้อมูลลูกค้าไม่ออกจากเครื่องคุณ
> ถ้าวันหนึ่งอยากยกเลิกการเข้าถึง — กดปุ่มเดียว ไฟล์ตายทันที"

Demo เวลา **10-15 นาที** จบ. ครอบคลุม 3 product pillars: **Encrypt / Audit / Revoke**

## ถ้ามีคำถาม

ถาม engineer ก่อน — เขามี QA handoff package เต็มที่ `docs/qa-handoff/` แล้ว
