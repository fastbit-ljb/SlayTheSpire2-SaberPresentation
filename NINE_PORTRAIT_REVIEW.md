# Nine 左侧角色动画核查与 Saber 修正

核查对象：本机订阅的 Chaos Zero Nightmare - Nine，`CznNineCharacter.dll` / `CznNineCharacter.pck`，2.0.0-beta。仅本地读取，未加载它的运行程序集。

## 直接查到的实现

- `NineBattleReadyRuntime.Profile` 指向 `scenes/nine_battle_ready.tscn` 和 `animations/body/nine_battle_ready_skel_data.tres`。
- 场景节点是 `SpineSprite`，独立于战场的小模型。静止参考帧不是它的完整动画。
- `b_idle` 为悬停循环；出牌路由为 `card_attack` / `card_casting`。骨架资源 `default_mix = 0.05`。
- `BattleReadyOverlayRuntime.PlayAnimationForCard` 播放一次动作；`OnAnimationCompleted` 后执行退出。移出卡牌、取消瞄准、正在出牌分别管理，重复焦点不会反复重启待机。
- 大立绘的平移只用于进入和退出，身体内部由 Spine 骨骼和变形动画驱动。

## 0.3.3 存在的问题

Saber 把几张独立生成的整个人物图依次切换。脸、银甲、裙摆比例和照明不完全一致。增加分辨率、改色或提高换图速度均不能获得连续动作。

## 0.3.4 的实际实现

移除整图动作轮播和对应安装包资源。使用一套头部／身体／持剑手臂 RGBA 分层，20 个原生 Bone2D、加权 Polygon2D 和 AnimationPlayer；纹理在整段动作期间保持相同实例。原版认可图作为分层补绘和颜色参考，原始 PNG 保留，补绘使用内置 image_gen，未做位图放大。

`b_idle` 连续呼吸与持剑；发丝、发带和裙摆使用独立循环。0.3.4 曾让同一张大立绘继续播放 `card_attack` 和 `card_casting`，但实机观感证明这会让双臂、握把和剑的空间关系失真。

另直接采样原 Saber 战斗骨骼 `attack`、`attackvfff`、`attack_heavy`、`cast` 和两段 `relaxed_loop` 的 11 个时点，记录武器骨骼位置和角度，选择没有剑光、碎石或粒子的悬停定格帧。

这是为 Saber 新做的二维分层骨骼，不是 Nine 原始 Spine 骨架，也不加载 CznCore 动画运行库。源 Nine 的骨架只用于核查方法。

## 验证

- 检查真实 Skeleton2D 骨骼数量、纹理实例不变、头发与完整角色待机在透明隔离视口中产生实际像素变化。
- 在实际游戏渲染器中导出 180 个待机姿态，用于审查双臂接缝、剑柄、头发和衣摆连续性。这是离线动画预览，不是性能录像。
- 成品 PCK 检查排除 `anime_v2`、`anime_v3`、`anime_v4` 和旧 `saber_attack_sequence`。
- 1920×1080 隔离实机检查攻击牌蓄力、技能牌插地守势、悬停无特效、鼠标移开恢复待机，以及正式出牌后大立绘退场。

Godot 原生实现依据：[二维骨骼](https://docs.godotengine.org/en/4.5/tutorials/animation/2d_skeletons.html)、[Bone2D](https://docs.godotengine.org/en/4.5/classes/class_bone2d.html)。

## 0.3.5 实际载入和逐骨骼采样

在隔离游戏进程中只加载 Nine PCK，实例化它的原始 SpineSprite 场景，没有加载 Nine / CznCore DLL。采样 `b_idle`、`card_attack`、`card_casting` 各 31 个时点，共 355 根骨骼、33,015 条记录；源样本哈希和核心运动统计保存在 `assets/references/nine_motion_metrics.json`。Nine 待机实际为 2.1333 秒，两个出牌片段均为 0.4667 秒。部分效果控制骨骼远在画外，不把那些位移当作手臂／身体动作。

0.3.5 缩小左侧角色约 26.5%，新增五块透明手臂图集：前后上臂、前后护臂、双手握把与完整剑。两条二段 IK 链独立求解，双手、剑柄和剑作为一个刚体，不再让整组手臂或剑穿过脸部。待机周期为 2.24 秒，身体、头、发丝、发带和裙摆连续运动。

结合千鹤完整演示后，0.3.5 删除大立绘的攻击／施法片段。左侧角色只负责悬停待机；攻击牌悬停把场内 Saber 定格在 `attack` 30% 的举剑蓄力帧，技能／防御牌定格在 `cast` 90% 的剑尖插地守势帧。鼠标移开或取消立即切回 `idle_loop`；卡牌正式出手时解除定格，大立绘约 170 ms 退场，场内骨骼与分层特效接管。该结构与 Nine 的独立预备层兼容，也符合千鹤演示里实际可见的职责分离。
