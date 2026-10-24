#!/usr/bin/env python3
"""Minimal ECMA-335 metadata reader + IL disassembler (no external deps).
Usage: dnparse.py <assembly.dll> <out.txt> [--il]
"""
import struct, sys

class R:
    def __init__(s, b, o=0): s.b, s.o = b, o
    def u1(s): v = s.b[s.o]; s.o += 1; return v
    def u2(s): v = struct.unpack_from('<H', s.b, s.o)[0]; s.o += 2; return v
    def u4(s): v = struct.unpack_from('<I', s.b, s.o)[0]; s.o += 4; return v
    def u8(s): v = struct.unpack_from('<Q', s.b, s.o)[0]; s.o += 8; return v

def cint(b, o):
    x = b[o]
    if x & 0x80 == 0: return x, o + 1
    if x & 0xC0 == 0x80: return ((x & 0x3F) << 8) | b[o+1], o + 2
    return ((x & 0x1F) << 24) | (b[o+1] << 16) | (b[o+2] << 8) | b[o+3], o + 4

CODED = {
 'TDOR': (2, [2, 1, 0x1B]), 'HasConst': (2, [4, 8, 0x17]),
 'HasCA': (5, [6,4,1,2,8,9,0xA,0,0xE,0x17,0x14,0x11,0x1A,0x1B,0x20,0x23,0x26,0x27,0x28,0x2A,0x2C,0x2B]),
 'HasFM': (1, [4, 8]), 'HasDS': (2, [2, 6, 0x20]), 'MRP': (3, [2, 1, 0x1A, 6, 0x1B]),
 'HasSem': (1, [0x14, 0x17]), 'MDOR': (1, [6, 0xA]), 'MemFwd': (1, [4, 6]),
 'Impl': (2, [0x26, 0x23, 0x27]), 'CAType': (3, [None, None, 6, 0xA, None]),
 'ResScope': (2, [0, 0x1A, 0x23, 1]), 'TOMD': (1, [2, 6]),
}
SCHEMA = {
 0x00: [('Gen','u2'),('Name','str'),('Mvid','guid'),('Enc','guid'),('EncB','guid')],
 0x01: [('Scope','ResScope'),('Name','str'),('Ns','str')],
 0x02: [('Flags','u4'),('Name','str'),('Ns','str'),('Extends','TDOR'),('FieldList',('t',4)),('MethodList',('t',6))],
 0x03: [('F',('t',4))],
 0x04: [('Flags','u2'),('Name','str'),('Sig','blob')],
 0x05: [('M',('t',6))],
 0x06: [('RVA','u4'),('Impl','u2'),('Flags','u2'),('Name','str'),('Sig','blob'),('ParamList',('t',8))],
 0x07: [('P',('t',8))],
 0x08: [('Flags','u2'),('Seq','u2'),('Name','str')],
 0x09: [('Class',('t',2)),('Iface','TDOR')],
 0x0A: [('Class','MRP'),('Name','str'),('Sig','blob')],
 0x0B: [('Type','u2'),('Parent','HasConst'),('Value','blob')],
 0x0C: [('Parent','HasCA'),('Type','CAType'),('Value','blob')],
 0x0D: [('Parent','HasFM'),('NT','blob')],
 0x0E: [('Action','u2'),('Parent','HasDS'),('PS','blob')],
 0x0F: [('Pack','u2'),('Size','u4'),('Parent',('t',2))],
 0x10: [('Off','u4'),('Field',('t',4))],
 0x11: [('Sig','blob')],
 0x12: [('Parent',('t',2)),('EventList',('t',0x14))],
 0x13: [('E',('t',0x14))],
 0x14: [('Flags','u2'),('Name','str'),('Type','TDOR')],
 0x15: [('Parent',('t',2)),('PropList',('t',0x17))],
 0x16: [('P',('t',0x17))],
 0x17: [('Flags','u2'),('Name','str'),('Type','blob')],
 0x18: [('Sem','u2'),('Method',('t',6)),('Assoc','HasSem')],
 0x19: [('Class',('t',2)),('Body','MDOR'),('Decl','MDOR')],
 0x1A: [('Name','str')],
 0x1B: [('Sig','blob')],
 0x1C: [('Flags','u2'),('Fwd','MemFwd'),('Name','str'),('Scope',('t',0x1A))],
 0x1D: [('RVA','u4'),('Field',('t',4))],
 0x1E: [('Tok','u4'),('Fn','u4')],
 0x1F: [('Tok','u4')],
 0x20: [('Hash','u4'),('V1','u2'),('V2','u2'),('V3','u2'),('V4','u2'),('Flags','u4'),('PK','blob'),('Name','str'),('Cult','str')],
 0x21: [('P','u4')],
 0x22: [('a','u4'),('b','u4'),('c','u4')],
 0x23: [('V1','u2'),('V2','u2'),('V3','u2'),('V4','u2'),('Flags','u4'),('PK','blob'),('Name','str'),('Cult','str'),('Hash','blob')],
 0x24: [('P','u4'),('A',('t',0x23))],
 0x25: [('a','u4'),('b','u4'),('c','u4'),('A',('t',0x23))],
 0x26: [('Flags','u4'),('Name','str'),('Hash','blob')],
 0x27: [('Flags','u4'),('Id','u4'),('Name','str'),('Ns','str'),('Impl','Impl')],
 0x28: [('Off','u4'),('Flags','u4'),('Name','str'),('Impl','Impl')],
 0x29: [('Nested',('t',2)),('Encl',('t',2))],
 0x2A: [('Num','u2'),('Flags','u2'),('Owner','TOMD'),('Name','str')],
 0x2B: [('Method','MDOR'),('Inst','blob')],
 0x2C: [('Owner',('t',0x2A)),('Constraint','TDOR')],
}

