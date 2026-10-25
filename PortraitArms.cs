using Godot;

namespace SaberPresentation;

// Rigid overlapping limb cutouts, two independent elbow chains, one shared
// two-hand grip. Metal plates and fingers are never mesh-warped.
internal sealed class PortraitArms
{
    private readonly Bone2D _frontUpper, _frontLower, _frontWrist, _rearUpper, _rearLower, _rearWrist, _grip;
    private readonly List<Polygon2D> _pieces = [];
    private readonly Vector2 _frontOffset, _rearOffset, _swordTip;
    private readonly float _frontBind, _rearBind;
    private const float FrontUpperLength = 190, FrontLowerLength = 180, RearUpperLength = 150, RearLowerLength = 170;
    internal bool Ready => _pieces.Count == 5;
    internal string Signature => string.Join(",", _pieces.Select(p => p.Texture.GetInstanceId()));
    internal float ReachError { get; private set; }
    internal float WristDeviation { get; private set; }
    internal Vector2 SwordTip => _grip.ToGlobal(_swordTip);

    internal PortraitArms(Dictionary<string, Bone2D> bones)
    {
        _frontUpper = bones["Shoulder"]; _frontLower = bones["Elbow"]; _frontWrist = bones["Wrist"];
        _rearUpper = bones["RearShoulder"]; _rearLower = bones["RearElbow"]; _rearWrist = bones["RearWrist"]; _grip = bones["Grip"];
        var atlas = Art.Texture("saber_arm_parts_v1");
        Piece(atlas, _frontUpper, new(170, 175), new(352, 455), FrontUpperLength, 3,
            [new(15, 35), new(310, 35), new(345, 300), new(455, 390), new(455, 535), new(240, 535), new(20, 290)]);
        Piece(atlas, _rearUpper, new(780, 185), new(640, 470), RearUpperLength, -1,
            [new(640, 70), new(915, 70), new(920, 330), new(735, 535), new(535, 535), new(530, 390), new(615, 190)]);
        Piece(atlas, _frontLower, new(130, 600), new(350, 928), FrontLowerLength, 4,
            [new(25, 530), new(240, 530), new(430, 865), new(450, 1015), new(265, 1020), new(30, 730)]);
        Piece(atlas, _rearLower, new(710, 600), new(555, 935), RearLowerLength, 1,
            [new(595, 535), new(810, 535), new(815, 710), new(675, 1015), new(475, 1015), new(475, 865)]);
        float swordRotation = Mathf.DegToRad(31.6f), swordScale = .55f;
        Vector2 gripPixel = new(900, 850);
        Vector2 Map(Vector2 p) => ((p - gripPixel) * swordScale).Rotated(swordRotation);
        var outline = new Vector2[] { new(1450, 0), new(1536, 0), new(1536, 210), new(1250, 785),
            new(1135, 990), new(940, 1024), new(710, 1024), new(720, 900), new(890, 700), new(900, 540) };
        var hands = new Polygon2D { Name = "SaberFixedGripAndBlade", Texture = atlas, Polygon = outline.Select(Map).ToArray(),
            UV = outline, ZIndex = 5, TextureFilter = CanvasItem.TextureFilterEnum.Linear };
        _grip.AddChild(hands); _pieces.Add(hands);
        _frontOffset = Map(new(914, 960)); _rearOffset = Map(new(1032, 895)); _swordTip = Map(new(1515, 16));
        _frontLower.Position = new Vector2(FrontUpperLength, 0); _frontWrist.Position = new Vector2(FrontLowerLength, 0);
        _rearLower.Position = new Vector2(RearUpperLength, 0); _rearWrist.Position = new Vector2(RearLowerLength, 0);
        // Establish the original raised guard from real joint lengths.
        Solve();
        _frontBind = _frontLower.GlobalRotation - _grip.GlobalRotation;
        _rearBind = _rearLower.GlobalRotation - _grip.GlobalRotation;
        foreach (var bone in new[] { _frontUpper, _frontLower, _frontWrist, _rearUpper, _rearLower, _rearWrist }) bone.Rest = bone.Transform;
    }

    private void Piece(Texture2D atlas, Bone2D bone, Vector2 near, Vector2 far, float length, int z, Vector2[] outline)
    {
        Vector2 axis = far - near;
        var polygon = new Polygon2D { Texture = atlas,
            Polygon = outline.Select(p => ((p - near) * (length / axis.Length())).Rotated(-axis.Angle())).ToArray(),
            UV = outline, ZIndex = z, TextureFilter = CanvasItem.TextureFilterEnum.Linear };
        bone.AddChild(polygon); _pieces.Add(polygon);
    }

    private static void Chain(Bone2D upper, Bone2D lower, float first, float second, Vector2 target, float bend)
    {
        var parent = (Node2D)upper.GetParent();
        Vector2 delta = parent.ToLocal(target) - upper.Position;
        float distance = Math.Clamp(delta.Length(), Math.Abs(first - second) + .5f, first + second - .5f);
        float arc = Mathf.Acos(Math.Clamp((first * first + distance * distance - second * second) / (2 * first * distance), -1, 1));
        upper.Rotation = delta.Angle() + bend * arc;
        Vector2 elbow = upper.Position + Vector2.FromAngle(upper.Rotation) * first;
        lower.Rotation = (parent.ToLocal(target) - elbow).Angle() - upper.Rotation;
    }

    internal void Solve()
    {
        var frontTarget = _grip.ToGlobal(_frontOffset); var rearTarget = _grip.ToGlobal(_rearOffset);
        Chain(_frontUpper, _frontLower, FrontUpperLength, FrontLowerLength, frontTarget, -1);
        Chain(_rearUpper, _rearLower, RearUpperLength, RearLowerLength, rearTarget, 1);
        ReachError = Math.Max(_frontWrist.GlobalPosition.DistanceTo(frontTarget), _rearWrist.GlobalPosition.DistanceTo(rearTarget));
        WristDeviation = Math.Max(Math.Abs(Mathf.RadToDeg(Mathf.AngleDifference(_frontBind, _frontLower.GlobalRotation - _grip.GlobalRotation))),
            Math.Abs(Mathf.RadToDeg(Mathf.AngleDifference(_rearBind, _rearLower.GlobalRotation - _grip.GlobalRotation))));
    }
    internal void SetVisible(bool visible) { foreach (var piece in _pieces) piece.Visible = visible; }
}
