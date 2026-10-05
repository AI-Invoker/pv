// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AI-Invoker
/* PV's small C ABI around ufbx. All buffers belong to pv_scene until freed. */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include "ufbx.h"
#define STB_IMAGE_IMPLEMENTATION
#define STBI_NO_STDIO
#define STBI_MAX_DIMENSIONS 16384
#include "stb_image.h"

#define PV_API __declspec(dllexport)
#define PV_MAX_TRIANGLES 4000000u

typedef struct { float p[3], n[3], uv[2], color[4]; } pv_vertex;
typedef struct { uint32_t first, count, material, reserved; } pv_part;
typedef struct { float color[4]; int32_t texture; uint32_t reserved; } pv_material;
typedef struct {
    char *relative, *absolute, *filename;
    void *content;
    uint8_t *pixels;
    uint64_t content_size;
    uint32_t width, height, wrap_u, wrap_v;
} pv_texture;
typedef struct {
    uint32_t vertices, parts, materials, meshes, textures;
    float bottom;
} pv_info;
typedef struct { const char *name; double begin, end; } pv_animation_info;
typedef struct { pv_animation_info info; const ufbx_anim *anim; } pv_animation;
typedef struct { uint32_t node, first, count; } pv_mesh_map;
typedef struct {
    pv_vertex *vertices;
    pv_part *parts;
    pv_material *materials;
    pv_texture *textures;
    pv_info info;
    ufbx_scene *source;
    pv_animation *animations;
    uint32_t animation_count;
    pv_vertex *alternate;
    uint32_t *indices;
    pv_mesh_map *meshes;
    double center[3], radius;
} pv_scene;
typedef char pv_vertex_layout[sizeof(pv_vertex) == 48 ? 1 : -1];
typedef char pv_part_layout[sizeof(pv_part) == 16 ? 1 : -1];
typedef char pv_material_layout[sizeof(pv_material) == 24 ? 1 : -1];
typedef char pv_texture_layout[sizeof(pv_texture) == 64 ? 1 : -1];
typedef char pv_info_layout[sizeof(pv_info) == 24 ? 1 : -1];
typedef char pv_animation_layout[sizeof(pv_animation_info) == 24 ? 1 : -1];