class Asm:
    def __init__(s, path):
        b = s.b = open(path, 'rb').read()
        pe = struct.unpack_from('<I', b, 0x3C)[0]
        nsec = struct.unpack_from('<H', b, pe + 6)[0]
        optsz = struct.unpack_from('<H', b, pe + 20)[0]
        opt = pe + 24
        magic = struct.unpack_from('<H', b, opt)[0]
        dd = opt + (96 if magic == 0x10B else 112)
        cli_rva = struct.unpack_from('<I', b, dd + 14 * 8)[0]
        s.secs = []
        st = opt + optsz
        for i in range(nsec):
            vs, va, rs, rp = struct.unpack_from('<IIII', b, st + i * 40 + 8)
            s.secs.append((va, max(vs, rs), rp))
        cli = s.rva(cli_rva)
        md_rva = struct.unpack_from('<I', b, cli + 8)[0]
        md = s.rva(md_rva)
        vlen = struct.unpack_from('<I', b, md + 12)[0]
        o = md + 16 + vlen
        nstreams = struct.unpack_from('<H', b, o + 2)[0]; o += 4
        s.streams = {}
        for _ in range(nstreams):
            off, size = struct.unpack_from('<II', b, o); o += 8
            e = b.index(b'\0', o); name = b[o:e].decode(); o = (e + 4) & ~3
            s.streams[name] = (md + off, size)
        s.strs = s.streams['#Strings'][0]
        s.blob = s.streams['#Blob'][0]
        s.us = s.streams['#US'][0]
        s.read_tables(s.streams.get('#~') or s.streams['#-'])

    def rva(s, r):
        for va, sz, rp in s.secs:
            if va <= r < va + sz: return r - va + rp
        raise ValueError(hex(r))

    def str(s, i):
        o = s.strs + i; e = s.b.index(b'\0', o); return s.b[o:e].decode('utf8', 'replace')

    def blobat(s, i):
        n, o = cint(s.b, s.blob + i); return s.b[o:o+n]

    def usstr(s, i):
        n, o = cint(s.b, s.us + i); return s.b[o:o+n-1].decode('utf-16le', 'replace')

    def read_tables(s, st):
        b = s.b; o = st[0]
        hs = b[o + 6]; valid = struct.unpack_from('<Q', b, o + 8)[0]; o += 24
        s.rows = [0] * 64
        for i in range(64):
            if valid >> i & 1: s.rows[i] = struct.unpack_from('<I', b, o)[0]; o += 4
        strsz = 4 if hs & 1 else 2; guidsz = 4 if hs & 2 else 2; blobsz = 4 if hs & 4 else 2
        def csize(k):
            bits, tabs = CODED[k]
            mx = max((s.rows[t] for t in tabs if t is not None), default=0)
            return 2 if mx < (1 << (16 - bits)) else 4
        s.tables = {}
        for t in range(0x2D):
            if not s.rows[t]: continue
            cols = []
            for name, ty in SCHEMA[t]:
                if ty == 'u1': sz = 1
                elif ty == 'u2': sz = 2
                elif ty == 'u4': sz = 4
                elif ty == 'str': sz = strsz
                elif ty == 'guid': sz = guidsz
                elif ty == 'blob': sz = blobsz
                elif isinstance(ty, tuple): sz = 2 if s.rows[ty[1]] < 65536 else 4
                else: sz = csize(ty)
                cols.append((name, ty, sz))
            rows = []
            for r in range(s.rows[t]):
                d = {}
                for name, ty, sz in cols:
                    v = b[o] if sz == 1 else struct.unpack_from('<H' if sz == 2 else '<I', b, o)[0]
                    o += sz
                    if ty == 'str': v = s.str(v)
                    elif isinstance(ty, str) and ty in CODED:
                        bits, tabs = CODED[ty]; v = (tabs[v & ((1 << bits) - 1)], v >> bits)
                    d[name] = v
                rows.append(d)
            s.tables[t] = rows
        s.T = lambda t: s.tables.get(t, [])
        # nesting
        s.encl = {r['Nested']: r['Encl'] for r in s.T(0x29)}
        # field/method ownership
        tds = s.T(2)
        s.field_owner, s.method_owner = {}, {}
        for i, td in enumerate(tds):
            fe = tds[i+1]['FieldList'] if i + 1 < len(tds) else s.rows[4] + 1
            me = tds[i+1]['MethodList'] if i + 1 < len(tds) else s.rows[6] + 1
            td['fields'] = list(range(td['FieldList'], fe))
            td['methods'] = list(range(td['MethodList'], me))
            for f in td['fields']: s.field_owner[f] = i + 1
            for m in td['methods']: s.method_owner[m] = i + 1

    def tdname(s, i):
        td = s.T(2)[i-1]
        n = td['Name']
        if i in s.encl: return s.tdname(s.encl[i]) + '/' + n
        return (td['Ns'] + '.' if td['Ns'] else '') + n

    def trname(s, i):
        tr = s.T(1)[i-1]
        sc = tr['Scope']
        pre = ''
        if sc[0] == 1 and sc[1]: pre = s.trname(sc[1]) + '/'
        elif tr['Ns']: pre = tr['Ns'] + '.'
        return pre + tr['Name']

    def tname(s, coded):
        t, i = coded
        if i == 0: return ''
        if t == 2: return s.tdname(i)
        if t == 1: return s.trname(i)
        if t == 0x1B: return s.sigtype(s.blobat(s.T(0x1B)[i-1]['Sig']), 0)[0]
        return '?'

    def sigtype(s, b, o):
        e = b[o]; o += 1
        prim = {1:'void',2:'bool',3:'char',4:'sbyte',5:'byte',6:'short',7:'ushort',8:'int',9:'uint',10:'long',11:'ulong',12:'float',13:'double',14:'string',0x16:'TypedRef',0x18:'IntPtr',0x19:'UIntPtr',0x1c:'object'}
        if e in prim: return prim[e], o
        if e in (0x11, 0x12):
            v, o = cint(b, o); return s.tname(([2, 1, 0x1B][v & 3], v >> 2)), o
        if e == 0x0f: t, o = s.sigtype(b, o); return t + '*', o
        if e == 0x10: t, o = s.sigtype(b, o); return 'ref ' + t, o
        if e == 0x1d: t, o = s.sigtype(b, o); return t + '[]', o
        if e in (0x13, 0x1e): v, o = cint(b, o); return ('!' if e == 0x13 else '!!') + str(v), o
        if e == 0x15:
            g, o = s.sigtype(b, o); n, o = cint(b, o); args = []
            for _ in range(n): a, o = s.sigtype(b, o); args.append(a)
            return g.split('`')[0] + '<' + ','.join(args) + '>', o
        if e == 0x14:
            t, o = s.sigtype(b, o); rank, o = cint(b, o)
            n, o = cint(b, o)
            for _ in range(n): _, o = cint(b, o)
            n, o = cint(b, o)
            for _ in range(n): _, o = cint(b, o)
            return t + '[' + ','*(rank-1) + ']', o
        if e in (0x1f, 0x20): _, o = cint(b, o); return s.sigtype(b, o)
        if e == 0x45: return s.sigtype(b, o)
        if e == 0x1b: return 'fnptr', len(b)
        return '?%x' % e, o

    def methsig(s, blob):
        b = blob; o = 0; cc = b[o]; o += 1
        if cc & 0x10: _, o = cint(b, o)
        n, o = cint(b, o)
        ret, o = s.sigtype(b, o)
        ps = []
        for _ in range(n):
            try: p, o = s.sigtype(b, o)
            except Exception: p = '?'
            ps.append(p)
        return ret, ps, bool(cc & 0x20)

    def fieldsig(s, blob):
        return s.sigtype(blob, 1)[0]

    def mdname(s, i, full=True):
        m = s.T(6)[i-1]
        return (s.tdname(s.method_owner[i]) + '::' if full else '') + m['Name']

    def tok(s, t):
        tab, i = t >> 24, t & 0xFFFFFF
        try:
            if tab == 0x70: return '"' + s.usstr(i).replace('\n', '\\n')[:200] + '"'
            if tab == 6: return s.mdname(i)
            if tab == 4: return s.tdname(s.field_owner[i]) + '::' + s.T(4)[i-1]['Name']
            if tab == 0xA:
                mr = s.T(0xA)[i-1]; c = mr['Class']
                cn = s.tname(c) if c[0] in (1, 2, 0x1B) else ('?' if c[0] != 6 else s.mdname(c[1]))
                return cn + '::' + mr['Name']
            if tab == 0x2B:
                ms = s.T(0x2B)[i-1]; mt, mi = ms['Method']
                return s.tok((mt << 24) | mi) + '<>'
            if tab in (1, 2, 0x1B): return s.tname((tab, i))
            if tab == 0x11: return 'sig'
        except Exception as ex: return '<bad %x>' % t
        return '<tok %x>' % t

