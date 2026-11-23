# Visual assets

All five raster assets were generated with the built-in imagegen tool specifically for Dedsec Tests. Final packaged PNGs are in `src/Dedsec.Tests/Assets/` (app, cpu, gpu, ram, disk); ICO is generated in multiple sizes. `server/web/icon.png`, `docs/assets/icon.png` and installer bitmap artwork derive from the app identity. Packaging resizes/encodes assets without changing their design. Navigation uses original WPF vector geometry and site feature icons use inline SVG, so no icon font or missing-glyph squares are required.

## Exact generation prompts

### Main app identity

Use case: logo-brand. Create a premium Windows app icon for Dedsec Tests, a hardware diagnostics and benchmark tool in hacker/cyberpunk Dedseckms style. Single centered geometric black titanium microchip shaped like a shield with a sharp stylized D and pulse/check mark cutout, bright acid lime green illuminated edges and subtle cyan highlights. Symmetrical clean silhouette, dark charcoal rounded-square background, ample padding, highly legible at 32 pixels. Professional original identity, no skulls, no lettering, no watermark. Square 1024x1024. This will be the main desktop executable icon and website identity.

### CPU

Use case: stylized-concept. Asset type: dedicated hardware category icon for Dedsec Tests Windows diagnostics app. Single microprocessor with detailed contact pins and etched circuitry, isolated centered three-quarter isometric view, black titanium material with acid lime green edge illumination and tiny restrained cyan accents. Premium cyberpunk hardware industrial design, coherent icon family, clearly recognizable silhouette, crisp readable details at 96 pixels, generous padding. Dark charcoal square backdrop, no frame, no text, no watermark. Square image.

### GPU

Use case: stylized-concept. Asset type: dedicated hardware category icon for Dedsec Tests Windows diagnostics app. Single graphics card with two precisely detailed cooling fans and PCIe contacts, isolated centered three-quarter isometric view, black titanium material with acid lime green edge illumination and tiny restrained cyan accents. Premium cyberpunk hardware industrial design, coherent icon family, clearly recognizable silhouette, crisp readable details at 96 pixels, generous padding. Dark charcoal square backdrop, no frame, no text, no watermark. Square image.

### RAM

Use case: stylized-concept. Asset type: dedicated hardware category icon for Dedsec Tests Windows diagnostics app. Single RAM DIMM module with four memory chips and clean gold contact fingers, isolated centered three-quarter isometric view, black titanium material with acid lime green edge illumination and tiny restrained cyan accents. Premium cyberpunk hardware industrial design, coherent icon family, clearly recognizable silhouette, crisp readable details at 96 pixels, generous padding. Dark charcoal square backdrop, no frame, no text, no watermark. Square image.

### DISK

Use case: stylized-concept. Asset type: dedicated hardware category icon for Dedsec Tests Windows diagnostics app. Single solid state drive with a luminous circular storage platter motif and fine circuit traces, isolated centered three-quarter isometric view, black titanium material with acid lime green edge illumination and tiny restrained cyan accents. Premium cyberpunk hardware industrial design, coherent icon family, clearly recognizable silhouette, crisp readable details at 96 pixels, generous padding. Dark charcoal square backdrop, no frame, no text, no watermark. Square image.

Installer artwork: `scripts/Prepare-Installer-Art.ps1`. ICO packaging: `scripts/Prepare-Icons.ps1`. Generated original PNGs remain in the local imagegen output directory; the project ships optimized 256px versions.
