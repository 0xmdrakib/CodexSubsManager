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
- `assets/clay-accounts.webp`: original artwork made with the built-in image generation tool, then refined for smoother surfaces at the user's request. The selected transparent image is optimized to WebP without changing its composition.
- `assets/social-preview.png`: the user-approved social preview, generated with the built-in image tool using the existing artwork and app icon as references, then exported at 1200 × 630 as an opaque PNG. Open Graph and X/Twitter metadata use its absolute HTTPS URL.

### Generation prompt

Use case: stylized-concept. Asset type: original transparent hero illustration for CodexSubsManager, a small Windows account-switching desktop companion. Create a polished hand-sculpted claymation still-life: three chunky rounded account tiles stacked and fanned, one lavender, one peach, one mint, each with a simple recessed circular avatar and two short raised lines. In front, a larger deep-teal rounded terminal block with a cream raised >_ symbol. Small ochre ball and soft pink clay arch behind the stack. Orthographic three-quarter view, cohesive compact composition, gentle studio light from upper left, subtle material imperfections, matte tactile clay, soft ambient occlusion. Palette strictly warm cream #fffaf0, lavender #b8a4ed, peach #ffb084, mint #a4d4c5, pink #ff4d8b, ochre #e8b94a, deep teal #1a3a3a. Objects centered with breathing room on all sides and no cropping. Genuine transparent background. No typography other than >_, no branding, no watermark, no interface screenshot, no glossy plastic.

### Refinement prompt

Refine this exact transparent 3D hero artwork to feel smoother and more premium. Preserve the same composition, three account tiles, recessed avatar forms, terminal block with raised >\_ glyph, pink arch, ochre ball, colors and object arrangement. Replace the rough porous/grainy clay surface with finely finished, velvety smooth matte clay: clean continuous surfaces, beautifully rounded edges, subtle soft satin highlights, no visible pits or grain. Use refined large-softbox studio lighting, softer ambient occlusion, gentle contact shadows and graceful light gradients. Keep depth and the tactile sculpted character; avoid harsh gloss or plastic glare. Keep true background transparency and uncropped silhouettes. Do not add text, branding, shapes or objects.

### Social preview prompt

Use case: ads-marketing / compositing.
Create ONE finished horizontal social-sharing Open Graph preview card for the Windows desktop project CodexSubsManager. This is a preview for the user's approval.
Canvas: exactly 1200 x 630 pixels, wide 1.9048:1 aspect ratio, opaque warm ivory background #fffaf0 all the way to the edges.
Reference image 1 (clay-accounts.webp): the existing project artwork. Preserve its recognizable smooth premium 3D account tiles: lavender, peach and mint profile cards, a dark teal terminal tile with the ivory >_ symbol, pink arch and ochre sphere. Recompose this complete illustration cleanly on the RIGHT, contained inside the canvas without cropping the important shapes. Remove any stray specks, ghost pixels or cutout artifacts. Soft natural studio shadows only.
Reference image 2 (app-icon.png): the user-supplied app logo. Reproduce the blue/violet terminal emblem faithfully as a small crisp logo next to the project name in the upper LEFT.
Overall design: premium, restrained editorial technology branding matching the existing cream-and-black website. Flat clean background, crisp black modern sans-serif typography similar to Inter, balanced spacing, no heavy gradients, no mock browser chrome.
Composition: safe margins around 58 px. Left text region approximately 55% width, illustration on the right 45%. Small logo and name near upper-left. Large two-line headline below, vertically balanced with the illustration. Keep all text comfortably inside the crop-safe area and fully legible at a small thumbnail size.
Text, EXACTLY and ONLY:
"CodexSubsManager"
"Many accounts."
"One workspace."
"Your Codex accounts, organized."
"codex.rakibhq.xyz"
Headline must show "Many accounts." on its own line and "One workspace." on the next line. One subtle lavender curved underline beneath "workspace." mirrors the website. Project name in smaller semibold type, headline in large elegant medium-weight type, supporting sentence and domain in restrained smaller type.
Constraints: exact spelling and capitalization; English text only; no added claims; no OpenAI logo; no buttons; no badges; no extra labels; no watermark; no decorative border; no duplicated elements; no checkerboard or transparent background.

## Hosting

Import the repository into Vercel and leave its root directory at the repository root. The root `vercel.json` points to this folder. Configuration follows [Vercel's file-based project configuration](https://vercel.com/docs/project-configuration).

The example account switcher is an illustrative browser-only demo. It never authenticates, reads, switches, or stores real Codex accounts. The FAQs use native HTML disclosure controls; the mobile navigation and demo remain keyboard accessible, and motion respects the visitor's reduced-motion preference.
