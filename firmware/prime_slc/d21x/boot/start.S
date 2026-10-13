.section .bootstrap,"ax",@progbits
.option norelax
.option norvc
.global bootstrap
bootstrap:
    csrw mie, zero
    csrw mip, zero
    li t0, 0x400ff058
    li t1, 0x40238cc8
    lui t2, %hi(__extension_size)
    addi t2, t2, %lo(__extension_size)
1:
    lbu t3, 0(t0)
    sb t3, 0(t1)
    addi t0, t0, 1
    addi t1, t1, 1
    addi t2, t2, -1
    bnez t2, 1b
    fence.i
    li t0, 0x40000008
    jr t0

.section .text.original_usb,"ax",@progbits
.global original_usb
original_usb:
    .2byte 0x7111, 0xf9a2, 0xf5a6, 0xfd86
    li t0, 0x40032bf8
    jr t0

.section .text.extension_file_thread,"ax",@progbits
.global extension_file_thread
extension_file_thread:
    addi sp, sp, -32
    sd ra, 0(sp)
    sd a0, 8(sp)
    call extension_initialize
    ld a0, 8(sp)
    ld ra, 0(sp)
    addi sp, sp, 32
    lui a5, 0x0d5a5
    .2byte 0x7119
    addi a5, a5, 1450
    .2byte 0xe03e
    li t0, 0x4003395c
    jr t0

.section .text.extension_file_timer,"ax",@progbits
.global extension_file_timer
extension_file_timer:
    addi sp, sp, -32
    sd ra, 0(sp)
    call extension_file_clock
    ld ra, 0(sp)
    addi sp, sp, 32
    li a4, 0x4022e840
    lw a4, 0(a4)
    li t0, 0x40033b10
    jr t0

.section .text.original_flush,"ax",@progbits
.global original_flush
original_flush:
    .2byte 0x711d, 0xec86, 0xe8a2, 0xe4a6
    li t0, 0x4002fa88
    jr t0

.section .text.original_video_render,"ax",@progbits
.global original_video_render
original_video_render:
    .2byte 0x1101, 0xec06, 0xe822, 0xe426
    li t0, 0x400a5688
    jr t0

.section .text.original_stream_open,"ax",@progbits
.global original_stream_open
original_stream_open:
    .2byte 0x7179, 0xec26, 0xe84a, 0xe44e
    li t0, 0x400a5158
    jr t0

.section .text.original_default_start,"ax",@progbits
.global original_default_start
original_default_start:
    addi sp, sp, -544
    li a1, 0
    li a0, 0x400d7800
    li t0, 0x4003383e
    jr t0

.section .text.extension_default_round,"ax",@progbits
.global extension_default_round
extension_default_round:
    lw a0, 1064(s0)
    addi a0, a0, -1
    beqz a0, 1f
    .word 0x07341a8b
    ld a7, 8(s5)
    li a0, 0x4009a810
    jr a0
1:
    li a0, 0x4009a648
    jr a0
