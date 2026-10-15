# Project website

**Live website:** https://codex.rakibhq.xyz

A static, dependency-free landing page for CodexSubsManager. Product copy and downloads deliberately have no release version to maintain. All three download buttons point to `https://github.com/0xmdrakib/CodexSubsManager/releases/latest`.

## Preview

Serve this directory with any static HTTP server. For example, with Node.js installed:

```powershell
npx serve website
```

Run the command from the repository root and open the printed localhost URL. The root `vercel.json` deploys only `website/`, with no build, dependency installation, API, tracking, or environment variables.

## Assets

- `assets/app-icon.png` and `favicon.ico`: the user-supplied desktop application artwork.
- `assets/inter.woff2`: Inter variable font from [the official Inter project](https://rsms.me/inter/). SIL Open Font License in `assets/Inter-OFL.txt`.
- Interface symbols follow the project's Lucide icon set. ISC/MIT notices are in `assets/Lucide-LICENSE.txt` and the root third-party notices.
- `assets/clay-accounts.webp`: original account artwork, optimized as a transparent WebP.
- `assets/social-preview.png`: the approved 1200 × 630 PNG used by Open Graph and X/Twitter link previews.

## Hosting

Import the repository into Vercel and leave its root directory at the repository root. The root `vercel.json` points to this folder. Configuration follows [Vercel's file-based project configuration](https://vercel.com/docs/project-configuration).

The example account switcher is an illustrative browser-only demo. It never authenticates, reads, switches, or stores real Codex accounts. The FAQs use native HTML disclosure controls; the mobile navigation and demo remain keyboard accessible, and motion respects the visitor's reduced-motion preference.
