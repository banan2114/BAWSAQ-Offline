#!/usr/bin/env python3
"""Automated regression tests for tools/ysc_tool.py and tools/ysc_disasm.py."""
import struct
import sys
import tempfile
import unittest
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))

from ysc_tool import YSC
from ysc_disasm import (
    Disassembler,
    JUMP_OPS,
    OPCODES,
    OPCODES_V1,
    load_crossmap,
    map_opcode,
)


def make_synthetic_ysc(code_bytes: bytes, rsc_version: int = 12, script_name: str = "test_script") -> bytes:
    """Build a minimal valid RSC7 YSC binary for negative/unit tests."""
    # Place code blocks pointer table, string block table, script name, and code inside sys segment.
    # Layout in sys (0x1000 bytes, flags=0x80000000? Let's use rsc_flags=0x0 -> 0? No: flags=0x8000000 -> s0=1, ss=0 -> 0x200 * 1 = 0x200).
    # Better: ss=3 (0x1000 bytes per unit), s0=1 -> flags = (1 << 27) | 3 = 0x08000003 -> 0x1000 bytes.
    sys_buf = bytearray(0x1000)
    code_off = 0x100
    code_ptr_off = 0x80
    str_ptr_off = 0x90
    name_off = 0xA0
    str_off = 0xC0

    sys_buf[code_off:code_off + len(code_bytes)] = code_bytes
    struct.pack_into("<I", sys_buf, code_ptr_off, 0x50000000 | code_off)
    struct.pack_into("<I", sys_buf, str_ptr_off, 0x50000000 | str_off)
    nb = script_name.encode("ascii") + b"\x00"
    sys_buf[name_off:name_off + len(nb)] = nb
    sys_buf[str_off:str_off + 2] = b"x\x00"

    # Populate YSC header
    struct.pack_into("<I", sys_buf, 0x08, 0x50000000)                  # page_map_ptr
    struct.pack_into("<I", sys_buf, 0x10, 0x50000000 | code_ptr_off)   # code_blocks_base
    struct.pack_into("<i", sys_buf, 0x18, 0x12345678)                  # globals_signature
    struct.pack_into("<i", sys_buf, 0x1C, len(code_bytes))             # code_size
    struct.pack_into("<i", sys_buf, 0x28, 0)                           # natives_count
    struct.pack_into("<I", sys_buf, 0x40, 0x50000000)                  # natives_ptr
    struct.pack_into("<I", sys_buf, 0x58, 0x11223344)                  # name_hash
    struct.pack_into("<I", sys_buf, 0x60, 0x50000000 | name_off)       # script_name_ptr
    struct.pack_into("<I", sys_buf, 0x68, 0x50000000 | str_ptr_off)    # string_blocks_base
    struct.pack_into("<i", sys_buf, 0x70, 2)                           # string_size

    co = zlib.compressobj(level=6, wbits=-15)
    comp = co.compress(bytes(sys_buf)) + co.flush()
    hdr = struct.pack("<4sIII", b"RSC7", rsc_version, 0x08000003, 0)
    return hdr + comp


