# CZN 全屏演出与 Saber 宝具衔接核查

## 视频来源与核查边界

用户明确确认，新参考 `D:\L\download\10月25日.mp4` 来源于 **Chaos Zero Nightmare - Chizuru (Ironclad Skin) (CznCore / Beta)**。本记录采用用户确认的来源，不用画面与另一角色资源的相似之处推翻该来源，也不将本地 Nine 内部资源名当成视频的实际素材名。

该视频为 1920×1080、30 fps、344 帧，时长约 11.47 秒，SHA-256 为 `ED5E1142ECECD931C080E160D88CE55659EEB78FF8CF09950C9FFC0CB9C4B351`。能观察到两次近景蓄力、全屏特写、独立场景攻击与原战场命中反馈。第二次出牌可读到 `Conflagration`。录像中的游戏计时推进快于视频时间，不能将录像时间直接当作包内动画的原始时长。

| 视频时点 | 可直接观察的表现 |
| --- | --- |
| 约 0.8 / 5.6 秒 | 原战位附近蓄力，镜头贴近角色 |
| 约 1.2 / 6.0 秒 | 身体、武器的局部特写 |
| 约 1.6–2.0 / 6.4–6.8 秒 | 人物与持剑近景，能量持续流动 |
| 约 2.4 / 7.2 秒 | 全屏斩击转场 |
| 约 2.8–3.2 / 7.6–8.0 秒 | 独立场景攻击，怪物留在演出构图中 |
| 约 4.0 / 8.8 秒起 | 原战场恢复，目标仍保留命中效果 |

这些时点仅定位构图；没有该录像运行时日志、具体安装版本或资源快照，不能进一步声称已把其中每帧唯一映射到本机某个内部资源。此前将此视频直接归为 Nine UX 的判断已撤回。以下主体是对**本机千鹤 DLL/PCK** 的直接检查，最后另列本地 Nine 相似结构的比较证据。

## 本机千鹤版本与证据文件

本次读取的千鹤安装位置：

- `D:\L\Game\Steam\steamapps\workshop\content\2868840\3826011833\ChizuruIroncladSkin.json`
- `D:\L\Game\Steam\steamapps\workshop\content\2868840\3826011833\ChizuruIroncladSkin.dll`
- `D:\L\Game\Steam\steamapps\workshop\content\2868840\3826011833\ChizuruIroncladSkin.pck`

清单声明作者 `Pelleas`、版本 `1.0.1-beta`，依赖 `CznCore >= 1.0.2`，`affects_gameplay` 为 false。DLL SHA-256 为 `E3662C18CCA5723624B506358F9E86180117953561DF3F3A70E2BA3F4F555569`；PCK SHA-256 为 `2DDCE887717D0508815D57A702ADB7A15100F397CC694DA82BF5AEAC2FA91DA1`，共 1343 个资源条目。此身份不证明录像必然使用相同构建，也不代表已核验当前远程版本。

## 千鹤实际路由与战斗同步

`ChizuruCombatPresentationProfile` 根据攻击上下文生成 `CombatPresentationAttackPlan`，其中不同的动作会产生不同全屏路线：

| 身体动画 | Presentation ID | 入口场景 |
| --- | --- | --- |
| `ug_all_attack_1` | `chizuru.ug_attack` | `res://ChizuruIroncladSkin/scenes/cinematic/ug_v2/stage.tscn` |
| `ux_attack_1` | `chizuru.ux_attack` | `res://ChizuruIroncladSkin/scenes/cinematic/chizuru_stage.tscn` |

`ResolveAttackCinematicPresentationId()`、`Entry.ResolveCombatAttackCinematicPresentationProfile()` 和 `DispatchSceneCinematic()` 完成上述映射。并非所有大招都必须沿用同一段角色位移。

