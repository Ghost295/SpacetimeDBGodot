shader_type spatial;
render_mode blend_mix,depth_draw_opaque,cull_back,diffuse_burley,specular_schlick_ggx;

// VAT Direct Shader - Per-Instance Data via Data Texture
// 
// OPTIMIZED VERSION with LOD-based quality reduction:
// - Frozen animations (speed=0): Single texture lookup, no interpolation
// - Mid LOD: Optional skip interpolation for further performance
// - Near LOD: Full interpolation for smooth animation
//
// Data Texture Layout - TWO TEXTURES:
//   instance_data_texture (RGBA float):
//     R = animation_offset (packed value - see encoding below)
//     G = start_frame (direct value)
//     B = end_frame (direct value)
//     A = team_index (integer encoded as float)
//   
//   instance_scale_texture (R float):
//     R = base_scale (used for index decoding)
//
// Animation Offset Encoding (R channel):
//   - packed >= 0 : loop offset (0..1), loops forever
//   - packed < 0  : one-shot mode, packed = -(start_ms + saved_offset)
//       where start_ms = floor(abs(packed)) = milliseconds since engine start
//             saved_offset = fract(abs(packed)) = preserved per-unit loop offset (0..1)
//   One-shot plays once from start_frame and clamps at end_frame.

// VAT textures
uniform sampler2D offset_map : filter_nearest;
uniform sampler2D normal_map : filter_nearest;
uniform sampler2D texture_albedo : source_color;

// Instance data textures
uniform sampler2D instance_data_texture : filter_nearest;
uniform sampler2D instance_scale_texture : filter_nearest;

// Data texture dimensions (must match what's set from C#)
uniform int data_texture_width = 256;

// Scale factor for encoding instance index
const float INDEX_SCALE_FACTOR = 0.00001;

uniform float fps = 24.0;

// Animation LOD: 1.0 = full speed, 0.5 = half speed, 0.0 = frozen
uniform float animation_speed : hint_range(0.0, 2.0) = 1.0;

// Skip frame interpolation for performance (set true for mid/far LOD)
uniform bool skip_interpolation = false;

uniform float specular : hint_range(0,1) = 0.5;
uniform float metallic : hint_range(0,1) = 0.0;
uniform float roughness : hint_range(0,1) = 1.0;

// Team tint palette (AoE/SC2 style)
uniform sampler2D team_palette_texture : filter_nearest;
uniform int team_palette_count = 2;

// SC2-style mask convention: 0 = fully tinted, 1 = no tint.
uniform bool team_mask_invert = true;

varying float v_team_idx;

