# Saber continuous portrait cutout rig — 0.3.4

Generator: built-in `image_gen.imagegen`, transparent edit mode. Reference for all three edits: the original user-approved `saber_ready_attack.png`. Outputs are native 1024×1536 RGBA. These are separate simultaneously visible rig layers, never sequential animation frames.

Saved workspace assets:

- `D:/L/Study/mod/SaberPresentation/assets/generated/saber_rig_body_v1.png`
- `D:/L/Study/mod/SaberPresentation/assets/generated/saber_rig_head_v1.png`
- `D:/L/Study/mod/SaberPresentation/assets/generated/saber_rig_arms_v1.png`

## saber_rig_body_v1

EDIT the supplied approved Saber illustration into a BODY UNDERPAINT LAYER for skeletal animation. Keep the same 1024×1536 portrait canvas, body placement, costume, detailed Japanese anime linework, cool silver armor and royal blue-white fabric. Remove the entire head, hair, neck, ribbons, both arms/hands and sword. Preserve torso, waist, skirt, hip plates and legs. Reconstruct the previously hidden upper chest and breastplate under the lifted arms as continuous torso with small shoulder roots. No redesign, extra decoration, realism or lighting fog. True transparent alpha.

## saber_rig_arms_v1

Extract only the two raised arms and complete sword as one connected transparent cutout. Preserve original hands, grip, silver gauntlets and blue puffed sleeves. Preserve the entire Excalibur including pommel and full pointed tip. Remove all head, hair, ribbons, neck, chest, waist, skirt and legs. Fill formerly occluded sleeve/gauntlet gaps, retain joint overlap. Match original anime lines and cool silver / royal blue colors. No additional pose or body. True alpha, no broad lighting haze.

## saber_rig_head_v1

Extract only the original head, hair, neck and blue ribbons. Same anime face, emerald eyes, pale skin, blonde braid and ahoge. Reconstruct hair tips and small neck areas hidden by the foreground arm, without changing identity, expression or lighting. Remove all other body parts. True alpha, no diffuse haze, text or extra pieces.

Generation did not exactly retain source pixel registration. Mesh bind geometry registers the three cutouts into one character, without editing raster pixels. Original textures remain at native size. Native Bone2D hierarchy, weights and keyframes are defined in `PortraitRig.cs`.
