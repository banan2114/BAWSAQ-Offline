#!/usr/bin/env python3
"""Independent verifier for the C1 BAWSAQ gate patch manifest (Variant C1).

Reads tools/bawsaq-restoration/c1-patch-manifest.json and validates every
invariant against game-scripts/appinternet1.ysc without modifying any file on disk.
"""
import copy
import json
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
TOOLS_DIR = REPO_ROOT / "tools"
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

from ysc_tool import YSC  # noqa: E402
from ysc_disasm import Disassembler, JUMP_OPS, OPCODES  # noqa: E402

DEFAULT_MANIFEST = Path(__file__).resolve().parent / "c1-patch-manifest.json"


def _hex_int(v) -> int:
    if isinstance(v, int):
        return v
    return int(str(v), 16) if str(v).lower().startswith("0x") else int(str(v))


def _stack_effect(ins, func_by_off):
    op = ins.op
    if op == 45:  # ENTER
        return (0, 0)
    if op == 46:  # LEAVE
        return (ins.args["rcount"], 0)
    if op == 44:  # NATIVE
        return (ins.args["args"], ins.args["rets"])
    if op == 93:  # CALL
        callee = func_by_off[ins.jump]
        return (callee["pcount"], callee["rcount"])
    effects = {
        0: (0, 0),
        1: (2, 1), 2: (2, 1), 3: (2, 1), 6: (1, 1), 8: (2, 1),
        14: (2, 1), 15: (2, 1), 22: (2, 1), 25: (2, 1),
        31: (2, 1), 32: (2, 1), 37: (0, 1), 41: (0, 1), 42: (1, 2), 43: (1, 0),
        52: (2, 1), 53: (2, 1), 56: (0, 1), 57: (1, 0), 61: (1, 1), 64: (1, 1), 65: (1, 1),
        67: (0, 1), 82: (0, 1), 83: (0, 1), 84: (1, 0),
        85: (0, 0), 86: (1, 0), 87: (2, 0), 88: (2, 0), 89: (2, 0), 91: (2, 0),
        98: (0, 1), 99: (1, 0), 100: (0, 1), 101: (1, 0), 102: (1, 1),
        112: (0, 1), 113: (0, 1), 114: (0, 1), 115: (0, 1), 116: (0, 1),
        117: (0, 1), 118: (0, 1), 119: (0, 1), 120: (0, 1),
        122: (0, 1), 126: (0, 1), 127: (0, 1), 128: (0, 1), 129: (0, 1),
    }
    if op not in effects:
        raise ValueError(f"unsupported opcode {ins.name} ({op}) in func_9474 stack check")
    return effects[op]


def verify_func_cfg_stack(insns, func_by_off):
    """Verify full CFG reachability, zero merge conflicts, and zero LEAVE exit depths."""
    leaders = {insns[0].off}
    for i, ins in enumerate(insns):
        if ins.op in JUMP_OPS:
            leaders.add(ins.jump)
            if i + 1 < len(insns):
                leaders.add(insns[i + 1].off)
        elif ins.op == 101:  # SWITCH
            for _, tgt in ins.args["entries"]:
                leaders.add(tgt)
            if i + 1 < len(insns):
                leaders.add(insns[i + 1].off)
        elif ins.op == 46:  # LEAVE
            if i + 1 < len(insns):
                leaders.add(insns[i + 1].off)

    sorted_leaders = sorted(leaders)
    off_to_idx = {ins.off: i for i, ins in enumerate(insns)}
    blocks = []
    for b_i, start_off in enumerate(sorted_leaders):
        si = off_to_idx[start_off]
        ei = off_to_idx[sorted_leaders[b_i + 1]] - 1 if b_i + 1 < len(sorted_leaders) else len(insns) - 1
        blocks.append((b_i, start_off, insns[ei].off, si, ei))

    block_by_off = {b[1]: b[0] for b in blocks}
    in_depth = {0: 0}
    work = [0]
    conflicts = []
    leave_depths = []

    while work:
        bi = work.pop(0)
        _, _, _, si, ei = blocks[bi]
        d = in_depth[bi]
        for k in range(si, ei + 1):
            pop_n, push_n = _stack_effect(insns[k], func_by_off)
            d -= pop_n
            if d < 0:
                raise AssertionError(f"negative stack depth {d} at {insns[k].off:#x}")
            d += push_n
        last = insns[ei]
        if last.op == 46:
            leave_depths.append((last.off, d))
            succs = []
        elif last.op == 85:
            succs = [block_by_off[last.jump]]
        elif last.op in JUMP_OPS:
            succs = [block_by_off[last.jump], block_by_off[insns[ei + 1].off]]
        elif last.op == 101:
            succs = [block_by_off[t] for _, t in last.args["entries"]] + [block_by_off[insns[ei + 1].off]]
        else:
            succs = [block_by_off[insns[ei + 1].off]] if ei + 1 < len(insns) else []

        for s in succs:
            if s not in in_depth:
                in_depth[s] = d
                work.append(s)
            elif in_depth[s] != d:
                conflicts.append((blocks[bi][1], blocks[s][1], d, in_depth[s]))

    if conflicts:
        raise AssertionError(f"CFG stack merge conflicts: {conflicts}")
    if len(in_depth) != len(blocks):
        raise AssertionError(f"unreachable blocks: {len(in_depth)}/{len(blocks)}")
    non_zero_leaves = [(hex(off), d) for off, d in leave_depths if d != 0]
    if non_zero_leaves:
        raise AssertionError(f"non-zero LEAVE stack depths: {non_zero_leaves}")
    return len(blocks), leave_depths


