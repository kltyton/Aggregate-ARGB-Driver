#include <stddef.h>

void *memcpy(void *destination, const void *source, size_t count)
{
    volatile unsigned char *target = destination;
    const unsigned char *input = source;
    for (size_t i = 0; i < count; ++i) target[i] = input[i];
    return destination;
}

void *memset(void *destination, int value, size_t count)
{
    volatile unsigned char *target = destination;
    for (size_t i = 0; i < count; ++i) target[i] = (unsigned char)value;
    return destination;
}
