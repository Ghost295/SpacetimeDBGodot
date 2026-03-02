@tool

extends Node
class_name TerrainEditor

@export var tile_size: int = 16
@export var cell_size: int = 1
@export var terrain: Terrain3D
#@export var tiles: Array[Rect2i] = []

#const Baker: Script = preload("res://addons/terrain_3d/menu/baker.gd")

# transform it into a get
#@export var display_flow_tiles: bool:
#	get:
#		return display_flow_tiles
#	set(value):
#		display_flow_tiles = value
#		if value:
#			debug_draw_flow_tiles()

@export var flow_regions_container: Node
@export var bounds: Rect2i

#@export var map_data: MapData

# Debug: MultiMesh for visualizing per-cell costs
var cost_points_multimesh: MultiMeshInstance3D

@export_tool_button("Generate Tiles", "NavigationRegion3D") var action: Callable = generate_tiles
#@export_tool_button("Calculate Cost Field") var cost_action = calculate_cost_field

#func _ready():
#	if display_flow_tiles:
#		debug_draw_flow_tiles()

#func calculate_cost_field():
#	print("FlowField3D: Calculating cost field...")
#
#	calculate_cells_costs(_get_terrain_bounds())
#	# var terrain_data: Terrain3DData = terrain.data

func generate_tiles():
	print("Generating tiles...")
	var terrain_bounds = _get_terrain_bounds()
	bounds = terrain_bounds
	
	_build_flow_tiles(terrain_bounds)

func _build_flow_tiles(terrain_bounds: Rect2i):
	"""Build grid cells only on walkable terrain surface"""
	print("FlowField3D: Building flow tiles...")

	# Clear the flow tiles
#	tiles.clear()

	# remove all existing flow regions
	for region in flow_regions_container.get_children():
		print("FlowField3D: Removing flow region: ", region.name)
		region.queue_free()

	for x in range(terrain_bounds.position.x, terrain_bounds.end.x, tile_size):
		for z in range(terrain_bounds.position.y, terrain_bounds.end.y, tile_size):
			var tile_rect = Rect2i(Vector2i(x, z), Vector2i(tile_size, tile_size))
			print("FlowField3D: Creating flow tile: ", tile_rect)
#			tiles.append(tile_rect)

			if flow_regions_container:
				var region = NavigationRegion3D.new()
				region.navigation_mesh = NavigationMesh.new()
				# Set the filter baking aabb to the tile rect 
				region.navigation_mesh.filter_baking_aabb = AABB(Vector3(tile_rect.position.x, -10, tile_rect.position.y), Vector3(tile_rect.size.x, 500, tile_rect.size.y))
				region.navigation_mesh.geometry_source_geometry_mode = NavigationMesh.SOURCE_GEOMETRY_GROUPS_WITH_CHILDREN
				region.navigation_mesh.geometry_source_group_name = "terrain3d_navs"
				region.navigation_mesh.geometry_parsed_geometry_type = NavigationMesh.PARSED_GEOMETRY_STATIC_COLLIDERS

				region.navigation_mesh.cell_size = ProjectSettings.get_setting("navigation/3d/default_cell_size")
				region.navigation_mesh.cell_height = ProjectSettings.get_setting("navigation/3d/default_cell_height")
				region.navigation_mesh.agent_height = ProjectSettings.get_setting("navigation/3d/default_cell_height")
				region.navigation_mesh.agent_radius = ProjectSettings.get_setting("navigation/3d/default_cell_size")
				region.navigation_mesh.agent_max_climb = ProjectSettings.get_setting("navigation/3d/default_cell_size")

				flow_regions_container.add_child(region)
				region.name = "FlowRegion_" + str(x) + "_" + str(z)
				region.owner = flow_regions_container.owner
				print("FlowField3D: Created flow region: ", region.name)

	print("FlowField3D: Created ", flow_regions_container.get_child_count(), " flow tiles")

