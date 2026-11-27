#!/usr/bin/env python3
"""YSC disassembler for GTA V (sequential opcodes, gen8/gen9 Enhanced).

Port of operand-size logic from maybegreat/GTA-V-Script-Decompiler (ScriptFile.cs)
and opcode semantics from Jaycadox/ysc-utils (disassemble.rs).

Usage:
  ysc_disasm.py info <file.ysc> [--crossmap map.json]
  ysc_disasm.py funcs <file.ysc>            # function table
  ysc_disasm.py func <file.ysc> <index>     # pretty-print one function
  ysc_disasm.py natives <file.ysc>          # native table (hash, count)
  ysc_disasm.py find-native <file.ysc> NAME [NAME...]   # call sites by name (needs crossmap)
  ysc_disasm.py find-global <file.ysc> IDX [IDX...]     # global refs by absolute index
  ysc_disasm.py build-crossmap <file.ysc> <decompiled.c> <out.json>
"""
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from ysc_tool import YSC

OPCODES = [
    "NOP", "IADD", "ISUB", "IMUL", "IDIV", "IMOD", "INOT", "INEG", "IEQ", "INE",
    "IGT", "IGE", "ILT", "ILE", "FADD", "FSUB", "FMUL", "FDIV", "FMOD", "FNEG",
    "FEQ", "FNE", "FGT", "FGE", "FLT", "FLE", "VADD", "VSUB", "VMUL", "VDIV",
    "VNEG", "IAND", "IOR", "IXOR", "I2F", "F2I", "F2V",
    "PUSH_CONST_U8", "PUSH_CONST_U8_U8", "PUSH_CONST_U8_U8_U8", "PUSH_CONST_U32", "PUSH_CONST_F",
    "DUP", "DROP", "NATIVE", "ENTER", "LEAVE", "LOAD", "STORE", "STORE_REV", "LOAD_N", "STORE_N",
    "ARRAY_U8", "ARRAY_U8_LOAD", "ARRAY_U8_STORE", "LOCAL_U8", "LOCAL_U8_LOAD", "LOCAL_U8_STORE",
    "STATIC_U8", "STATIC_U8_LOAD", "STATIC_U8_STORE", "IADD_U8", "IMUL_U8",
    "IOFFSET", "IOFFSET_U8", "IOFFSET_U8_LOAD", "IOFFSET_U8_STORE",
    "PUSH_CONST_S16", "IADD_S16", "IMUL_S16", "IOFFSET_S16", "IOFFSET_S16_LOAD", "IOFFSET_S16_STORE",
    "ARRAY_U16", "ARRAY_U16_LOAD", "ARRAY_U16_STORE", "LOCAL_U16", "LOCAL_U16_LOAD", "LOCAL_U16_STORE",
    "STATIC_U16", "STATIC_U16_LOAD", "STATIC_U16_STORE",
    "GLOBAL_U16", "GLOBAL_U16_LOAD", "GLOBAL_U16_STORE",
    "J", "JZ", "IEQ_JZ", "INE_JZ", "IGT_JZ", "IGE_JZ", "ILT_JZ", "ILE_JZ",
    "CALL", "LOCAL_U24", "LOCAL_U24_LOAD", "LOCAL_U24_STORE",
    "GLOBAL_U24", "GLOBAL_U24_LOAD", "GLOBAL_U24_STORE", "PUSH_CONST_U24",
    "SWITCH", "STRING", "STRINGHASH",
    "TEXT_LABEL_ASSIGN_STRING", "TEXT_LABEL_ASSIGN_INT", "TEXT_LABEL_APPEND_STRING",
    "TEXT_LABEL_APPEND_INT", "TEXT_LABEL_COPY", "CATCH", "THROW", "CALLINDIRECT",
    "PUSH_CONST_M1", "PUSH_CONST_0", "PUSH_CONST_1", "PUSH_CONST_2", "PUSH_CONST_3",
    "PUSH_CONST_4", "PUSH_CONST_5", "PUSH_CONST_6", "PUSH_CONST_7",
    "PUSH_CONST_FM1", "PUSH_CONST_F0", "PUSH_CONST_F1", "PUSH_CONST_F2", "PUSH_CONST_F3",
    "PUSH_CONST_F4", "PUSH_CONST_F5", "PUSH_CONST_F6", "PUSH_CONST_F7", "IS_BIT_SET",
]

