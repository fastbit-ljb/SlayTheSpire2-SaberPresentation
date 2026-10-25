using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace SaberPresentation;

// A single cutout character: native Bone2D transforms deform weighted meshes.
// The artwork/face/armor never changes during a card animation.
internal sealed class PortraitRig
{
    private readonly Node2D _root;
    private readonly Skeleton2D _skeleton;
    private readonly Dictionary<string, Bone2D> _bones = [];
    private readonly AnimationPlayer _main, _secondary;
    private readonly List<Polygon2D> _layers = [];
    private bool _playing;
    internal string Clip { get; private set; } = "b_idle";
    internal int RenderSamples { get; private set; }
    internal bool IsReady => _layers.Count == 3 && _skeleton.GetBoneCount() >= 20;
    internal string Source => _layers[0].Texture.ResourcePath;
    internal Vector2 Resolution => _layers[0].Texture.GetSize();
    internal int BoneCount => _skeleton.GetBoneCount();
    internal Vector2 MotionSample => _bones["RibbonTip"].GlobalPosition;
    internal Vector2 SwordTip => _bones["Wrist"].ToGlobal(Registered(2, new Vector2(995, 40)) - new Vector2(365, 314));
    internal string TextureSignature => string.Join(",", _layers.Select(p => p.Texture.GetInstanceId()));

    internal PortraitRig(Node2D parent)
    {
        _root = new Node2D { Name = "SaberPortraitRig", Position = new Vector2(-512, -1536) };
        parent.AddChild(_root);
        _skeleton = new Skeleton2D { Name = "Skeleton" };
        _root.AddChild(_skeleton);
        Bone("Root", null, new(470, 715));
        Bone("Torso", "Root", new(25, -160));
        Bone("Neck", "Torso", new(35, -70));
        Bone("Head", "Neck", new(10, -55));
        Bone("HairFlow", "Head", Vector2.Zero);
        Bone("HairLeft", "HairFlow", new(-80, -60));
        Bone("HairLeftTip", "HairLeft", new(-45, 110));
        Bone("HairRight", "HairFlow", new(65, -45));
        Bone("HairRightTip", "HairRight", new(55, 125));
        Bone("Bangs", "HairFlow", new(10, -65));
        Bone("Ahoge", "HairFlow", new(95, -165));
        Bone("Ribbon", "HairFlow", new(110, 15));
        Bone("RibbonTip", "Ribbon", new(105, 95));
        Bone("Shoulder", "Torso", new(105, -10));
        Bone("Elbow", "Shoulder", new(-180, -79));
        Bone("Wrist", "Elbow", new(-55, -152));
        Bone("SkirtLeft", "Root", new(-190, 170));
        Bone("SkirtLeftTip", "SkirtLeft", new(-40, 280));
        Bone("SkirtRight", "Root", new(220, 145));
        Bone("SkirtRightTip", "SkirtRight", new(140, 260));

        Layer("saber_rig_body_v1", 0, BodyWeights);
        Layer("saber_rig_head_v1", 1, HeadWeights);
        Layer("saber_rig_arms_v1", 2, ArmWeights);
        _main = Player("CharacterMotion");
        var library = new AnimationLibrary();
        library.AddAnimation("b_idle", Idle());
        library.AddAnimation("card_attack", Attack());
        library.AddAnimation("card_casting", Cast());
        _main.AddAnimationLibrary("saber", library);
        _main.AnimationFinished += _ => { _playing = false; IdlePose(); };
        _secondary = Player("HairAndClothMotion");
        var cloth = new AnimationLibrary();
        cloth.AddAnimation("wind", Secondary());
        _secondary.AddAnimationLibrary("saber", cloth);
        _secondary.Play("saber/wind");
        IdlePose();
    }

    private void Bone(string name, string? parent, Vector2 position)
    {
        var bone = new Bone2D { Name = name, Position = position };
        bone.SetAutocalculateLengthAndAngle(false);
        bone.SetLength(60);
        bone.Rest = bone.Transform;
        (parent == null ? (Node)_skeleton : _bones[parent]).AddChild(bone);
        _bones.Add(name, bone);
    }
    private AnimationPlayer Player(string name)
    {
        var player = new AnimationPlayer { Name = name, RootNode = new NodePath("..") };
        _root.AddChild(player);
        return player;
    }
    private static float Smooth(float a, float b, float v)
    {
        float x = Math.Clamp((v - a) / (b - a), 0, 1);
        return x * x * (3 - 2 * x);
    }