func _get_terrain_bounds() -> Rect2i:
	"""Get the 2D bounds of all active terrain regions"""
	var active_regions: Array[Terrain3DRegion] = terrain.data.get_regions_active()
	if active_regions.is_empty():
		print("FlowField3D: No active terrain regions")
		return Rect2i()

	var region_size_world: float = float(terrain.get_region_size()) * terrain.get_vertex_spacing()
	print("FlowField3D: Region size world: ", region_size_world)
	print("FlowField3D: Tile size: ", tile_size)
	print("FlowField3D: Region size world / tile size: ", region_size_world / tile_size)
	var min_bounds_f := Vector2(1.0e20, 1.0e20)
	var max_bounds_f := Vector2(-1.0e20, -1.0e20)

	for region in active_regions:
		if region and not region.is_deleted():
			var location: Vector2i = region.get_location()
			var world_pos := Vector2(location.x * region_size_world, location.y * region_size_world)
			var world_end := world_pos + Vector2(region_size_world, region_size_world)

			min_bounds_f.x = min(min_bounds_f.x, world_pos.x)
			min_bounds_f.y = min(min_bounds_f.y, world_pos.y)
			max_bounds_f.x = max(max_bounds_f.x, world_end.x) 
			max_bounds_f.y = max(max_bounds_f.y, world_end.y)

	var min_i := Vector2i(int(floor(min_bounds_f.x)), int(floor(min_bounds_f.y)))
	var size_i := Vector2i(
		int(ceil(max_bounds_f.x) - min_i.x),
		int(ceil(max_bounds_f.y) - min_i.y)
	)
	return Rect2i(min_i, size_i)

#func debug_draw_flow_tiles():
#	if not Engine.is_editor_hint():
#		return
#	
#	"""Create MultiMesh for grid cells as 2D square outlines with per-instance colors"""
#	print("FlowField3D: Debug drawing flow tiles")
#	# Create 4 thin rectangles to form hollow square outlines
#	var edge_thickness = tile_size * 0.02  # Thickness of the outline
#	var edge_height = 0.05  # Very small height for flat 2D appearance
#	
#	# Create 4 MultiMesh instances for the 4 edges of the hollow square
#	var edges = [
#		{"offset": Vector3(0, 0, tile_size * 0.5), "size": Vector3(tile_size, edge_height, edge_thickness)},      # Top edge
#		{"offset": Vector3(0, 0, -tile_size * 0.5), "size": Vector3(tile_size, edge_height, edge_thickness)},     # Bottom edge  
#		{"offset": Vector3(tile_size * 0.5, 0, 0), "size": Vector3(edge_thickness, edge_height, tile_size)},      # Right edge
#		{"offset": Vector3(-tile_size * 0.5, 0, 0), "size": Vector3(edge_thickness, edge_height, tile_size)}     # Left edge
#	]
#	
#	for edge in edges:
#		var multi_mesh_instance = MultiMeshInstance3D.new()
#		var multi_mesh = MultiMesh.new()
#		
#		# Create thin box for this edge
#		var box_mesh = BoxMesh.new()
#		box_mesh.size = edge.size
#		
#		# Setup MultiMesh with color support
#		multi_mesh.transform_format = MultiMesh.TRANSFORM_3D
#		
#		multi_mesh.mesh = box_mesh
#		
#		# Enable per-instance colors if we have color data
#		# if colors.size() == positions.size():
#		# 	multi_mesh.use_colors = true
#		
#		# Use Baker helper to find nav regions for this terrain without accessing editor UI
#		var baker: Node = Baker.new()
#		var flow_regions: Array[NavigationRegion3D] = baker.find_terrain_nav_regions(terrain)
#		print("FlowField3D: Found ", flow_regions.size(), " flow regions")
#
#		# Must be after the use_colors is set
#		multi_mesh.instance_count = flow_regions.size()
#
#		# Set positions and colors for each edge instance
#		for i in range(flow_regions.size()):
#			var transform = Transform3D()
#			var filter_baking_aabb = flow_regions[i].navigation_mesh.filter_baking_aabb
#
#			var height: float = terrain.data.get_height(filter_baking_aabb.position + filter_baking_aabb.size / 2)
#			print("FlowField3D: Height: ", height)
#			
#			print("FlowField3D: Filter baking aabb: ", filter_baking_aabb)
#			transform.origin = Vector3(filter_baking_aabb.position.x + filter_baking_aabb.size.x / 2, height + 10, filter_baking_aabb.position.z + filter_baking_aabb.size.z / 2) + edge.offset
#			multi_mesh.set_instance_transform(i, transform)
#			
#			# Set per-instance color if available
#			# if colors.size() == positions.size():
#			# 	multi_mesh.set_instance_color(i, colors[i])
#		
#		# Create material for the edges
#		var material = StandardMaterial3D.new()
#		material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
#		material.emission_enabled = true
#		material.no_depth_test = false
#		material.flags_unshaded = true
#		material.cull_mode = BaseMaterial3D.CULL_DISABLED
#		material.albedo_color = Color.GREEN
#		material.emission = Color.GREEN * 1.0
#		
#		multi_mesh_instance.multimesh = multi_mesh
#		multi_mesh_instance.material_override = material
#		multi_mesh_instance.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
#		
#		add_child(multi_mesh_instance)


