#include "video_stream.h"
#include "../native/abi.h"
#include "../native/compression.h"
#include "../../third_party/miniz/miniz_tinfl.h"

#define BLOCK_BYTES 65536u
#define RAW_BLOCK 0x80000000u
struct video_header {
    uint32_t magic, version;
    uint64_t size;
    uint32_t block_bytes, blocks, index, fps;
    float x, y, width, height;
    uint32_t rotation, duration, reserved, checksum;
};
struct block_index { uint32_t offset, size_flags, checksum; };
struct native_frame {
    uint32_t retained[7];
    int32_t width, height, crop_enable, crop_x, crop_y, crop_width, crop_height;
    uint32_t format, buffer_flags;
    uint64_t pts;
    uint32_t id, flags;
};
struct video_stream {
    int64_t (*read)(struct video_stream *, void *, int64_t);
    int64_t (*write)(struct video_stream *, const void *, int64_t);
    int64_t (*tell)(struct video_stream *);
    int32_t (*close)(struct video_stream *);
    int64_t (*seek)(struct video_stream *, int64_t, int32_t);
    int64_t (*size)(struct video_stream *);
    intptr_t file;
    uint64_t position, file_size;
    struct video_header header;
    uint32_t cached_block, cached_bytes;
    tinfl_decompressor inflater;
    unsigned char compressed[BLOCK_BYTES], decoded[BLOCK_BYTES];
};
_Static_assert(sizeof(struct video_header) == 64, "HLV1 header");
_Static_assert(sizeof(struct block_index) == 12, "HLV1 block index");
_Static_assert(sizeof(struct native_frame) == 80 && offsetof(struct native_frame, crop_enable) == 36, "D21x frame descriptor");
_Static_assert(offsetof(struct video_stream, file) == 48, "D21x six-slot stream ABI");
static volatile uint32_t opened;
static uint32_t read_errors, read_bytes;
static struct video_header current;
extern int32_t original_stream_open(const char *, void **, int32_t);