OPS1 = {0x00:'nop',0x01:'break',0x02:'ldarg.0',0x03:'ldarg.1',0x04:'ldarg.2',0x05:'ldarg.3',0x06:'ldloc.0',0x07:'ldloc.1',0x08:'ldloc.2',0x09:'ldloc.3',0x0A:'stloc.0',0x0B:'stloc.1',0x0C:'stloc.2',0x0D:'stloc.3',0x14:'ldnull',0x15:'ldc.i4.m1',0x25:'dup',0x26:'pop',0x2A:'ret',0x58:'add',0x59:'sub',0x5A:'mul',0x5B:'div',0x5F:'and',0x60:'or',0x7A:'throw',0x8E:'ldlen',0xDC:'endfinally',0x8C:'box',0x8D:'newarr',0xA5:'unbox.any',0xD0:'ldtoken',0x28:'call',0x6F:'callvirt',0x73:'newobj',0x72:'ldstr',0x7B:'ldfld',0x7C:'ldflda',0x7D:'stfld',0x7E:'ldsfld',0x7F:'ldsflda',0x80:'stsfld',0x74:'castclass',0x75:'isinst',0x71:'ldobj',0x81:'stobj',0xA3:'ldelem',0xA4:'stelem',0x8F:'ldelema',0x79:'unbox',0x27:'jmp',0x29:'calli',0x70:'cpobj',0xC2:'refanyval',0xC6:'mkrefany',
 0x0E:'ldarg.s',0x0F:'ldarga.s',0x10:'starg.s',0x11:'ldloc.s',0x12:'ldloca.s',0x13:'stloc.s',0x1F:'ldc.i4.s',0x20:'ldc.i4',0x21:'ldc.i8',0x22:'ldc.r4',0x23:'ldc.r8',0x45:'switch',0xDD:'leave',0xDE:'leave.s',
 0x2B:'br.s',0x2C:'brfalse.s',0x2D:'brtrue.s',0x2E:'beq.s',0x2F:'bge.s',0x30:'bgt.s',0x31:'ble.s',0x32:'blt.s',0x33:'bne.un.s',0x34:'bge.un.s',0x35:'bgt.un.s',0x36:'ble.un.s',0x37:'blt.un.s',
 0x38:'br',0x39:'brfalse',0x3A:'brtrue',0x3B:'beq',0x3C:'bge',0x3D:'bgt',0x3E:'ble',0x3F:'blt',0x40:'bne.un',0x41:'bge.un',0x42:'bgt.un',0x43:'ble.un',0x44:'blt.un',0x65:'neg',0x66:'not',0x69:'conv.i4',0x6B:'conv.r4',0x6C:'conv.r8',0x6A:'conv.i8',0x5D:'rem',0x61:'xor',0x62:'shl',0x63:'shr'}
