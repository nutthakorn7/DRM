# Open Questions — for the eng team to answer

QA engineer: as you work through testing, you'll hit questions where the
spec is silent. List them here so the eng team can answer in batches.
**Don't get stuck waiting** — write the question down, mark the affected
test as `BLOCKED`, move on.

## Question template

```markdown
## Q-001: <one-line summary>
**Affects:** Tier <N> scenario T<X.Y>
**Asked:** YYYY-MM-DD by <QA name>
**Status:** OPEN | ANSWERED | DEFERRED

### Context
<What you were trying to test>

### Question
<The specific thing you need decided>

### What I tried meanwhile
<Workaround if any. Often this becomes the answer.>

### Answer (eng to fill in)
<TBD>
```

---

## Seed questions (already known unknowns)

### Q-001: What's the expected response when a tenant is suspended mid-session?
**Affects:** T2.6 — Tenant suspension
**Status:** OPEN

### Context
The Tenants tab has a "Suspend" action. Testing what happens to in-flight
operations against a suspended tenant.

### Question
- Does `POST /api/files/{id}/keys/unwrap` return 403 immediately, or does
  it complete the current call and reject the next one?
- Does an in-progress upload abort or finish?
- Do scheduled audit retention workers skip suspended tenants?

### Workaround
Test the simple cases (suspend with no in-flight work) only. Add the
in-flight scenarios once the answer arrives.

---

### Q-002: What's the contract for `Permissions` parsing?
**Affects:** T2.1 — Policy templates
**Status:** OPEN

### Context
`PermissionParser.TryParse` accepts strings like `"View, Print"`. Edge cases:
- `"View,Print"` (no space) — works?
- `"VIEW"` (uppercase) — works?
- `"view"` (lowercase) — works?
- `"View | Print"` (pipe separator) — works?
- `""` (empty) — returns `Permission.None` or fails?
- `"View, NonExistent"` — partial accept or fail?

### Question
Which of those are intentional valid forms? Lock the contract so we test
all the documented variations and reject the rest.

---

### Q-003: Is offline lease "renewable" or "fixed-window"?
**Affects:** T3.5 — Long-running viewer
**Status:** OPEN

### Context
`OfflineLeaseMinutes` on a policy template. When the viewer holds a file,
does the lease:
- (A) Fixed window from first open: 60min from t=0 — viewer must reconnect
      every 60 minutes regardless of activity?
- (B) Renewable: every API call extends the window from "now"?
- (C) Hybrid: extends on heartbeat, hard cap at some longer interval?

The viewer behaviour and the audit log will differ depending on the answer.

---

### Q-004: What happens when MaxOpens is decreased after some opens are consumed?
**Affects:** T1.6 — MaxOpens enforcement
**Status:** PARTIALLY ANSWERED

### Context
File has `maxOpens: 5`, User Alice has consumed 3 opens (2 remaining).
Admin lowers the template's `maxOpens` to 2.

### Question
Should:
- (A) Alice gets denied immediately (she's already at 3, over the new cap of 2)
- (B) Alice keeps her remaining count from the old cap, sees `opensRemaining: 2 - 3 = -1` clamped to 0 → denied
- (C) Alice's counter resets to 0 (new cap, fresh start)

### Eng note (from PolicyEvaluator implementation)
Current behaviour is (B) — the evaluator returns `Deny("opens_exhausted")`
with `OpensRemaining: 0` if `OpensUsed >= MaxOpens`. Documented in the
`Denies_with_opens_exhausted_even_when_opens_used_exceeds_max` test.

**Status: behaviour locked, doc as (B).**

---

### Q-005: Are share-link `MaxUses` and the brute-force threshold independent?
**Affects:** T1.5 + T2.7 — Share links + brute force
**Status:** OPEN

### Context
A share link has `MaxUses: 5` (successful redemptions). The tenant's
brute-force policy is `threshold: 10` (failed verification attempts).

### Question
- If a guest redeems successfully 5 times → link locked due to MaxUses. Subsequent attempts: do they still count toward the brute-force window? Or does MaxUses-locked stop logging failures?
- If a guest fails 10 times (auto-revoke) and then a "successful" guest tries with a leaked code, does the still-revoked-state prevent the redemption?

### Workaround
Test the two systems in isolation first. Combined scenarios deferred.

---

### Q-006: Should `share_link_auto_revoked` error reveal whether the share exists?
**Affects:** T2.7 — Brute-force C2
**Status:** OPEN (security review)

### Context
Currently the 10th wrong attempt returns `share_link_auto_revoked` while
attempts 1-9 return `invalid_verification_code`. An attacker can use the
error-code change as a signal that the link was real.

### Question
Should the API mask the auto-revoke event (return the SAME error code as
"invalid code") and only surface the auto-revoke in the admin log?

### Trade-off
- Masking: better against information-disclosure attacks
- Distinct error: better UX for the legitimate "typo storm" user who
  hits the threshold and then doesn't realize the link is dead

Current implementation chose UX over information-hiding. Security
team should confirm this is OK.

---

## How to use this file

1. Copy the template at the top of this file when you have a new question
2. Number sequentially (Q-007, Q-008, ...)
3. Don't wait — mark the affected test BLOCKED and move on
4. Eng triages the file weekly and answers in batches
5. Once ANSWERED, the answer goes into `02-test-plan.md` or `06-known-issues.md`
   as appropriate, and the question stays here as audit history
