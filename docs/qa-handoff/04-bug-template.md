# Bug Report Template

File via GitHub Issues: <https://github.com/nutthakorn7/DRM/issues/new>

Or paste this skeleton into Slack / email if GitHub access isn't set up yet.

---

```markdown
## Summary
<One sentence: what is broken from the user's perspective>

## Severity
- [ ] P0 — production down, data loss risk, security exposure
- [ ] P1 — feature broken, no workaround
- [ ] P2 — feature broken, workaround exists
- [ ] P3 — cosmetic, minor UX issue

## Environment
- URL: https://drm.zcr.ai/...
- Version: v1.6.1 (from page title or CHANGELOG)
- Browser: Chrome 125 on macOS 14.5
- Tenant ID (test): <guid>
- User: <admin / guest / specific role>
- Time observed (UTC): 2026-05-21T07:30:00Z

## Steps to reproduce
1. Open ...
2. Click ...
3. Fill ...
4. Click Submit

## Expected
<What the spec / behaviour says should happen>

## Actual
<What actually happened. Include error codes, HTTP responses, console errors>

## Evidence
- Screenshot: [attach]
- Console log: [paste relevant lines]
- Network request: [paste URL + method + response status + response body]
- Audit event (if relevant): query `/api/admin/audit?tenantId=...` and paste the entry

## Repro rate
- [ ] Every time
- [ ] Intermittent (X out of Y attempts)
- [ ] Once, can't reproduce

## Suspected root cause (optional, if you peeked at the code)
<File:line reference if you found it. Empty is fine.>

## Workaround
<What the user can do meanwhile, or "none">
```

---

## Severity guidelines

| Severity | Time-to-fix expectation | Example |
|----------|------------------------|---------|
| **P0** | Same day; pull eng on-call | `/healthz` returns 500 in production; admin key leaks in browser console; share link returns file content to wrong recipient |
| **P1** | This sprint | Tenants tab broken (regression of the v1.2.x bug); MaxOpens not enforced; CI master red |
| **P2** | Next sprint | Wrong button label; form validation timing weird; copy-link button doesn't show toast |
| **P3** | When convenient | Personalize modal uses emoji icons instead of SVG; placeholder text could be clearer |

## Before filing — quick checks

These save the eng team time:

1. **Is it already in `06-known-issues.md`?** If yes, link to it instead of filing new.
2. **Is it reproducible in incognito?** Saved localStorage can cause false positives.
3. **Is the URL really https://drm.zcr.ai?** Local dev (`localhost:5210`) and prod sometimes drift; bug in dev only is a separate ticket.
4. **What does `/healthz` say?** If it's not OK, file ONE P0 bug for that and stop testing until eng acks.
5. **What does the audit log say?** Often the root cause is visible there.

## What NOT to file as a bug

These are explicit non-goals (see README):

- "No dark mode" — out of scope, user decision
- "No iOS / Android app" — out of scope, user decision
- "No public API docs site" — known gap, scheduled separately
- "No marketing landing at root" — known gap, scheduled separately
- "Personalize modal uses emoji" — cosmetic, in known-issues backlog

Things that LOOK like bugs but aren't:

- **403 on `/api/admin/*` without `X-DRM-Admin-Key`** — by design
- **`/admin/cases/` and `/admin/compatibility/` open in new tab** — `target="_blank"` intentional
- **Tab nav scrolls horizontally on mobile** — by design after v1.2.2 fix
- **No "Forgot password" link** — admin tokens are not passwords; design choice
