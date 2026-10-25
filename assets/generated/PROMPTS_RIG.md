# Saber continuous portrait cutout rig — 0.3.5

Generator: built-in `image_gen.imagegen`, transparent edit mode. The first three edits reference the original user-approved `saber_ready_attack.png` and are native 1024×1536 RGBA. The replacement arm-parts atlas is 1536×1024 RGBA. These are separate simultaneously visible rig layers, never sequential animation frames.

Saved workspace assets:

- `D:/L/Study/mod/SaberPresentation/assets/generated/saber_rig_body_v1.png`
- `D:/L/Study/mod/SaberPresentation/assets/generated/saber_rig_head_v1.png`
- `D:/L/Study/mod/SaberPresentation/assets/generated/saber_rig_arms_v1.png`
- `D:/L/Study/mod/SaberPresentation/assets/generated/saber_arm_parts_v1.png`

## saber_rig_body_v1

EDIT the supplied approved Saber illustration into a BODY UNDERPAINT LAYER for skeletal animation. Keep the same 1024×1536 portrait canvas, body placement, costume, detailed Japanese anime linework, cool silver armor and royal blue-white fabric. Remove the entire head, hair, neck, ribbons, both arms/hands and sword. Preserve torso, waist, skirt, hip plates and legs. Reconstruct the previously hidden upper chest and breastplate under the lifted arms as continuous torso with small shoulder roots. No redesign, extra decoration, realism or lighting fog. True transparent alpha.

## saber_rig_arms_v1

Extract only the two raised arms and complete sword as one connected transparent cutout. Preserve original hands, grip, silver gauntlets and blue puffed sleeves. Preserve the entire Excalibur including pommel and full pointed tip. Remove all head, hair, ribbons, neck, chest, waist, skirt and legs. Fill formerly occluded sleeve/gauntlet gaps, retain joint overlap. Match original anime lines and cool silver / royal blue colors. No additional pose or body. True alpha, no broad lighting haze.

## saber_rig_head_v1

Extract only the original head, hair, neck and blue ribbons. Same anime face, emerald eyes, pale skin, blonde braid and ahoge. Reconstruct hair tips and small neck areas hidden by the foreground arm, without changing identity, expression or lighting. Remove all other body parts. True alpha, no diffuse haze, text or extra pieces.

Generation did not exactly retain source pixel registration. Mesh bind geometry registers the cutouts into one character, without editing raster pixels. Original textures remain at native size. Native Bone2D hierarchy, weights and keyframes are defined in `PortraitRig.cs` and `PortraitArms.cs`.

## saber_arm_parts_v1

Use case: background-extraction / animation cutout preparation. Edit target: the provided blue-and-silver Saber arms and complete Excalibur. Produce ONE clean transparent RGBA CUTOUT PARTS ATLAS for a professional 2D skeletal rig, landscape 1536x1024 if possible. This is NOT a sequence of character poses. Exactly FIVE separated pieces, generous transparent gutters: TOP LEFT, the larger FRONT UPPER ARM including the royal blue sleeve and round shoulder cap, reconstructed as a complete limb with rounded elbow underpaint; BOTTOM LEFT, the FRONT SILVER FOREARM/gauntlet WITHOUT ANY HAND, reconstructed with complete rounded wrist and elbow overlap surfaces; TOP CENTER, the smaller REAR UPPER ARM in the same blue fabric with full shoulder/elbow underpaint; BOTTOM CENTER, the smaller REAR SILVER FOREARM WITHOUT HAND, complete wrist/elbow overlap surfaces; RIGHT HALF, the COMPLETE sword plus BOTH armored hands rigidly gripping the original handle together, cut off cleanly at both wrists. Keep sword full length including tip, pommel, exact blue/gold guard and blade. Source style and cool colors must match exactly: sharp high-end Japanese anime lines, cool silver plates, royal blue fabric, no yellow light tint on armor. Each limb can be straightened slightly to expose full overlap areas, but preserve its original costume and proportions. Do not include torso, head, hair, legs, effects, shadows, scenery, text, labels, checkerboard or frames. No broad glow haze. Every piece must be cleanly isolated; none may touch another. Do not duplicate hands on the forearm pieces. Fill all formerly hidden joint edges so they overlap naturally when rotated. Genuine transparent alpha.

The older `saber_rig_arms_v1.png` remains as provenance only and is excluded from the PCK. Runtime uses the five cutouts above: two independent two-bone IK chains converge on one rigid two-hand grip and full sword.
