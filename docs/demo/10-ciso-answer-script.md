# CISO answer script — Monday demo

> Live answer crib for the ~15-20 min Q&A after the 25-min walkthrough.
> Each Q has the *honest* state of the product + the language to use.
> If a customer pushes deeper than the printed answer, fall back to:
> **"Good question — that's on our Q3 roadmap. What would the ideal
> behavior look like for your team?"** — turns the gap into discovery.

The cheat is structural: lead with what *is* shipped (concrete: endpoint
name, table name, feature name) → name the gap honestly → close with a
roadmap commitment or a discovery question. Never apologize. Never pad.

---

## 1. Audit & compliance

### Q: How do you prove who accessed what, and when?

**A:** "Every state-changing event writes one row in the `AuditEvents`
table with TenantId, FileId, UserId or ActorAdminId, EventType,
ReasonCode, and a UTC timestamp. The admin pulls it via
`GET /api/admin/audit?tenantId=X` (or `audit.csv` for export). We
shipped a 20-event Activity feed on the admin landing page so the
operator sees the most recent activity without filtering."

### Q: What if my auditor wants one row per share with recipient + permissions + when?

**A:** "Today the data is split across three tables — `AuditEvents`
(when + who acted), `ProtectedFiles` (permissions bitfield), and
`ExternalShareLinks` (recipient email + expiry). The admin console
JOINs them when you click into a file. We're consolidating that into
a single `Detail` JSON column on AuditEvents in Q3 so the CSV export
gives auditors one row per share end-to-end. **The data is captured
today** — what we're improving is how it's surfaced."

### Q: Tamper-proof?

**A:** "Yes — `AuditChainService` chains each event's hash to the
previous one. If a row is mutated or deleted the chain breaks and
the verifier flags it. `/api/admin/audit-chain/verify` returns the
break point if any. SIEM mirror via webhook is also wired so an
external log destination has the same record."

### Q: Retention + GDPR erase?

**A:** "`DataRetentionService` runs on a worker — set TTL per tenant
in admin settings; events past TTL get purged. GDPR erase is a
per-user endpoint that nullifies PII (email, name) while keeping the
audit row for compliance — same pattern most banking systems use."

---

## 2. Encryption & key management

### Q: AES key strength?

**A:** "AES-256-GCM per file — random 256-bit content key generated
client-side by the agent, wrapped by the tenant master key on the
server, never written to disk in plaintext. The `.drmcontainer`
format (folder shares) uses PBKDF2 with 600,000 iterations + 16-byte
random salt per container — that's the current OWASP recommendation."

### Q: How is the share link itself protected?

**A:** "256-bit random token generated server-side, hashed with
SHA-256 before storing in `ExternalShareLinks.TokenHash` — same
pattern as a password column. The plaintext token only exists in
the URL we hand to the sender and in the verification email link;
the database can only verify a presented token against the hash,
never reconstruct the token. Single-use until verified, optional
expiry + max-uses, brute-force protection auto-revokes after 5
failed attempts. We hardened the token format in May after an
internal audit found one endpoint emitting hex instead of base64
— that's now caught by an integration test."

### Q: Where do keys live?

**A:** "Per-file content keys are wrapped by a tenant master key.
`FileKeyProtection` handles wrap/unwrap; `KeyRotationService` +
`KeyRotationWorker` rotate the master on a configurable schedule.
**Today**: master lives in the database, encrypted by an
operator-supplied bootstrap key (env var). **Q3**: HSM integration
(AWS KMS / Azure Key Vault). What's your preferred KMS?"

### Q: If your database is leaked?

**A:** "An attacker gets ciphertext + wrapped keys. Without the
bootstrap key the wrapped keys don't decrypt. We document
rotate-bootstrap-and-revoke-all-shares as the IR runbook."

---

## 3. User access control / permissions

### Q: What can a regular employee do vs an admin?

**A:** "Four personas: `Employee` (protect + invite guests),
`KnowledgeWorker` (+ revoke own files), `Executive` (+ tenant-wide
audit), `Admin` (everything). Defined in `Drm.Domain.DrmPersona` +
`PersonaCapabilities`. The matrix is in code, no migration to change."

