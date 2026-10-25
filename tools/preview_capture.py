"""Assemble captured game frames; no generated/fabricated gameplay footage."""
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "build/verification-standalone"


def main():
    sequence = [(SOURCE / "hover_attack.png", 650)]
    sequence += [(p, 90) for p in sorted(SOURCE.glob("hover_frame_*.png"))[:10]]
    sequence += [(SOURCE / "hover_skill.png", 500)]
    sequence += [(p, 90) for p in sorted(SOURCE.glob("hover_frame_*.png"))[10:]]
    sequence += [(SOURCE / "hover_cancelled.png", 450)]
    sequence += [(SOURCE / f"portrait_attack_frame_{i}.png", 180) for i in range(3)]
    sequence += [(p, 90) for p in sorted(SOURCE.glob("finisher_frame_*.png"))]
    sequence += [(SOURCE / "killing_blow_excalibur.png", 450)]
    frames = [Image.open(path).convert("RGB").resize((960, 540)).quantize(colors=192) for path, _ in sequence]
    out = ROOT / "dist/SaberPresentation-0.3.0-preview.gif"
    frames[0].save(out, save_all=True, append_images=frames[1:], duration=[delay for _, delay in sequence], loop=0, optimize=False, disposal=2)
    print(f"Actual gameplay preview: {len(frames)} captured frames; {out.stat().st_size:,} bytes")


if __name__ == "__main__":
    main()
