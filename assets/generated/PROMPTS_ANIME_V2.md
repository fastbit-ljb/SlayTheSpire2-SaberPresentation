# Anime portrait revision — 0.3.3

Generator: built-in `image_gen.imagegen`, transparent-background edit mode.
The user selected the anime-v1 design and explicitly rejected the realistic character-select animation. Old sources remain unchanged.

The approved original portraits are **1024×1536**; the final wide action outputs are **1536×1024**. These are the actual native generation sizes. They are not 4K assets and were not enlarged. The 5457×5009 fan-art reference was used for illustration details, not packed as a game asset. V2 portraits/actions below are rejected exploration, excluded from the final PCK. The final runtime uses the approved `saber_ready_attack` / `saber_ready_cast` pair and V4 actions.

Reference: https://www.zerochan.net/4375855 — indexed as Tsukudani (Ore624). Source URL, image hash and dimensions are recorded in `provenance.json`.

## saber_ready_attack_anime_v2

Use case: identity-preserve. Asset: animated lower-left Saber cutout for a card-battle game. Image 1 is the USER-APPROVED character identity, outfit, raised-sword pose and framing; preserve these. Image 2 is Saber fan artwork, used ONLY as a reference for delicate hand-drawn anime detailing, elegant face rendering, flowing fabric and polished illustrated armor highlights. Produce an original refined HD redraw of image 1 with the beautiful 2D ANIME craftsmanship visible in image 2, without copying its composition, crown or fur cloak. Keep the precise youthful anime face, emerald eyes, blonde braided bun and ahoge, blue ribbons, blue and white armored dress with gold trim, and Excalibur raised overhead in both hands as in image 1. Sharply defined fine ink lines, exquisite cel-shaded silver plate motifs, detailed gold embroidery, translucent golden blade light, richly drawn flowing blue skirt. No realistic human face, no pores, no 3D, no western gritty realism, no oil-painting treatment. Full body with entire sword and feet inside portrait frame and small padding. Transparent alpha background with clean edges, no scenery, gradients, text, logos or UI. Highest supported portrait resolution; newly drawn fine details rather than pixel enlargement.

## saber_ready_cast_anime_v2

Use case: identity-preserve. Produce the SKILL-READY matching portrait for a 2D anime Saber game mod. Image 1 is exact required full-body stance and framing: Excalibur vertical downward in left hand, right armored hand extended toward the viewer, composed in a portrait canvas. Image 2 is the newly finished ATTACK portrait and is the exact required character identity and illustration treatment. Match its youthful expressive emerald-eyed anime face, blonde braid/bun and ahoge, cobalt blue ribbons, fine gold embroidery and filigree on silver armor, blue-and-white armored dress, exquisite detailed cel shading and clean Japanese anime linework. Image 3 is Saber fan artwork used only for delicate anime craftsmanship, flowing cloth and soft illustrated armor highlights; do not add its crown or fur cloak. This is not realism: no human photoreal face, pores, 3D or gritty painting. Preserve image 1's pose while matching image 2's face, costume details and visual style closely. Full figure, entire sword and fingers inside portrait frame, small padding. Genuinely transparent alpha background with clean edges and subtle blade glow, no scenery, background, text, watermark or UI. Highest supported native portrait resolution, redraw fine detail rather than enlarge pixels.

## Wide action framing repair

The first portrait-shaped action drafts clipped the blade. They were rejected and not used in the game. The following shared prompt repaired composition before installation:

Fix the input illustration's cropped sword by RECOMPOSING into a WIDE LANDSCAPE 3:2 canvas, 1536x1024 native or higher. This is a precise framing repair for a game sprite, preserve the same anime face, hair, blue-white dress, ornate armor, gloves and drawing detail. The ENTIRE Excalibur sword from pommel through crossguard to pointed blade tip MUST be visible inside the frame with at least 120 pixels of transparent padding to its RIGHT. Explicitly reserve the RIGHT THIRD for the distal blade and its complete pointed tip. The sword must retain a plausible long full blade, do not shorten it. Recompose the character to the LEFT HALF. Show a large detailed upper body down to hips/upper skirt rather than the legs; no full-body requirement. Waist can extend below the bottom edge as this is a lower-screen combat cut-in. Keep the head, both hands, sword and ribbons completely intact inside frame. True transparent alpha background, no scenery, no text, no UI, no realistic face, 2D Japanese anime only. Do not crop any part of the weapon or its glow.

## saber_slash_anime_v2

Shared framing repair plus: Both hands thrust the sword horizontally to the RIGHT at shoulder/chest height. Place crossguard just LEFT of image center, allowing the full distal blade to reach a clearly visible point around 88% of image width.

## saber_recover_anime_v2

Shared framing repair plus: Both hands lower the sword diagonally down toward RIGHT, after the slash. Keep the crossguard around image center and blade slope shallow enough that the entire blade's clearly pointed tip ends around 86% width and 86% height with generous transparent padding on both right and bottom.

Generation preserved the full pointed blade; the engine additionally positions both tips inside the viewport with a 25-pixel inset. Measured game screenshots are the final framing check.

## saber_slash_anime_v4 / saber_recover_anime_v4

V3 first removed the ornate V2 patterns and matched the original blue-white costume. V4 is a color-only image edit of each V3 action using the original approved portrait as the color master and the user's game screenshot as a second reference:

EDIT ONLY COLOR, lighting and material finish of IMAGE 2; preserve exact anatomy, pose, linework, costume, composition and complete blade. IMAGE 1 is the original color master: pale ivory skin, soft champagne blonde (not orange), emerald eyes, cool silver white with blue-lavender shadows, deep royal blue. IMAGE 3 is the approved game screenshot. Remove the amber/yellow cast, orange hair, yellow skin, warm silver and broad golden fog/backdrop. Sword has a small local white core, thin pale gold edge and faint narrow glow. True alpha. No added patterns, realism, text or logos.

Generation files retained under versioned names; original approved portraits were not recolored or overwritten. The in-game mesh and source-sized textures supply movement; no raster upscaling or pixel editing was applied.