def verify_manifest(manifest: dict, ysc: YSC | None = None, disasm: Disassembler | None = None) -> dict:
    src = manifest["source_ysc"]
    ysc_path = REPO_ROOT / src["path"]
    if ysc is None:
        ysc = YSC(str(ysc_path))

    # 1. Verify original YSC SHA256 and header metadata
    if ysc.sha256 != src["sha256"]:
        raise AssertionError(f"YSC sha256 mismatch: got {ysc.sha256}, expected {src['sha256']}")
    if ysc.raw_size != src["file_size"]:
        raise AssertionError(f"YSC file_size mismatch: got {ysc.raw_size}, expected {src['file_size']}")
    if ysc.rsc_version != src["rsc_version"]:
        raise AssertionError(f"RSC7 version mismatch: got {ysc.rsc_version}, expected {src['rsc_version']}")
    if ysc.sys_size != src["sys_size"]:
        raise AssertionError(f"sys_size mismatch: got {ysc.sys_size}, expected {src['sys_size']}")
    if ysc.script_name != src["script_name"]:
        raise AssertionError(f"script_name mismatch: got {ysc.script_name!r}, expected {src['script_name']!r}")
    if (ysc.h_name_hash & 0xFFFFFFFF) != _hex_int(src["name_hash"]):
        raise AssertionError(f"name_hash mismatch: got {ysc.h_name_hash & 0xFFFFFFFF:#x}")
    if (ysc.h_globals_signature & 0xFFFFFFFF) != _hex_int(src["globals_signature"]):
        raise AssertionError(f"globals_signature mismatch: got {ysc.h_globals_signature & 0xFFFFFFFF:#x}")
    if ysc.h_code_size != src["code_size"]:
        raise AssertionError(f"code_size mismatch: got {ysc.h_code_size}, expected {src['code_size']}")
    if ysc.h_natives_count != src["native_count"]:
        raise AssertionError(f"native_count mismatch: got {ysc.h_natives_count}, expected {src['native_count']}")

    if disasm is None:
        disasm = Disassembler(ysc, opcode_table=src["opcode_table"])
    if disasm.opcode_table != src["opcode_table"]:
        raise AssertionError(f"opcode_table mismatch: {disasm.opcode_table} != {src['opcode_table']}")

    # 2. Verify func_9474 identity
    tf = manifest["target_function"]
    fn = disasm.functions[tf["index"]]
    if fn["off"] != _hex_int(tf["enter_pc"]):
        raise AssertionError(f"func_{tf['index']} ENTER mismatch: {fn['off']:#x} != {tf['enter_pc']}")
    if fn["leave_off"] != _hex_int(tf["leave_pc"]):
        raise AssertionError(f"func_{tf['index']} LEAVE mismatch: {fn['leave_off']:#x} != {tf['leave_pc']}")
    if (fn["pcount"], fn["vcount"], fn["rcount"]) != (tf["pcount"], tf["vcount"], tf["rcount"]):
        raise AssertionError(f"func_{tf['index']} signature mismatch: {fn}")
    func_insns = disasm.instrs[fn["start_i"] : fn["end_i"] + 1]
    if len(func_insns) != tf["insn_count"]:
        raise AssertionError(f"func_{tf['index']} insn_count mismatch: {len(func_insns)} != {tf['insn_count']}")

    # 3. Prove RSC7 code page table mapping -> page 191
    ep = manifest["expected_page"]
    page_offs = ysc._page_offsets(ysc.h_code_blocks_base, ysc.h_code_size)
    if len(page_offs) != src["code_page_count"]:
        raise AssertionError(f"code page count mismatch: {len(page_offs)} != {src['code_page_count']}")
    if ysc.h_code_blocks_base != _hex_int(ep["code_blocks_base_sys_offset"]):
        raise AssertionError(f"code_blocks_base mismatch: {ysc.h_code_blocks_base:#x}")
    page_idx = ep["page_index"]
    if ysc.h_code_blocks_base + page_idx * 8 != _hex_int(ep["page_ptr_entry_sys_offset"]):
        raise AssertionError("page_ptr_entry_sys_offset mismatch")
    if page_offs[page_idx] != _hex_int(ep["page_sys_offset"]):
        raise AssertionError(f"page {page_idx} sys offset mismatch: {page_offs[page_idx]:#x}")

    # 4. Verify 36-byte signature uniqueness across entire code buffer
    sig = manifest["signature"]
    sig_bytes = bytes.fromhex(sig["hex"])
    if len(sig_bytes) != sig["length"]:
        raise AssertionError(f"signature byte length {len(sig_bytes)} != {sig['length']}")
    if sig["spaced_hex"].replace(" ", "").lower() != sig["hex"].lower():
        raise AssertionError("signature spaced_hex != hex")
    sig_pc = _hex_int(sig["start_pc"])
    if sig_pc + sig["length"] != _hex_int(sig["end_pc"]):
        raise AssertionError("signature end_pc != start_pc + length")

    hits = []
    pos = 0
    code = disasm.code
    while True:
        idx = code.find(sig_bytes, pos)
        if idx == -1:
            break
        hits.append(idx)
        pos = idx + 1
    if len(hits) != sig["expected_occurrences"] or hits != [sig_pc]:
        raise AssertionError(f"signature occurrences mismatch: hits={[hex(h) for h in hits]}, expected={[hex(sig_pc)]}")

    # 5. Verify both patch sites, page containment, original bytes, lengths, and neighbors
    ins_by_off = {ins.off: i for i, ins in enumerate(func_insns)}
    patched_buf = bytearray(code)

    for p in manifest["patches"]:
        pc = _hex_int(p["pc"])
        length = p["length"]
        orig_bytes = bytes.fromhex(p["original_hex"])
        new_bytes = bytes.fromhex(p["patched_hex"])

        if len(orig_bytes) != length or len(new_bytes) != length:
            raise AssertionError(f"{p['id']}: length mismatch")
        if p["original_spaced"].replace(" ", "").lower() != p["original_hex"].lower():
            raise AssertionError(f"{p['id']}: original_spaced mismatch")
        if p["patched_spaced"].replace(" ", "").lower() != p["patched_hex"].lower():
            raise AssertionError(f"{p['id']}: patched_spaced mismatch")

        # Page containment proof
        if (pc >> 14) != page_idx or ((pc + length - 1) >> 14) != page_idx:
            raise AssertionError(f"{p['id']}: pc {pc:#x}..{pc+length-1:#x} not strictly inside page {page_idx}")
        if p["page_index"] != page_idx:
            raise AssertionError(f"{p['id']}: manifest page_index != {page_idx}")
        in_page = pc & 0x3FFF
        if in_page != _hex_int(p["in_page_offset"]):
            raise AssertionError(f"{p['id']}: in_page_offset {in_page:#x} != {p['in_page_offset']}")
        sys_off = page_offs[page_idx] + in_page
        if sys_off != _hex_int(p["sys_offset"]):
            raise AssertionError(f"{p['id']}: sys_offset {sys_off:#x} != {p['sys_offset']}")

        # Match between RSC7 sys segment, flat code buffer, and manifest
        if ysc.sys[sys_off : sys_off + length] != orig_bytes:
            raise AssertionError(f"{p['id']}: RSC7 sys bytes mismatch")
        if code[pc : pc + length] != orig_bytes:
            raise AssertionError(f"{p['id']}: flat code bytes mismatch")

        # Instruction boundary & neighbor verification
        if pc not in ins_by_off:
            raise AssertionError(f"{p['id']}: pc {pc:#x} is not an instruction boundary")
        idx = ins_by_off[pc]
        ins = func_insns[idx]
        if ins.size != length or ins.name != p["original_op"]:
            raise AssertionError(f"{p['id']}: instruction mismatch ({ins.name} size={ins.size})")

        prev_ins = func_insns[idx - 1]
        next_ins = func_insns[idx + 1]
        if prev_ins.off != _hex_int(p["prev_insn"]["pc"]) or prev_ins.name != p["prev_insn"]["op"]:
            raise AssertionError(f"{p['id']}: prev_insn mismatch")
        if prev_ins.off + prev_ins.size != pc:
            raise AssertionError(f"{p['id']}: prev_insn does not abut pc")
        if next_ins.off != _hex_int(p["next_insn"]["pc"]) or next_ins.name != p["next_insn"]["op"]:
            raise AssertionError(f"{p['id']}: next_insn mismatch")
        if pc + length != next_ins.off:
            raise AssertionError(f"{p['id']}: patch does not abut next_insn")

        # Validate patched opcodes & stack delta before applying
        expected_ops = p["patched_ops"]
        patched_buf[pc : pc + length] = new_bytes
        if len(expected_ops) != len(new_bytes):
            # Only works for 1-byte opcodes like PUSH_CONST_0/1 + NOPs; general decode check follows below
            pass

    # 6. Re-disassemble patched code buffer across the entire file & verify func_9474 CFG + stack
    d_patched = copy.copy(disasm)
    d_patched.code = bytes(patched_buf)
    d_patched._parse_with_table(src["opcode_table"])

    p_fn = d_patched.functions[tf["index"]]
    if p_fn["off"] != fn["off"] or p_fn["leave_off"] != fn["leave_off"]:
        raise AssertionError("func_9474 bounds shifted after patch!")

    p_func_insns = d_patched.instrs[p_fn["start_i"] : p_fn["end_i"] + 1]
    p_by_off = {ins.off: ins for ins in p_func_insns}
    for p in manifest["patches"]:
        pc = _hex_int(p["pc"])
        if pc not in p_by_off:
            raise AssertionError(f"{p['id']}: patched pc {pc:#x} missing as instruction boundary")
        cur = pc
        seen_ops = []
        while cur < pc + p["length"]:
            if cur not in p_by_off:
                raise AssertionError(f"{p['id']}: internal misalignment at {cur:#x}")
            seen_ops.append(p_by_off[cur].name)
            cur += p_by_off[cur].size
        if cur != pc + p["length"]:
            raise AssertionError(f"{p['id']}: patched instructions overran boundary ({cur:#x})")
        if seen_ops != p["patched_ops"]:
            raise AssertionError(f"{p['id']}: decoded patched ops {seen_ops} != {p['patched_ops']}")
        next_pc = _hex_int(p["next_insn"]["pc"])
        if next_pc not in p_by_off or p_by_off[next_pc].name != p["next_insn"]["op"]:
            raise AssertionError(f"{p['id']}: next_insn corrupted after patch")

    func_by_off = {f["off"]: f for f in d_patched.functions}
    block_count, leave_depths = verify_func_cfg_stack(p_func_insns, func_by_off)
    if block_count != tf["basic_block_count"]:
        raise AssertionError(f"basic block count changed: {block_count} != {tf['basic_block_count']}")

    # 7. Verify exact reversibility in memory
    for p in manifest["patches"]:
        pc = _hex_int(p["pc"])
        patched_buf[pc : pc + p["length"]] = bytes.fromhex(p["original_hex"])
    if bytes(patched_buf) != code:
        raise AssertionError("restoring original bytes failed to reproduce exact code buffer")

    return {
        "ysc_sha256": ysc.sha256,
        "func_index": tf["index"],
        "page_index": page_idx,
        "signature_pc": hex(sig_pc),
        "blocks_verified": block_count,
        "leave_depths": [(hex(off), d) for off, d in leave_depths],
        "patch_count": len(manifest["patches"]),
    }


def main():
    manifest_path = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_MANIFEST
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    summary = verify_manifest(manifest)
    print("C1 manifest verification PASSED:")
    for k, v in summary.items():
        print(f"  {k}: {v}")


if __name__ == "__main__":
    main()