千鹤的单体加权攻击分支还考虑伤害、稀有度和费用。在允许卡牌演出、通过其他适用条件后：Ancient 分支按分数 45/65 分别选择 UG/UX；其他分支的 UX 要求单次伤害至少 60 且总分至少 65，UG 要求伤害加费用分至少 40 且总分至少 45。其他多段、群攻和低伤分支另有选择，不能把这一段阈值概括成所有卡牌的固定触发标准。

`TryPrepareCombatAttackRoute()` 对包含 cut-in 或 cinematic ID 的计划调用 `CombatAttackFullScreenPresentationPolicy.Allows()`；不允许全屏时，改用 `ResolveAttackPlanWithoutFullScreenPresentation()`。这说明演出路由与全屏资格是两层判断。

`DispatchSceneCinematic()` 使用当前攻击 session 和实际目标列表建立 `SceneCinematic`。`MirrorCinematicActor()` 调用 `CznCore.Presentation.CombatTargetStaging.TryCreateMirroredTargetProxy()` 创建角色与目标镜像，保存原节点可见性。目标阵型和命中偏移来自当场敌人的视口位置及 hitbox，不是预先画在背景里的敌人。

| 千鹤信号 / 方法 | 实际职责 |
| --- | --- |
| `configure()` | 传入路线、角色代理、原战位、目标代理、目标位置、音量与命中偏移 |
| `presentation_started` | 资源准备好后隐藏原可视节点，结束悬停大立绘，交给舞台 |
| `hit_reached` / `SceneCinematic.Hit` | 给原攻击命中等待提供完成信号 |
| `targets_restored` | 先归还原目标可视节点 |
| `actors_restored` | 归还角色及剩余原可视节点 |
| `stage_finished` / `SceneCinematic.Finished` | 关键演出段完成，结束会话等待 |
| `resources_finished` | 清理音频、粒子和特效尾部资源 |
| `TryPlaySceneCinematicHit()` | 将原伤害命中对接到舞台；UG 多次命中可调用 `replay_target_hit()` |
| `CancelSceneCinematic()` / `ReleaseSceneCinematic()` | 退出、取消、生命周期变化时恢复并清理 |

UG 另外调用 `bind_native_targets()`，传入原房间后景 VFX 容器、真实目标、局部命中点与深度；多段攻击可设置 `hold_for_hits`。这些接口让命中尾部能接回真实战场，而不是一直把游戏盖在一段影片下面。

## 千鹤舞台与不同演出变体

共有两个实际入口，共用 `chizuru_stage.gd` 的大部分资源与取消管理。`ug_v2/stage.gd` 继承该脚本，并改写相机、待机立绘与身体恢复的交接方式。

| 路线 | 时间线数据 | 直接读取 `plan.layers` 的实际计数 |
| --- | --- | --- |
| UG | `res://ChizuruIroncladSkin/cinematic/ug_attack/bundle.json` | 42 条：23 个 Spine 特效、2 个身体层、4 个粒子、8 个相机、4 个伤害标记、1 个目标节点 |
| UX | `res://ChizuruIroncladSkin/cinematic/ux_attack/bundle.json` | 51 条：33 个 Spine 特效、1 个身体层、3 个粒子、6 个相机、7 个伤害标记、1 个目标节点 |

不能直接照抄 bundle 的 `summary`：本机 UG 的 summary 仍写 39 层，UX summary 写 52 层且包含 1 个 cut-in，但实际 UX `plan.layers` 没有 `layer_class == cutin` 条目。这里以脚本真正遍历的 `plan.layers` 计数，保留 summary 差异作为证据，不把元数据声明当成已经播放的内容。

### UG：待机立绘交接、独立攻击与恢复

UG 中 `card_casting` 从 0 到 433 ms；`STANDBY_OFF` 在 433 ms；约 466 ms 起，`cutin_1/2`、rim、wisp 与屏幕效果构成特写。`bind_standby()` 接收原战斗预备立绘的视口变换，`_apply_actor_transforms()` 将 STANDBY 曲线作用于该起点，减少换层跳变。

