# zcrDRM Design System

Source of truth for visual decisions across the zcrDRM web surfaces. Tokens live in
[`src/Drm.Server/wwwroot/static/tokens.css`](src/Drm.Server/wwwroot/static/tokens.css) —
this document explains the **why** behind those tokens so future changes can be
calibrated against intent.

Decided: 2026-05-17. Brand layer added 2026-05-20 (v1.3.0).
Latest audit: see `~/.gstack/projects/quirky-visvesvaraya-d75114/designs/design-audit-20260519/`.

---

## Product name & positioning

**Name:** `zcrDRM` — lowercase `zcr` (brand prefix, CyberDefense house mark) +
uppercase `DRM` (category descriptor so outsiders categorize instantly).

**Canonical URL:** `drm.zcr.ai` — production admin console. The `zcr.ai`
parent domain belongs to CyberDefense; the `drm` subdomain isolates this
product. The `.ai` TLD reinforces the modern/technical positioning.

**Memorable thing:** "Self-hosted DRM, running in 5 minutes. No SaaS lock-in."

**Positioning vs incumbents** (FinalCode / Vera / Seclore / Microsoft Purview):
- Engineer-friendly, not enterprise-procurement-friendly
- On-prem first, Docker-deployable, Postgres-native
- Per-file AES-256, per-event audit chain, instant revoke from anywhere
- No SaaS license dance, no vendor cloud dependency

**Three product pillars** (used in hero band on Overview tab):
1. **Encrypt** — AES-256 per file, RSA-2048 wrapping, FIPS 140-2 ready
2. **Audit** — every open, every device, every share, tamper-proof chain
3. **Revoke** — kill files from anywhere after they left the network

---

## Wordmark

```
zcrDRM
```

- Lowercase `zcr` rendered in `var(--accent)` (`#275d72`)
- Uppercase `DRM` rendered in `var(--ink)` (default dark)
- IBM Plex Sans, weight 700 (bold)
- Letter-spacing: -0.01em (tight)
- Paired with the brand-seal icon (24x24 SVG) on the left

The wordmark replaces the previous `🔒 DRM Management` text. It carries the brand
in every page-header, every favicon, every email footer.

---

## Brand direction: teal on slate

The product is **enterprise security tooling**. Customers are IT admins, security
officers, and compliance teams. Trust is the dominant emotion the design must serve.

| Trait | Choice | Why |
|---|---|---|
| Primary accent | `#275d72` teal | Reads as "trustworthy / professional", not warm/SaaS. Was originally only on `/share/`; chosen as the system spine after a 2026-05-17 audit judged `/share/` the most polished surface. |
| Page surface | `#f3f4f6` cool slate | Replaced the prior cream `#f5f5f2`. Cool neutrals support the teal accent better than warm cream did. |
| Dark rail | `#20242a` slate-800 | Reads as the "chrome" — never competes with content. Same color across `/admin/` and every other surface that uses a dark sidebar. |
| Typeface | IBM Plex Sans + IBM Plex Mono | Distinctive without being trendy. Carries authority in dense data UIs. Replaced Arial/system stack across all surfaces. Loaded via Google Fonts with `preconnect` + `font-display: swap`. |
| Mood adjective | "Calm, deliberate, opinionated" | Not "playful", not "minimalist-cool", not "warm SaaS". |

---

## Three-tier token system

`tokens.css` is structured in three layers. **Touch the highest layer that solves
your problem** — primitives only when you're designing a system-level capability.

### 1. Primitives — raw values

- **Color scales:** `--color-teal-50..900`, `--color-slate-50..900`,
  `--color-cream-{50,200}` for warm text on dark rail
- **Spacing:** `--space-1` (4px) → `--space-10` (64px), 4px base
- **Type:** `--text-xs` (11px) → `--text-hero` (36px), 1.25 major-third ratio
- **Radii:** sm 6 / md 10 / lg 14 / xl 20 / pill 999
- **Shadows:** xs / card / pop / drawer / modal
- **Motion:** `--duration-fast` (120ms), `--duration-base` (180ms), `--duration-slow` (280ms)
  with `--ease-out` and `--ease-in-out` cubic-béziers
- **Z-index:** base 1 / sticky 50 / backdrop 80 / drawer 90 / modal 100 / toast 200
- **Touch:** `--touch-target` 44px (WCAG/Apple HIG), `--touch-target-sm` 36px for secondary nav

### 2. Aliases — semantic intent

- `--ink`, `--muted`, `--line`, `--surface`, `--page` for neutrals
- `--accent`, `--accent-dark`, `--accent-soft`, `--accent-tint-12/06` for brand
- `--rail`, `--rail-ink`, `--rail-muted`, `--rail-line` for the dark sidebar
- `--ok`, `--error`, `--warning-{bg,ink}` for status
- `--focus-ring` (3px teal glow at 35% opacity)

### 3. Component tokens — component-scoped

