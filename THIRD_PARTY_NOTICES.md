# Third party notices

`src/CodexAccounts.Core/ShutdownCoordinator.cs` is adapted from ZOONGG/codex-swap-account revision `e0998f5991ef28fedaccdff7d455ff493b659a38`.

Copyright (c) 2026 Codex Profile Overlay contributors. Used under the MIT license, reproduced in `licenses/CodexProfileOverlay-MIT.txt`.

The standalone executable bundles Microsoft .NET and WPF runtime components. Application, adapted-source, and Microsoft license notices are embedded in the executable and accessible through **About Codex Accounts > Software licenses**. Readable copies are available in the repository's `licenses` directory and separate build outputs; the release ZIP contains only the EXE.

Cockpit Tools, CodexBar, codex-auth, and Relay informed the source research and design. Their source is not bundled into the product beyond the specifically attributed Windows shutdown coordinator above.

Codex Accounts is an independent companion and is not an official OpenAI product. Codex and OpenAI belong to their respective trademark owners.

The application logo in `Assets/Brand.png` and its Windows icon derivatives was supplied by the user for this build.

Supporting interface icons are locally bundled from [Lucide](https://github.com/lucide-icons/lucide), revision `dab294f313897bacaa968b725b3259fa37c17bd1`, under the ISC license and the included MIT notice for Feather-derived icons. Copyright (c) 2026 Lucide Icons and Contributors; copyright (c) 2013-present Cole Bemis for the listed Feather-derived artwork. The complete notices are in `licenses/Lucide-ISC.txt` and embedded under Software licenses. The app does not fetch icons or fonts at runtime. `Glyph.cs` converts the embedded SVG geometry to native WPF drawing commands.

The project website uses Inter under the SIL Open Font License. Its notice is in `website/assets/Inter-OFL.txt`. The website's original illustrations and social preview were generated for this project. The website also includes a copy of the Lucide notices in `website/assets/Lucide-LICENSE.txt`.
