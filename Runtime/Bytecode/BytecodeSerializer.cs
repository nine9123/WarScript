#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace WarScript.Bytecode
{
    /// <summary>
    /// Serializes and deserializes compiled WarScript bytecode to a compact
    /// binary format. This allows scripts to be pre-compiled at build time
    /// and loaded at runtime without the lexer, parser, or compiler.
    ///
    /// Binary format (little-endian):
    ///   Header: "WSBC" magic (4 bytes) + version (u8) + format hash (u32);
    ///   both must equal <see cref="BytecodeFormat"/> exactly (T1.7, PROP-18)
    ///   Top-level CompiledFunction (script body)
    ///   Function table (user-defined functions)
    ///   Class table (user-defined classes, recursive for nesting)
    ///
    /// Usage:
    ///   // Build time: compile and save
    ///   script.Run();
    ///   BytecodeSerializer.Save(script, stream);
    ///
    ///   // Runtime: load without parsing
    ///   script.LoadBytecode(stream);
    ///   var tick = script.GetFunction("tick", 1);
    ///   script.Call(tick, dt);
    /// </summary>
    public static class BytecodeSerializer
    {
        private static readonly byte[] Magic = { (byte)'W', (byte)'S', (byte)'B', (byte)'C' };

        // ────────────────────────────────────────────────────────
        //  Save
        // ────────────────────────────────────────────────────────

        public static void Save(BinaryWriter w, CompiledFunction topLevel,
            DefinitionScope definitionScope)
        {
            // Header
            w.Write(Magic);
            w.Write(BytecodeFormat.Version);
            w.Write(BytecodeFormat.Hash);

            // Top-level script body
            WriteCompiledFunction(w, topLevel);

            // Function and class tables from the definition scope
            WriteDefinitionScope(w, definitionScope);
        }

        private static void WriteDefinitionScope(BinaryWriter w, DefinitionScope scope)
        {
            // Functions (skip natives — they're re-registered at load time)
            var userFunctions = new List<FunctionDefinition>();
            foreach (var f in scope.Functions)
            {
                if (f is NativeFunctionDefinition) continue;
                userFunctions.Add(f);
            }

            w.Write((ushort)userFunctions.Count);
            foreach (var f in userFunctions)
                WriteFunctionDef(w, f);

            // Classes
            var classes = new List<ClassDefinition>();
            foreach (var c in scope.ClassDefinitions)
                classes.Add(c);

            w.Write((ushort)classes.Count);
            foreach (var c in classes)
                WriteClassDef(w, c);
        }

        private static void WriteFunctionDef(BinaryWriter w, FunctionDefinition funcDef)
        {
            WriteString(w, funcDef.Details.Name);
            w.Write((ushort)funcDef.Details.Arguments.Count);
            foreach (var arg in funcDef.Details.Arguments)
                WriteString(w, arg);
            w.Write((ushort)funcDef.Details.MinArity);
            WriteCompiledFunction(w, funcDef.Compiled!);
        }

        private static void WriteClassDef(BinaryWriter w, ClassDefinition classDef)
        {
            // Class identity
            WriteString(w, classDef.ClassDetails.Name);
            w.Write((ushort)classDef.ClassDetails.Properties.Count);
            foreach (var prop in classDef.ClassDetails.Properties)
                WriteString(w, prop);

            // Base types
            w.Write((ushort)classDef.BaseTypes.Count);
            foreach (var bt in classDef.BaseTypes)
            {
                WriteString(w, bt.Name);
                w.Write((ushort)bt.Properties.Count);
                foreach (var prop in bt.Properties)
                    WriteString(w, prop);
            }

            // Constructor bytecode (optional)
            w.Write((byte)(classDef.CompiledConstructor != null ? 1 : 0));
            if (classDef.CompiledConstructor != null)
                WriteCompiledFunction(w, classDef.CompiledConstructor);

            // Methods and nested classes live in the class's DefinitionScope
            WriteDefinitionScope(w, classDef.GetDefinitionScope());
        }

        private static void WriteCompiledFunction(BinaryWriter w, CompiledFunction func)
        {
            WriteString(w, func.Name);
            w.Write((ushort)func.Arity);
            w.Write((ushort)func.LocalCount);

            // Constant pool
            w.Write((ushort)func.Chunk.Constants.Count);
            foreach (var c in func.Chunk.Constants)
                WriteConstant(w, c);

            // Bytecode
            w.Write((uint)func.Chunk.Code.Count);
            foreach (var b in func.Chunk.Code)
                w.Write(b);

            // Line numbers (for debug / stack traces): run-length entries (10.2)
            var lines = func.Chunk.Lines;
            w.Write((uint)lines.EntryCount);
            for (int i = 0; i < lines.EntryCount; i++)
            {
                w.Write((ushort)lines.OffsetAt(i));
                w.Write(lines.LineAt(i));
            }

            // Local variable names (for debugger)
            w.Write((ushort)func.LocalNames.Length);
            foreach (var name in func.LocalNames)
                WriteString(w, name ?? "");

            // Inline cache slot count (rebuilt at runtime, just need the size)
            w.Write((ushort)func.Chunk.PropertyCaches.Length);
        }

        private static void WriteConstant(BinaryWriter w, in WarValue val)
        {
            w.Write((byte)val.Tag);
            switch (val.Tag)
            {
                case ValueTag.Null:
                    break;
                case ValueTag.Numeric:
                    w.Write(val.Numeric.Raw);
                    break;
                case ValueTag.Text:
                    WriteString(w, val.TextValue);
                    break;
                case ValueTag.Logical:
                    w.Write((byte)(val.LogicalValue ? 1 : 0));
                    break;
                case ValueTag.NativeObject when val.Ref is CompiledFunction:
                    // Lambda function value — serialize the compiled function inline
                    WriteCompiledFunction(w, (CompiledFunction)val.Ref);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Cannot serialize constant of type {val.Tag}");
            }
        }

        private static void WriteString(BinaryWriter w, string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            w.Write((ushort)bytes.Length);
            w.Write(bytes);
        }

        // ────────────────────────────────────────────────────────
        //  Load
        // ────────────────────────────────────────────────────────

        /// <summary>
        /// Reads a bytecode file. Anything malformed — wrong header, truncated
        /// data, counts larger than the bytes left, invalid UTF-8, nesting
        /// deeper than <see cref="MaxNesting"/>, or code the
        /// <see cref="BytecodeVerifier"/> rejects — throws
        /// <see cref="InvalidDataException"/> (v2 plan 10.4, SEC-1). Every
        /// function is verified while it is read, and nothing is installed:
        /// the caller installs the returned scope, so a failed load changes
        /// nothing.
        /// </summary>
        public static (CompiledFunction topLevel, DefinitionScope scope) Load(
            BinaryReader r, WarScriptLanguage script, DefinitionScope parentScope)
        {
            var reader = new BoundedReader(r);
            try
            {
                // Header
                var magic = reader.Bytes(System.Math.Min(4, reader.Remaining), "magic");
                if (magic.Length < 4 || magic[0] != 'W' || magic[1] != 'S' || magic[2] != 'B' || magic[3] != 'C')
                    throw new InvalidDataException("Not a WSBC file");
                ReadHeader(reader);

                // Top-level script body
                var topLevel = ReadCompiledFunction(reader, 0);
                RequireNoArity(topLevel, "script body");

                // Rebuild DefinitionScope
                var scope = new DefinitionScope(script, parentScope);
                ReadDefinitionScope(reader, script, scope, 0);

                return (topLevel, scope);
            }
            catch (EndOfStreamException)
            {
                throw reader.Truncated();
            }
        }

        /// <summary>Lambda constants and nested classes nest at most this deep (10.4).</summary>
        internal const int MaxNesting = 32;

        /// <summary>
        /// The version and format hash after the magic: the loader accepts
        /// only the exact current format (R4, PROP-18), so bytecode compiled
        /// by an older or newer runtime is rejected instead of misread (DET-4).
        /// </summary>
        private static void ReadHeader(BoundedReader r)
        {
            if (r.Remaining < 5) throw new InvalidDataException("Truncated bytecode header");
            var version = r.U8();
            var hash = r.U32();
            if (version != BytecodeFormat.Version)
                throw new InvalidDataException(System.FormattableString.Invariant(
                    $"Bytecode format version {version} is not the supported version {BytecodeFormat.Version}; recompile the script"));
            if (hash != BytecodeFormat.Hash)
                throw new InvalidDataException(System.FormattableString.Invariant(
                    $"Bytecode format hash {hash:X8} does not match {BytecodeFormat.Hash:X8}; recompile the script"));
        }

        private static void ReadDefinitionScope(BoundedReader r,
            WarScriptLanguage script, DefinitionScope scope, int depth)
        {
            // Functions: name, argument count, min arity, body (at least 2 + 2 + 2 bytes and a function)
            var funcCount = r.Count(MinFunctionDefBytes, "function");
            for (int i = 0; i < funcCount; i++)
            {
                var funcDef = ReadFunctionDef(r, depth);
                scope.AddFunction(funcDef);
            }

            // Classes
            var classCount = r.Count(MinClassDefBytes, "class");
            for (int i = 0; i < classCount; i++)
            {
                var classDef = ReadClassDef(r, script, scope, depth);
                scope.AddClass(classDef);
            }
        }

        // The smallest encodings, for checking counts against the bytes left.
        private const int MinCompiledFunctionBytes = 2 + 2 + 2 + 2 + 4 + 4 + 2 + 2; // empty name, no constants, code, line entries or local names
        private const int MinFunctionDefBytes = 2 + 2 + 2 + MinCompiledFunctionBytes;
        private const int MinClassDefBytes = 2 + 2 + 2 + 1 + 2 + 2;

        private static FunctionDefinition ReadFunctionDef(BoundedReader r, int depth)
        {
            var name = r.String();
            var argCount = r.Count(2, "argument");
            var args = new List<string>(argCount);
            for (int i = 0; i < argCount; i++)
                args.Add(r.String());

            var minArity = r.U16();
            var compiled = ReadCompiledFunction(r, depth);

            var details = new FunctionDetails(name, args, minArity);
            var funcDef = new FunctionDefinition(details, null!, null!);
            funcDef.Compiled = compiled;
            return funcDef;
        }

        private static ClassDefinition ReadClassDef(BoundedReader r,
            WarScriptLanguage script, DefinitionScope parentScope, int depth)
        {
            if (depth >= MaxNesting)
                throw new InvalidDataException("Bytecode classes nest deeper than 32 levels");

            // Class identity
            var name = r.String();
            var propCount = r.Count(2, "property");
            var props = new List<string>(propCount);
            for (int i = 0; i < propCount; i++)
                props.Add(r.String());

            // Base types
            var baseCount = r.Count(4, "base type");
            var baseTypes = new List<ClassDetails>(baseCount);
            for (int i = 0; i < baseCount; i++)
            {
                var btName = r.String();
                var btPropCount = r.Count(2, "property");
                var btProps = new List<string>(btPropCount);
                for (int j = 0; j < btPropCount; j++)
                    btProps.Add(r.String());
                baseTypes.Add(new ClassDetails(btName, btProps));
            }

            // Constructor bytecode
            CompiledFunction? compiledCtor = null;
            var hasCtor = r.U8();
            if (hasCtor > 1) throw new InvalidDataException("Invalid constructor flag in class '" + name + "'");
            if (hasCtor != 0)
            {
                compiledCtor = ReadCompiledFunction(r, depth);
                RequireNoArity(compiledCtor, "constructor of class '" + name + "'");
            }

            // Build the class's own DefinitionScope (for methods + nested classes)
            var classScope = new DefinitionScope(script, parentScope);
            ReadDefinitionScope(r, script, classScope, depth + 1);

            var classDetails = new ClassDetails(name, props);
            var classDef = new ClassDefinition(classDetails, baseTypes, null!, classScope);
            classDef.CompiledConstructor = compiledCtor;
            return classDef;
        }

        /// <summary>
        /// The VM enters the script body and constructor frames without
        /// arguments, while the verifier starts every function at height =
        /// arity: a nonzero arity there would let code pop below the frame's
        /// stack base (or read a slot nothing wrote). The compiler always
        /// gives both arity 0.
        /// </summary>
        private static void RequireNoArity(CompiledFunction function, string what)
        {
            if (function.Arity != 0)
                throw new InvalidDataException(System.FormattableString.Invariant(
                    $"Bytecode {what} has arity {function.Arity}; it takes no arguments"));
        }

        private static CompiledFunction ReadCompiledFunction(BoundedReader r, int depth)
        {
            if (depth >= MaxNesting)
                throw new InvalidDataException("Bytecode lambda constants nest deeper than 32 levels");

            var name = r.String();
            var arity = r.U16();
            var localCount = r.U16();

            var func = new CompiledFunction(name, arity);
            func.LocalCount = localCount;

            // Constant pool
            var constCount = r.Count(1, "constant");
            for (int i = 0; i < constCount; i++)
                func.Chunk.Constants.Add(ReadConstant(r, depth));

            // Bytecode
            var codeLen = r.U32();
            if (codeLen > BytecodeVerifier.MaxCodeLength)
                throw new InvalidDataException("Bytecode function '" + name + "' is longer than 65,535 bytes");
            func.Chunk.Code.AddRange(r.Bytes((int)codeLen, "code"));

            // Line numbers: (code offset u16, line i32) where the line
            // changes; at most one entry per code byte. The verifier checks
            // that the offsets start at 0, increase and stay inside the code.
            var lineEntries = r.U32();
            if (lineEntries > codeLen)
                throw new InvalidDataException("Bytecode function '" + name + "' has more line entries than code bytes");
            r.Need((long)lineEntries * 6, "line table");
            for (uint i = 0; i < lineEntries; i++)
            {
                var offset = r.U16();
                func.Chunk.Lines.AddEntry(offset, r.I32());
            }

            // Local variable names
            var nameCount = r.Count(2, "local name");
            var names = new string[nameCount];
            for (int i = 0; i < nameCount; i++)
            {
                var n = r.String();
                names[i] = n.Length == 0 ? null! : n;
            }
            func.LocalNames = names;

            // Inline cache slots (runtime-only, just allocate empty array)
            var cacheCount = r.U16();
            func.Chunk.PropertyCaches = new InlineCache[cacheCount];

            BytecodeVerifier.Verify(func, atLoad: true);
            return func;
        }

        private static WarValue ReadConstant(BoundedReader r, int depth)
        {
            var tag = (ValueTag)r.U8();
            switch (tag)
            {
                case ValueTag.Null:    return WarValue.Null;
                case ValueTag.Numeric: return WarValue.FromRawNumeric(r.I64());
                case ValueTag.Text:    return WarValue.FromText(r.String());
                case ValueTag.Logical: return WarValue.FromLogical(r.U8() != 0);
                case ValueTag.NativeObject:
                    // Lambda function value — deserialize the compiled function
                    var cf = ReadCompiledFunction(r, depth + 1);
                    return WarValue.FromNativeObject(cf);
                default:
                    throw new InvalidDataException(System.FormattableString.Invariant($"Unknown constant tag: {(byte)tag}"));
            }
        }

        /// <summary>
        /// Reads through a <see cref="BinaryReader"/> while knowing how many
        /// bytes are left, so a count is checked against them before anything
        /// is allocated for it (10.4). A non-seekable stream is copied first.
        /// </summary>
        private sealed class BoundedReader
        {
            private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

            private readonly BinaryReader _r;
            private readonly long _end;

            public BoundedReader(BinaryReader r)
            {
                if (!r.BaseStream.CanSeek)
                {
                    var copy = new MemoryStream();
                    r.BaseStream.CopyTo(copy);
                    copy.Position = 0;
                    r = new BinaryReader(copy, Encoding.UTF8);
                }
                _r = r;
                _end = r.BaseStream.Length;
            }

            public long Offset => _r.BaseStream.Position;
            public int Remaining => (int)System.Math.Min(int.MaxValue, System.Math.Max(0, _end - Offset));

            public InvalidDataException Truncated() =>
                new InvalidDataException(System.FormattableString.Invariant($"truncated bytecode at offset {Offset}"));

            public void Need(long bytes, string what)
            {
                if (bytes > Remaining)
                    throw new InvalidDataException(System.FormattableString.Invariant(
                        $"truncated bytecode at offset {Offset}: {what} needs {bytes} bytes, {Remaining} left"));
            }

            /// <summary>A u16 count of elements of at least <paramref name="minSize"/> bytes each.</summary>
            public int Count(int minSize, string what)
            {
                int count = U16();
                Need((long)count * minSize, what + " count");
                return count;
            }

            public byte U8() => _r.ReadByte();
            public ushort U16() => _r.ReadUInt16();
            public uint U32() => _r.ReadUInt32();
            public int I32() => _r.ReadInt32();
            public long I64() => _r.ReadInt64();

            public byte[] Bytes(int count, string what)
            {
                Need(count, what);
                return _r.ReadBytes(count);
            }

            public string String()
            {
                var bytes = Bytes(U16(), "string");
                try { return StrictUtf8.GetString(bytes); }
                catch (System.ArgumentException)
                {
                    throw new InvalidDataException(System.FormattableString.Invariant($"invalid UTF-8 in bytecode before offset {Offset}"));
                }
            }
        }
    }
}