# fixed operand sizes (opcode -> bytes); ENTER/SWITCH/NATIVE handled specially
OP_SIZE = {}
for _i in range(0, 37):
    OP_SIZE[_i] = 0
OP_SIZE[37] = 1   # PUSH_CONST_U8
OP_SIZE[38] = 2   # PUSH_CONST_U8_U8
OP_SIZE[39] = 3   # PUSH_CONST_U8_U8_U8
OP_SIZE[40] = 4   # PUSH_CONST_U32
OP_SIZE[41] = 4   # PUSH_CONST_F
OP_SIZE[42] = 0   # DUP
OP_SIZE[43] = 0   # DROP
OP_SIZE[44] = 3   # NATIVE (special)
OP_SIZE[45] = -1  # ENTER (special)
OP_SIZE[46] = 2   # LEAVE
for _i in range(47, 52):
    OP_SIZE[_i] = 0
for _i in range(52, 63):
    OP_SIZE[_i] = 1
OP_SIZE[63] = 0   # IOFFSET
for _i in range(64, 67):
    OP_SIZE[_i] = 1
for _i in range(67, 85):
    OP_SIZE[_i] = 2
for _i in range(85, 93):
    OP_SIZE[_i] = 2   # jumps
OP_SIZE[93] = 3       # CALL
for _i in range(94, 101):
    OP_SIZE[_i] = 3
OP_SIZE[101] = -2     # SWITCH (special)
OP_SIZE[102] = 0      # STRING
OP_SIZE[103] = 0      # STRINGHASH
for _i in range(104, 108):
    OP_SIZE[_i] = 1
for _i in range(108, 131):
    OP_SIZE[_i] = 0

JUMP_OPS = set(range(85, 93))  # J..ILE_JZ
GLOBAL_OPS = {82: ("U16", "ADDR"), 83: ("U16", "LOAD"), 84: ("U16", "STORE"),
              97: ("U24", "ADDR"), 98: ("U24", "LOAD"), 99: ("U24", "STORE")}


def u16le(b, i):
    return b[i] | (b[i + 1] << 8)


def u24le(b, i):
    return b[i] | (b[i + 1] << 8) | (b[i + 2] << 16)


def i16le(b, i):
    v = u16le(b, i)
    return v - 0x10000 if v >= 0x8000 else v


class Instr:
    __slots__ = ("off", "op", "name", "size", "args", "jump", "native_idx", "native_hash",
                 "global_idx", "global_kind", "str_key", "func_enter")

    def __init__(self, off, op, name, size, args=None):
        self.off = off
        self.op = op
        self.name = name
        self.size = size
        self.args = args or {}
        self.jump = None
        self.native_idx = None
        self.native_hash = None
        self.global_idx = None
        self.global_kind = None
        self.str_key = None
        self.func_enter = None