### Q: Can junior employees only use templates instead of picking permissions themselves?

**A:** "Today every employee can flip the Print/Copy/Edit/Download
checkboxes per share. **Q3 capability flag** — `CanCustomizePermissions`
locked to false for a `Restricted` persona means they inherit the
admin's default template. The plumbing is there (`PolicyTemplates`
table + `DefaultPolicyTemplateId` in tenant settings); we're adding
the persona gate on the UI side. How granular do you want this?
Per-department, per-template, or per-user?"

### Q: Can a user see their own share history?

**A:** "Yes — shipped this week. Sender opens `/me/`, scrolls to
**My recent shares**, and sees a table with recipient, sent date,
expiry, open count, permissions, and live status (Active / Revoked
/ Expired / Used-up). Each active row has a **Revoke** button that
flips the share dead immediately — no admin involvement needed.
Endpoint is `/api/me/shares` (list) and
`/api/me/shares/{id}/revoke` (self-revoke); both ownership-gated so
a user only sees and acts on their own shares."

### Q: What if a sender realises they shared with the wrong recipient?

**A:** "Click **Revoke** on that row in `/me/` My Shares. The share
link goes dead immediately; the recipient's next attempt to verify
or open returns 'link revoked'. Audit row writes with ReasonCode
`external_share_link_self_revoked` — distinct from admin revoke and
the brute-force auto-revoke worker — so the IR timeline shows
exactly who killed the share and when."

### Q: Can a sender share one file with multiple recipients in one go?

**A:** "Yes — the Quick Send recipient field accepts comma or
semicolon-separated emails. The agent encrypts the file once, mints
one share link per recipient against the same fileId, and opens
one email composer per recipient. Each recipient gets only their
own access token — never sees other recipients' links. Audit log
shows one `protected_file_registered` row + N
`external_share_link_created` rows, each tagged with the right
guest email. Per-recipient revoke still works the same way."

### Q: Walk me through what happens when an employee shares a file.

**A:** "Four things, all in roughly two seconds: (1) Agent reads
the file, generates a random AES-256-GCM key, encrypts the bytes,
writes `<original>.drmx` next to the source. (2) Agent calls
`/api/admin/files/{fileId}/share-links` to register the file and
mint a single-use access token, wrapping the per-file key with the
tenant master key. (3) Agent opens the sender's Outlook with the
`.drmx` already attached + subject + body + share URL pre-filled;
sender clicks Send. (4) On the audit side, two `AuditEvents` rows
land: `protected_file_registered` and
`external_share_link_created`. Every step is one log entry. The
sender sees inline status — `'✅ Wrote <file>.drmx + Outlook
opened with it attached. Just hit Send.'` — and a clipboard copy
of the share URL as a fallback."

---

## 4. Recipient experience

### Q: Does the recipient need to install anything?

**A:** "For external recipients without our agent: **no install for
policy verification** — they click the share link, verify their
email, see the permissions chips and access summary in the browser
(/share/ page). To open the actual document they need our Windows
viewer because that's where we enforce print/copy/edit at the OS
level. We surface a download link on /share/ for guests who don't
have it. **Q3** — web viewer with in-browser PDF preview so guests
read the document without installing anything."

### Q: Internal recipients (your own employees)?

**A:** "Today they still go through /share/ verification — same as
external. **Q3 frictionless internal flow**: when the agent detects
the recipient is in the same tenant, it creates a `FileGrant`
(direct user binding) instead of an `ExternalShareLink`, and the
viewer auto-decrypts when they double-click the .drmx — no /share/
detour. That's the FinalCode-style internal experience."

### Q: How long does verification take?

**A:** "6-digit code emailed by us; arrives in 10-30 seconds in
typical deployments. Code expires in 10 minutes. Failed attempts
auto-revoke the link after 5 — `BruteForceProtectionService` handles
that. Resend is a separate button on the page."

### Q: Who sees the encrypted file in transit?