void vertex() {
	// Get actual scale from transform
	float actual_scale = length(MODEL_MATRIX[0].xyz);
	
	// We need to find our instance index, but we don't know base_scale yet.
	// Solution: iterate through possible indices near our scale to find a match.
	// Since INDEX_SCALE_FACTOR is tiny, we can estimate the index range.
	
	// First estimate: assume base_scale is close to actual_scale (index is small)
	// For most cases, the index contribution is < 0.1 (10000 instances * 0.00001 = 0.1)
	// So we can estimate base_scale as actual_scale rounded to nearest 0.5
	float estimated_base = max(0.5, round(actual_scale * 2.0) / 2.0);
	
	// Calculate estimated index
	int estimated_idx = int(round((actual_scale / estimated_base - 1.0) / INDEX_SCALE_FACTOR));
	estimated_idx = clamp(estimated_idx, 0, 65535);
	
	// Fetch the actual base_scale from the scale texture to verify/correct
	ivec2 tex_coord = ivec2(estimated_idx % data_texture_width, estimated_idx / data_texture_width);
	float stored_base_scale = texelFetch(instance_scale_texture, tex_coord, 0).r;
	
	// If stored scale is valid (> 0), recalculate index with correct base
	int instance_idx;
	if (stored_base_scale > 0.001) {
		instance_idx = int(round((actual_scale / stored_base_scale - 1.0) / INDEX_SCALE_FACTOR));
		instance_idx = clamp(instance_idx, 0, 65535);
		// Update tex_coord with correct index
		tex_coord = ivec2(instance_idx % data_texture_width, instance_idx / data_texture_width);
	} else {
		instance_idx = estimated_idx;
	}
	
	// Fetch instance data from texture
	vec4 instance_data = texelFetch(instance_data_texture, tex_coord, 0);
	v_team_idx = instance_data.a;
	
	// Decode packed animation_offset:
	// - >= 0 : loop offset (0..1)
	// - < 0  : one-shot mode, packed = -(start_ms + saved_offset)
	float packed = instance_data.r;
	float start_frame = instance_data.g;
	float end_frame = instance_data.b;
	
	bool one_shot = packed < 0.0;
	float animation_offset;
	float one_shot_start_time; // in seconds (only valid if one_shot)
	
	if (one_shot) {
		float abs_packed = abs(packed);
		float start_ms = floor(abs_packed);
		animation_offset = fract(abs_packed); // saved loop offset (preserved)
		one_shot_start_time = start_ms * 0.001; // convert ms to seconds
	} else {
		animation_offset = packed; // normal loop offset (0..1)
		one_shot_start_time = 0.0; // unused
	}
	// Frame calculation
	float num_frames = max(1.0, end_frame - start_frame);
	int frame_count = max(1, int(num_frames));
	
	// Get VAT texture size once
	float pixel_size = 1.0 / float(textureSize(offset_map, 0).y);
	
	// Initialize outputs with safe defaults
	vec3 final_offset = vec3(0.0);
	vec3 final_normal = vec3(0.0, 1.0, 0.0);
	
	// ============================================
	// OPTIMIZATION: Frozen animation fast path
	// ============================================
	if (animation_speed < 0.001) {
		// Frozen: Use static frame based on offset, NO interpolation needed
		float static_frame = start_frame + animation_offset * num_frames;
		static_frame = clamp(floor(static_frame), start_frame, end_frame - 1.0);
		
		vec2 uv_offset = vec2(0.0, -((static_frame + 0.5) * pixel_size));
		
		vec3 offset_raw = texture(offset_map, UV2 + uv_offset).xyz;
		final_offset = vec3(offset_raw.x, offset_raw.z, offset_raw.y);
		
		vec3 normal_raw = texture(normal_map, UV2 + uv_offset).xyz;
		final_normal = vec3((normal_raw.x * 2.0) - 1.0, (normal_raw.z * 2.0) - 1.0, (normal_raw.y * 2.0) - 1.0);
	}
	// ============================================
	// OPTIMIZATION: Skip interpolation for mid LOD
	// ============================================
	else if (skip_interpolation) {
		float current_frame;
		if (one_shot) {
			float local_time = max(0.0, (TIME - one_shot_start_time) * animation_speed);
			current_frame = start_frame + min(local_time * fps, num_frames - 0.001);
		} else {
			float time_scale_normalized = fps / num_frames;
			float scaled_time = TIME * animation_speed;
			float effective_time = mod(scaled_time * time_scale_normalized, 1.0);
			
			float frame_offset_val = num_frames * animation_offset;
			current_frame = start_frame + mod((effective_time * num_frames) + frame_offset_val, num_frames);
		}
		
		float frame_floor = clamp(floor(current_frame), start_frame, end_frame - 1.0);
		
		vec2 frame_floor_uv = vec2(0.0, -((frame_floor + 0.5) * pixel_size));
		
		vec3 offset_raw = texture(offset_map, UV2 + frame_floor_uv).xyz;
		final_offset = vec3(offset_raw.x, offset_raw.z, offset_raw.y);
		
		vec3 normal_raw = texture(normal_map, UV2 + frame_floor_uv).xyz;
		final_normal = vec3((normal_raw.x * 2.0) - 1.0, (normal_raw.z * 2.0) - 1.0, (normal_raw.y * 2.0) - 1.0);
	}
	// ============================================
	// Full quality: Frame interpolation for near LOD
	// ============================================
	else {
		float current_frame;
		if (one_shot) {
			float local_time = max(0.0, (TIME - one_shot_start_time) * animation_speed);
			current_frame = start_frame + min(local_time * fps, num_frames - 0.001);
		} else {
			float time_scale_normalized = fps / num_frames;
			float scaled_time = TIME * animation_speed;
			float effective_time = mod(scaled_time * time_scale_normalized, 1.0);
			
			float frame_offset_val = num_frames * animation_offset;
			current_frame = start_frame + mod((effective_time * num_frames) + frame_offset_val, num_frames);
		}
		
		float frame_floor = clamp(floor(current_frame), start_frame, end_frame - 1.0);
		float frame_ceil;
		if (one_shot) {
			frame_ceil = min(frame_floor + 1.0, end_frame - 1.0);
		} else {
			frame_ceil = float(int(ceil(current_frame) - start_frame) % frame_count) + start_frame;
		}
		float lerp_factor = current_frame - frame_floor;
		
		vec2 frame_floor_uv = vec2(0.0, -((frame_floor + 0.5) * pixel_size));
		vec2 frame_ceil_uv = vec2(0.0, -((frame_ceil + 0.5) * pixel_size));
		
		vec3 offset_floor = texture(offset_map, UV2 + frame_floor_uv).xyz;
		vec3 offset_ceil = texture(offset_map, UV2 + frame_ceil_uv).xyz;
		vec3 offset_lerp = mix(offset_floor, offset_ceil, lerp_factor);
		final_offset = vec3(offset_lerp.x, offset_lerp.z, offset_lerp.y);
		
		vec3 normal_floor = texture(normal_map, UV2 + frame_floor_uv).xyz;
		vec3 normal_ceil = texture(normal_map, UV2 + frame_ceil_uv).xyz;
		vec3 normal_lerp = mix(normal_floor, normal_ceil, lerp_factor);
		final_normal = vec3((normal_lerp.x * 2.0) - 1.0, (normal_lerp.z * 2.0) - 1.0, (normal_lerp.y * 2.0) - 1.0);
	}
	
	// Apply final vertex offset and normal
	VERTEX += final_offset;
	NORMAL = final_normal;
}

void fragment() {
	vec4 base = texture(texture_albedo, UV);
	float tint_amount = team_mask_invert ? (1.0 - base.a) : base.a;

	int palette_count = max(team_palette_count, 1);
	int team_idx = clamp(int(round(v_team_idx)), 0, palette_count - 1);
	vec3 team_color = texelFetch(team_palette_texture, ivec2(team_idx, 0), 0).rgb;

	vec3 tinted = base.rgb * team_color;
	ALBEDO = mix(base.rgb, tinted, tint_amount);
	METALLIC = metallic;
	ROUGHNESS = roughness;
	SPECULAR = specular;
}