static float pv_clamp(double x) { return !isfinite(x) ? 1.0f : (float)(x < 0.0 ? 0.0 : x > 1.0 ? 1.0 : x); }
static int pv_cancelled(const volatile int *flag) { return flag && *flag != 0; }
static void pv_error(char *buffer, uint32_t size, const char *text) {
    if (buffer && size) { snprintf(buffer, size, "%s", text ? text : "Unknown FBX error"); buffer[size - 1] = 0; }
}
static char *pv_string(ufbx_string str) {
    char *s = (char*)malloc(str.length + 1);
    if (s) { memcpy(s, str.data, str.length); s[str.length] = 0; }
    return s;
}
static ufbx_progress_result pv_progress(void *user, const ufbx_progress *progress) {
    (void)progress;
    return pv_cancelled((const volatile int*)user) ? UFBX_PROGRESS_CANCEL : UFBX_PROGRESS_CONTINUE;
}
static int pv_visible(ufbx_node *node) {
    for (; node; node = node->parent) if (!node->visible) return 0;
    return 1;
}
static ufbx_vec3 pv_position(ufbx_node *node, size_t index) {
    ufbx_mesh *mesh = node->mesh;
    ufbx_vec3 p = ufbx_get_vertex_vec3(&mesh->skinned_position, index);
    if (mesh->skinned_is_local) p = ufbx_transform_position(&node->geometry_to_world, p);
    return p;
}
static ufbx_texture *pv_base_texture(ufbx_material *mat) {
    ufbx_texture *tex = NULL;
    if (mat->pbr.base_color.texture_enabled) tex = mat->pbr.base_color.texture;
    if (!tex && mat->fbx.diffuse_color.texture_enabled) tex = mat->fbx.diffuse_color.texture;
    if (tex && tex->type != UFBX_TEXTURE_FILE) tex = tex->file_textures.count ? tex->file_textures.data[0] : NULL;
    return tex;
}
static int pv_animation_range(const ufbx_anim *anim, double *begin, double *end) {
    if (!anim) return 0;
    double min_time = 1e300, max_time = -1e300;
    int changing = 0;
    for (size_t li = 0; li < anim->layers.count; li++) {
        ufbx_anim_layer *layer = anim->layers.data[li];
        for (size_t vi = 0; vi < layer->anim_values.count; vi++) {
            ufbx_anim_value *value = layer->anim_values.data[vi];
            for (int ci = 0; ci < 3; ci++) {
                ufbx_anim_curve *curve = value->curves[ci];
                if (!curve || !curve->keyframes.count) continue;
                if (curve->max_value != curve->min_value) changing = 1;
                for (size_t ki = 0; !changing && ki < curve->keyframes.count; ki++) {
                    ufbx_keyframe *key = &curve->keyframes.data[ki];
                    if (key->interpolation == UFBX_INTERPOLATION_CUBIC && (key->left.dy != 0 || key->right.dy != 0)) changing = 1;
                }
                double a = curve->keyframes.data[0].time, b = curve->keyframes.data[curve->keyframes.count - 1].time;
                if (isfinite(a) && a < min_time) min_time = a;
                if (isfinite(b) && b > max_time) max_time = b;
            }
        }
    }
    if (!changing || min_time > max_time) return 0;
    *begin = anim->time_begin; *end = anim->time_end;
    if (!isfinite(*begin) || !isfinite(*end) || *end <= *begin) { *begin = min_time; *end = max_time; }
    return isfinite(*end - *begin) && *end - *begin > 1e-6;
}
static int pv_vertex_pose(const pv_scene *scene, pv_vertex *vertex, ufbx_node *node, size_t index, const ufbx_matrix *normals) {
    ufbx_vec3 p = pv_position(node, index), n = ufbx_get_vertex_vec3(&node->mesh->skinned_normal, index);
    if (node->mesh->skinned_is_local) n = ufbx_transform_direction(normals, n);
    double length = sqrt(n.x * n.x + n.y * n.y + n.z * n.z);
    if (length < 1e-12 || !isfinite(length)) { n.x = 0; n.y = 1; n.z = 0; length = 1; }
    for (int j = 0; j < 3; j++) {
        double value = (p.v[j] - scene->center[j]) / scene->radius;
        if (!isfinite(value) || fabs(value) > 1e30) return 0;
        vertex->p[j] = (float)value; vertex->n[j] = (float)(n.v[j] / length);
    }
    return 1;
}

PV_API void pv_fbx_free(pv_scene *scene) {
    if (!scene) return;
    for (uint32_t i = 0; i < scene->info.textures; i++) {
        pv_texture *t = &scene->textures[i];
        free(t->relative); free(t->absolute); free(t->filename); free(t->content); stbi_image_free(t->pixels);
    }
    for (uint32_t i = 0; i < scene->animation_count; i++) free((void*)scene->animations[i].info.name);
    ufbx_free_scene(scene->source);
    free(scene->animations); free(scene->alternate); free(scene->indices); free(scene->meshes);
    free(scene->vertices); free(scene->parts); free(scene->materials); free(scene->textures); free(scene);
}

