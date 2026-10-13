# PRIME SLC II LCD 固件扩展

扩展让匹配的屏幕保存并播放已准备的动画，并在设备端绘制监控文字和时钟。后台只更新变化的数据；GUI 负责裁剪、编码、容量判断和字形准备。源码包含扩展和升级包工具，不包含原厂固件、私有动画、字体或厂商 DLL。

```mermaid
flowchart LR
    Stock[自行取得的匹配原厂包] --> Package[升级包工具]
    Source[扩展 C 与汇编源码] --> Compiler[RISC-V 编译器]
    Compiler --> ELF[扩展 ELF]
    ELF --> Package
    Package --> Output[本机升级包]
```

## 适用范围

只支持白色 PRIME SLC II LCD 的 D21x 平台及以下 1.0.4 原厂包：

```text
E5D2826D076F7F0490BA7745C01CD655CBA38BC88391C8755DF254F7A913E386
```

打包工具核对完整包、可执行镜像、原始指令、内存地址和分区上限，保留另一平台的数据。硬编码地址属于这个镜像的 ABI，不能套用到其他版本。生成升级包不代表已在另一台设备上验证启动。

## 构建

需要 .NET 10 SDK 和 Xuantie-900-gcc-elf-newlib-mingw-V2.6.1 工具链。工具链来自 [Luban-Lite SDK](https://github.com/artinchip/luban-lite)，参考版本 `77fbe30dd2de366fae702ece84e1694153d94591`。工具链目录下应存在 `bin/riscv64-unknown-elf-gcc.exe`、`nm` 和 `objcopy`。

在仓库根目录执行，并把参数替换为自己的实际路径：

```powershell
.\firmware\prime_slc\tools\BuildFirmware.ps1 `
  -StockPackage '<原厂 1.0.4 升级包的绝对路径>' `
  -Toolchain '<Xuantie 工具链目录>' `
  -TemporaryDirectory '<构建临时目录>' `
  -ReportPath '<构建报告的绝对路径>' `
  -OutputDirectory '.\dist\HardwareLite.Firmware\d21x'
```

输出包括 `screen.elf`、扩展载荷及 `PRIME_SLC_II_WHT_1.0.4_local.bin`。用 GUI 的固件维护入口选择升级包并核对机型；该入口检查文件结构和 SHA-256，并等待升级后设备重新连接。升级包含有原厂代码，仅保留在本机，不随仓库分发。

## 源码职责

| 目录 | 职责 |
| --- | --- |
| `d21x/boot` | 启动与链接布局 |
| `d21x/usb` | 扩展通信协议和工作线程 |
| `d21x/media` | 存储、随机读解压与动画播放 |
| `d21x/display` | 像素叠加、文字排版与时钟绘制 |
| `d21x/native` | 匹配原厂 ABI 和内存接口 |
| `tools/D21xPackage` | 升级包校验与合成 |
| `third_party/miniz` | MIT 许可的 DEFLATE 解压代码 |
