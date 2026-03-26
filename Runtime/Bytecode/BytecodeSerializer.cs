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
    ///   Header: "WSBC" magic (4 bytes) + version (u8)
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
        private const byte Version = 1;

        // ────────────────────────────────────────────────────────
        //  Save
        // ────────────────────────────────────────────────────────

        public static void Save(BinaryWriter w, CompiledFunction topLevel,
            DefinitionScope definitionScope)
        {
            // Header
            w.Write(Magic);
            w.Write(Version);

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

            // Line numbers (for debug / stack traces)
            w.Write((uint)func.Chunk.Lines.Count);
            foreach (var line in func.Chunk.Lines)
                w.Write(line);

            // Local variable names (for debugger)
            w.Write((ushort)func.LocalNames.Length);
            foreach (var name in func.LocalNames)
                WriteString(w, name ?? "");
        }

        private static void WriteConstant(BinaryWriter w, in WarValue val)
        {
            w.Write((byte)val.Tag);
            switch (val.Tag)
            {
                case ValueTag.Null:
                    break;
                case ValueTag.Numeric:
                    w.Write(val.Numeric);
                    break;
                case ValueTag.Text:
                    WriteString(w, val.TextValue);
                    break;
                case ValueTag.Logical:
                    w.Write((byte)(val.LogicalValue ? 1 : 0));
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

        public static (CompiledFunction topLevel, DefinitionScope scope) Load(
            BinaryReader r, WarScriptLanguage script, DefinitionScope parentScope)
        {
            // Header
            var magic = r.ReadBytes(4);
            if (magic[0] != 'W' || magic[1] != 'S' || magic[2] != 'B' || magic[3] != 'C')
                throw new InvalidDataException("Not a WSBC file");
            var version = r.ReadByte();
            if (version > Version)
                throw new InvalidDataException(
                    $"Bytecode version {version} is newer than supported version {Version}");

            // Top-level script body
            var topLevel = ReadCompiledFunction(r);

            // Rebuild DefinitionScope
            var scope = new DefinitionScope(script, parentScope);
            ReadDefinitionScope(r, script, scope);

            return (topLevel, scope);
        }

        private static void ReadDefinitionScope(BinaryReader r,
            WarScriptLanguage script, DefinitionScope scope)
        {
            // Functions
            var funcCount = r.ReadUInt16();
            for (int i = 0; i < funcCount; i++)
            {
                var funcDef = ReadFunctionDef(r, script, scope);
                scope.AddFunction(funcDef);
            }

            // Classes
            var classCount = r.ReadUInt16();
            for (int i = 0; i < classCount; i++)
            {
                var classDef = ReadClassDef(r, script, scope);
                scope.AddClass(classDef);
            }
        }

        private static FunctionDefinition ReadFunctionDef(BinaryReader r,
            WarScriptLanguage script, DefinitionScope parentScope)
        {
            var name = ReadString(r);
            var argCount = r.ReadUInt16();
            var args = new List<string>(argCount);
            for (int i = 0; i < argCount; i++)
                args.Add(ReadString(r));

            var compiled = ReadCompiledFunction(r);

            var details = new FunctionDetails(name, args);
            var funcDef = new FunctionDefinition(details, null!, null!);
            funcDef.Compiled = compiled;
            return funcDef;
        }

        private static ClassDefinition ReadClassDef(BinaryReader r,
            WarScriptLanguage script, DefinitionScope parentScope)
        {
            // Class identity
            var name = ReadString(r);
            var propCount = r.ReadUInt16();
            var props = new List<string>(propCount);
            for (int i = 0; i < propCount; i++)
                props.Add(ReadString(r));

            // Base types
            var baseCount = r.ReadUInt16();
            var baseTypes = new List<ClassDetails>(baseCount);
            for (int i = 0; i < baseCount; i++)
            {
                var btName = ReadString(r);
                var btPropCount = r.ReadUInt16();
                var btProps = new List<string>(btPropCount);
                for (int j = 0; j < btPropCount; j++)
                    btProps.Add(ReadString(r));
                baseTypes.Add(new ClassDetails(btName, btProps));
            }

            // Constructor bytecode
            CompiledFunction? compiledCtor = null;
            var hasCtor = r.ReadByte();
            if (hasCtor != 0)
                compiledCtor = ReadCompiledFunction(r);

            // Build the class's own DefinitionScope (for methods + nested classes)
            var classScope = new DefinitionScope(script, parentScope);
            ReadDefinitionScope(r, script, classScope);

            var classDetails = new ClassDetails(name, props);
            var classDef = new ClassDefinition(classDetails, baseTypes, null!, classScope);
            classDef.CompiledConstructor = compiledCtor;
            return classDef;
        }

        private static CompiledFunction ReadCompiledFunction(BinaryReader r)
        {
            var name = ReadString(r);
            var arity = r.ReadUInt16();
            var localCount = r.ReadUInt16();

            var func = new CompiledFunction(name, arity);
            func.LocalCount = localCount;

            // Constant pool
            var constCount = r.ReadUInt16();
            for (int i = 0; i < constCount; i++)
                func.Chunk.Constants.Add(ReadConstant(r));

            // Bytecode
            var codeLen = r.ReadUInt32();
            for (uint i = 0; i < codeLen; i++)
                func.Chunk.Code.Add(r.ReadByte());

            // Line numbers
            var lineCount = r.ReadUInt32();
            for (uint i = 0; i < lineCount; i++)
                func.Chunk.Lines.Add(r.ReadInt32());

            // Local variable names
            var nameCount = r.ReadUInt16();
            var names = new string[nameCount];
            for (int i = 0; i < nameCount; i++)
            {
                var n = ReadString(r);
                names[i] = n.Length == 0 ? null! : n;
            }
            func.LocalNames = names;

            return func;
        }

        private static WarValue ReadConstant(BinaryReader r)
        {
            var tag = (ValueTag)r.ReadByte();
            switch (tag)
            {
                case ValueTag.Null:    return WarValue.Null;
                case ValueTag.Numeric: return WarValue.FromNumeric(r.ReadDouble());
                case ValueTag.Text:    return WarValue.FromText(ReadString(r));
                case ValueTag.Logical: return WarValue.FromLogical(r.ReadByte() != 0);
                default:
                    throw new InvalidDataException($"Unknown constant tag: {tag}");
            }
        }

        private static string ReadString(BinaryReader r)
        {
            var len = r.ReadUInt16();
            var bytes = r.ReadBytes(len);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