约 1800 ms 起，火焰、attack 与前后景效果形成独立攻击场景；约 1833 ms，`MOVE` 将自身和目标排到独立舞台两侧，同时开始 `ug_all_attack_1`。相机从 1.5 倍回到 1 倍，并有独立震动曲线。原目标可约 3166 ms 先恢复；角色身体片段保留到约 4099.7 ms，特效计划尾部约 4332.7 ms。`ug_v2._actor_end()` 明确保留完整身体恢复动画，避免只因 IDLE 或 UI 恢复事件就过早切掉收招。

这确实是一种带独立排位的舞台方式，但不意味着 UX、其他技能或 Saber 光炮也必须让人物移动到画面中间。

### UX：自身镜头、分层特写与目标运动

UX 使用 `ux_attack_1` 身体片段，从 0 到 1433.3 ms。其结构与 UG 不同：

| 包内时间 | 千鹤 UX 事件与资源 |
| --- | --- |
| 0–1000 ms | `CAM_MOVE SELF(-45,-120)` 与 `CAM_ZOOM 1.7`，角色原地起手并由镜头推近 |
| 566–899 ms | `BG_BLEND` 隐去原房间背景，保持角色和效果独立 |
| 1433.3 ms | `IDLE SELF`；之后镜头重置，进入另一组特写层 |
| 1434.3 ms 起 | `chizuru_1060_skill_x_cutin_01/02/03/05...` 多个 Spine 层与 `ux_attack_02_b/f` 组成特写，不依赖一段单独的视频 |
| 2722.3 ms 起 | `ux_attack_03_*` 与后续 cutin_04 分层；`NODE_ANI` 驱动目标代理 2500 ms |
| 4800 ms | 原始 `HIT action` 记录；运行脚本使用 bundle 的 `damage_release_ms = 4988.3` 作为实际伤害放行点 |
| 5289–5622 ms | 原房间背景渐回、目标恢复事件；`ux_attack_04` 和粒子继续收尾 |
| 约 6289 ms | 特效计划尾部，资源仍按实际声音与粒子生命周期释放 |

`_build()` 为每层创建独立 Spine 或粒子节点，按 `SELF`、`TARGET`、`CENTER`、`SCREEN` 等附着对象放入舞台或屏幕层。原始 UX companions 由 `SpineAtlasResource.load_from_atlas_file()` 与 `SpineSkeletonFileResource.load_from_file()` 加载；其他层可直接加载 `.tres`。PMA、additive、multiply、screen 材质按层配置，前后景效果不会全挤在人物胸口。

`_process()` / `_apply()` 共享同一 `clock_ms`，逐层设置 Spine `track_time`、颜色、可见性、目标运动和粒子发射。`_camera_at()` 将镜头曲线应用于世界空间 `stage`；`screen` 保持屏幕空间内容。`_apply_actor_transforms()`、`_update_target_motion()`、`_place_native_target_effect()` 分别处理演员变换、怪物受击动作和接回原战场的命中锚点。相机推进和场内人物自己滑到中间是两种不同的事。

## 本地 Nine 比较：共享接口不等于视频来源