class Disassembler:
    def __init__(self, ysc: YSC):
        self.ysc = ysc
        self.code = ysc.code()
        self.native_table = ysc.native_table()
        self.instrs = []
        self.functions = []   # (enter_index, offset, name, pcount, vcount, rcount)
        self._last_push = 0
        self._parse()

    def _parse(self):
        code = self.code
        n = len(code)
        pos = 0
        func_idx = -1
        last_leave_off = -3
        while pos < n:
            op = code[pos]
            if op >= len(OPCODES):
                raise ValueError(f"unknown opcode {op} at {pos:#x}")
            name = OPCODES[op]
            if op == 45:  # ENTER
                namelen = code[pos + 4]
                fname_bytes = code[pos + 5: pos + 5 + namelen]
                fname = fname_bytes.split(b"\x00")[0].decode("latin1", "replace")
                pcount = code[pos + 1]
                vcount = u16le(code, pos + 2)
                size = 5 + namelen
                # rcount lives at the LEAVE: code[leave+2]
                ins = Instr(pos, op, name, size, {"pcount": pcount, "vcount": vcount,
                                                  "name": fname, "namelen": namelen})
                func_idx += 1
                ins.func_enter = func_idx
                self.functions.append({"index": func_idx, "off": pos, "name": fname,
                                       "pcount": pcount, "vcount": vcount,
                                       "leave_off": None, "rcount": None,
                                       "start_i": len(self.instrs)})
            elif op == 46:  # LEAVE
                ins = Instr(pos, op, name, 3, {"argcount": code[pos + 1],
                                               "rcount": code[pos + 2]})
                if self.functions:
                    f = self.functions[-1]
                    f["leave_off"] = pos
                    f["rcount"] = code[pos + 2]
                    f["end_i"] = len(self.instrs)
            elif op == 44:  # NATIVE
                packed = code[pos + 1]
                idx = (code[pos + 2] << 8) | code[pos + 3]
                ins = Instr(pos, op, name, 4, {"args": packed >> 2, "rets": packed & 3})
                ins.native_idx = idx
                if idx < len(self.native_table):
                    ins.native_hash = self.native_table[idx]
                else:
                    raise ValueError(f"native index {idx} out of range at {pos:#x}")
            elif op == 101:  # SWITCH
                count = code[pos + 1]
                size = 2 + 6 * count
                entries = []
                for k in range(count):
                    base = pos + 2 + 6 * k
                    val = u24le(code, base) | (code[base + 3] << 24)
                    jrel = i16le(code, base + 4)
                    # C# Instruction.GetSwitchTarget (non-RDR2): Offset+8+index*6+jrel
                    target = pos + 8 + 6 * k + jrel
                    entries.append((val, target))
                ins = Instr(pos, op, name, size, {"count": count, "entries": entries})
            else:
                sz = OP_SIZE[op]
                if sz < 0:
                    raise ValueError(f"unhandled special op {name} at {pos:#x}")
                ins = Instr(pos, op, name, 1 + sz)
                if op in JUMP_OPS:
                    rel = i16le(code, pos + 1)
                    ins.jump = pos + 3 + rel
                elif op == 93:  # CALL
                    ins.jump = u24le(code, pos + 1)
                elif op in GLOBAL_OPS:
                    kind, gkind = GLOBAL_OPS[op]
                    ins.global_idx = u16le(code, pos + 1) if kind == "U16" else u24le(code, pos + 1)
                    ins.global_kind = gkind
                elif op == 37:  # PUSH_CONST_U8
                    self._last_push = code[pos + 1]
                elif op == 38:
                    self._last_push = code[pos + 1] | (code[pos + 2] << 8)
                elif op == 39:
                    self._last_push = code[pos + 1] | (code[pos + 2] << 8) | (code[pos + 3] << 16)
                elif op == 40:  # PUSH_CONST_U32
                    self._last_push = u24le(code, pos + 1) | (code[pos + 4] << 24)
                elif op == 67:  # PUSH_CONST_S16
                    self._last_push = i16le(code, pos + 1) & 0xFFFFFFFF
                elif op == 100:  # PUSH_CONST_U24
                    self._last_push = u24le(code, pos + 1)
                elif 112 <= op <= 129:  # PUSH_CONST_M1/0..7/FM1/F0..F7
                    table = {112: 0xFFFFFFFF, 113: 0, 114: 1, 115: 2, 116: 3, 117: 4,
                             118: 5, 119: 6, 120: 7, 121: 0xFFFFFFFF}
                    self._last_push = table.get(op, 0)
                elif op == 102:  # STRING
                    ins.str_key = self._last_push
            self.instrs.append(ins)
            pos += ins.size
        if pos != n:
            raise ValueError(f"parse overrun: {pos} != {n}")
        # validate function starts/ends
        for f in self.functions:
            i = f["start_i"]
            if self.instrs[i].op != 45:
                raise ValueError(f"function {f['index']} does not start with ENTER")
            if f["leave_off"] is None or self.instrs[f["end_i"]].op != 46:
                raise ValueError(f"function {f['index']} has no LEAVE")

    # ---- lookups -------------------------------------------------------
    def find_natives(self, hashes):
        want = set(hashes)
        out = []
        cur = -1
        for ins in self.instrs:
            if ins.func_enter is not None:
                cur = ins.func_enter
            if ins.op == 44 and ins.native_hash in want:
                out.append((cur, ins))
        return out

    def find_globals(self, idxs):
        want = set(idxs)
        out = []
        cur = -1
        for ins in self.instrs:
            if ins.func_enter is not None:
                cur = ins.func_enter
            if ins.global_idx in want:
                out.append((cur, ins))
        return out

    def func_natives(self, fi):
        f = self.functions[fi]
        return [i.native_hash for i in self.instrs[f["start_i"]: f["end_i"] + 1] if i.op == 44]

    def pretty(self, fi, hash2name=None, max_ins=None):
        f = self.functions[fi]
        lines = [f"// function {fi} '{f['name']}' @ {f['off']:#x} p={f['pcount']} v={f['vcount']} r={f['rcount']}"]
        count = 0
        for ins in self.instrs[f["start_i"]: f["end_i"] + 1]:
            s = f"{ins.off:07x}  {ins.name}"
            if ins.op == 44:
                nm = (hash2name or {}).get(ins.native_hash, "")
                s += f" args={ins.args['args']} rets={ins.args['rets']} idx={ins.native_idx} {hex(ins.native_hash)} {nm}"
            elif ins.op == 45:
                s += f" '{ins.args['name']}' p={ins.args['pcount']} v={ins.args['vcount']}"
            elif ins.op == 46:
                s += f" r={ins.args['rcount']}"
            elif ins.jump is not None:
                s += f" -> {ins.jump:#x}"
            elif ins.global_idx is not None:
                s += f" Global_{ins.global_idx} ({ins.global_kind})"
            elif ins.op == 102:
                s += f" str_key={ins.str_key}"
            elif ins.op == 101:
                s += f" count={ins.args['count']}"
            elif ins.size > 1 and ins.op not in (44, 45, 46, 101):
                s += " " + self.code[ins.off + 1: ins.off + ins.size].hex()
            lines.append(s)
            count += 1
            if max_ins and count >= max_ins:
                lines.append("... truncated ...")
                break
        return "\n".join(lines)