class TestYSCTools(unittest.TestCase):
    EXPECTED = {
        "stock_controller1.ysc": {
            "rsc_version": 12,
            "script_name": "stock_controller",
            "name_hash": "0xc9833b07",
            "globals_signature_hex": "0xa2be51ac",
            "opcode_table": "v2",
            "opcode_count": 131,
            "functions": 109,
            "instrs": 15665,
            "natives": 75,
        },
        "stock_controller2.ysc": {
            "rsc_version": 10,
            "script_name": "stock_controller",
            "name_hash": "0xc9833b07",
            "globals_signature_hex": "0x86ef03cb",
            "opcode_table": "v1",
            "opcode_count": 128,
            "functions": 96,
            "instrs": 15289,
            "natives": 70,
        },
        "appinternet1.ysc": {
            "rsc_version": 12,
            "script_name": "appInternet",
            "name_hash": "0x6a172273",
            "globals_signature_hex": "0xa2be51ac",
            "opcode_table": "v2",
            "opcode_count": 131,
            "functions": 9777,
            "instrs": 1120191,
            "natives": 1315,
        },
        "appinternet2.ysc": {
            "rsc_version": 10,
            "script_name": "appinternet",
            "name_hash": "0x6a172273",
            "globals_signature_hex": "0x86ef03cb",
            "opcode_table": "v1",
            "opcode_count": 128,
            "functions": 671,
            "instrs": 122819,
            "natives": 440,
        },
    }

    def test_opcode_tables(self):
        self.assertEqual(len(OPCODES), 131)
        self.assertEqual(len(OPCODES_V1), 128)
        self.assertEqual(OPCODES[94:97], ["LOCAL_U24", "LOCAL_U24_LOAD", "LOCAL_U24_STORE"])
        self.assertEqual(OPCODES_V1[94:97], ["GLOBAL_U24", "GLOBAL_U24_LOAD", "GLOBAL_U24_STORE"])
        self.assertEqual(map_opcode(94, "v1"), 97)
        self.assertEqual(map_opcode(127, "v1"), 130)
        self.assertEqual(map_opcode(94, "v2"), 94)
        with self.assertRaises(ValueError):
            map_opcode(128, "v1")
        with self.assertRaises(ValueError):
            map_opcode(131, "v2")
        with self.assertRaises(ValueError):
            map_opcode(0, "v99")

    def test_all_four_ysc_files(self):
        for fname, exp in self.EXPECTED.items():
            path = ROOT / "game-scripts" / fname
            self.assertTrue(path.exists(), f"missing {path}")
            y = YSC(str(path))
            info = y.info()
            self.assertEqual(info["rsc_version"], exp["rsc_version"])
            self.assertEqual(info["script_name"], exp["script_name"])
            self.assertEqual(info["name_hash"], exp["name_hash"])
            self.assertEqual(info["globals_signature_hex"], exp["globals_signature_hex"])
            self.assertEqual(info["natives_count"], exp["natives"])

            d = Disassembler(y)
            self.assertEqual(d.opcode_table, exp["opcode_table"])
            self.assertEqual(d.opcode_count, exp["opcode_count"])
            self.assertEqual(len(d.functions), exp["functions"])
            self.assertEqual(len(d.instrs), exp["instrs"])

            # Verify full byte coverage & instruction boundary integrity
            self.assertEqual(sum(ins.size for ins in d.instrs), len(d.code))
            ins_offs = {ins.off for ins in d.instrs}
            for ins in d.instrs:
                if ins.op in JUMP_OPS or ins.op == 93:
                    self.assertIn(ins.jump, ins_offs)
                elif ins.op == 101:
                    for _, tgt in ins.args["entries"]:
                        self.assertIn(tgt, ins_offs)

            # Explicit wrong table must fail cleanly
            wrong = "v1" if exp["opcode_table"] == "v2" else "v2"
            with self.assertRaises(ValueError):
                Disassembler(y, opcode_table=wrong)

    def test_crossmaps_and_globals(self):
        s1 = Disassembler(YSC(str(ROOT / "game-scripts" / "stock_controller1.ysc")))
        x_s1 = load_crossmap(str(ROOT / "tools" / "xmap_stock1.json"))
        self.assertEqual(len(set(s1.native_table) - set(x_s1)), 0)
        self.assertGreater(len(s1.find_globals([65064, 65065, 65066, 80362])), 0)

        a1 = Disassembler(YSC(str(ROOT / "game-scripts" / "appinternet1.ysc")))
        x_a1 = load_crossmap(str(ROOT / "tools" / "xmap_app1.json"))
        self.assertEqual(len(set(a1.native_table) - set(x_a1)), 0)
        self.assertGreater(len(a1.find_globals([65064, 65065, 65066, 65068, 77568, 80362])), 0)

        # *2 scripts have zero references to *1 runtime globals
        s2 = Disassembler(YSC(str(ROOT / "game-scripts" / "stock_controller2.ysc")))
        self.assertEqual(len(s2.find_globals([65064, 65065, 77568, 80362])), 0)
        self.assertGreater(len(s2.find_globals([46183, 49064, 51056, 51057, 53868, 53869])), 0)

        a2 = Disassembler(YSC(str(ROOT / "game-scripts" / "appinternet2.ysc")))
        self.assertEqual(len(a2.find_globals([65064, 65065, 77568, 80362])), 0)
        self.assertGreater(len(a2.find_globals([46183, 53868, 53869, 66227, 67010])), 0)
        # Day-1 NETWORK_IS_SIGNED_ONLINE hash
        self.assertIn(0x1077788E268557C2, set(a2.native_table))

    def test_rejects_corrupt_or_invalid_table(self):
        # Synthetic valid minimal function: ENTER(0,0,0), LEAVE(0,0)
        good_code = bytes([45, 0, 0, 0, 0, 46, 0, 0])
        # Corrupt opcode 255
        bad_code = bytes([45, 0, 0, 0, 0, 255, 46, 0, 0])
        with tempfile.TemporaryDirectory() as td:
            gp = Path(td) / "good.ysc"
            bp = Path(td) / "bad.ysc"
            gp.write_bytes(make_synthetic_ysc(good_code, rsc_version=12))
            bp.write_bytes(make_synthetic_ysc(bad_code, rsc_version=12))
            d_good = Disassembler(YSC(str(gp)))
            self.assertEqual(len(d_good.functions), 1)
            with self.assertRaises(ValueError):
                Disassembler(YSC(str(gp)), opcode_table="invalid")
            with self.assertRaises(ValueError):
                Disassembler(YSC(str(bp)))


if __name__ == "__main__":
    unittest.main()
