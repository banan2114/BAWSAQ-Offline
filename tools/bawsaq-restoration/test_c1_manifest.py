#!/usr/bin/env python3
"""Automated tests for tools/bawsaq-restoration/c1-patch-manifest.json and verify_c1_manifest.py."""
import copy
import json
import sys
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO_ROOT = HERE.parents[1]
TOOLS_DIR = REPO_ROOT / "tools"
for p in (str(HERE), str(TOOLS_DIR)):
    if p not in sys.path:
        sys.path.insert(0, p)

from ysc_tool import YSC  # noqa: E402
from ysc_disasm import Disassembler  # noqa: E402
from verify_c1_manifest import verify_manifest  # noqa: E402


class TestC1Manifest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = json.loads((HERE / "c1-patch-manifest.json").read_text(encoding="utf-8"))
        cls.ysc = YSC(str(REPO_ROOT / cls.manifest["source_ysc"]["path"]))
        cls.disasm = Disassembler(cls.ysc, opcode_table=cls.manifest["source_ysc"]["opcode_table"])

    def test_valid_manifest_passes(self):
        summary = verify_manifest(self.manifest, ysc=self.ysc, disasm=self.disasm)
        self.assertEqual(summary["func_index"], 9474)
        self.assertEqual(summary["page_index"], 191)
        self.assertEqual(summary["signature_pc"], "0x2fc3d8")
        self.assertEqual(summary["blocks_verified"], 123)
        self.assertEqual(summary["patch_count"], 2)
        for _, depth in summary["leave_depths"]:
            self.assertEqual(depth, 0)

    def test_rejects_wrong_sha256(self):
        m = copy.deepcopy(self.manifest)
        m["source_ysc"]["sha256"] = "0" * 64
        with self.assertRaises(AssertionError):
            verify_manifest(m, ysc=self.ysc, disasm=self.disasm)

    def test_rejects_wrong_original_bytes(self):
        m = copy.deepcopy(self.manifest)
        m["patches"][0]["original_hex"] = "00000000"
        m["patches"][0]["original_spaced"] = "00 00 00 00"
        with self.assertRaises(AssertionError):
            verify_manifest(m, ysc=self.ysc, disasm=self.disasm)

    def test_rejects_non_unique_signature(self):
        m = copy.deepcopy(self.manifest)
        # Truncate signature to 2 bytes ('56 17') which occurs multiple times
        m["signature"]["hex"] = "5617"
        m["signature"]["spaced_hex"] = "56 17"
        m["signature"]["length"] = 2
        m["signature"]["end_pc"] = hex(int(m["signature"]["start_pc"], 16) + 2)
        with self.assertRaises(AssertionError):
            verify_manifest(m, ysc=self.ysc, disasm=self.disasm)

    def test_rejects_wrong_page(self):
        m = copy.deepcopy(self.manifest)
        m["expected_page"]["page_index"] = 190
        with self.assertRaises(AssertionError):
            verify_manifest(m, ysc=self.ysc, disasm=self.disasm)

    def test_rejects_neighbor_boundary_corruption(self):
        m = copy.deepcopy(self.manifest)
        # Replace 4-byte NATIVE with a 5-byte PUSH_CONST_U32 (0x28 = 40) truncated to 4 bytes,
        # which consumes the next INOT byte and breaks instruction boundaries.
        m["patches"][0]["patched_hex"] = "28000000"
        m["patches"][0]["patched_spaced"] = "28 00 00 00"
        m["patches"][0]["patched_ops"] = ["PUSH_CONST_U32"]
        with self.assertRaises((AssertionError, ValueError)):
            verify_manifest(m, ysc=self.ysc, disasm=self.disasm)

    def test_rejects_unbalanced_variant_a_jz_to_j(self):
        m = copy.deepcopy(self.manifest)
        # Replace site 2 check with Variant A style JZ->J at 0x2FC3F9 (stack conflict 1 != 0)
        m["patches"] = [
            {
                "id": "bad_variant_a",
                "pc": "0x2FC3F9",
                "page_index": 191,
                "in_page_offset": "0x03F9",
                "sys_offset": "0x3043F9",
                "length": 3,
                "original_hex": "561700",
                "original_spaced": "56 17 00",
                "original_op": "JZ",
                "original_desc": "JZ -> 0x2FC413",
                "patched_hex": "551700",
                "patched_spaced": "55 17 00",
                "patched_ops": ["J"],
                "prev_insn": {"pc": "0x2FC3F8", "length": 1, "op": "IOR"},
                "next_insn": {"pc": "0x2FC3FC", "length": 4, "op": "GLOBAL_U24_LOAD"},
            }
        ]
        with self.assertRaisesRegex(AssertionError, "CFG stack merge conflicts"):
            verify_manifest(m, ysc=self.ysc, disasm=self.disasm)


if __name__ == "__main__":
    unittest.main()