# ---- decompile alignment --------------------------------------------
# Every function definition line ends with "// Position - 0xADDR (DEC)" and
# starts at column 0 (calls are indented). Named helpers (_FOO), enum return
# types (eCharacter), struct<10> etc. all carry this comment, and the count
# of Position comments equals the bytecode function count exactly.
POS_DEF_RE = re.compile(r"^([^\s][^\n]*?)//\s*Position\s*-\s*(0x[0-9A-Fa-f]+)", re.M)
FUNC_NAME_RE = re.compile(r"\b([A-Za-z_][A-Za-z0-9_]*)\s*\(")
NATIVE_RE = re.compile(r"\b([A-Z][A-Z_0-9]+)::([A-Z_0-9]+)\s*\(")


def parse_decompiled(path):
    """Return list of (name, [native names], pos) in file order (all functions)."""
    text = Path(path).read_text(encoding="utf-8", errors="replace")
    defs = list(POS_DEF_RE.finditer(text))
    funcs = []
    for i, m in enumerate(defs):
        header = m.group(1)
        nm = FUNC_NAME_RE.search(header)
        name = nm.group(1) if nm else "?"
        start = m.end()
        end = defs[i + 1].start() if i + 1 < len(defs) else len(text)
        body = text[start:end]
        natives = [f"{ns}::{n}" for ns, n in NATIVE_RE.findall(body)]
        pos = int(m.group(2), 16)
        funcs.append((name, natives, pos))
    return funcs


def build_crossmap(ysc_path, decompiled_path, out_path, verbose=True):
    d = Disassembler(YSC(ysc_path))
    dec = parse_decompiled(decompiled_path)
    bc_funcs = d.functions
    if verbose:
        print(f"bytecode functions: {len(bc_funcs)}, decompiled functions: {len(dec)}")
    votes = {}   # hash -> {name: count}
    # align by Position comment (exact ENTER offset) when available
    bc_by_off = {f["off"]: f["index"] for f in bc_funcs}
    alignment = []
    pos_matched = 0
    for di, (dname, dseq, dpos) in enumerate(dec):
        if dpos is not None and dpos in bc_by_off:
            alignment.append((bc_by_off[dpos], di))
            pos_matched += 1
    if verbose:
        print(f"position-matched: {pos_matched}/{len(dec)}")
    if pos_matched < 0.5 * len(dec):
        # fallback: sequence alignment by native counts
        import difflib
        a = [len(d.func_natives(i)) for i in range(len(bc_funcs))]
        b = [len(x[1]) for x in dec]
        sm = difflib.SequenceMatcher(a=a, b=b, autojunk=False)
        alignment = []
        for blk in sm.get_matching_blocks():
            for k in range(blk.size):
                alignment.append((blk.a + k, blk.b + k))
        if verbose:
            print(f"aligned {len(alignment)} functions (of {len(bc_funcs)}/{len(dec)})")
    matched_funcs = 0
    pairs = 0
    for bi, di in alignment:
        bseq = d.func_natives(bi)
        dseq = dec[di][1]
        if len(bseq) != len(dseq):
            if verbose and dseq:
                print(f"  MISMATCH func bc#{bi} off={bc_funcs[bi]['off']:#x} "
                      f"vs dec '{dec[di][0]}': {len(bseq)} vs {len(dseq)} natives")
            continue
        matched_funcs += 1
        pairs += len(bseq)
        for h, nm in zip(bseq, dseq):
            votes.setdefault(h, {})
            votes[h][nm] = votes[h].get(nm, 0) + 1
    crossmap = {}
    conflicts = []
    for h, cand in votes.items():
        best = max(cand.items(), key=lambda kv: kv[1])
        total = sum(cand.values())
        if len(cand) > 1 and best[1] < 0.8 * total:
            conflicts.append((hex(h), cand))
        crossmap[hex(h)] = best[0]
    if verbose:
        print(f"matched functions: {matched_funcs}, native pairs: {pairs}, "
              f"distinct hashes named: {len(crossmap)}, conflicts: {len(conflicts)}")
        for c in conflicts[:10]:
            print("  conflict:", c)
    Path(out_path).write_text(json.dumps(crossmap, indent=1, sort_keys=True))
    if verbose:
        print(f"wrote {out_path}")
    return crossmap


