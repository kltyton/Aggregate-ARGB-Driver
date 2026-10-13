#include "text.h"
#include "../native/abi.h"

#define PANEL 720
#define MAX_FONTS 512
#define MAX_ELEMENTS 256
struct glyph { const unsigned char *record; };
struct face { uint32_t count, pairs; const unsigned char *pair_data; float ascent, height; struct glyph *glyphs; };
struct element { const unsigned char *record, *runs; uint32_t run_count; const unsigned char *value; uint32_t length; };
struct ui_state {
    unsigned char *blob;
    uint32_t size, fonts, elements, rotation, background, version;
    struct face faces[MAX_FONTS];
    struct element items[MAX_ELEMENTS];
    const unsigned char *angles;
};
static struct ui_state *active;
static unsigned char *pending;
static uint32_t pending_size, pending_offset, pending_crc;
static unsigned char *values;
static size_t values_capacity;
static uint32_t hour, minute, second;

static uint32_t u32(const unsigned char *p)
{ return (uint32_t)p[0] | (uint32_t)p[1] << 8 | (uint32_t)p[2] << 16 | (uint32_t)p[3] << 24; }
static uint16_t u16(const unsigned char *p) { return (uint16_t)p[0] | (uint16_t)p[1] << 8; }
static float real(const unsigned char *p) { union { uint32_t bits; float value; } v = { .bits = u32(p) }; return v.value; }
static int32_t integer(const unsigned char *p) { return (int32_t)u32(p); }
static void release(struct ui_state *state)
{
    if (!state) return;
    for (uint32_t i = 0; i < state->fonts; ++i) if (state->faces[i].glyphs) native_free(state->faces[i].glyphs);
    if (state->blob) native_free(state->blob);
    native_free(state);
}
void ui_clear(void)
{
    release(active); active = NULL;
    if (pending) native_free(pending);
    pending = NULL; pending_size = pending_offset = 0;
    if (values) native_free(values);
    values = NULL; values_capacity = 0;
}
enum storage_status ui_begin(uint32_t size, uint32_t checksum)
{
    if (!size || size > 8u * 1024 * 1024) return STORAGE_SEQUENCE;
    unsigned char *next = native_allocate(size);
    if (!next) return STORAGE_SPACE;
    if (pending) native_free(pending);
    pending = next; pending_size = size; pending_offset = 0; pending_crc = checksum;
    return STORAGE_OK;
}
enum storage_status ui_append(uint32_t offset, const unsigned char *data, size_t count)
{
    if (!pending || offset != pending_offset || count > pending_size - pending_offset) return STORAGE_SEQUENCE;
    for (size_t i = 0; i < count; ++i) pending[offset + i] = data[i];
    pending_offset += count; return STORAGE_OK;
}
static int take(uint32_t *position, uint32_t count, uint32_t size)
{
    if (*position > size || count > size - *position) return -1;
    *position += count; return 0;
}
enum storage_status ui_commit(void)
{
    if (!pending || pending_offset != pending_size) return STORAGE_SEQUENCE;
    if (native_checksum(pending, pending_size) != pending_crc) return STORAGE_CHECKSUM;
    if (pending_size < 56 || u32(pending) != 0x49554c48 || (u32(pending + 4) != 1 && u32(pending + 4) != 2) ||
        u32(pending + 8) > MAX_ELEMENTS || u32(pending + 12) > MAX_FONTS || u32(pending + 16) > 3) return STORAGE_SEQUENCE;
    struct ui_state *next = native_allocate(sizeof(*next));
    if (!next) return STORAGE_SPACE;
    unsigned char *zero = (unsigned char *)next;
    for (size_t i = 0; i < sizeof(*next); ++i) zero[i] = 0;
    next->size = pending_size; next->fonts = u32(pending + 12); next->elements = u32(pending + 8);
    next->rotation = u32(pending + 16); next->background = u32(pending + 20);
    next->version = u32(pending + 4);
    uint32_t glyph_header = next->version == 2 ? 20 : 16, pair_stride = next->version == 2 ? 16 : 12;
    uint32_t position = 56;
    enum storage_status error = STORAGE_SEQUENCE;
    for (uint32_t i = 0; i < next->fonts; ++i) {
        if (take(&position, 12, pending_size)) goto failed;
        const unsigned char *record = pending + position - 12;
        struct face *face = &next->faces[i];
        face->count = u32(record); face->ascent = real(record + 4); face->height = real(record + 8);
        if (!face->count || face->count > 65536 || !(face->height > 0 && face->height <= 4096) ||
            !(face->ascent > 0 && face->ascent <= 4096)) goto failed;
        face->glyphs = native_allocate(face->count * sizeof(struct glyph));
        if (!face->glyphs) { error = STORAGE_SPACE; goto failed; }
        uint32_t previous = 0;
        for (uint32_t g = 0; g < face->count; ++g) {
            if (take(&position, glyph_header, pending_size)) goto failed;
            const unsigned char *glyph = pending + position - glyph_header;
            uint32_t code = u32(glyph), width = u16(glyph + glyph_header - 4), height = u16(glyph + glyph_header - 2);
            if ((g && code <= previous) || code > 0x10ffff || width > 4096 || height > 4096 ||
                !(real(glyph + 4) >= 0 && real(glyph + 4) <= 4096) || take(&position, width * height, pending_size)) goto failed;
            face->glyphs[g].record = glyph; previous = code;
            if (next->version == 2 && !(real(glyph + 8) >= 0 && real(glyph + 8) <= 4096)) goto failed;
        }
        if (take(&position, 4, pending_size)) goto failed;
        face->pairs = u32(pending + position - 4);
        if (face->pairs > (pending_size - position) / pair_stride) goto failed;
        face->pair_data = pending + position; position += face->pairs * pair_stride;
        for (uint32_t pair = 0; pair < face->pairs; ++pair)
            for (uint32_t field = 8; field < pair_stride; field += 4)
                if (!(real(face->pair_data + pair * pair_stride + field) >= -4096 && real(face->pair_data + pair * pair_stride + field) <= 4096)) goto failed;
    }
    for (uint32_t i = 0; i < next->elements; ++i) {
        if (take(&position, 76, pending_size)) goto failed;
        const unsigned char *record = pending + position - 76;
        struct element *item = &next->items[i];
        item->record = record; item->run_count = u32(record + 72);
        int32_t x = integer(record + 4), y = integer(record + 8), width = integer(record + 12), height = integer(record + 16);
        int32_t font = integer(record + 24), unit = integer(record + 28);
        if (u32(record) > 19 || x < 0 || y < 0 || x >= PANEL || y >= PANEL || width < 1 || height < 1 || width > PANEL || height > PANEL || x + width > PANEL || y + height > PANEL ||
            u32(record + 20) > 2 || font < -1 || font >= (int32_t)next->fonts || unit < -1 || unit >= (int32_t)next->fonts ||
            item->run_count > (pending_size - position) / 8) goto failed;
        if (!(real(record + 40) >= 0 && real(record + 40) <= 1024)) goto failed;
        for (uint32_t field = 48; field <= 56; field += 4)
            if (!(real(record + field) >= 0.1f && real(record + field) <= 1.0f)) goto failed;
        for (uint32_t field = 60; field <= 68; field += 4)
            if (!(real(record + field) >= 1 && real(record + field) <= 20)) goto failed;
        item->runs = pending + position;
        uint32_t pixels = 0;
        for (uint32_t run = 0; run < item->run_count; ++run) {
            uint32_t count = u32(pending + position + run * 8);
            if (!count || count > (uint32_t)(width * height) - pixels) goto failed;
            pixels += count;
        }
        if (pixels != (uint32_t)(width * height)) goto failed;
        position += item->run_count * 8;
    }
    if (take(&position, 3600 * 8, pending_size) || position != pending_size) goto failed;
    next->angles = pending + position - 3600 * 8;
    next->blob = pending; pending = NULL; pending_size = pending_offset = 0;
    release(active); active = next;
    if (values) native_free(values);
    values = NULL; values_capacity = 0; return STORAGE_OK;
failed:
    release(next); return error;
}
static const unsigned char *glyph_at(const struct face *face, uint32_t code)
{
    uint32_t low = 0, high = face->count;
    while (low < high) {
        uint32_t middle = low + (high - low) / 2;
        const unsigned char *glyph = face->glyphs[middle].record;
        uint32_t found = u32(glyph);
        if (found == code) return glyph;
        if (found < code) low = middle + 1; else high = middle;
    }
    return NULL;
}
static float advance(const unsigned char *glyph, int ordinary)
{ return real(glyph + (ordinary && active->version == 2 ? 8 : 4)); }
static float kern(const struct face *face, uint32_t left, uint32_t right, int ordinary)
{
    uint32_t stride = active->version == 2 ? 16 : 12;
    for (uint32_t i = 0; i < face->pairs; ++i) {
        const unsigned char *pair = face->pair_data + i * stride;
        if (u32(pair) == left && u32(pair + 4) == right) return real(pair + (ordinary && active->version == 2 ? 12 : 8));
    }
    return 0;
}
enum storage_status ui_values(const unsigned char *data, size_t count)
{
    if (!active || count < 16 || u32(data) > 23 || u32(data + 4) > 59 || u32(data + 8) > 59 ||
        u32(data + 12) != active->elements) return STORAGE_SEQUENCE;
    uint32_t position = 16;
    for (uint32_t i = 0; i < active->elements; ++i) {
        if (take(&position, 8, count)) return STORAGE_SEQUENCE;
        uint32_t id = u32(data + position - 8), length = u32(data + position - 4);
        if (id != i || length > (count - position) / 4) return STORAGE_SEQUENCE;
        const unsigned char *record = active->items[i].record;
        int32_t font = integer(record + 24), unit = integer(record + 28);
        for (uint32_t g = 0; g < length && font >= 0; ++g) {
            uint32_t code = u32(data + position + g * 4);
            if (code != 10 && code != 13 && !glyph_at(&active->faces[font], code) &&
                (unit < 0 || !glyph_at(&active->faces[unit], code))) return STORAGE_SEQUENCE;
        }
        position += length * 4;
    }
    if (position != count) return STORAGE_SEQUENCE;
    if (count > values_capacity) {
        unsigned char *next = native_allocate(count);
        if (!next) return STORAGE_SPACE;
        if (values) native_free(values);
        values = next; values_capacity = count;
    }
    for (size_t i = 0; i < count; ++i) values[i] = data[i];
    hour = u32(data); minute = u32(data + 4); second = u32(data + 8);
    position = 16;
    for (uint32_t i = 0; i < active->elements; ++i) {
        active->items[i].length = u32(values + position + 4);
        active->items[i].value = values + position + 8;
        position += 8 + active->items[i].length * 4;
    }
    return STORAGE_OK;
}
static void pixel(unsigned char *target, int x, int y, uint32_t color, uint32_t alpha)
{
    if (!alpha || x < 0 || y < 0 || x >= PANEL || y >= PANEL) return;
    int rx = x, ry = y;
    switch (active->rotation) {
    case 1: rx = PANEL - 1 - y; ry = x; break;
    case 2: rx = PANEL - 1 - x; ry = PANEL - 1 - y; break;
    case 3: rx = y; ry = PANEL - 1 - x; break;
    default: break;
    }
    unsigned char *p = target + (ry * PANEL + rx) * 4;
    uint32_t combined = alpha * 255 + p[3] * (255 - alpha);
    for (uint32_t i = 0; i < 3; ++i) p[i] = (((color >> (i * 8)) & 255) * alpha * 255 + p[i] * p[3] * (255 - alpha) + combined / 2) / combined;
    p[3] = (combined + 127) / 255;
}
static float text_width(const struct face *face, const unsigned char *text, uint32_t length, int ordinary)
{
    float width = 0, widest = 0;
    for (uint32_t i = 0; i < length; ++i) {
        uint32_t code = u32(text + i * 4);
        if (code == 10 || code == 13) {
            if (width > widest) widest = width;
            width = 0;
            if (code == 13 && i + 1 < length && u32(text + (i + 1) * 4) == 10) i++;
            continue;
        }
        const unsigned char *g = glyph_at(face, u32(text + i * 4));
        if (g) width += advance(g, ordinary);
        if (i + 1 < length) width += kern(face, u32(text + i * 4), u32(text + (i + 1) * 4), ordinary);
    }
    return width > widest ? width : widest;
}
static void text_run(unsigned char *target, const struct element *item, const struct face *face,
    const unsigned char *text, uint32_t length, float x, float y, int ordinary)
{
    const unsigned char *record = item->record;
    int clip_x = integer(record + 4), clip_y = integer(record + 8), clip_w = integer(record + 12), clip_h = integer(record + 16);
    uint32_t color = u32(record + 36);
    float origin = x;
    for (uint32_t i = 0; i < length; ++i) {
        uint32_t code = u32(text + i * 4);
        if (code == 10 || code == 13) {
            y += face->height; x = origin;
            if (code == 13 && i + 1 < length && u32(text + (i + 1) * 4) == 10) i++;
            continue;
        }
        const unsigned char *glyph = glyph_at(face, u32(text + i * 4));
        if (!glyph) continue;
        uint32_t header = active->version == 2 ? 20 : 16;
        int gx = (int)(x + 0.5f) + (int16_t)u16(glyph + header - 8), gy = (int)(y + 0.5f) + (int16_t)u16(glyph + header - 6);
        uint32_t width = u16(glyph + header - 4), height = u16(glyph + header - 2);
        for (uint32_t row = 0; row < height; ++row) {
            int py = gy + row;
            if (py < clip_y || py >= clip_y + clip_h) continue;
            for (uint32_t col = 0; col < width; ++col) {
                int px = gx + col;
                if (px >= clip_x && px < clip_x + clip_w) pixel(target, px, py, color, glyph[header + row * width + col]);
            }
        }
        x += advance(glyph, ordinary);
        if (i + 1 < length) x += kern(face, u32(text + i * 4), u32(text + (i + 1) * 4), ordinary);
    }
}
static float aligned(uint32_t alignment, float width, float span)
{ return alignment == 1 ? (span - width) / 2 : alignment == 2 ? span - width : 0; }
static void value_text(unsigned char *target, const struct element *item)
{
    const unsigned char *record = item->record;
    int32_t id = integer(record + 24), uid = integer(record + 28);
    if (id < 0) return;
    const struct face *face = &active->faces[id];
    float x = integer(record + 4), y = integer(record + 8) + integer(record + 32), span = integer(record + 12);
    uint32_t alignment = u32(record + 20), length = item->length;
    if (uid >= 0) {
        uint32_t split = 0;
        for (uint32_t i = 1; i < length; ++i) if (u32(item->value + i * 4) == 32) split = i;
        if (split) {
            const struct face *unit = &active->faces[uid];
            float number_width = text_width(face, item->value, split, 0);
            float unit_width = text_width(unit, item->value + (split + 1) * 4, length - split - 1, 0);
            x += aligned(alignment, number_width + unit_width, span);
            text_run(target, item, face, item->value, split, x, y, 0);
            text_run(target, item, unit, item->value + (split + 1) * 4, length - split - 1,
                x + number_width + 3, y + face->ascent - unit->ascent, 0);
            return;
        }
    }
    float margin = real(record + 40);
    uint32_t start = 0;
    while (start < length && y < integer(record + 8) + integer(record + 16)) {
        uint32_t end = start, word = start;
        float width = 0, limit = span - margin * 2;
        while (end < length) {
            uint32_t code = u32(item->value + end * 4);
            if (code == 10 || code == 13) break;
            const unsigned char *glyph = glyph_at(face, code);
            float step = glyph ? advance(glyph, 1) : 0;
            if (end > start) step += kern(face, u32(item->value + (end - 1) * 4), code, 1);
            if (width + step > limit && end > start) { if (word > start) end = word; break; }
            width += step; end++;
            if (code == 32) word = end;
        }
        if (end == start && u32(item->value + end * 4) != 10 && u32(item->value + end * 4) != 13) end++;
        uint32_t visible = end;
        while (visible > start && u32(item->value + (visible - 1) * 4) == 32) visible--;
        int trimmed = end < length && y + face->height * 2 > integer(record + 8) + integer(record + 16);
        static const unsigned char ellipsis[4] = { 0x26, 0x20, 0, 0 };
        float extra = trimmed ? text_width(face, ellipsis, 1, 1) : 0;
        width = text_width(face, item->value + start * 4, visible - start, 1);
        while (trimmed && visible > start && width + extra > limit) {
            visible--; width = text_width(face, item->value + start * 4, visible - start, 1);
        }
        float origin = x + margin + aligned(alignment, width + extra, span - margin * 2);
        text_run(target, item, face, item->value + start * 4, visible - start, origin, y, 1);
        if (trimmed) { text_run(target, item, face, ellipsis, 1, origin + width, y, 1); break; }
        start = end;
        if (start < length && u32(item->value + start * 4) == 13) {
            start++;
            if (start < length && u32(item->value + start * 4) == 10) start++;
        } else if (start < length && u32(item->value + start * 4) == 10) start++;
        else while (start < length && u32(item->value + start * 4) == 32) start++;
        y += face->height;
    }
}
static float absolute(float x) { return x < 0 ? -x : x; }
static void hand(unsigned char *target, const struct element *item, uint32_t angle, float length, float width)
{
    const unsigned char *record = item->record;
    int ix = integer(record + 4), iy = integer(record + 8), iw = integer(record + 12), ih = integer(record + 16);
    float cx = ix + iw / 2.0f, cy = iy + ih / 2.0f, radius = (iw < ih ? iw : ih) / 2.0f - 8;
    const unsigned char *point = active->angles + (angle % 3600) * 8;
    float dx = real(point) * radius * length, dy = -real(point + 4) * radius * length;
    float square = dx * dx + dy * dy;
    if (square <= 0) return;
    float edge = width / 2 + 0.5f, edge2 = edge * edge, inner = width / 2 - 0.5f, inner2 = inner * inner;
    int left = (int)(cx + (dx < 0 ? dx : 0) - edge - 1), top = (int)(cy + (dy < 0 ? dy : 0) - edge - 1);
    int right = (int)(cx + (dx > 0 ? dx : 0) + edge + 1), bottom = (int)(cy + (dy > 0 ? dy : 0) + edge + 1);
    for (int y = top; y <= bottom; ++y) for (int x = left; x <= right; ++x) {
        if (x < ix || x >= ix + iw || y < iy || y >= iy + ih) continue;
        float px = x - cx, py = y - cy, along = (px * dx + py * dy) / square;
        if (along < 0) along = 0; else if (along > 1) along = 1;
        float ex = px - along * dx, ey = py - along * dy, distance = ex * ex + ey * ey;
        if (distance >= edge2) continue;
        float alpha = distance <= inner2 ? 255 : 255 * (edge2 - distance) / (edge2 - inner2);
        if (absolute(alpha) > 0) pixel(target, x, y, u32(record + 36), (uint32_t)alpha);
    }
}
enum storage_status ui_draw(unsigned char *pixels)
{
    if (!active || !values) return STORAGE_BUSY;
    uint32_t color = active->background;
    for (uint32_t i = 0; i < PANEL * PANEL; ++i)
        for (uint32_t b = 0; b < 4; ++b) pixels[i * 4 + b] = color >> (b * 8);
    for (uint32_t i = 0; i < active->elements; ++i) {
        const struct element *item = &active->items[i];
        if (!item->length) continue;
        const unsigned char *record = item->record;
        uint32_t index = 0, width = u32(record + 12);
        for (uint32_t r = 0; r < item->run_count; ++r) {
            uint32_t count = u32(item->runs + r * 8), source = u32(item->runs + r * 8 + 4);
            if (source >> 24) for (uint32_t p = 0; p < count; ++p)
                pixel(pixels, integer(record + 4) + (index + p) % width, integer(record + 8) + (index + p) / width, source, source >> 24);
            index += count;
        }
        if (u32(record) == 14) {
            hand(pixels, item, (hour % 12 * 60 + minute) * 5, real(record + 48), real(record + 60));
            hand(pixels, item, minute * 60 + second, real(record + 52), real(record + 64));
            if (u32(record + 44)) hand(pixels, item, second * 60, real(record + 56), real(record + 68));
        } else value_text(pixels, item);
    }
    return STORAGE_OK;
}
