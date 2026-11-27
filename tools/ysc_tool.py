#!/usr/bin/env python3
"""YSC (GTA V script) reader: RSC7 decompress + header/native/string/code extraction.

Format references (read from sources, not invented):
- rage-formats (VIRUXE) src/resource.rs: RSC7 header + deflate body, size-from-flags.
- ysc-utils (Jaycadox) src/ysc.rs: YSC header layout, native-table rotation,
  0x4000-byte code/string page tables.
- GTA-V-Script-Decompiler (maybegreat48) ScriptHeaders.cs: same header, confirms
  pointers are 24-bit and relative to system-segment start.
"""
import struct, zlib, sys, os, json

def rsc_size(flags: int) -> int:
    s0 = ((flags >> 27) & 1) << 0
    s1 = ((flags >> 26) & 1) << 1
    s2 = ((flags >> 25) & 1) << 2
    s3 = ((flags >> 24) & 1) << 3
    s4 = ((flags >> 17) & 0x7F) << 4
    s5 = ((flags >> 11) & 0x3F) << 5
    s6 = ((flags >> 7) & 0xF) << 6
    s7 = ((flags >> 5) & 0x3) << 7
    s8 = ((flags >> 4) & 0x1) << 8
    ss = flags & 0xF
    return (0x200 << ss) * (s0 + s1 + s2 + s3 + s4 + s5 + s6 + s7 + s8)

def rotl64(v: int, n: int) -> int:
    n %= 64
    if n == 0:
        return v & 0xFFFFFFFFFFFFFFFF
    return ((v << n) | (v >> (64 - n))) & 0xFFFFFFFFFFFFFFFF

class YSC:
    def __init__(self, path):
        raw = open(path, 'rb').read()
        self.raw_size = len(raw)
        self.sha256 = __import__('hashlib').sha256(raw).hexdigest()
        if raw[:4] != b'RSC7':
            raise ValueError('not RSC7')
        self.rsc_version = struct.unpack_from('<I', raw, 4)[0]
        self.sys_flags = struct.unpack_from('<I', raw, 8)[0]
        self.gfx_flags = struct.unpack_from('<I', raw, 12)[0]
        self.sys_size = rsc_size(self.sys_flags)
        self.gfx_size = rsc_size(self.gfx_flags)
        body = raw[16:]
        do = zlib.decompressobj(-15)
        out = do.decompress(body) + do.flush()
        if len(out) < self.sys_size:
            raise ValueError(f'decompressed {len(out)} < sys_size {self.sys_size}')
        self.sys = out[:self.sys_size]          # system segment (contains YSC header)
        self.gfx = out[self.sys_size:] if len(out) > self.sys_size else b''
        self._parse_header()

    def _u32(self, p): return struct.unpack_from('<I', self.sys, p)[0]
    def _i32(self, p): return struct.unpack_from('<i', self.sys, p)[0]
    def _ptr(self, p): return self._u32(p) & 0xFFFFFF

    def _parse_header(self):
        s, p = self.sys, 0
        def ri():
            nonlocal p
            v = self._i32(p); p += 4; return v
        def rlarge():
            nonlocal p
            v = self._i32(p); p += 8; return v   # 4 read + 4 skipped
        def rptr():
            nonlocal p
            v = self._ptr(p); p += 8; return v   # 4 read(24-bit) + 4 skipped
        self.h_page_base = rlarge()
        self.h_page_map_ptr = rptr()
        self.h_code_blocks_base = rptr()
        self.h_globals_signature = ri()          # "GlobalsVersion" in decompiler
        self.h_code_size = ri()
        self.h_param_count = ri()
        self.h_static_count = ri()
        self.h_global_count = ri()               # packed: count | (blockidx<<18) per decompiler Save()
        self.h_natives_count = ri()
        self.h_statics_ptr = rptr()
        self.h_globals_ptr = rptr()
        self.h_natives_ptr = rptr()
        self.h_unk1 = rlarge()
        self.h_unk2 = rlarge()
        self.h_name_hash = ri()
        self.h_unk3 = ri()
        self.h_script_name_ptr = rptr()
        self.h_string_blocks_base = rptr()
        self.h_string_size = rlarge()
        # script name
        off = self.h_script_name_ptr
        end = self.sys.index(b'\x00', off)
        self.script_name = self.sys[off:end].decode('ascii', 'replace')
        # globals block info (per decompiler ScriptFile.Save)
        self.globals_block_index = self.h_global_count >> 18
        self.globals_count = self.h_global_count & 0x3FFFF
        self.globals_base = 0x40000 * self.globals_block_index

    def native_table(self):
        """Decrypt native hashes: each rotl-left by (code_size + i)."""
        out = []
        p = self.h_natives_ptr
        for i in range(self.h_natives_count):
            h = struct.unpack_from('<Q', self.sys, p)[0]
            p += 8
            out.append(rotl64(h, self.h_code_size + i))
        return out

    def _page_offsets(self, base_ptr, total):
        count = (total + 0x3FFF) >> 14
        offs = []
        p = base_ptr
        for _ in range(count):
            offs.append(self._ptr(p)); p += 8
        return offs

    def code(self):
        offs = self._page_offsets(self.h_code_blocks_base, self.h_code_size)
        buf = bytearray()
        for i, o in enumerate(offs):
            n = self.h_code_size % 0x4000 if (i + 1) * 0x4000 >= self.h_code_size else 0x4000
            buf += self.sys[o:o + n]
        assert len(buf) == self.h_code_size, (len(buf), self.h_code_size)
        return bytes(buf)

    def strings(self):
        """Return dict offset->string for the concatenated string table."""
        offs = self._page_offsets(self.h_string_blocks_base, self.h_string_size)
        buf = bytearray(self.h_string_size)
        off = 0
        for i, o in enumerate(offs):
            n = self.h_string_size % 0x4000 if (i + 1) * 0x4000 >= self.h_string_size else 0x4000
            buf[off:off + n] = self.sys[o:o + n]
            off += n
        res, cur, start = {}, bytearray(), 0
        for i, b in enumerate(buf):
            if b == 0:
                res[start] = bytes(cur).decode('latin1')
                cur.clear(); start = i + 1
            else:
                cur.append(b)
        return res

    def info(self):
        return {
            'file_size': self.raw_size, 'sha256': self.sha256,
            'rsc_version': self.rsc_version,
            'sys_flags': hex(self.sys_flags), 'gfx_flags': hex(self.gfx_flags),
            'sys_size': self.sys_size, 'gfx_size': self.gfx_size,
            'script_name': self.script_name, 'name_hash': hex(self.h_name_hash & 0xFFFFFFFF),
            'code_size': self.h_code_size, 'param_count': self.h_param_count,
            'static_count': self.h_static_count, 'globals_count': self.globals_count,
            'globals_block_index': self.globals_block_index, 'globals_base': self.globals_base,
            'globals_signature': self.h_globals_signature,
            'natives_count': self.h_natives_count, 'string_size': self.h_string_size,
        }

if __name__ == '__main__':
    for f in sys.argv[1:]:
        y = YSC(f)
        print(f'=== {os.path.basename(f)}')
        for k, v in y.info().items():
            print(f'  {k}: {v}')
        print()