def load_crossmap(path):
    raw = json.loads(Path(path).read_text())
    return {int(k, 16): v for k, v in raw.items()}


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    cmd, path = sys.argv[1], sys.argv[2]
    rest = sys.argv[3:]
    if cmd == "info":
        ysc = YSC(path)
        d = Disassembler(ysc)
        cm = load_crossmap(rest[0]) if rest else None
        print(f"file: {path}")
        print(f"script: {ysc.script_name}  code: {len(d.code)} bytes")
        print(f"functions: {len(d.functions)}  instructions: {len(d.instrs)}")
        print(f"native table: {len(d.native_table)} entries, "
              f"distinct: {len(set(d.native_table))}")
        if cm:
            named = sum(1 for h in set(d.native_table) if h in cm)
            print(f"crossmap covers: {named}/{len(set(d.native_table))}")
        ng = sum(1 for i in d.instrs if i.global_idx is not None)
        print(f"global refs: {ng}")
        names = [f["name"] for f in d.functions if f["name"]]
        print(f"named functions: {names[:20]}")
    elif cmd == "funcs":
        d = Disassembler(YSC(path))
        for f in d.functions:
            print(f"{f['index']:5d} {f['off']:07x} leave={f['leave_off'] and hex(f['leave_off'])} "
                  f"p={f['pcount']} v={f['vcount']} r={f['rcount']} '{f['name']}' "
                  f"natives={len(d.func_natives(f['index']))}")
    elif cmd == "func":
        d = Disassembler(YSC(path))
        cm = load_crossmap(rest[1]) if len(rest) > 1 else None
        print(d.pretty(int(rest[0]), hash2name=cm))
    elif cmd == "natives":
        ysc = YSC(path)
        tab = ysc.native_table()
        cm = load_crossmap(rest[0]) if rest else {}
        for i, h in enumerate(tab):
            print(f"{i:5d} {hex(h)} {cm.get(h, '')}")
    elif cmd == "find-native":
        cm = load_crossmap(rest[-1]) if len(rest) > 1 and rest[-1].endswith(".json") else {}
        wanted = rest[:-1] if len(rest) > 1 and rest[-1].endswith(".json") else rest
        name2hash = {}
        for h, nm in cm.items():
            name2hash.setdefault(nm, h)
        hashes = []
        for w in wanted:
            if w in name2hash:
                hashes.append(name2hash[w])
            else:
                print(f"WARNING: {w} not in crossmap")
        d = Disassembler(YSC(path))
        for fi, ins in d.find_natives(hashes):
            fname = d.functions[fi]["name"] if fi >= 0 else "?"
            nm = cm.get(ins.native_hash, "?")
            print(f"func {fi} '{fname}' @ {ins.off:#x}: {nm} ({hex(ins.native_hash)}) "
                  f"args={ins.args['args']} rets={ins.args['rets']}")
    elif cmd == "find-global":
        idxs = [int(x) for x in rest]
        d = Disassembler(YSC(path))
        for fi, ins in d.find_globals(idxs):
            fname = d.functions[fi]["name"] if fi >= 0 else "?"
            print(f"func {fi} '{fname}' @ {ins.off:#x}: Global_{ins.global_idx} {ins.global_kind}")
    elif cmd == "build-crossmap":
        build_crossmap(path, rest[0], rest[1])
    else:
        print("unknown cmd", cmd)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
