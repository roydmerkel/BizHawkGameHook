﻿using GameHook.Domain;
using GameHook.Domain.Interfaces;

namespace GameHook.Application
{
    public class NES_PlatformOptions : IPlatformOptions
    {
        public EndianTypes EndianType { get; } = EndianTypes.BigEndian;

        public MemoryAddressBlock[] Ranges { get; } = new List<MemoryAddressBlock>()
        {
            new MemoryAddressBlock("Internal RAM", 0x0000, 0x0400) // 2kB Internal RAM, mirrored 4 times
        }.ToArray();
    }

    public class SNES_PlatformOptions : IPlatformOptions
    {
        public EndianTypes EndianType { get; } = EndianTypes.LittleEndian;

        // Implemented the full WRAM range for the SNES. Tested with my Super Mario RPG Mapper and the BS Zelda Mapper file. -Jesco
        public MemoryAddressBlock[] Ranges { get; } = new List<MemoryAddressBlock>()
        {
        new MemoryAddressBlock("?", 0x7E0000, 0x7E3FFF),
        new MemoryAddressBlock("?", 0x7E4000, 0x7E7FFF),
        new MemoryAddressBlock("?", 0x7E8000, 0x7EBFFF),
        new MemoryAddressBlock("?", 0x7EC000, 0x7EFFFF),
        new MemoryAddressBlock("?", 0x7F0000, 0x7F3FFF),
        new MemoryAddressBlock("?", 0x7F4000, 0x7F7FFF),
        new MemoryAddressBlock("?", 0x7F8000, 0x7FBFFF),
        new MemoryAddressBlock("?", 0x7FC000, 0x7FFFFF)
        }.ToArray();
    }

    public class GB_PlatformOptions : IPlatformOptions
    {
        public EndianTypes EndianType { get; } = EndianTypes.LittleEndian;

        public MemoryAddressBlock[] Ranges { get; } = new List<MemoryAddressBlock>()
        {
            new MemoryAddressBlock("ROM Bank 00", 0x0000, 0x3FFF),
            new MemoryAddressBlock("ROM Bank 01", 0x4000, 0x7FFF),
            new MemoryAddressBlock("VRAM", 0x8000, 0x9FFF),
            new MemoryAddressBlock("External RAM (Part 1)", 0xA000, 0xAFFF),
            new MemoryAddressBlock("External RAM (Part 2)", 0xB000, 0xBFFF),
            new MemoryAddressBlock("Work RAM (Bank 0)", 0xC000, 0xCFFF),
            new MemoryAddressBlock("Work RAM (Bank 1)", 0xD000, 0xDFFF),
            new MemoryAddressBlock("I/O Registers", 0xFF00, 0xFF7F),
            new MemoryAddressBlock("High RAM", 0xFF80, 0xFFFF),
            // RAM Banks 0x02:D000 to 0x07:D000 for GBC
            new MemoryAddressBlock("Work RAM (Bank 2)", 0x10000, 0x10FFF),
            new MemoryAddressBlock("Work RAM (Bank 3)", 0x11000, 0x11FFF),
            new MemoryAddressBlock("Work RAM (Bank 4)", 0x12000, 0x12FFF),
            new MemoryAddressBlock("Work RAM (Bank 5)", 0x13000, 0x13FFF),
            new MemoryAddressBlock("Work RAM (Bank 6)", 0x14000, 0x14FFF),
            new MemoryAddressBlock("Work RAM (Bank 7)", 0x15000, 0x15FFF)
        }.ToArray();
    }

    public class GBA_PlatformOptions : IPlatformOptions
    {
        public EndianTypes EndianType { get; } = EndianTypes.BigEndian;

        public MemoryAddressBlock[] Ranges { get; } = new List<MemoryAddressBlock>()
        {
            new MemoryAddressBlock("Partial EWRAM 1", 0x00000000, 0x00003FFF),
            new MemoryAddressBlock("Partial EWRAM 2", 0x02020000, 0x02020FFF),
            new MemoryAddressBlock("Partial EWRAM 3", 0x02021000, 0x02022FFF),
            new MemoryAddressBlock("Partial EWRAM 4", 0x02023000, 0x02023FFF),
            new MemoryAddressBlock("Partial EWRAM 5", 0x02024000, 0x02027FFF),
            new MemoryAddressBlock("Partial EWRAM 6", 0x02030000, 0x02033FFF),
            new MemoryAddressBlock("Partial EWRAM 7", 0x02037000, 0x02039999),
            new MemoryAddressBlock("Partial EWRAM 8", 0x0203A000, 0x0203AFFF),
            new MemoryAddressBlock("Partial EWRAM 9", 0x0203B000, 0x0203BFFF),
            new MemoryAddressBlock("Partial EWRAM 10", 0x0203C000, 0x0203CFFF),
            new MemoryAddressBlock("Partial EWRAM 11", 0x0203D000, 0x0203DFFF),
            new MemoryAddressBlock("IWRAM", 0x03001000, 0x03004FFF),
            new MemoryAddressBlock("IWRAM", 0x03005000, 0x03005FFF),
            new MemoryAddressBlock("IWRAM", 0x03006000, 0x03006FFF),
            new MemoryAddressBlock("IWRAM", 0x03007000, 0x03007FFF)
        }.ToArray();
    }

    public class NDS_PlatformOptions : IPlatformOptions
    {
        public EndianTypes EndianType { get; } = EndianTypes.BigEndian;

        public MemoryAddressBlock[] Ranges { get; } = Array.Empty<MemoryAddressBlock>();
    }

    /* ===== PlayStation ===== */
    /* http://www.raphnet.net/electronique/psx_adaptor/Playstation.txt */
    /* https://problemkaputt.de/psx-spx.htm */
    public class PSX_PlatformOptions : IPlatformOptions
    {
        public EndianTypes EndianType { get; } = EndianTypes.BigEndian;
        public MemoryAddressBlock[] Ranges { get; } = new List<MemoryAddressBlock>()
        {
            new MemoryAddressBlock("Kernel", 0x00000000, 0x0000ffff),
            new MemoryAddressBlock("User Memory", 0x00010000, 0x001fffff),
            new MemoryAddressBlock("Parallel Port", 0x1f000000, 0x001fffff),
            new MemoryAddressBlock("Scratch Pad", 0x1f800000, 0x1f8003ff),
            new MemoryAddressBlock("User Memory", 0x1f801000, 0x1f802fff),
            new MemoryAddressBlock("Kernel and User Memory Mirror", 0x80000000, 0x801fffff),
            new MemoryAddressBlock("Kernel and User Memory Mirror", 0xa0000000, 0xa01fffff),
            new MemoryAddressBlock("BIOS", 0xbfc00000, 0xbfc7ffff)
        }.ToArray();
    }
}
