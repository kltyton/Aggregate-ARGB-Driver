ENTRY(bootstrap)
EXTERN(extension_usb extension_file_thread extension_file_timer extension_flush
       extension_video_render extension_default_start extension_default_round extension_stream_open)
SECTIONS
{
    . = 0x40238cc8;
    __extension_start = .;
    .bootstrap : { KEEP(*(.bootstrap)) }
    .text : { *(.text*) }
    .rodata : { *(.rodata*) }
    .data : { *(.data*) *(.sdata*) }
    .bss (NOLOAD) : { *(.bss*) *(.sbss*) *(COMMON) }
    . = ALIGN(8);
    __extension_end = .;
    __extension_size = __extension_end - __extension_start;
    ASSERT(__extension_end < 0x40300000, "Extension overlaps the retained native heap")
    /DISCARD/ : { *(.eh_frame*) *(.comment*) *(.riscv.attributes) }
}
