extends SceneTree

# Read-only preview of the user's installed CZN packs in the isolated runner.
func _initialize():
    call_deferred("run")

func run():
    ProjectSettings.load_resource_pack("D:/L/Game/Steam/steamapps/workshop/content/2868840/3826011578/CznCore.pck")
    ProjectSettings.load_resource_pack("D:/L/Game/Steam/steamapps/workshop/content/2868840/3815657213/CznNineCharacter.pck")
    var stage = Node2D.new()
    root.add_child(stage)
    var size = root.get_visible_rect().size
    var bg = ColorRect.new()
    bg.size = size
    bg.color = Color(0.035, 0.045, 0.07)
    stage.add_child(bg)
    var names = [
        "close_combat_step_player_move", "close_combat_step_player_arrive",
        "nine_30047_attack_play1_self_f", "nine_30047_attack_play2_self_f_1",
        "nine_30047_attack_play1_target_b", "nine_30047_u1_attack_play_target_b",
        "nine_30047_u3_attack_play_target_f_1", "nine_30047_u2_attack_play_f_1",
        "system_buff_shield_f1", "system_buff_shield_f2", "system_eff_buff_in", "system_eff_buff"]
    var sprites = []
    var cell = size / Vector2(4, 3)
    for i in range(names.size()):
        var name = names[i]
        var prefix = "res://CznNineCharacter/animations/basic_effects/" if i < 8 else "res://CznCore/animations/common/"
        var data = load(prefix + name + "_skel_data.tres")
        var animations = data.get_animations()
        var animation_name = animations[0].get_name()
        print("CZN_PREVIEW ", name, " clip=", animation_name, " duration=", animations[0].get_duration())
        var sprite = ClassDB.instantiate("SpineSprite")
        sprite.skeleton_data_res = data
        sprite.position = Vector2((i % 4 + 0.5) * cell.x, (int(i / 4) + 0.72) * cell.y)
        sprite.scale = Vector2(0.28, 0.28)
        stage.add_child(sprite)
        var label = Label.new()
        label.position = Vector2((i % 4) * cell.x + 8, int(i / 4) * cell.y + 4)
        label.text = name.replace("nine_30047_", "")
        label.add_theme_font_size_override("font_size", 16)
        stage.add_child(label)
        sprites.append([sprite, animation_name])
    await process_frame
    for item in sprites:
        item[0].get_animation_state().set_animation(item[1], false, 0)
    for frame in range(3):
        await create_timer(0.16).timeout
        await RenderingServer.frame_post_draw
        root.get_texture().get_image().save_png("D:/L/Study/mod/SaberPresentation/build/czn_assets_" + str(frame) + ".png")
    quit()