此前额外查看了本地 `build\runner-czn\mods\CznNineCharacter\`，清单为同一作者 `Pelleas`、版本 `2.0.0-beta`，依赖 `CznCore >= 1.0.0`。其独立 `NineCinematicRuntime` 使用 `CreateProxy()`、`hit_reached`、`actors_restored`、`stage_finished`、`resources_finished`；`nine_cinematic_stage.gd` 同样采用统一时钟、屏幕/世界分层与目标代理。

可以确认的共同点是**共享 CznCore 命名空间和镜像目标、全屏演出门槛等接口，以及相似的分层生命周期结构**。双方都有自己的程序集类、路线 ID、时间线数据与角色资源。已列出完整 PCK 路径检查：本机千鹤的 1343 个条目没有 `nine` 或 `30047` 命名路径；本机 Nine 的 1410 个条目没有 `chizuru` 命名路径。没有足够证据声称二者全屏动画素材相同、其中一方换皮引用另一方，或录像必然来自本地 Nine 包。

作为纯比较证据，本地 Nine 的 `ux_all_attack.json` 计划有 72 条记录：27 个 Spine 特效、1 个身体层、1 个粒子、2 个 cut-in、1 个伤害标记及 40 个相机记录。后者包括 13 个 CAM_MOVE、11 个 CAM_ZOOM、16 个 SHAKE。`s1_ld_leg`、`s1_ld_blade1–3`、`s2_bg`、`back1–5` 等名称只属于这份本地 Nine 数据，**不是已证实的视频实际资源名**。其两段 cut-in 和 901 ms WEBP 等待屏障同样只能解释该本地 Nine 实现，不可当作千鹤视频的精确时间线。

上述证据支持“同一作者、共享基础设施、分别实现不同角色舞台”的谨慎结论；没有源码历史或版本对应证据，不能进一步断言代码复制方向、最初来源或录像使用哪一版内部数据。

## Saber 采用的演出结构

根据用户指出的全屏特写、场景联动与光炮特点，Saber 采用以下结构，由 Saber 自有代码实现：

1. 保持原战位，双手举剑蓄力；不把没有步行片段的身体平移到中央，不单独放大身体制造走近效果。
2. 全屏 Saber 特写承担脸、剑、解封与释放镜头，配合同步日配。
3. 白光转接到 Camelot，给约 0.25 秒建立镜头，让建筑、地面与双方角色先被看清。
4. Saber 留在左侧向真实目标位置释放约 1.9 秒光束；命中、闪光和环境染色持续联动。
5. 光束余辉、角色收招、原环境恢复分阶段结束；退出时恢复父节点、变换、颜色与可见性。

上述时长是新版实施设计，最终实机证据和通过项由对应版本验证报告记录。旧版 `Hit()` 中已有目标 `node.Modulate = Colors.White`，不能把“目标透明度从未恢复”列为原因。此前 Camelot 难以观察的实际问题是视频遮挡占时较长、暖色环境峰值和光束显示期过短。

## 本地导出与素材来源边界

可复核的分析导出保留于 `D:\L\Study\mod\SaberPresentation\build\reference-chizuru-variants\`：

- `fine_video.jpg`：新视频 0.4 秒间隔联系表；`new_video_sheet_1.jpg`：2 秒间隔概览。这些只是分析帧，不作为成品素材。
- `ug_stage.gd`、`ux_stage.gd`：千鹤 UG 子脚本与共享舞台脚本。
- `chizuru_ug_attack_bundle.json`、`chizuru_ux_attack_bundle.json`：本机千鹤原始时间线与渲染计划。
- `ChizuruCombatPresentationProfile.decompiled.cs`：本地千鹤路由配置的反编译分析导出。
- 千鹤 Entry 完整导出另在 `D:\L\Study\mod\SaberPresentation\build\reference\ChizuruIroncladSkin.Entry.decompiled.cs`。
- `nine_cinematic_stage.gd/.tscn`、`nine_ux_all_attack.json`、`nine_cutins.json` 及 `NineAttackRoutes.decompiled.cs`、`NineBasicCombatRuntime.decompiled.cs`、`NineCinematicRuntime.decompiled.cs` 仅供比较本地 Nine 的实现，不用于证明视频来源。

用户视频由用户提供，用于动作与构图分析；不能据此推定作者授权或再发行许可。此次核查不把千鹤或 Nine 的角色图、骨架、切入帧、程序、语音或攻击资源加入 Saber 包。包内 `official://...` 字符串是原包的来源标记，不等于已核验的素材许可证。Saber 自身使用的星铁官方公开演示片段与既有日配来源继续记录在 `assets/sources.json` 和 `THIRD_PARTY_NOTICES.md`。参考的是分层、相机与命中交接方式；Saber 舞台代码独立实现，不复制 CZN 程序，也不新增 CZN 模组运行依赖。
