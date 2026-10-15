# Project website

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

### Generation prompt

Use case: stylized-concept. Asset type: original transparent hero illustration for CodexSubsManager, a small Windows account-switching desktop companion. Create a polished hand-sculpted claymation still-life: three chunky rounded account tiles stacked and fanned, one lavender, one peach, one mint, each with a simple recessed circular avatar and two short raised lines. In front, a larger deep-teal rounded terminal block with a cream raised >_ symbol. Small ochre ball and soft pink clay arch behind the stack. Orthographic three-quarter view, cohesive compact composition, gentle studio light from upper left, subtle material imperfections, matte tactile clay, soft ambient occlusion. Palette strictly warm cream #fffaf0, lavender #b8a4ed, peach #ffb084, mint #a4d4c5, pink #ff4d8b, ochre #e8b94a, deep teal #1a3a3a. Objects centered with breathing room on all sides and no cropping. Genuine transparent background. No typography other than >_, no branding, no watermark, no interface screenshot, no glossy plastic.

### Refinement prompt

Refine this exact transparent 3D hero artwork to feel smoother and more premium. Preserve the same composition, three account tiles, recessed avatar forms, terminal block with raised >\_ glyph, pink arch, ochre ball, colors and object arrangement. Replace the rough porous/grainy clay surface with finely finished, velvety smooth matte clay: clean continuous surfaces, beautifully rounded edges, subtle soft satin highlights, no visible pits or grain. Use refined large-softbox studio lighting, softer ambient occlusion, gentle contact shadows and graceful light gradients. Keep depth and the tactile sculpted character; avoid harsh gloss or plastic glare. Keep true background transparency and uncropped silhouettes. Do not add text, branding, shapes or objects.

## Hosting

Import the repository into Vercel and leave its root directory at the repository root. The root `vercel.json` points to this folder. Configuration follows [Vercel's file-based project configuration](https://vercel.com/docs/project-configuration).

The example account switcher is an illustrative browser-only demo. It never authenticates, reads, switches, or stores real Codex accounts. The FAQs use native HTML disclosure controls; the mobile navigation and demo remain keyboard accessible, and motion respects the visitor's reduced-motion preference.
