# Used by screenshot.sh: godot --path . -s res://shell/Godot/Screenshot.gd -- <scene> <out.png> <frames>
# Runs <scene> for <frames> frames, then saves the root viewport's last frame as a PNG.
extends SceneTree

var _out := ""
var _frames := 0

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 3:
		printerr("usage: -- <scene> <out.png> <frames>")
		quit(2)
		return
	_out = args[1]
	_frames = int(args[2])
	var err := change_scene_to_file(args[0])
	if err != OK:
		printerr("cannot load scene ", args[0], ": ", error_string(err))
		quit(1)

func _process(_delta: float) -> bool:
	_frames -= 1
	if _frames > 0:
		return false
	var image := root.get_texture().get_image()
	var err := image.save_png(_out)
	if err != OK:
		printerr("cannot save ", _out, ": ", error_string(err))
		quit(1)
	else:
		print("NEO_SCREENSHOT ", _out, " ", image.get_width(), "x", image.get_height())
		quit(0)
	return false
