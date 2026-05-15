# External Browser Viewer Shell Design

## Goal

Build a public browser viewer shell for external share recipients at `/share/`. The shell lets a guest complete the existing share verification flow and open the existing verified viewer session, then shows viewer-safe metadata and disabled document actions. It does not render protected content, return file keys, return ciphertext, or decrypt files in the browser.

## Scope

This phase covers the guest-facing browser workflow after an enterprise admin has already created an external share link:

- Collect tenant ID, access token, and guest email.
- Start verification through `POST /api/share-links/verification/start`.
- Confirm the emailed code through `POST /api/share-links/verification/confirm`.
- Open a viewer metadata session through `POST /api/share-links/viewer/session`.
- Display content type, file ID, expiry, guest identity, watermark template, and disabled download/print/export controls.

This phase intentionally excludes real content streaming, browser PDF rendering, file-key release, email delivery integration, and public link routing that embeds tenant/token values in a URL.

## Architecture

The server already serves static files from `src/Drm.Server/wwwroot`. Add a new static app under `src/Drm.Server/wwwroot/share/` with `index.html`, `app.css`, and `app.js`. Add a redirect from `/share` to `/share/` in `Program.cs`, matching the existing `/admin` behavior.

The JavaScript app uses `fetch` against the existing public external-share endpoints. It stores the transient verification session token in memory only, not `localStorage`, `sessionStorage`, or the DOM. Once the verified viewer session opens, the app clears the token variable and renders only the safe metadata returned by the API.

## UI Design

Visual thesis: a quiet enterprise document access surface with a left-side verification workflow and a right-side locked document preview/status pane.

Content plan:

- Top context: product name, external viewer status, no marketing copy.
- Left workflow: connection fields, verification code entry, and clear status messages.
- Right preview: locked document panel, metadata rows, watermark pattern, disabled action controls.
- Footer/status: last operation result and safe reason codes.

Interaction thesis:

- Step sections become active/complete as the guest progresses.
- Status messages use compact success/error states without modal interruptions.
- The locked preview updates after `viewer/session` returns and keeps download, print, and export visibly unavailable.

## Data Flow

1. Guest enters `tenantId`, `accessToken`, and `guestEmail`.
2. `startVerification()` posts to `/api/share-links/verification/start`.
3. Server sends or records the verification code through the configured sender.
4. Guest enters the code.
5. `confirmVerification()` posts to `/api/share-links/verification/confirm`.
6. The app keeps `verificationSessionToken` in a closure variable.
7. `openViewerSession()` posts to `/api/share-links/viewer/session`.
8. The app clears `verificationSessionToken` and renders the viewer metadata response.

## Error Handling

The app shows HTTP failures as `reasonCode` when the API returns one. `404` responses show a neutral "share not found or no longer available" message so token/email guessing does not reveal share state. Network failures show a generic connection error.

Invalid local inputs are blocked client-side before API calls:

- Tenant ID is required.
- Access token is required for verification start.
- Guest email is required for verification start.
- Verification ID and code are required for verification confirm.
- Verification session token must exist before opening a viewer session.

## Security Boundary

The app must not persist or display the access token, verification code, or verification session token after use. The static shell must not include any API for key unwrap, content download, ciphertext retrieval, or decrypted document bytes.

The visible download, print, and export controls are disabled because enforcement is not yet content-aware. Real browser document rendering and secure content/key release require a separate phase with a stricter threat model.

## Testing

Add server static asset tests that verify:

- `GET /share` redirects to `/share/`.
- `GET /share/` serves the viewer HTML.
- `GET /share/app.css` and `GET /share/app.js` serve assets.
- HTML includes the expected workflow surfaces and locked viewer controls.
- JavaScript references the three public share endpoints and does not reference browser storage APIs.
- JavaScript does not reference file-key, unwrap, ciphertext, decrypted, download-content, print APIs, or export APIs.

Run the focused server tests, then run full solution tests and the existing Windows desktop builds.

## Self Review

- Placeholder scan: no placeholders remain.
- Scope check: this is one implementation slice and does not include content rendering or key release.
- Consistency check: endpoint paths match the existing external share API.
- Ambiguity check: token persistence is explicitly in-memory only, and content rendering is explicitly out of scope.