for k in range(0x16, 0x1F): OPS1[k] = 'ldc.i4.%d' % (k - 0x16)
TOK1 = {0x27,0x28,0x29,0x6F,0x70,0x71,0x72,0x73,0x74,0x75,0x79,0x7B,0x7C,0x7D,0x7E,0x7F,0x80,0x81,0x8C,0x8D,0x8F,0xA3,0xA4,0xA5,0xC2,0xC6,0xD0}
B1 = {0x0E,0x0F,0x10,0x11,0x12,0x13,0x1F,0xDE} | set(range(0x2B, 0x38))
B4 = {0x20,0x22,0xDD} | set(range(0x38, 0x45))
OPS2 = {0x01:'ceq',0x02:'cgt',0x03:'cgt.un',0x04:'clt',0x05:'clt.un',0x06:'ldftn',0x07:'ldvirtftn',0x09:'ldarg',0x0A:'ldarga',0x0B:'starg',0x0C:'ldloc',0x0D:'ldloca',0x0E:'stloc',0x15:'initobj',0x16:'constrained.',0x1A:'rethrow',0x1C:'sizeof',0x12:'unaligned.',0x19:'no.'}
TOK2 = {0x06,0x07,0x15,0x16,0x1C}

def disasm(a, rva):
    b = a.b; o = a.rva(rva); h = b[o]
    if h & 3 == 2: size = h >> 2; o += 1
    else:
        size = struct.unpack_from('<I', b, o + 4)[0]; o += (struct.unpack_from('<H', b, o)[0] >> 12) * 4
    code = b[o:o+size]; out = []; p = 0
    while p < len(code):
        start = p; op = code[p]; p += 1; arg = ''
        if op == 0xFE:
            op2 = code[p]; p += 1; name = OPS2.get(op2, 'fe.%02x' % op2)
            if op2 in TOK2: arg = a.tok(struct.unpack_from('<I', code, p)[0]); p += 4
            elif op2 in (0x09,0x0A,0x0B,0x0C,0x0D,0x0E): arg = str(struct.unpack_from('<H', code, p)[0]); p += 2
            elif op2 in (0x12, 0x19): p += 1
        else:
            name = OPS1.get(op, 'op.%02x' % op)
            if op in TOK1: arg = a.tok(struct.unpack_from('<I', code, p)[0]); p += 4
            elif op in B1:
                v = struct.unpack_from('<b', code, p)[0]; p += 1
                arg = ('IL_%04x' % (p + v)) if 0x2B <= op <= 0x37 or op == 0xDE else str(v)
            elif op in B4:
                if op == 0x22: arg = str(struct.unpack_from('<f', code, p)[0])
                else:
                    v = struct.unpack_from('<i', code, p)[0]
                    arg = ('IL_%04x' % (p + 4 + v)) if op >= 0x38 else str(v)
                p += 4
            elif op in (0x21, 0x23):
                arg = str(struct.unpack_from('<q' if op == 0x21 else '<d', code, p)[0]); p += 8
            elif op == 0x45:
                n = struct.unpack_from('<I', code, p)[0]; p += 4 + 4 * n; arg = '(%d)' % n
        out.append('      IL_%04x: %s %s' % (start, name, arg))
    return out

