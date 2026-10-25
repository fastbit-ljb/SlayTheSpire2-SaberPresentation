# 千鹤角色演出结构核查

核查对象是本机订阅的 `Chaos Zero Nightmare - Chizuru (Ironclad Skin) (CznCore / Beta)` 1.0.1-beta，以及用户提供的 GIF 和约 255 秒完整演示视频。只读取资源和画面用于理解演出结构，没有把千鹤角色图、骨架或程序打入 Saber 成品。

## 直接确认的结构

- `scenes/combat/overlays/1060_chizuru_ironclad_ready_point.tscn` 是独立 `SpineSprite` 预备层，位置约 `(164, 700)`、缩放约 `1.31`，悬停循环为 `b_idle`。
- 场内角色身体是另一套 Spine 资源。大立绘和场内角色不是同一个节点，也不靠逐张整图轮播完成攻击。
- 战斗特效按前景／背景拆分，使用独立 Spine 层和 PMA 材质；命中特效在目标位置单独生成。
- 完整演示中，大立绘在悬停和选牌阶段保持待机；卡牌正式打出时它快速退出，挥砍由场内角色和分层特效完成。

## Saber 对应实现

- 左侧高清层仅播放连续 `b_idle`，不再让大立绘挥剑。
- 攻击牌悬停时，场内 Saber 保持 `attack` 30% 的举剑蓄力帧；技能／防御牌悬停保持 `cast` 90% 的剑尖插地守势帧。两处均选在原骨骼动画的无特效区间。
- 鼠标移开或取消后立即切回 `idle_loop`；瞄准期间保留姿态。
- 卡牌正式结算时解除悬停定格，大立绘约 170 ms 退场，原 Saber 战斗骨骼和已内置的前景／背景／目标特效接管。

这样保留千鹤模组清晰的职责分层，同时继续使用 Saber 自身美术、动作、日配和数值。

## 0.3.6 宝具舞台核查

进一步读取本地千鹤 DLL 的 `Entry.SceneCinematic` 与 `DispatchSceneCinematic`：它维护角色／目标代理、隐藏节点清单、`Hit`／`Finished` 完成信号；舞台通过 `hit_reached`、`targets_restored`、`actors_restored`、`stage_finished` 和 `resources_finished` 控制伤害时序与恢复。`MirrorCinematicActor` 创建镜像代理，避免移动游戏原节点。`TryPlaySceneCinematicHit` 把原伤害命中映射到对应的舞台目标。

Saber 的实现独立编写于 `FinisherStage.cs`：原生最终伤害确认致死后、死亡节点清理前，临时将 Saber 与目标的可视身体重挂入独立 CanvasLayer，保存父节点、顺序、变换和颜色；演出后恢复。没有改写伤害或预先重跑伤害计算。与千鹤的提前 `hit_reached` 结算不同，Saber 使用已确认的致死结果作为触发窗口，因此不会预判格挡、免死、重定向或随机目标。

舞台从原战位推进并放大 Saber，冻结原骨骼的举剑蓄力姿态；该骨骼没有步行动画，因此这一段是镜头推进式移动。星铁官方 Saber 动画的 4.55 秒与 7.5 秒分别同步日配释放与最终光柱命中，怪物身体接收实际中心位置的特效、闪白和位移震动；Saber 直接播放原骨骼重击，避免普通攻击补丁干预舞台坐标。结束或退出房间时取消补间并还原节点。千鹤自身角色图、骨架、DLL、舞台代码均未打入成品。