**A:** "Only the sender's mail provider and the recipient's mail
provider — same path as any normal email attachment. The agent
encrypts the file locally, attaches the resulting `.drmx` to a new
mail item in the sender's own Outlook (via the Outlook COM API), and
the email travels through whatever SMTP / Exchange path that
mailbox already uses. **The file never touches our servers.** What
DOES touch our servers is the wrapped content key (so we can
authorize the recipient at /share/ time) and the audit record of
the share. If the sender's mail client isn't Outlook we fall back
to a `mailto:` composer — same data path, sender just drags the
`.drmx` in themselves."

### Q: What if my employee's laptop doesn't have a default mail client?

**A:** "The agent warns at launch with a yellow banner on the Quick
Send tab — `'No default mail client detected. Set a default at
Settings → Apps → Default apps → Mail.'` Quick Send still encrypts
the file and copies the share URL to clipboard either way, so the
sender can paste into webmail manually. We shipped this guard in
May after we noticed fresh corporate images sometimes ship with no
mail client registered at all."

---

## 5. Integration

### Q: SSO?

**A:** "SCIM bearer auth for directory sync from Entra ID is shipped.
SAML/OIDC SSO is Q3. Today the admin console takes a bearer-style
admin API key tied to AdminIdentity — works with bookmarklets or
IdP-protected proxies. What IdP are you on?"

### Q: SIEM?

**A:** "`SiemWebhookService` dispatches every audit event to your
webhook URL — Splunk, Elastic, Datadog, Microsoft Sentinel — set the
URL in admin console. Format is structured JSON. `SiemDelivery` log
shows delivery success/failure per event."

### Q: Box / SharePoint / Office?

**A:** "Box integration is in the box (`BoxIntegration.cs`) — agent
can pull from Box folder, protect, push back. Outlook add-in
(`wwwroot/outlook-addin/`) + Word add-in (`wwwroot/word-addin/`) are
plumbed for in-place document protection. SharePoint is Q4."

---

## 6. Operations

### Q: HA / failover?

**A:** "Single instance today, deployed via docker compose. Postgres
backed (we hold a snoozed plan for nightly snapshots that's coming
back online next quarter). **Q3** — multi-region replica + automated
failover. For pilot deployments single region is the realistic
recommendation."

### Q: Backup?

**A:** "Postgres + the on-disk encrypted file blobs (if you're not
using Box). Standard PG dump + S3-or-equivalent. **Coming Q3** —
managed daily snapshot service."

### Q: Self-hosted vs SaaS?

**A:** "Self-hosted is our default. Single-tenant SaaS is available
for customers who want us to operate it. Both run the same code —
just the deployment changes."

---

## 7. Vendor / open-source posture

### Q: Open source?

**A:** "Not yet. We're evaluating dual-license (BSL-style) for Q4 so
the core is auditable but our hosted/SaaS option stays sustainable.
Source-code escrow is available for enterprise contracts."

### Q: Data residency?

**A:** "Self-hosted = your hardware, your jurisdiction. Hosted =
Thailand (Bangkok) by default, EU/SG on request."

### Q: How long has CyberDefense Co. been doing this?

**A:** "[Tailor to your story — engineering team, prior DRM
experience, customer count once you have references.]"

---

## When in doubt — the universal pivots

| Customer says | You answer with |
|---|---|
| "Can it do X?" | "Today we do Y, which covers your need — show me your specific X case so we can map it" |
| "Why don't you have Z?" | "We chose to ship Y first because [reason]. Z is on Q3 — would your team need it before that?" |
| "Competitor has Q" | "Yes that's table stakes — we have Q via [our way]. The differentiator is [self-hosted / audit chain / persona model]" |
| "Pricing?" | "Depends on # users + on-prem vs hosted. Let's spec your environment after we agree the technical fit" |

---

## Self-honesty checklist (before each answer)

- Am I claiming something that doesn't exist? → stop, restate honestly.
- Am I hiding a gap? → name it explicitly + close with roadmap or
  discovery.
- Am I padding to fill silence? → stop talking; let them ask the
  next question.
- Did I just say "of course" or "absolutely"? → those are buzzwords.
  Replace with the concrete feature/file name.
