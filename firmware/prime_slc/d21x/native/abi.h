#ifndef PRIME_SLC_NATIVE_ABI_H
#define PRIME_SLC_NATIVE_ABI_H

#include <stddef.h>
#include <stdint.h>

typedef void *native_thread;
typedef void *native_semaphore;

struct native_statfs { size_t block_size, blocks, free_blocks; };
struct native_filesystem_ops {
    const char *name;
    uint32_t flags;
    const void *file_ops;
    const void *mount, *unmount, *make;
    int (*statfs)(void *, struct native_statfs *);
};
struct native_filesystem {
    void *device;
    const char *path;
    const struct native_filesystem_ops *ops;
    void *data;
};
_Static_assert(offsetof(struct native_filesystem, ops) == 0x10, "D21x filesystem ABI");
_Static_assert(offsetof(struct native_filesystem_ops, statfs) == 0x30, "D21x statfs ABI");

static inline int native_free_space(const char *path, uint64_t *bytes)
{
    struct native_filesystem *fs = ((struct native_filesystem *(*)(const char *))0x400151c0)(path);
    struct native_statfs status;
    if (!fs || !fs->ops->statfs || fs->ops->statfs(fs, &status) != 0) return -1;
    *bytes = (uint64_t)status.block_size * status.free_blocks;
    return 0;
}

static inline void *native_allocate(size_t size)
{ return ((void *(*)(size_t))0x40014760)(size); }
static inline void native_free(void *memory)
{ ((void (*)(void *))0x400148b0)(memory); }
static inline native_thread native_thread_create(const char *name, void (*entry)(void *), void *argument,
    uint32_t stack_size, uint8_t priority, uint32_t time_slice)
{ return ((native_thread (*)(const char *, void (*)(void *), void *, uint32_t, uint8_t, uint32_t))0x40013490)
    (name, entry, argument, stack_size, priority, time_slice); }
static inline int native_thread_start(native_thread thread)
{ return ((int (*)(native_thread))0x400133c0)(thread); }
static inline native_semaphore native_semaphore_create(const char *name)
{ return ((native_semaphore (*)(const char *, uint32_t, uint8_t))0x40011690)(name, 0, 0); }
static inline native_semaphore native_semaphore_create_count(const char *name, uint32_t count)
{ return ((native_semaphore (*)(const char *, uint32_t, uint8_t))0x40011690)(name, count, 0); }
static inline int native_semaphore_wait(native_semaphore semaphore)
{ return ((int (*)(native_semaphore, intptr_t))0x40011740)(semaphore, -1); }
static inline int native_semaphore_signal(native_semaphore semaphore)
{ return ((int (*)(native_semaphore))0x40011860)(semaphore); }
static inline intptr_t native_open(const char *path, uint32_t flags)
{ return ((intptr_t (*)(const char *, uint32_t, uint32_t))0x40014a20)(path, flags, 0644); }
static inline intptr_t native_read(intptr_t file, void *buffer, size_t count)
{ return ((intptr_t (*)(intptr_t, void *, size_t))0x40014af0)(file, buffer, count); }
static inline intptr_t native_write(intptr_t file, const void *buffer, size_t count)
{ return ((intptr_t (*)(intptr_t, const void *, size_t))0x40014b50)(file, buffer, count); }
static inline int native_sync(intptr_t file)
{ return ((int (*)(intptr_t))0x40014cd0)(file); }
static inline int64_t native_seek(intptr_t file, int64_t offset, int whence)
{ return ((int64_t (*)(intptr_t, int64_t, int))0x40014bb0)(file, offset, whence); }
static inline int native_close(intptr_t file)
{ return ((int (*)(intptr_t))0x40014aa0)(file); }
static inline int64_t native_file_size(const char *path)
{
    intptr_t file = native_open(path, 0);
    if (file < 0) return -1;
    int64_t size = ((int64_t (*)(intptr_t, int64_t, int))0x40014bb0)(file, 0, 2);
    return native_close(file) == 0 ? size : -1;
}
static inline int native_rename(const char *source, const char *destination)
{ return ((int (*)(const char *, const char *))0x40014c50)(source, destination); }
static inline int native_unlink(const char *path)
{ return ((int (*)(const char *))0x400164b0)(path); }
static inline int native_file_is_open(const char *path)
{ return ((int (*)(const char *))0x40016040)(path) == 0; }
static inline uint32_t native_checksum(const void *data, size_t count)
{ return ((uint32_t (*)(const void *, size_t))0x400373e0)(data, count); }
static inline int native_usb_write(const void *data, uint32_t count)
{ return ((int (*)(uint32_t, const void *, uint32_t))0x400368d0)(0x81, data, count); }
static inline int native_usb_receive(void *data)
{ return ((int (*)(uint32_t, void *, uint32_t))0x40036c40)(2, data, 0xfa000); }

#endif
