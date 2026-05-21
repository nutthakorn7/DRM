# Engineer Prep — 30-Minute Setup Before Demo

> ⚠️ **อ่านอันนี้ก่อน:** Tenant + users + template + brute-force policy ของ demo ถูก seed ไว้บน prod แล้ว ตั้งแต่ 2026-05-21. ดู credentials ที่ [09-prod-seeded-credentials.md](09-prod-seeded-credentials.md) — ใช้ค่าจากนั้นได้เลย ไม่ต้องทำ Step 2 + Step 3 ของหน้านี้
>
> หน้า 01 นี้ยังอยู่เผื่อ seed หลุดหาย (run smoke ตามใน 09 → ถ้าไม่ 200 → กลับมาที่นี่) หรือต้องเซ็ตอัพ tenant อันใหม่หลัง demo
>
> ถ้า prod seed ยังครบ — ข้ามไป [08-engineer-windows-msi-setup.md](08-engineer-windows-msi-setup.md) เลย

---

> **เป้าหมาย:** ทำให้ทั้ง 3 ลิ้งก์ของ demo ทำงานได้ราบรื่นด้วยข้อมูล "เหมือนของจริง"
> เมื่อเสร็จ คุณจะมี Tenant ID + ไฟล์ตัวอย่าง + share URL พร้อมส่งต่อ

## Step 1 — Smoke test (3 นาที) ว่าทุกอย่างขึ้น

```bash
# ทดสอบ healthcheck
curl -s https://drm.zcr.ai/healthz
# คาดผล: {"status":"ok"}

# 3 หน้าหลักโหลดได้
for url in https://drm.zcr.ai/admin/ https://drm.zcr.ai/me/ https://drm.zcr.ai/share/; do
  echo "$url → $(curl -s -o /dev/null -w '%{http_code}' $url)"
done
# คาดผล: 200 / 200 / 200
```

ถ้ามี non-200 ตรงไหน → หยุดแล้วแจ้งเจ้าของก่อนทำต่อ

## Step 2 — สร้าง Demo Tenant ใหม่ (5 นาที)

ใช้ tenant แยกสำหรับ demo ไม่ปนกับ test/dev ของคุณ:

1. เปิด **incognito window** → <https://drm.zcr.ai/admin/>
2. Welcome modal ขึ้น → กด **"Create test tenant"** (ปุ่มที่ใหญ่ที่สุด)
3. ฟอร์มจะ auto-fill 3 ค่า — **คัดลอกเก็บไว้:**

```yaml
# จดไว้ใน password manager หรือ notes — ใช้ตลอด demo
demo_tenant_id: ____________________________________
demo_admin_key: ____________________________________
demo_admin_user_id: ____________________________________
```

4. กด **"Save session"** → ค่า persist ใน localStorage (ไม่ต้องพิมพ์ใหม่ทุกครั้ง)
5. กด **"Forget"** ไม่ต้องกด — จะลบทิ้ง

## Step 3 — เตรียมข้อมูลเหมือนของจริง (10 นาที)

### 3.1 สร้าง user ลูกค้า

Identity tab → **Users** subtab → กรอกในฟอร์ม "Create user":

```yaml
# คนรับ (พนักงาน customer ABC Co.)
user_id: aaaaaaaa-1111-2222-3333-aaaaaaaaaaaa
email: somchai@abc.co.th
display_name: Somchai Jaidee
```

กด **"Create user"** → user ขึ้นในตาราง

ทำซ้ำกับคนอีก 1 คน (recipient ภายนอกของลูกค้า):

```yaml
user_id: bbbbbbbb-1111-2222-3333-bbbbbbbbbbbb
email: malee@xyz.com
display_name: Malee Wongprasert
```

### 3.2 สร้าง Policy Template — "Confidential Contract"

Policy tab → **Policy templates** subtab → ฟอร์ม "Create template":

```yaml
template_id: cccccccc-1111-2222-3333-cccccccccccc
name: "Confidential Contract"
permissions: "View"            # ดูได้อย่างเดียว — print/copy/edit blocked
watermark_template: "{user} · {time} · ABC CONFIDENTIAL"
offline_lease_minutes: 60       # ดูออฟไลน์ได้ 1 ชม.
max_opens: 3                    # เปิดได้ 3 ครั้งต่อคน (FinalCode C1)
allow_print: false              # ห้ามพิมพ์
```

กด **"Create template"** → template ขึ้นในตาราง พร้อมคอลัมน์ "Max opens: 3 / user"

### 3.3 ตั้งค่า Brute-force protection

Tenants tab → ใช้ API ตั้งค่า (UI ยังไม่มี — ใช้ curl ตามนี้):

```bash
TENANT=<demo_tenant_id ที่จดไว้ Step 2>
ADMIN_KEY=<demo_admin_key>

curl -X PUT https://drm.zcr.ai/api/admin/brute-force-policy \
  -H "X-DRM-Admin-Key: $ADMIN_KEY" \
  -H "Content-Type: application/json" \
  -d "{
    \"tenantId\": \"$TENANT\",
    \"enabled\": true,
    \"threshold\": 3,
    \"windowMinutes\": 30
  }"
# คาดผล: HTTP 200 พร้อม policy ที่ตั้ง
```