static int read_exact(intptr_t file, void *buffer, size_t bytes)
{
    size_t offset = 0;
    while (offset < bytes) {
        intptr_t count = native_read(file, (unsigned char *)buffer + offset, bytes - offset);
        if (count <= 0 || (size_t)count > bytes - offset) return -1;
        offset += (size_t)count;
    }
    return 0;
}
static int load_block(struct video_stream *stream, uint32_t index)
{
    if (index == stream->cached_block) return 0;
    struct block_index block;
    if (index >= stream->header.blocks || native_seek(stream->file, stream->header.index + (int64_t)index * sizeof(block), 0) < 0 ||
        read_exact(stream->file, &block, sizeof(block)) != 0) return -1;
    uint32_t bytes = block.size_flags & ~RAW_BLOCK;
    uint64_t remaining = stream->header.size - (uint64_t)index * BLOCK_BYTES;
    uint32_t expected = remaining > BLOCK_BYTES ? BLOCK_BYTES : (uint32_t)remaining;
    if (!bytes || bytes > BLOCK_BYTES || block.offset < stream->header.index + (uint64_t)stream->header.blocks * sizeof(block) ||
        (uint64_t)block.offset + bytes > stream->file_size || native_seek(stream->file, block.offset, 0) < 0 ||
        read_exact(stream->file, stream->compressed, bytes) != 0) return -1;
    if (block.size_flags & RAW_BLOCK) {
        if (bytes != expected) return -1;
        for (size_t offset = 0; offset < bytes; ++offset) stream->decoded[offset] = stream->compressed[offset];
    } else {
        tinfl_init(&stream->inflater);
        size_t consumed = bytes, written = expected;
        if (tinfl_decompress(&stream->inflater, stream->compressed, &consumed, stream->decoded, stream->decoded, &written,
            TINFL_FLAG_PARSE_ZLIB_HEADER | TINFL_FLAG_USING_NON_WRAPPING_OUTPUT_BUF) != TINFL_STATUS_DONE ||
            consumed != bytes || written != expected) return -1;
    }
    if (native_checksum(stream->decoded, expected) != block.checksum) return -1;
    stream->cached_block = index; stream->cached_bytes = expected;
    return 0;
}
static int64_t stream_read(struct video_stream *stream, void *buffer, int64_t bytes)
{
    if (bytes < 0) return -1;
    uint64_t remaining = stream->header.size - stream->position;
    uint64_t requested = (uint64_t)bytes < remaining ? (uint64_t)bytes : remaining;
    uint64_t copied = 0;
    while (copied < requested) {
        uint32_t index = stream->position / BLOCK_BYTES, offset = stream->position % BLOCK_BYTES;
        if (load_block(stream, index) != 0) { ++read_errors; return -1; }
        uint64_t count = stream->cached_bytes - offset;
        if (count > requested - copied) count = requested - copied;
        for (uint64_t byte = 0; byte < count; ++byte) ((unsigned char *)buffer)[copied + byte] = stream->decoded[offset + byte];
        stream->position += count; copied += count; read_bytes += (uint32_t)count;
    }
    return (int64_t)copied;
}
static int64_t stream_write(struct video_stream *stream, const void *data, int64_t bytes)
{ (void)stream; (void)data; (void)bytes; return -1; }
static int64_t stream_tell(struct video_stream *stream) { return (int64_t)stream->position; }
static int64_t stream_size(struct video_stream *stream) { return (int64_t)stream->header.size; }
static int64_t stream_seek(struct video_stream *stream, int64_t offset, int32_t origin)
{
    uint64_t base;
    switch (origin) { case 0: base = 0; break; case 1: base = stream->position; break; case 2: base = stream->header.size; break; default: return -1; }
    if (offset < 0) {
        uint64_t difference = (uint64_t)(-(offset + 1)) + 1;
        if (difference > base) return -1;
        stream->position = base - difference;
    } else {
        if ((uint64_t)offset > stream->header.size - base) return -1;
        stream->position = base + (uint64_t)offset;
    }
    return (int64_t)stream->position;
}
static int32_t stream_close(struct video_stream *stream)
{
    int32_t result = native_close(stream->file);
    if (opened) --opened;
    native_free(stream);
    return result;
}
static int media_path(const char *path)
{
    const char *expected = storage_media_path();
    while (*path && *path == *expected) { ++path; ++expected; }
    return *path == *expected;
}
int32_t extension_stream_open(const char *uri, void **out, int32_t flags)
{
    if (!media_path(uri) || flags != 0) return original_stream_open(uri, out, flags);
    intptr_t file = native_open(uri, 0);
    if (file < 0) return -1;
    struct video_header header;
    int valid = read_exact(file, &header, sizeof(header)) == 0;
    if (!valid || header.magic != 0x31564c48) {
        if (native_close(file) != 0) return -1;
        return original_stream_open(uri, out, flags);
    }
    int64_t length = native_seek(file, 0, 2);
    if (header.version != 1 || header.block_bytes != BLOCK_BYTES || !header.size || !header.blocks || header.index != 64 ||
        header.blocks != (header.size + BLOCK_BYTES - 1) / BLOCK_BYTES || header.size > INT64_MAX || !header.fps || header.fps > 60 ||
        !header.duration || header.rotation != 0 || header.reserved != 0 || native_checksum(&header, 60) != header.checksum || length < 0 ||
        header.index + (uint64_t)header.blocks * sizeof(struct block_index) > (uint64_t)length ||
        !(header.x >= 0 && header.y >= 0 && header.width > 0 && header.height > 0 &&
            header.x + header.width <= 1.000001f && header.y + header.height <= 1.000001f)) {
        native_close(file); return -1;
    }
    struct video_stream *stream = native_allocate(sizeof(*stream));
    if (!stream) { native_close(file); return -1; }
    stream->read = stream_read; stream->write = stream_write; stream->tell = stream_tell;
    stream->close = stream_close; stream->seek = stream_seek; stream->size = stream_size;
    stream->file = file; stream->position = 0; stream->file_size = (uint64_t)length;
    stream->header = header; stream->cached_block = UINT32_MAX; stream->cached_bytes = 0;
    if (!opened) { read_errors = 0; read_bytes = 0; }
    current = header; ++opened; *out = stream;
    return 0;
}
uint8_t video_stream_stored(void)
{
    intptr_t file = native_open(storage_media_path(), 0);
    if (file < 0) return 0;
    uint32_t magic;
    int valid = read_exact(file, &magic, sizeof(magic)) == 0 && magic == 0x31564c48;
    if (native_close(file) != 0) return 0;
    return valid;
}
uint8_t video_stream_active(void) { return opened != 0; }
uint32_t video_stream_duration(void) { return current.duration; }
uint32_t video_stream_errors(void) { return read_errors; }
uint32_t video_stream_bytes(void) { return read_bytes; }
int video_stream_crop(const void *frame, unsigned char copy[80])
{
    if (!opened) return 0;
    struct native_frame descriptor;
    for (size_t index = 0; index < sizeof(descriptor); ++index) ((unsigned char *)&descriptor)[index] = ((const unsigned char *)frame)[index];
    int32_t visible_x = descriptor.crop_enable ? descriptor.crop_x : 0, visible_y = descriptor.crop_enable ? descriptor.crop_y : 0;
    int32_t visible_width = descriptor.crop_enable ? descriptor.crop_width : descriptor.width;
    int32_t visible_height = descriptor.crop_enable ? descriptor.crop_height : descriptor.height;
    if (visible_x < 0 || visible_y < 0 || visible_width < 2 || visible_height < 2 ||
        (int64_t)visible_x + visible_width > descriptor.width || (int64_t)visible_y + visible_height > descriptor.height) return -1;
    int32_t x = (visible_x + (int32_t)(current.x * visible_width)) & ~1;
    int32_t y = (visible_y + (int32_t)(current.y * visible_height)) & ~1;
    if (x < visible_x) x = (visible_x + 1) & ~1;
    if (y < visible_y) y = (visible_y + 1) & ~1;
    int32_t width = (int32_t)(current.width * visible_width) & ~1;
    int32_t height = (int32_t)(current.height * visible_height) & ~1;
    if (width < 2) width = 2;
    if (height < 2) height = 2;
    if (width > visible_x + visible_width - x) width = (visible_x + visible_width - x) & ~1;
    if (height > visible_y + visible_height - y) height = (visible_y + visible_height - y) & ~1;
    if (width < 2 || height < 2) return -1;
    descriptor.crop_enable = 1; descriptor.crop_x = x; descriptor.crop_y = y; descriptor.crop_width = width; descriptor.crop_height = height;
    for (size_t index = 0; index < sizeof(descriptor); ++index) copy[index] = ((const unsigned char *)&descriptor)[index];
    return 1;
}
