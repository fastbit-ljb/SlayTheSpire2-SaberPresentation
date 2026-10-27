using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace SaberPresentation;

// The caller owns the clock and lifetime. Only the narrow blade section is
// painted; the native sword, hands, and character materials remain intact.
internal sealed class SwordChargeGlow
{
    internal const float BladeStart = 56f;
    internal const float BladeEnd = 206.7f;
    internal const float MaximumWidth = 20f;
    private const float MinimumWidth = 14f;
    // nv27's painted blade is offset from its bone axis. These positions are
    // sampled from the SHA-locked native mesh, with Spine Y converted to Godot.
    internal static readonly Vector2 BladeStartPoint = new(BladeStart, -3.2f);
    internal static readonly Vector2 BladeEndPoint = new(BladeEnd, -5.64f);
    private static readonly Vector2 BladeAxis = BladeEndPoint - BladeStartPoint;
    private static Shader? _shader;
    private readonly NCreature _actor;
    private readonly Sprite2D _blade;
    private readonly ShaderMaterial _material;
    private readonly Vector2 _frameSize;
    private bool _cleared;

    internal Node2D Root { get; }
    internal float Progress { get; private set; }
    internal float Strength { get; private set; }
    internal bool Exists => !_cleared && Valid(Root);
    internal bool Visible => Exists && Root.IsVisibleInTree();
    internal Transform2D BladeTransform => Exists ? Root.GlobalTransform : Transform2D.Identity;

    // Compare the actual painted sprite endpoints to the native mesh's blade
    // centerline, detecting both temporal lag and a misplaced/rotated child.
    internal float AlignmentError
    {
        get
        {
            if (!Exists || !GodotObject.IsInstanceValid(_blade) || !TryBlade(out var blade)) return float.PositiveInfinity;
            Transform2D painted = _blade.GlobalTransform;
            Vector2 paintedStart = painted * new Vector2(-_frameSize.X * 0.5f, 0);
            Vector2 paintedEnd = painted * new Vector2(_frameSize.X * 0.5f, 0);
            return Math.Max(paintedStart.DistanceTo(blade * BladeStartPoint),
                paintedEnd.DistanceTo(blade * BladeEndPoint));
        }
    }
    internal bool BladeAligned => AlignmentError <= 0.5f;

    internal SwordChargeGlow(NCreature actor, Node parent)
    {
        _actor = actor;
        var texture = Art.Frame(2, 0);
        _frameSize = texture.GetSize();
        _material = new ShaderMaterial { Shader = _shader ??= new Shader { Code = ShaderCode } };
        _material.SetShaderParameter("frame_size", _frameSize);
        _material.SetShaderParameter("progress", 0f);
        _material.SetShaderParameter("strength", 0f);
        Root = new Node2D { Name = "SaberSwordChargeGlow", Visible = false, ZIndex = 1 };
        _blade = new Sprite2D
        {
            Name = "SaberGoldenBlade",
            Texture = texture,
            Position = (BladeStartPoint + BladeEndPoint) * 0.5f,
            Rotation = BladeAxis.Angle(),
            Scale = new Vector2(BladeAxis.Length() / _frameSize.X, MinimumWidth / _frameSize.Y),
            Material = _material,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear
        };
        parent.AddChild(Root);
        Root.AddChild(_blade);
    }

    internal void Update(float progress)
    {
        if (_cleared || !GodotObject.IsInstanceValid(Root)) return;
        Progress = float.IsFinite(progress) ? Math.Clamp(progress, 0f, 1f) : 0f;
        float gathered = Progress * Progress * (3f - 2f * Progress);
        Strength = 0.68f * gathered;
        _material.SetShaderParameter("progress", Progress);
        _material.SetShaderParameter("strength", Strength);
        _blade.Scale = new Vector2(BladeAxis.Length() / _frameSize.X,
            (MinimumWidth + (MaximumWidth - MinimumWidth) * gathered) / _frameSize.Y);
        if (!Valid(Root) || !TryBlade(out var blade))
        {
            Root.Hide();
            return;
        }
        // The native transform includes sprite reflection, scale and parent
        // staging. No viewport-specific positions or independent rotation are
        // needed as the actor raises the sword.
        Root.GlobalTransform = blade;
        Root.Visible = Strength > 0.0001f && _actor.IsVisibleInTree();
    }

    internal void Clear()
    {
        if (_cleared) return;
        _cleared = true;
        Progress = Strength = 0f;
        if (!GodotObject.IsInstanceValid(Root)) return;
        Root.Hide();
        Root.QueueFree();
    }

    private bool TryBlade(out Transform2D transform)
    {
        transform = Transform2D.Identity;
        if (!Valid(_actor) || _actor.Visuals?.SpineBody?.GetGlobalBoneTransform("Ys_wuqi1") is not { } blade
            || blade.X.LengthSquared() < 0.000001f || blade.Y.LengthSquared() < 0.000001f) return false;
        transform = blade;
        return true;
    }

    private static bool Valid(Node? node) => node != null && GodotObject.IsInstanceValid(node) && node.IsInsideTree();

    private const string ShaderCode = """
        shader_type canvas_item;
        render_mode blend_mix, unshaded;

        uniform vec2 frame_size = vec2(1.0);
        uniform float progress : hint_range(0.0, 1.0) = 0.0;
        uniform float strength : hint_range(0.0, 1.0) = 0.0;
        varying vec2 blade_uv;

        void vertex() {
            // AtlasTexture's sampling UV refers to the full atlas. Geometry
            // supplies a separate 0..1 coordinate for this single beam cell.
            blade_uv = VERTEX / frame_size + vec2(0.5);
        }

        void fragment() {
            vec4 painted = texture(TEXTURE, UV);
            float front = mix(-0.08, 1.08, progress);
            float gathered = 1.0 - smoothstep(front - 0.08, front + 0.08, blade_uv.x);
            float ends = smoothstep(0.0, 0.025, blade_uv.x)
                * (1.0 - smoothstep(0.96, 1.0, blade_uv.x));
            // Addition leaves a white native blade white. A translucent warm
            // surface can tint it while retaining the original blade detail.
            // The mask is narrow and tapers at the real tip, rather than
            // extending the beam cell into a rectangular column of light.
            float tip_width = max(0.001, 1.0 - smoothstep(0.87, 1.0, blade_uv.x));
            float radial = abs(blade_uv.y * 2.0 - 1.0) / tip_width;
            float silhouette = 1.0 - smoothstep(0.70, 1.0, radial);
            float gold_edge = smoothstep(0.16, 0.72, radial);
            vec3 core = vec3(1.0, 0.97, 0.82);
            vec3 rim = mix(vec3(1.0, 0.80, 0.30), vec3(1.0, 0.64, 0.08), progress);
            // The atlas supplies only fine variation: its smoky transparent
            // margins must not remove the continuous gold along the sword.
            float detail = mix(0.94, 1.0, painted.r * painted.a);
            vec3 light = mix(core, rim, gold_edge) * detail;
            COLOR = vec4(light, gathered * ends * silhouette * strength);
        }
        """;
}