    private Dictionary<string, float> BodyWeights(Vector2 p)
    {
        float upper = 1 - Smooth(590, 780, p.Y);
        float cloth = Smooth(770, 920, p.Y) * (1 - Smooth(1200, 1450, p.Y));
        float left = 1 - Smooth(270, 500, p.X);
        float right = Smooth(530, 770, p.X);
        float tip = Smooth(940, 1200, p.Y);
        return new() {
            ["Torso"] = upper,
            ["Root"] = (1 - upper) * (1 - cloth * Math.Max(left, right)),
            [tip < 0.5f ? "SkirtLeft" : "SkirtLeftTip"] = cloth * left,
            [tip < 0.5f ? "SkirtRight" : "SkirtRightTip"] = cloth * right,
        };
    }
    private Dictionary<string, float> HeadWeights(Vector2 p)
    {
        float ribbon = Smooth(625, 720, p.X) * Smooth(420, 465, p.Y);
        float hairL = (1 - Smooth(440, 485, p.X)) * Smooth(350, 415, p.Y);
        float hairR = Smooth(610, 660, p.X) * Smooth(340, 400, p.Y);
        float ahoge = Smooth(600, 650, p.X) * (1 - Smooth(285, 310, p.Y));
        float bangs = Smooth(480, 510, p.X) * (1 - Smooth(600, 635, p.X))
            * Smooth(300, 320, p.Y) * (1 - Smooth(355, 375, p.Y)) * 0.35f;
        float tip = Smooth(430, 490, p.Y);
        float free = Math.Max(ahoge, Math.Max(bangs, Math.Max(ribbon, Math.Max(hairL, hairR))));
        return new() { ["Head"] = 1 - free, ["RibbonTip"] = ribbon,
            ["HairLeft"] = hairL * (1 - ribbon) * (1 - tip), ["HairLeftTip"] = hairL * (1 - ribbon) * tip,
            ["HairRight"] = hairR * (1 - ribbon) * (1 - tip), ["HairRightTip"] = hairR * (1 - ribbon) * tip,
            ["Bangs"] = bangs, ["Ahoge"] = ahoge };
    }
    private Dictionary<string, float> ArmWeights(Vector2 p)
    {
        // All blade/hand vertices share a rigid wrist; deformation is confined
        // to the sleeve/elbow overlap. No stretched sword or rubber fingers.
        float wrist = 1 - Smooth(345, 450, p.Y);
        float upper = Smooth(470, 535, p.Y);
        if (p.Y < 330) wrist = 1;
        return new() { ["Shoulder"] = upper, ["Elbow"] = (1 - upper) * (1 - wrist),
            ["Wrist"] = (1 - upper) * wrist };
    }

    private void Layer(string texture, int z, Func<Vector2, Dictionary<string, float>> weights)
    {
        int columns = z == 1 ? 64 : 32, rows = z == 1 ? 96 : 48;
        var points = new Vector2[(columns + 1) * (rows + 1)];
        var uv = new Vector2[points.Length];
        var boneWeights = _bones.Keys.ToDictionary(k => k, _ => new float[points.Length]);
        for (int y = 0; y <= rows; y++) for (int x = 0; x <= columns; x++)
        {
            int i = y * (columns + 1) + x;
            uv[i] = new Vector2(x * 1024f / columns, y * 1536f / rows);
            points[i] = Registered(z, uv[i]);
            var values = weights(points[i]).Where(p => p.Value > 0).OrderByDescending(p => p.Value).Take(4).ToDictionary(p => p.Key, p => p.Value);
            float sum = values.Values.Sum();
            foreach (var (bone, value) in values) boneWeights[bone][i] = value / Math.Max(sum, 0.0001f);
        }
        var faces = new Godot.Collections.Array<int[]>();
        for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
        {
            int i = y * (columns + 1) + x;
            faces.Add([i, i + 1, i + columns + 2, i + columns + 1]);
        }
        var mesh = new Polygon2D { Name = texture, Texture = Art.Texture(texture), Polygon = points,
            UV = uv, Polygons = (Godot.Collections.Array)faces, ZIndex = z,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear };
        _root.AddChild(mesh);
        mesh.Skeleton = mesh.GetPathTo(_skeleton);
        foreach (var (name, values) in boneWeights)
            if (values.Any(v => v > 0)) mesh.AddBone(_skeleton.GetPathTo(_bones[name]), values);
        _layers.Add(mesh);
    }

