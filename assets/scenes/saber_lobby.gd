extends Control

# Native scene consumed by CznMainMenu's installed-skin lobby picker.
var backdrop: TextureRect
var portrait: Polygon2D
var original: PackedVector2Array
var elapsed := 0.0
const COLS := 12
const ROWS := 24

func _ready() -> void:
	backdrop = TextureRect.new()
	backdrop.texture = load("res://SaberPresentation/generated/camelot_stage.png")
	backdrop.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	backdrop.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED
	backdrop.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(backdrop)
	portrait = Polygon2D.new()
	portrait.texture = load("res://SaberPresentation/generated/saber_ready_cast.png")
	var uvs := PackedVector2Array()
	for y in range(ROWS + 1):
		for x in range(COLS + 1):
			original.append(Vector2(float(x) / COLS * 1024.0 - 512.0, float(y) / ROWS * 1536.0 - 1536.0))
			uvs.append(Vector2(float(x) / COLS * 1024.0, float(y) / ROWS * 1536.0))
	var faces: Array = []
	for y in range(ROWS):
		for x in range(COLS):
			var i := y * (COLS + 1) + x
			faces.append(PackedInt32Array([i, i + 1, i + COLS + 2, i + COLS + 1]))
	portrait.polygon = original
	portrait.uv = uvs
	portrait.polygons = faces
	add_child(portrait)
	_layout()

func _layout() -> void:
	var viewport_size := get_viewport_rect().size
	backdrop.position = Vector2(-20, -10)
	backdrop.size = viewport_size + Vector2(40, 20)
	portrait.position = viewport_size * Vector2(0.72, 1.25)
	portrait.scale = Vector2.ONE * viewport_size.y * 1.25 / 1536.0

func _process(delta: float) -> void:
	if portrait == null:
		return
	elapsed += delta
	_layout()
	backdrop.position.x += sin(elapsed * 0.13) * 10
	var points := original.duplicate()
	for i in range(points.size()):
		var h := -points[i].y / 1536.0
		var cloth := clampf((0.66 - h) * 2.8, 0, 1)
		points[i].x += sin(elapsed * 1.9) * 2.5 * h + sin(elapsed * 2.5 + points[i].y * 0.007) * 9 * cloth
		points[i].y += sin(elapsed * 1.9) * 3.8 * h
	portrait.polygon = points
