#nullable enable

using System.Collections.Generic;
using System.IO;
using WarScript.Expression.Value;

namespace WarScript.Bytecode
{
    /// <summary>
    /// Checks one compiled function against <see cref="OpInfo.Table"/> (v2
    /// plan 10.3, T1.7, SEC-1) and computes its <see cref="CompiledFunction.MaxStack"/>.
    /// Every loaded function is verified before anything is installed; the
    /// compiler verifies its own output, where a rejection is a compiler bug.
    ///
    /// Heights are relative to the frame's stack base: a function starts at
    /// height <see cref="CompiledFunction.Arity"/> (its arguments, padded by
    /// the VM), a rescue body at its <c>PushHandler</c>'s height + 1 (the
    /// error value) with one more handler open (the ensure-only handler,
    /// Q-14), an ensure body at that height. The block scopes and
    /// handlers a frame has open are tracked the same way, so a frame never
    /// pops what its caller pushed and a <c>TailCall</c> leaves nothing open
    /// (beyond the list in 10.3: the VM relies on both). Only instructions reachable
    /// from the start or from a handler target are interpreted; every
    /// instruction is still decoded and its operands checked.
    /// </summary>
    internal static class BytecodeVerifier
    {
        /// <summary>The VM's value stack (<c>WarVM.StackMax</c>), the limit on a loaded function's <see cref="CompiledFunction.MaxStack"/>.</summary>
        public const int StackLimit = 1024;

        /// <summary>The most arguments a call can pass (its count operand is one byte, 5.12).</summary>
        public const int MaxArity = 255;

        /// <summary>The largest code a function may have: jumps and handler addresses are 16-bit.</summary>
        public const int MaxCodeLength = 65535;