def main():
    a = Asm(sys.argv[1]); il = '--il' in sys.argv
    out = open(sys.argv[2], 'w')
    props = {}
    for pm in a.T(0x15):
        pass
    for i, td in enumerate(a.T(2), 1):
        ext = a.tname(td['Extends']) if td['Extends'][1] else ''
        out.write('\nTYPE %s : %s\n' % (a.tdname(i), ext))
        for f in td['fields']:
            fr = a.T(4)[f-1]
            st = 'static ' if fr['Flags'] & 0x10 else ''
            out.write('  FIELD %s%s %s\n' % (st, a.fieldsig(a.blobat(fr['Sig'])), fr['Name']))
        for m in td['methods']:
            mr = a.T(6)[m-1]
            try: ret, ps, inst = a.methsig(a.blobat(mr['Sig']))
            except Exception: ret, ps, inst = '?', [], True
            st = '' if mr['Flags'] & 0x10 == 0 else 'static '
            vis = ['priv?','private','famandassem','internal','protected','famorassem','public'][mr['Flags'] & 7] if mr['Flags'] & 7 < 7 else '?'
            out.write('  METHOD %s %s%s %s(%s)\n' % (vis, st, ret, mr['Name'], ', '.join(ps)))
            if il and mr['RVA']:
                try: out.write('\n'.join(disasm(a, mr['RVA'])) + '\n')
                except Exception as ex: out.write('      <disasm error %s>\n' % ex)
    out.close()

if __name__ == '__main__':
    main()