- `--btn-*` (padding, min-height, primary-bg, hover, ghost, danger)
- `--field-*` (padding, border, focus, bg, min-height)
- `--card-*` (bg, border, radius, padding, shadow)
- `--pill-*` (padding, radius, min-height)

Re-theme a control by editing one component token, not by hunting through surface CSS.

---

## Heading scale

Major-third (1.25) ratio applied globally via tokens.css:

| Level | Token | Size | Weight |
|---|---|---|---|
| h1 | `--text-h1` | 28px | bold |
| h2 | `--text-h2` | 22px | bold |
| h3 | `--text-h3` | 18px | semibold |
| h4 | `--text-lg` | 16px | semibold |
| Body | `--text-body` | 14px | regular |

Surface-specific overrides (e.g., `.rail h1` 29px, `.viewer-header h1` 34px) are
intentional — they raise the prominence of a hero heading within its own context.
Don't touch them unless redesigning the whole hero.

---

## Surfaces

| Surface | Role | Audience | Chrome |
|---|---|---|---|
| `/admin/` | Multi-panel ops console | IT admins | Dark sidebar (collapsible) + 5 tabs + sub-nav inside each tab |
| `/me/` | Single-task send tool | Internal end users | Top bar, focused form, no app nav |
| `/share/` | External viewer | Recipients (often outside the org) | Brand bar only, no cross-page nav |
| `/admin/cases/` | Reference docs | Sales, customer success | Sticky TOC sidebar + back-to-admin link |
| `/admin/compatibility/` | Reference matrix | IT, support | Auto-built TOC from rendered categories |

Each surface has its own `app.css` that imports `/static/tokens.css` first, then
overrides only what's specific to that surface.

---

## Patterns

### Welcome screen
First-time visitors to `/admin/` see a full-screen card that generates Tenant ID,
Admin Key, and Admin user ID in one click. Replaces the prior "wall of GUIDs"
greeting. Dismissed state persisted via `localStorage["drm:bootstrapped"]`.

### Getting Started checklist
5-step onboarding card pinned to the top of Overview tab. Steps auto-check as the
user completes them (creates a user, creates a policy template, protects a file).
Dismissed state persisted via `localStorage["drm:gettingStartedDismissed"]`.

### Settings drawer
License + Server Health live in a right-slide drawer triggered by a gear button
in the page header. Keeps the daily workspace clean — these surfaces are read
quarterly, not daily.

### Sub-nav within tabs
Each of the 5 main tabs has a pill-style sub-nav that auto-builds from the
panels grouped under that `data-tab`. Only one panel is visible at a time;
search mode (`data-active-tab="all"`) reveals everything.

### Empty states
Use the `.empty-state` card (in tokens.css) when a list is empty. The
`emptyStateRow(colspan, opts)` helper in `admin/app.js` embeds it inside a `<td>`.
Always include an icon, plain-language title, and a hint that tells the user the
concrete next step — never "No items found."

### Plain-language tooltips
Jargon panel headings (Transparent, Containers, SIEM, Folder watcher, …) get a
small `ⓘ` pill that surfaces a one-sentence explanation on hover. New jargon
should follow suit — see the `PANEL_TIPS` map in `admin/app.js`.

### Touch targets
Every interactive element is at least **44px tall** (`--touch-target`). Secondary
nav pills can drop to **36px** (`--touch-target-sm`) but no smaller.

### Focus rings
`--focus-ring` is the 3px teal glow at 35% opacity. Apply via `box-shadow` on
`:focus-visible` for buttons/links and on `:focus` for form fields. Never use
`outline: none` without replacement.

### Reduced motion
`prefers-reduced-motion: reduce` collapses all `--duration-*` tokens to 0ms AND
globally clamps any un-tokenized transition/animation to 0.01ms. Belt-and-suspenders.

---

## What we explicitly chose NOT to do

- **No purple gradients, blue-to-purple, or violet anything.** That's the #1 AI
  slop signal.
- **No icons-in-colored-circles 3-column feature grid.** That's the #2 AI slop signal.
- **No centered-everything.** Hero copy left-aligns; only interactive targets
  (`.drop-zone`) center.
- **No system-ui as the primary font.** That's the "I gave up on typography"
  signal.
- **No default font stacks** (Inter / Roboto / Arial / system). We picked Plex
  deliberately.
- **No `outline: none` without replacement.** Focus rings are non-negotiable.
- **No splash interstitials or forced tours.** `/me/` killed a 2-modal product
  tour after audit showed it drained 30 goodwill points per first visit. Personalization
  is opt-in via a "Personalize" link in the topbar.

---

## When to add a new token

Add a primitive when: a value will be used 3+ times AND has a clear name.

Add an alias when: there's semantic intent worth naming (e.g., "this is the
focus-ring color", not "this is teal at 35%").

Add a component token when: re-theming this one control in isolation would be a
real need (e.g., danger buttons → red palette without touching everything else).

When in doubt, don't add a token. The current set is calibrated; resist drift.