        /// <summary>
        /// Verifies <paramref name="function"/> (not the lambdas in its
        /// constants: the caller verifies each function it creates) and sets
        /// its <see cref="CompiledFunction.MaxStack"/>. With
        /// <paramref name="atLoad"/> the limits a compiler reports itself
        /// (T1.8) are rejected too: a maximum height above
        /// <see cref="StackLimit"/> and an arity above <see cref="MaxArity"/>.
        /// Without it such a function is accepted, and one needing more stack
        /// raises "Value stack overflow" when it is called.
        /// </summary>
        /// <exception cref="InvalidDataException">The function is malformed.</exception>
        public static void Verify(CompiledFunction function, bool atLoad)
        {
            var chunk = function.Chunk;
            var code = chunk.Code;
            int length = code.Count;
            string name = function.Name;

            if (length > MaxCodeLength) throw Reject(name, 0, "code is longer than 65,535 bytes");
            // A call carries at most 255 arguments (u8); the VM pads up to the arity with a byte count.
            if (atLoad && function.Arity > MaxArity) throw Reject(name, 0, "arity is above 255");
            var lineProblem = chunk.Lines.Problem(length);
            if (lineProblem != null) throw Reject(name, 0, lineProblem);

            var isStart = Decode(name, code);

            // ── Operands ──
            int constantCount = chunk.Constants.Count;
            int cacheCount = chunk.PropertyCaches.Length;
            for (int at = 0; at < length;)
            {
                var info = OpInfo.Get(code[at])!;
                int end = at + info.Length, p = at + 1;
                foreach (var kind in info.Operands)
                {
                    int v = OpInfo.OperandSize(kind) == 1 ? code[p] : (code[p] << 8) | code[p + 1];
                    p += OpInfo.OperandSize(kind);
                    switch (kind)
                    {
                        case OperandKind.U16Const:
                            if (v >= constantCount) throw Reject(name, at, info.Name + " constant index out of range");
                            break;
                        case OperandKind.U16Name:
                            if (v >= constantCount) throw Reject(name, at, info.Name + " constant index out of range");
                            if (chunk.Constants[v].Tag != ValueTag.Text) throw Reject(name, at, info.Name + " name operand is not a text constant");
                            break;
                        case OperandKind.U16Cache:
                            if (v >= cacheCount) throw Reject(name, at, info.Name + " cache slot out of range");
                            break;
                        case OperandKind.U8Flag:
                            if (v > 1) throw Reject(name, at, info.Name + " flag is not 0 or 1");
                            break;
                        case OperandKind.U16JumpForward:
                            if (end + v > length || !isStart[end + v]) throw Reject(name, at, info.Name + " target is not an instruction boundary");
                            break;
                        case OperandKind.U16JumpBackward:
                            if (end - v > at) throw Reject(name, at, info.Name + " does not jump backward");
                            if (end - v < 0 || !isStart[end - v]) throw Reject(name, at, info.Name + " target is not an instruction boundary");
                            break;
                        case OperandKind.U16Address:
                            if (v > length || !isStart[v]) throw Reject(name, at, info.Name + " address is not an instruction boundary");
                            break;
                    }
                }
                if (info.Op == OpCode.PushHandler)
                {
                    int rescue = ReadU16(code, at + 1), ensure = ReadU16(code, at + 3), endIP = ReadU16(code, at + 5);
                    if (rescue < end || ensure < rescue || endIP < ensure)
                        throw Reject(name, at, "handler addresses are not ordered after the PushHandler");
                }
                at = end;
            }

            // ── Stack heights, block scopes and handlers over the control-flow graph ──
            // Per reached offset: the stack height, the block scopes
            // (PushScope) and the handlers (PushHandler) the frame has open.
            var height = new int[length + 1];
            var scopes = new int[length + 1];
            var handlers = new int[length + 1];
            for (int i = 0; i < height.Length; i++) height[i] = -1;
            var work = new Stack<int>();
            int max = function.Arity;

            void Reach(int target, int h, int sc, int hd, int from)
            {
                if (target == length) throw Reject(name, from, "a path falls off the end of the code");
                if (height[target] == -1)
                {
                    height[target] = h;
                    scopes[target] = sc;
                    handlers[target] = hd;
                    if (h > max) max = h;
                    work.Push(target);
                }
                else if (height[target] != h)
                    throw Reject(name, target, "stack height " + Str(h) + " from offset " + Str(from)
                                               + " differs from " + Str(height[target]));
                else if (scopes[target] != sc || handlers[target] != hd)
                    throw Reject(name, target, "open scopes or handlers from offset " + Str(from) + " differ");
            }

            if (length == 0) throw Reject(name, 0, "a path falls off the end of the code");
            Reach(0, function.Arity, 0, 0, 0);
            while (work.Count > 0)
            {
                int at = work.Pop();
                int h = height[at], sc = scopes[at], hd = handlers[at];
                var info = OpInfo.Get(code[at])!;
                int end = at + info.Length;
                int pops = info.PopsAt(code, at);
                if (h < pops) throw Reject(name, at, info.Name + " needs " + Str(pops) + " values, the stack has " + Str(h));

                switch (info.Op)
                {
                    case OpCode.GetLocal:
                    case OpCode.SetLocal:
                    case OpCode.IndexSetLocal:
                        // The slot must hold a live value below the operands.
                        if (ReadU16(code, at + 1) >= h - pops)
                            throw Reject(name, at, info.Name + " slot is not below the stack height");
                        break;
                    case OpCode.PushScope:
                        sc++;
                        break;
                    case OpCode.PopScope:
                        // Would pop a scope the frame did not push.
                        if (sc == 0) throw Reject(name, at, "PopScope without an open block scope");
                        sc--;
                        break;
                    case OpCode.PopHandler:
                        // Would remove a handler of a calling frame.
                        if (hd == 0) throw Reject(name, at, "PopHandler without an open handler");
                        hd--;
                        break;
                    case OpCode.TailCall:
                        // The frame is reused: nothing of the old function's may stay open.
                        if (sc != 0 || hd != 0) throw Reject(name, at, "TailCall inside a block scope or handler");
                        break;
                }

                int next = h - pops + info.Pushes;
                if (next > max) max = next;

                if (info.Terminates) continue;
                switch (info.Op)
                {
                    case OpCode.Jump:
                        Reach(end + ReadU16(code, at + 1), next, sc, hd, at);
                        continue;
                    case OpCode.Loop:
                        Reach(end - ReadU16(code, at + 1), next, sc, hd, at);
                        continue;
                    case OpCode.JumpIfFalse:
                    case OpCode.JumpIfTrue:
                    case OpCode.LessJump:
                        Reach(end + ReadU16(code, at + 1), next, sc, hd, at);
                        break;
                    case OpCode.PushHandler:
                        // The VM enters both with the stack and scopes as they
                        // were at the PushHandler; the ensure block with the
                        // handler removed, the rescue body with it replaced by
                        // an ensure-only handler that its end removes (Q-14).
                        Reach(ReadU16(code, at + 1), h + 1, sc, hd + 1, at); // rescue: the error value
                        Reach(ReadU16(code, at + 3), h, sc, hd, at);         // ensure
                        hd++;
                        break;
                }
                Reach(end, next, sc, hd, at);
            }

            if (atLoad && max > StackLimit)
                throw Reject(name, 0, "needs " + Str(max) + " stack slots, more than " + Str(StackLimit));
            function.MaxStack = max;
        }

        /// <summary>
        /// The instruction boundaries the verifier decodes (offsets of
        /// instruction starts, in order); throws like <see cref="Verify"/>
        /// on an unknown opcode or a cut-off instruction.
        /// </summary>
        public static List<int> InstructionStarts(CompiledFunction function)
        {
            var isStart = Decode(function.Name, function.Chunk.Code);
            var starts = new List<int>();
            for (int i = 0; i < isStart.Length; i++)
                if (isStart[i]) starts.Add(i);
            return starts;
        }

        /// <summary>Opcodes and instruction boundaries: one flag per code offset (plus the end).</summary>
        private static bool[] Decode(string name, List<byte> code)
        {
            int length = code.Count;
            var isStart = new bool[length + 1];
            for (int at = 0; at < length;)
            {
                var info = OpInfo.Get(code[at]);
                if (info == null) throw Reject(name, at, "unknown opcode " + Str(code[at]));
                if (at + info.Length > length) throw Reject(name, at, info.Name + " runs past the end of the code");
                isStart[at] = true;
                at += info.Length;
            }
            return isStart;
        }

        private static int ReadU16(List<byte> code, int at) => (code[at] << 8) | code[at + 1];

        private static string Str(int v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static InvalidDataException Reject(string function, int offset, string problem) =>
            new InvalidDataException("Invalid bytecode in '" + function + "' at offset " + Str(offset) + ": " + problem);
    }
}