    private static Vector2 Registered(int layer, Vector2 source) => layer switch {
        0 => source + new Vector2(0, 75),
        1 => source * 0.55f + new Vector2(275, 49),
        _ => (source - new Vector2(520, 665)) * 0.69f + new Vector2(600, 545)
    };

    private Animation New(float length, bool loop = false) => new() { Length = length,
        LoopMode = loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None };
    private void Rotate(Animation animation, string bone, params (float t, float degrees)[] keys)
    {
        int track = animation.AddTrack(Animation.TrackType.Value);
        animation.TrackSetPath(track, new NodePath(_root.GetPathTo(_bones[bone]) + ":rotation"));
        animation.TrackSetInterpolationType(track, Animation.InterpolationType.Cubic);
        foreach (var (t, angle) in keys) animation.TrackInsertKey(track, t, Mathf.DegToRad(angle));
    }
    private void Move(Animation animation, string bone, params (float t, float x, float y)[] keys)
    {
        int track = animation.AddTrack(Animation.TrackType.Value);
        animation.TrackSetPath(track, new NodePath(_root.GetPathTo(_bones[bone]) + ":position"));
        animation.TrackSetInterpolationType(track, Animation.InterpolationType.Cubic);
        var rest = _bones[bone].Rest.Origin;
        foreach (var (t, x, y) in keys) animation.TrackInsertKey(track, t, rest + new Vector2(x, y));
    }
    private Animation Idle()
    {
        var a = New(3.2f, true);
        Rotate(a, "Torso", (0, -0.8f), (1.6f, 0.8f), (3.2f, -0.8f));
        Rotate(a, "Neck", (0, 0.3f), (1.6f, -0.4f), (3.2f, 0.3f));
        Rotate(a, "Head", (0, -0.4f), (1.6f, 0.6f), (3.2f, -0.4f));
        Rotate(a, "Shoulder", (0, -1.3f), (1.6f, 1.5f), (3.2f, -1.3f));
        Rotate(a, "Wrist", (0, 0.6f), (1.6f, -0.5f), (3.2f, 0.6f));
        Rotate(a, "Elbow", (0, 0), (3.2f, 0));
        Move(a, "Elbow", (0, 0, 0), (3.2f, 0, 0));
        Move(a, "Wrist", (0, 0, 0), (3.2f, 0, 0));
        Move(a, "Root", (0, 0, 2), (1.6f, 0, -4), (3.2f, 0, 2));
        Rotate(a, "HairFlow", (0, 0), (3.2f, 0));
        return a;
    }
    private Animation Attack()
    {
        var a = New(1.14f);
        Rotate(a, "Torso", (0, 0), (.2f, -3), (.43f, 4), (.62f, 5), (.86f, 2), (1.14f, 0));
        Rotate(a, "Neck", (0, 0), (.2f, 2), (.46f, -4), (.72f, -2), (1.14f, 0));
        Rotate(a, "Head", (0, 0), (.24f, -2), (.48f, -3), (.8f, 1), (1.14f, 0));
        Rotate(a, "Shoulder", (0, 0), (.2f, -6), (.28f, -6), (.43f, 18), (.62f, 20), (.86f, 6), (1.14f, 0));
        Rotate(a, "Elbow", (0, 0), (.2f, -2), (.43f, 3), (.62f, 2), (.86f, -1), (1.14f, 0));
        Rotate(a, "Wrist", (0, 0), (.22f, -8), (.28f, -7), (.43f, 1), (.62f, -2), (.86f, -1), (1.14f, 0));
        Move(a, "Elbow", (0, 0, 0), (1.14f, 0, 0));
        Move(a, "Wrist", (0, 0, 0), (1.14f, 0, 0));
        Move(a, "Root", (0, 0, 0), (.22f, -12, 14), (.43f, 36, 3), (.62f, 40, 5), (.86f, 12, 2), (1.14f, 0, 0));
        Rotate(a, "HairFlow", (0, 0), (.24f, 3), (.50f, -6), (.72f, -3), (.93f, 2), (1.14f, 0));
        return a;
    }
    private Animation Cast()
    {
        var a = New(1.22f);
        Rotate(a, "Torso", (0, 0), (.35f, -3), (.68f, 2), (1.22f, 0));
        Rotate(a, "Neck", (0, 0), (.35f, 1), (.7f, -2), (1.22f, 0));
        Rotate(a, "Head", (0, 0), (.38f, -2.5f), (.72f, -1), (1.22f, 0));
        Rotate(a, "Shoulder", (0, 0), (.34f, -8), (.68f, 7), (.94f, 3), (1.22f, 0));
        Rotate(a, "Wrist", (0, 0), (.36f, -2), (.7f, 4), (1.22f, 0));
        Rotate(a, "Elbow", (0, 0), (1.22f, 0));
        Move(a, "Elbow", (0, 0, 0), (1.22f, 0, 0));
        Move(a, "Wrist", (0, 0, 0), (1.22f, 0, 0));
        Move(a, "Root", (0, 0, 0), (.38f, -5, -8), (.7f, 10, -5), (1.22f, 0, 0));
        Rotate(a, "HairFlow", (0, 0), (.4f, -2), (.78f, 3), (1.22f, 0));
        return a;
    }
    private Animation Secondary()
    {
        var a = New(4.8f, true);
        Rotate(a, "HairLeft", (0, -1.5f), (1.2f, 2.8f), (2.4f, 0), (3.6f, -2.5f), (4.8f, -1.5f));
        Rotate(a, "HairLeftTip", (0, 1), (1.45f, -5), (2.65f, -1), (3.85f, 5), (4.8f, 1));
        Rotate(a, "HairRight", (0, 1.5f), (1.2f, -2.2f), (2.4f, -2.8f), (3.6f, 2.4f), (4.8f, 1.5f));
        Rotate(a, "HairRightTip", (0, -1), (1.45f, 5), (2.65f, 2), (3.85f, -6), (4.8f, -1));
        Rotate(a, "Bangs", (0, -.5f), (1.1f, 1.1f), (2.5f, -.8f), (3.8f, .6f), (4.8f, -.5f));
        Rotate(a, "Ahoge", (0, -1), (1.35f, 3), (2.65f, -2), (3.95f, 2), (4.8f, -1));
        Rotate(a, "Ribbon", (0, -2), (1.2f, 3), (2.4f, 1), (3.6f, -3), (4.8f, -2));
        Rotate(a, "RibbonTip", (0, 3), (1.2f, -4), (2.4f, -1), (3.6f, 4), (4.8f, 3));
        Rotate(a, "SkirtLeft", (0, -1), (1.2f, 1.7f), (2.4f, 0), (3.6f, -2), (4.8f, -1));
        Rotate(a, "SkirtLeftTip", (0, 1), (1.2f, -2), (2.4f, -.8f), (3.6f, 2), (4.8f, 1));
        Rotate(a, "SkirtRight", (0, 1), (1.2f, -1.4f), (2.4f, -.7f), (3.6f, 2), (4.8f, 1));
        Rotate(a, "SkirtRightTip", (0, -1), (1.2f, 2), (2.4f, .8f), (3.6f, -2), (4.8f, -1));
        return a;
    }

    internal void IdlePose()
    {
        if (_playing || _main.CurrentAnimation == "saber/b_idle") return;
        Clip = "b_idle";
        _main.Play("saber/b_idle", customBlend: 0.12);
    }
    internal double Play(CardType type)
    {
        Clip = type == CardType.Attack ? "card_attack" : "card_casting";
        _playing = true;
        _main.Play("saber/" + Clip, customBlend: 0.10);
        return type == CardType.Attack ? 1.14 : 1.22;
    }
    internal void Sample() { if (_playing) RenderSamples++; }
#if SABER_TEST
    internal void ExportPose(string clip, double seconds)
    {
        _main.Set("callback_mode_process", 2);
        _secondary.Set("callback_mode_process", 2);
        _main.Play("saber/" + clip, customBlend: 0);
        _main.Seek(seconds, update: true);
        _secondary.Seek(seconds % 4.8, update: true);
    }
#endif
}