#func calculate_cells_costs(terrain_bounds: Rect2i):
#	map_data.cost_field.clear()
#
#	# Prepare transforms and colors
#	var instance_transforms: Array = []
#	var instance_colors: Array[Color] = []
#
#	for x in range(terrain_bounds.position.x, terrain_bounds.end.x, cell_size):
#		for z in range(terrain_bounds.position.y, terrain_bounds.end.y, cell_size):
#			var cell := Vector2i(x, z)
#			var is_walkable: bool = terrain.data.get_control_navigation(Vector3(float(x) + float(cell_size) * 0.5, 0.0, float(z) + float(cell_size) * 0.5))
#			var cost: float = 0.0 if is_walkable else 999.0
#			
#			if cost != 0.0:
#				map_data.cost_field[cell] = cost
#
#			# Position point at cell center and slightly above terrain height
#			var cell_center := Vector3(float(x) + float(cell_size) * 0.5, 0.0, float(z) + float(cell_size) * 0.5)
#			var height: float = 0.0
#			if terrain and terrain.data:
#				height = terrain.data.get_height(cell_center)
#			var xform := Transform3D()
#			xform.origin = Vector3(cell_center.x, height + 0.2, cell_center.z)
#			instance_transforms.append(xform)
#			instance_colors.append(Color(1, 0, 0, 1) if cost == 999.0 else Color(0, 1, 0, 1))
#
#	# Build MultiMesh for points
#	var multi_mesh_instance := MultiMeshInstance3D.new()
#	var multi_mesh := MultiMesh.new()
#	var box_mesh := BoxMesh.new()
#	box_mesh.size = Vector3(float(cell_size) * 0.2, float(cell_size) * 0.2, float(cell_size) * 0.2)
#
#	multi_mesh.transform_format = MultiMesh.TRANSFORM_3D
#	multi_mesh.use_colors = true
#	multi_mesh.mesh = box_mesh
#	multi_mesh.instance_count = instance_transforms.size()
#
#	for i in instance_transforms.size():
#		multi_mesh.set_instance_transform(i, instance_transforms[i])
#		multi_mesh.set_instance_color(i, instance_colors[i])
#
#	var material := StandardMaterial3D.new()
#	material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
#	material.vertex_color_use_as_albedo = true
#	material.albedo_color = Color(1, 1, 1, 1)
#	material.emission_enabled = true
#	material.emission = Color(1, 1, 1, 1)
#	material.no_depth_test = false
#	material.flags_unshaded = true
#	material.cull_mode = BaseMaterial3D.CULL_DISABLED
#
#	multi_mesh_instance.multimesh = multi_mesh
#	multi_mesh_instance.material_override = material
#	multi_mesh_instance.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
#
#	add_child(multi_mesh_instance)
#	cost_points_multimesh = multi_mesh_instance