**ตั้ง threshold = 3** (default คือ 10) เพื่อให้ demo เห็น auto-revoke ได้เร็ว — เผื่อพิมพ์รหัสผิดแค่ 3 ครั้ง link จะถูกยกเลิกอัตโนมัติ

### 3.4 เตรียมไฟล์ตัวอย่าง

เตรียม PDF จริงที่เปิดได้ ไฟล์ขนาด **< 5 MB** หน้าตาเหมือนเอกสารธุรกิจ:

- `Q4-Sales-Contract-ABC-XYZ.pdf` (~ 2 MB)
- มีหัวกระดาษ ABC Co. (สมมติ)
- เนื้อหา confidential clauses (lorem ipsum ก็ได้)

อย่าใช้ไฟล์ลูกค้าจริง — เป็น demo เท่านั้น

### 3.5 ทดสอบ flow ส่งไฟล์ end-to-end

เปิด **`<https://drm.zcr.ai/me/>` ใน tab ใหม่:**

1. กรอก Tenant ID + User ID (ใช้ค่าของ Somchai ที่สร้างใน 3.1)
2. ลาก-วาง ไฟล์ `Q4-Sales-Contract-ABC-XYZ.pdf` ลง drop zone
3. ใส่ recipient email: `malee@xyz.com`
4. คลิก **"Advanced options"** → เลือก template "Confidential Contract" (ถ้ามี dropdown — ถ้าไม่มี ข้ามได้ ใช้ default)
5. กด **"Send protected file"**
6. รอ → result panel ขึ้น → **"Copy link"** → คัดลอก share URL

**share URL ที่ได้ จดไว้:** `_______________________________________`

7. เปิด **incognito window ใหม่** (ทำเหมือนลูกค้าเปิด link)
8. แปะ share URL → หน้า /share/ ขึ้น
9. ใส่ guest email = `malee@xyz.com` → กด "Send verification code"
10. ตอน demo จริง — ลูกค้าจะเห็น verification code ในอีเมล

**ถ้าทำ Step 9 แล้ว verification code ไม่มาในอีเมล (เพราะ email integration ยังไม่ตั้ง):**

```bash
# Engineer ต้อง grep หา code จาก server log
ssh root@drm.zcr.ai 'docker compose -f /opt/drm/deploy/management/docker/docker-compose.yml logs --tail=50 drm-server' \
  | grep -i "verification.*code"
# จะเจอ log line ที่บอกรหัสที่จะถูกส่งไป
```

จดรหัสไว้ — ตอน demo จะกรอกแทนลูกค้า

## Step 4 — Pre-demo sanity check (5 นาที)

ทำตามนี้ก่อน demo 15 นาที:

```bash
# 1. Server ยังขึ้น
curl -s https://drm.zcr.ai/healthz
# คาดผล: {"status":"ok"}

# 2. Demo tenant ยังอยู่
curl -s -H "X-DRM-Admin-Key: $ADMIN_KEY" \
  "https://drm.zcr.ai/api/admin/policy-templates?tenantId=$TENANT" | head -5
# คาดผล: template "Confidential Contract" ขึ้นมา

# 3. Brute-force policy ตั้งไว้
curl -s -H "X-DRM-Admin-Key: $ADMIN_KEY" \
  "https://drm.zcr.ai/api/admin/brute-force-policy?tenantId=$TENANT"
# คาดผล: threshold:3, windowMinutes:30, enabled:true
```

## Step 5 — สิ่งที่ต้องเตรียมก่อน demo ในรูปแบบเช็คลิสต์

- [ ] Tenant ID, Admin Key, Admin User ID จดใน password manager
- [ ] User "Somchai" + User "Malee" สร้างใน Identity ครบ
- [ ] Policy template "Confidential Contract" (MaxOpens=3) สร้างแล้ว
- [ ] Brute-force policy ตั้ง threshold=3
- [ ] Sample PDF `Q4-Sales-Contract-ABC-XYZ.pdf` พร้อมบนเครื่องที่ demo
- [ ] ทดสอบ flow ส่งไฟล์ end-to-end ผ่านครบจาก /me/ → /share/
- [ ] verification code สามารถ fetch จาก server log ได้ (ถ้า email ไม่ทำงาน)
- [ ] Bookmark **3 URLs** บนเครื่องที่ demo:
  - https://drm.zcr.ai/admin/
  - https://drm.zcr.ai/me/
  - https://drm.zcr.ai/share/

## ส่งให้เจ้าของหลัง prep เสร็จ

```markdown
Demo prep เสร็จแล้วครับ

Tenant ID: ____________________
Admin Key: ____________________ (ห้ามแชร์ในแชท — ใส่ password manager)

Sample file: Q4-Sales-Contract-ABC-XYZ.pdf บน Desktop
Sample share URL พร้อมใช้ (ทดสอบแล้วเปิดได้): ____________________
Recipient email สำหรับ verification: malee@xyz.com
Verification code จะดูได้จาก server log ถ้า email ไม่มา — ผม standby ดูให้

3 URLs ที่ใช้ demo:
1. https://drm.zcr.ai/admin/
2. https://drm.zcr.ai/me/
3. https://drm.zcr.ai/share/

อ่าน 03-demo-script.md ของผม ก่อนเริ่ม
```
