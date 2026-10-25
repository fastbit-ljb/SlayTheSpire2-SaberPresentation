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

`b_idle` 连续呼吸与持剑；`card_attack` 包含蓄势、挥剑、跟进、回弹和收招；`card_casting` 为提剑蓄力与回落。发丝、发带和裙摆使用独立循环，出牌不重启。动作以原生插值连续求值，完成后混合回待机再退出。

另直接采样原 Saber 战斗骨架 `attack`、`attackvfff`、`attack_heavy` 的 11 个时点，记录武器骨骼位置和角度。参考其先立剑蓄势、迅速向前落剑、短暂跟进后回收的节奏，限制肩部转角并加入肘部协调，避免整组手臂做大圆周旋转。头发新增分根部／发梢的相位滞后、刘海和呆毛，挥剑和施法有额外惯性回弹。

这是为 Saber 新做的二维分层骨骼，不是 Nine 原始 Spine 骨架，也不加载 CznCore 动画运行库。源 Nine 的骨架只用于核查方法。

## 验证

- 检查真实 Skeleton2D 骨骼数量、动作中纹理实例不变、至少 30 个实际过程帧、剑尖全程在视口内。
- 在实际游戏渲染器中导出 172 个等间隔骨骼姿态，合成 30 fps 连续预览，用于审查接缝、变形和衔接。这是离线动画预览，不是性能录像。
- 成品 PCK 检查排除 `anime_v2`、`anime_v3`、`anime_v4` 和旧 `saber_attack_sequence`。

Godot 原生实现依据：[二维骨骼](https://docs.godotengine.org/en/4.5/tutorials/animation/2d_skeletons.html)、[Bone2D](https://docs.godotengine.org/en/4.5/classes/class_bone2d.html)。