PV_API pv_scene *pv_fbx_load(const wchar_t *path, const volatile int *cancelled, char *error, uint32_t error_size) {
    ufbx_scene *src = NULL;
    pv_scene *dst = NULL;
    char *filename = NULL;
    ufbx_texture **material_textures = NULL, **unique_textures = NULL;
    uint32_t *tri_indices = NULL;
    size_t tri_capacity = 0;
    FILE *file = NULL;
    const char *failure = "Not enough memory to open this model";
    int utf8_size = WideCharToMultiByte(CP_UTF8, 0, path, -1, NULL, 0, NULL, NULL);
    if (utf8_size <= 0) { pv_error(error, error_size, "Invalid file path"); return NULL; }
    filename = (char*)malloc((size_t)utf8_size);
    if (!filename) goto fail;
    WideCharToMultiByte(CP_UTF8, 0, path, -1, filename, utf8_size, NULL, NULL);
    file = _wfopen(path, L"rb");
    if (!file) { failure = "Unable to read the FBX file"; goto fail; }
    ufbx_load_opts opts = {0};
    opts.filename.data = filename; opts.filename.length = (size_t)utf8_size - 1;
    opts.file_format = UFBX_FILE_FORMAT_FBX;
    opts.target_axes = ufbx_axes_right_handed_y_up;
    opts.target_unit_meters = 1.0;
    opts.generate_missing_normals = true;
    opts.evaluate_skinning = true;
    opts.clean_skin_weights = true;
    opts.use_blender_pbr_material = true;
    opts.node_depth_limit = 512;
    opts.temp_allocator.memory_limit = (size_t)512 * 1024 * 1024;
    opts.result_allocator.memory_limit = (size_t)1024 * 1024 * 1024;
    opts.progress_cb.fn = pv_progress; opts.progress_cb.user = (void*)cancelled;
    ufbx_error load_error = {0};
    src = ufbx_load_stdio(file, &opts, &load_error);
    fclose(file); file = NULL;
    if (!src) {
        pv_error(error, error_size, load_error.description.data);
        goto fail_without_error;
    }
    if (pv_cancelled(cancelled)) { failure = "Cancelled"; goto fail; }
    size_t triangles = 0, part_count = 0, mesh_count = 0;
    double minv[3] = {1e300,1e300,1e300}, maxv[3] = {-1e300,-1e300,-1e300};
    for (size_t ni = 0; ni < src->nodes.count; ni++) {
        ufbx_node *node = src->nodes.data[ni];
        ufbx_mesh *mesh = node->mesh;
        if (!mesh || !mesh->num_triangles || !pv_visible(node)) continue;
        if (triangles > PV_MAX_TRIANGLES || mesh->num_triangles > PV_MAX_TRIANGLES - triangles) {
            failure = "Model exceeds the 4 million triangle viewing limit"; goto fail;
        }
        triangles += mesh->num_triangles; part_count += mesh->material_parts.count; mesh_count++;
        if (mesh->max_face_triangles * 3 > tri_capacity) tri_capacity = mesh->max_face_triangles * 3;
        for (size_t vi = 0; vi < mesh->num_vertices; vi++) {
            uint32_t ix = mesh->vertex_first_index.data[vi];
            if (ix == UFBX_NO_INDEX) continue;
            ufbx_vec3 p = pv_position(node, ix);
            for (int j = 0; j < 3; j++) {
                if (!isfinite(p.v[j])) { failure = "Model contains invalid vertex coordinates"; goto fail; }
                if (p.v[j] < minv[j]) minv[j] = p.v[j];
                if (p.v[j] > maxv[j]) maxv[j] = p.v[j];
            }
        }
        if (pv_cancelled(cancelled)) { failure = "Cancelled"; goto fail; }
    }
    if (!triangles) { failure = "This FBX has no visible polygon mesh"; goto fail; }
    if (part_count > UINT32_MAX || src->materials.count >= UINT32_MAX) goto fail;
    dst = (pv_scene*)calloc(1, sizeof(pv_scene));
    if (!dst) goto fail;
    size_t animation_capacity = src->anim_stacks.count ? src->anim_stacks.count : 1;
    if (animation_capacity > UINT32_MAX) goto fail;
    dst->animations = (pv_animation*)calloc(animation_capacity, sizeof(pv_animation));
    if (!dst->animations) goto fail;
    for (size_t ai = 0; ai < animation_capacity; ai++) {
        ufbx_anim_stack *stack = src->anim_stacks.count ? src->anim_stacks.data[ai] : NULL;
        const ufbx_anim *anim = stack ? stack->anim : src->anim;
        double begin, end;
        if (!pv_animation_range(anim, &begin, &end)) continue;
        pv_animation *clip = &dst->animations[dst->animation_count++];
        clip->info.name = pv_string(stack ? stack->name : (ufbx_string){"Animation", 9});
        if (!clip->info.name) goto fail;
        clip->info.begin = begin; clip->info.end = end; clip->anim = anim;
    }
    dst->info.materials = (uint32_t)src->materials.count + 1;
    dst->info.meshes = (uint32_t)mesh_count;
    dst->vertices = (pv_vertex*)malloc(triangles * 3 * sizeof(pv_vertex));
    dst->parts = (pv_part*)calloc(part_count, sizeof(pv_part));
    dst->materials = (pv_material*)calloc(dst->info.materials, sizeof(pv_material));
    dst->textures = (pv_texture*)calloc(dst->info.materials, sizeof(pv_texture));
    if (dst->animation_count) {
        dst->indices = (uint32_t*)malloc(triangles * 3 * sizeof(uint32_t));
        dst->meshes = (pv_mesh_map*)calloc(mesh_count, sizeof(pv_mesh_map));
        if (!dst->indices || !dst->meshes) goto fail;
    }
    material_textures = (ufbx_texture**)calloc(dst->info.materials, sizeof(ufbx_texture*));
    unique_textures = (ufbx_texture**)calloc(dst->info.materials, sizeof(ufbx_texture*));
    tri_indices = (uint32_t*)malloc(tri_capacity * sizeof(uint32_t));
    if (!dst->vertices || !dst->parts || !dst->materials || !dst->textures || !material_textures || !unique_textures || !tri_indices) goto fail;
    for (uint32_t mi = 0; mi < dst->info.materials; mi++) {
        pv_material *m = &dst->materials[mi];
        m->color[0] = 0.72f; m->color[1] = 0.76f; m->color[2] = 0.82f; m->color[3] = 1.0f; m->texture = -1;
        if (mi >= src->materials.count) continue;
        ufbx_material *mat = src->materials.data[mi];
        ufbx_texture *tex = pv_base_texture(mat);
        material_textures[mi] = tex;
        ufbx_material_map *color = mat->pbr.base_color.has_value ? &mat->pbr.base_color : &mat->fbx.diffuse_color;
        double factor = mat->pbr.base_color.has_value ? mat->pbr.base_factor.value_real : mat->fbx.diffuse_factor.value_real;
        if (color->has_value) for (int j = 0; j < 3; j++) m->color[j] = pv_clamp(color->value_vec3.v[j] * factor);
        else if (tex) for (int j = 0; j < 3; j++) m->color[j] = 1.0f;
        if (mat->pbr.opacity.has_value) m->color[3] = pv_clamp(mat->pbr.opacity.value_real);
        if (!tex) continue;
        uint32_t ti;
        for (ti = 0; ti < dst->info.textures; ti++) if (unique_textures[ti] == tex) break;
        if (ti == dst->info.textures) {
            unique_textures[ti] = tex; dst->info.textures++;
            pv_texture *t = &dst->textures[ti];
            t->relative = pv_string(tex->relative_filename); t->absolute = pv_string(tex->absolute_filename); t->filename = pv_string(tex->filename);
            if (!t->relative || !t->absolute || !t->filename) goto fail;
            t->wrap_u = tex->wrap_u == UFBX_WRAP_CLAMP; t->wrap_v = tex->wrap_v == UFBX_WRAP_CLAMP;
            if (tex->content.size && tex->content.size <= 128u * 1024 * 1024) {
                t->content = malloc(tex->content.size);
                if (!t->content) goto fail;
                memcpy(t->content, tex->content.data, tex->content.size); t->content_size = tex->content.size;
            }
        }
        m->texture = (int32_t)ti;
    }
    double center[3], extent[3];
    for (int j = 0; j < 3; j++) { center[j] = minv[j] * 0.5 + maxv[j] * 0.5; extent[j] = maxv[j] * 0.5 - minv[j] * 0.5; }
    double radius = hypot(hypot(extent[0],extent[1]),extent[2]);
    if (!isfinite(radius)) { failure = "Model bounds are too large"; goto fail; }
    if (radius <= 0.0) radius = 1.0;
    memcpy(dst->center, center, sizeof(center)); dst->radius = radius;
    dst->info.bottom = (float)((minv[1] - center[1]) / radius);
    uint32_t mesh_index = 0;
    for (size_t ni = 0; ni < src->nodes.count; ni++) {
        ufbx_node *node = src->nodes.data[ni];
        ufbx_mesh *mesh = node->mesh;
        if (!mesh || !mesh->num_triangles || !pv_visible(node)) continue;
        pv_mesh_map *mapping = dst->meshes ? &dst->meshes[mesh_index++] : NULL;
        if (mapping) { mapping->node = node->typed_id; mapping->first = dst->info.vertices; }
        ufbx_matrix normals = ufbx_matrix_for_normals(&node->geometry_to_world);
        for (size_t pi = 0; pi < mesh->material_parts.count; pi++) {
            ufbx_mesh_part *part = &mesh->material_parts.data[pi];
            if (!part->num_triangles) continue;
            ufbx_material *mat = part->index < node->materials.count ? node->materials.data[part->index] : NULL;
            uint32_t mi = mat ? mat->typed_id : (uint32_t)src->materials.count;
            if (mi >= dst->info.materials) mi = (uint32_t)src->materials.count;
            pv_part *out_part = &dst->parts[dst->info.parts++];
            out_part->first = dst->info.vertices; out_part->material = mi;
            pv_material *m = &dst->materials[mi];
            ufbx_texture *tex = material_textures[mi];
            const ufbx_vertex_vec2 *uvs = &mesh->vertex_uv;
            if (tex && tex->uv_set.length) for (size_t ui = 0; ui < mesh->uv_sets.count; ui++) {
                ufbx_uv_set *set = &mesh->uv_sets.data[ui];
                if (set->name.length == tex->uv_set.length && memcmp(set->name.data, tex->uv_set.data, set->name.length) == 0) { uvs = &set->vertex_uv; break; }
            }
            for (size_t fi = 0; fi < part->face_indices.count; fi++) {
                if ((fi & 1023) == 0 && pv_cancelled(cancelled)) { failure = "Cancelled"; goto fail; }
                ufbx_face face = mesh->faces.data[part->face_indices.data[fi]];
                uint32_t num_tri = ufbx_triangulate_face(tri_indices, tri_capacity, mesh, face);
                if ((size_t)dst->info.vertices + num_tri * 3 > triangles * 3) { failure = "Invalid mesh triangulation"; goto fail; }
                for (uint32_t vi = 0; vi < num_tri * 3; vi++) {
                    size_t ix = tri_indices[vi]; pv_vertex *v = &dst->vertices[dst->info.vertices++];
                    if (!pv_vertex_pose(dst, v, node, ix, &normals)) { failure = "Invalid vertex coordinates"; goto fail; }
                    if (dst->indices) dst->indices[dst->info.vertices - 1] = (uint32_t)ix;
                    ufbx_vec3 uv = {0};
                    if (uvs->exists) { ufbx_vec2 base = ufbx_get_vertex_vec2(uvs, ix); uv.x = base.x; uv.y = base.y; }
                    if (tex && tex->has_uv_transform) uv = ufbx_transform_position(&tex->uv_to_texture, uv);
                    v->uv[0] = (float)uv.x; v->uv[1] = (float)(1.0 - uv.y);
                    for (int j = 0; j < 4; j++) v->color[j] = m->color[j];
                    if (mesh->vertex_color.exists) { ufbx_vec4 c = ufbx_get_vertex_vec4(&mesh->vertex_color, ix); for (int j = 0; j < 4; j++) v->color[j] *= pv_clamp(c.v[j]); }
                }
            }
            out_part->count = dst->info.vertices - out_part->first;
        }
        if (mapping) mapping->count = dst->info.vertices - mapping->first;
    }
    // Static files release ufbx immediately. Animated files retain one source
    // scene and reuse two vertex buffers, without baking every animation frame.
    if (dst->animation_count) { dst->source = src; src = NULL; }
    free(filename); free(material_textures); free(unique_textures); free(tri_indices); ufbx_free_scene(src);
    return dst;
fail:
    pv_error(error, error_size, failure);
fail_without_error:
    if (file) fclose(file);
    free(filename); free(material_textures); free(unique_textures); free(tri_indices);
    ufbx_free_scene(src); pv_fbx_free(dst); return NULL;
}

PV_API void pv_fbx_info(const pv_scene *scene, pv_info *info) { if (scene && info) *info = scene->info; }
PV_API const pv_vertex *pv_fbx_vertices(const pv_scene *scene) { return scene ? scene->vertices : NULL; }
PV_API const pv_part *pv_fbx_parts(const pv_scene *scene) { return scene ? scene->parts : NULL; }
PV_API const pv_material *pv_fbx_materials(const pv_scene *scene) { return scene ? scene->materials : NULL; }
PV_API uint32_t pv_fbx_animation_count(const pv_scene *scene) { return scene ? scene->animation_count : 0; }
PV_API int pv_fbx_animation_info(const pv_scene *scene, uint32_t index, pv_animation_info *info) {
    if (!scene || !info || index >= scene->animation_count) return 0;
    *info = scene->animations[index].info; return 1;
}
// Called by a single worker on the buffer that the UI is not drawing.
PV_API const pv_vertex *pv_fbx_evaluate(pv_scene *scene, int32_t animation, double position, uint32_t buffer, char *error, uint32_t error_size) {
    if (!scene || !scene->source || buffer > 1 || animation < -1 || (animation >= 0 && (uint32_t)animation >= scene->animation_count) || !isfinite(position)) {
        pv_error(error, error_size, "Invalid animation request"); return NULL;
    }
    if (buffer && !scene->alternate) {
        scene->alternate = (pv_vertex*)malloc((size_t)scene->info.vertices * sizeof(pv_vertex));
        if (!scene->alternate) { pv_error(error, error_size, "Not enough memory for animation"); return NULL; }
        memcpy(scene->alternate, scene->vertices, (size_t)scene->info.vertices * sizeof(pv_vertex));
    }
    pv_vertex *vertices = buffer ? scene->alternate : scene->vertices;
    ufbx_scene *evaluated = NULL, *pose = scene->source;
    if (animation >= 0) {
        pv_animation *clip = &scene->animations[animation];
        double time = clip->info.begin + fmax(0.0, fmin(clip->info.end - clip->info.begin, position));
        ufbx_evaluate_opts opts = {0}; opts.evaluate_skinning = true;
        opts.temp_allocator.memory_limit = (size_t)512 * 1024 * 1024;
        opts.result_allocator.memory_limit = (size_t)1024 * 1024 * 1024;
        ufbx_error eval_error = {0};
        evaluated = ufbx_evaluate_scene(scene->source, clip->anim, time, &opts, &eval_error);
        if (!evaluated) { pv_error(error, error_size, eval_error.description.data); return NULL; }
        pose = evaluated;
    }
    int valid = 1;
    for (uint32_t mi = 0; valid && mi < scene->info.meshes; mi++) {
        pv_mesh_map *mapping = &scene->meshes[mi];
        if (mapping->node >= pose->nodes.count) { valid = 0; break; }
        ufbx_node *node = pose->nodes.data[mapping->node];
        if (!node->mesh) { valid = 0; break; }
        ufbx_matrix normals = ufbx_matrix_for_normals(&node->geometry_to_world);
        int visible = pv_visible(node);
        for (uint32_t vi = mapping->first; vi < mapping->first + mapping->count; vi++) {
            uint32_t index = scene->indices[vi];
            if (index >= node->mesh->num_indices || !pv_vertex_pose(scene, &vertices[vi], node, index, &normals)) { valid = 0; break; }
            if (!visible) memset(vertices[vi].p, 0, sizeof(vertices[vi].p));
        }
    }
    ufbx_free_scene(evaluated);
    if (!valid) { pv_error(error, error_size, "Invalid animated mesh coordinates"); return NULL; }
    return vertices;
}
PV_API int pv_fbx_texture_info(const pv_scene *scene, uint32_t index, pv_texture *texture) {
    if (!scene || index >= scene->info.textures || !texture) return 0;
    *texture = scene->textures[index]; return 1;
}
PV_API int pv_fbx_decode_texture(pv_scene *scene, uint32_t index, const void *bytes, uint32_t size) {
    if (!scene || index >= scene->info.textures) return 0;
    pv_texture *t = &scene->textures[index];
    if (!bytes) { bytes = t->content; size = (uint32_t)t->content_size; }
    int w = 0, h = 0, channels = 0;
    if (!bytes || size > 128u * 1024 * 1024 || !stbi_info_from_memory((const stbi_uc*)bytes, (int)size, &w, &h, &channels)) return 0;
    if (w <= 0 || h <= 0 || w > 8192 || h > 8192 || (uint64_t)w * h > 16777216) return 0;
    uint64_t total = (uint64_t)w * h * 4;
    for (uint32_t i = 0; i < scene->info.textures; i++) if (i != index) total += (uint64_t)scene->textures[i].width * scene->textures[i].height * 4;
    if (total > 256u * 1024 * 1024) return 0;
    uint8_t *pixels = stbi_load_from_memory((const stbi_uc*)bytes, (int)size, &w, &h, &channels, 4);
    if (!pixels) return 0;
    stbi_image_free(t->pixels); t->pixels = pixels; t->width = (uint32_t)w; t->height = (uint32_t)h;
    free(t->content); t->content = NULL; t->content_size = 0;
    return 1;
}
