using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using WarScript;
using WarScript.Bytecode;
using WarScript.Expression.Value;
using WarScript.Native;

namespace Tests
{
    /// <summary>
    /// Values passed between instances (plan 6.7, Q-1, T3.5): plain data
    /// crosses, including reading and writing the fields of another
    /// instance's class instance; calling a method or a function value owned
    /// by another instance (or instantiating one of its nested classes)
    /// raises a rescuable TypeError "belongs to another script instance".
    /// </summary>
    [TestFixture]
    public class CrossInstanceTests
    {
        private const string OwnerSource = @"
class Unit(hp)
    fun heal(n)
        @hp = @hp + n
        return @hp
    end
    class Part(w)
    end
end
fun make_unit()
    return new Unit(10)
end
fun make_lambda()
    return fun(x) return x * 2 end
end
fun make_data()
    return [1, ""two"", [3]]
end
";

        private const string UserSource = @"
fun read_hp(u)
    return u.hp
end
fun write_hp(u, v)
    u.hp = v
    return u.hp
end
fun call_heal(u)
    return u.heal(5)
end
fun rescue_heal(u)
    begin
        u.heal(5)
    rescue e
        return ""rescued: "" + e
    end
end
fun call_local(f)
    r = f(21)
    return r
end
fun call_global(f)
    global g
    g = f
    r = g(21)
    return r
end
fun tail_call_global(f)
    global g
    g = f
    return g(21)
end
fun nested(u)
    return u.new Part(1)
end
fun own_lambda()
    f = fun(x) return x + 1 end
    return f(1)
end
fun count(a)
    return Array_length(a)
end
# A name no class of the module set has is a compile error (T3.8,
# PROP-29): this script's own view of the owner's class.
class UnitView(hp)
    fun heal(n)
        return n
    end
end
";

        private static (WarScriptLanguage script, List<string> output) Instance(string name, string source)
        {
            var output = new List<string>();
            var script = new WarScriptLanguage(name, source, null, (s, m) => output.Add(m));
            WarScriptLibraryRegistry.RegisterAll(script, script.GlobalDefinitionScope);
            script.LogUnhandledErrors = false;
            Assert.IsTrue(script.Run().IsOk);
            return (script, output);
        }

        private static WarValue Get(WarScriptLanguage script, string function)
        {
            var r = script.Call(script.GetFunction(function, 0));
            Assert.IsTrue(r.IsOk, r.Error?.ToString());
            return r.Value;
        }

        private static ScriptResult Call(WarScriptLanguage script, string function, params WarValue[] args) =>
            script.Call(script.GetFunction(function, args.Length), args);

        private static void AssertForeign(ScriptResult r, string what)
        {
            Assert.AreEqual(ScriptStatus.Error, r.Status);
            Assert.AreEqual(ScriptErrorKind.TypeError, r.Error.Kind);
            Assert.AreEqual("Cannot call " + what + ": it belongs to another script instance", r.Error.Message);
        }

        [Test]
        public void ForeignInstance_FieldsCanBeReadAndWritten()
        {
            var (a, _) = Instance("a", OwnerSource);
            var (b, _) = Instance("b", UserSource);
            var unit = Get(a, "make_unit");

            var read = Call(b, "read_hp", unit);
            Assert.IsTrue(read.IsOk, read.Error?.ToString());
            Assert.IsTrue(read.Value.Numeric == 10);

            var write = Call(b, "write_hp", unit, WarValue.FromNumeric(FixMath.F64.FromInt(42)));
            Assert.IsTrue(write.IsOk, write.Error?.ToString());
            Assert.IsTrue(write.Value.Numeric == 42);
            // The owner sees the write: it is the same instance.
            Assert.IsTrue(unit.ClassValue.GetProperty("hp").Numeric == 42);
        }

        [Test]
        public void ForeignArray_CrossesAsPlainData()
        {
            var (a, _) = Instance("a", OwnerSource);
            var (b, _) = Instance("b", UserSource);
            var r = Call(b, "count", Get(a, "make_data"));
            Assert.IsTrue(r.IsOk, r.Error?.ToString());
            Assert.IsTrue(r.Value.Numeric == 3);
        }

        [Test]
        public void ForeignMethod_RaisesTypeError()
        {
            var (a, _) = Instance("a", OwnerSource);
            var (b, _) = Instance("b", UserSource);
            var unit = Get(a, "make_unit");

            AssertForeign(Call(b, "call_heal", unit), "method 'heal'");
            Assert.IsTrue(unit.ClassValue.GetProperty("hp").Numeric == 10, "the method did not run");

            // The owner may still call it.
            var own = Call(a, "make_unit");
            Assert.IsTrue(own.IsOk);
        }

        [Test]
        public void ForeignMethod_ErrorIsRescuable()
        {
            var (a, _) = Instance("a", OwnerSource);
            var (b, _) = Instance("b", UserSource);
            var r = Call(b, "rescue_heal", Get(a, "make_unit"));
            Assert.IsTrue(r.IsOk, r.Error?.ToString());
            Assert.AreEqual("rescued: Cannot call method 'heal': it belongs to another script instance", r.Value.TextValue);
            Assert.IsFalse(b.IsHalted);
        }

        [Test]
        public void ForeignFunctionValue_InALocal_RaisesTypeError()
        {
            var (a, _) = Instance("a", OwnerSource);
            var (b, _) = Instance("b", UserSource);
            AssertForeign(Call(b, "call_local", Get(a, "make_lambda")), "<fun lambda>");
        }

        [Test]
        public void ForeignFunctionValue_InAGlobal_RaisesTypeError()
        {
            var (a, _) = Instance("a", OwnerSource);
            var (b, _) = Instance("b", UserSource);
            var lambda = Get(a, "make_lambda");
            AssertForeign(Call(b, "call_global", lambda), "'g'");
            b.ClearFault();
            AssertForeign(Call(b, "tail_call_global", lambda), "'g'");
        }

        [Test]
        public void ForeignNestedClass_RaisesTypeError()
        {
            var (a, _) = Instance("a", OwnerSource);
            var (b, _) = Instance("b", UserSource);
            AssertForeign(Call(b, "nested", Get(a, "make_unit")), "the constructor of nested class 'Part'");
        }

        [Test]
        public void OwnValues_AreCallable()
        {
            var (a, _) = Instance("a", OwnerSource);
            var (b, _) = Instance("b", UserSource);
            var r = Call(b, "own_lambda");
            Assert.IsTrue(r.IsOk, r.Error?.ToString());
            Assert.IsTrue(r.Value.Numeric == 2);

            // A value that went out of its instance and comes back is its own.
            var lambda = Get(a, "make_lambda");
            var (a2, _) = Instance("a2", OwnerSource + UserSource);
            var foreign = Call(a2, "call_local", lambda);
            AssertForeign(foreign, "<fun lambda>");
            a2.ClearFault();
            var mine = Call(a2, "call_local", Get(a2, "make_lambda"));
            Assert.IsTrue(mine.IsOk, mine.Error?.ToString());
            Assert.IsTrue(mine.Value.Numeric == 42);
            var heal = Call(a2, "call_heal", Get(a2, "make_unit"));
            Assert.IsTrue(heal.IsOk, heal.Error?.ToString());
            Assert.IsTrue(heal.Value.Numeric == 15);
        }

        [Test]
        public void LoadedBytecode_IsOwnedByTheLoadingInstance()
        {
            var (a, _) = Instance("a", OwnerSource + UserSource);
            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                a.SaveBytecode(ms);
                bytes = ms.ToArray();
            }
            var b = new WarScriptLanguage("b", "", null, (s, m) => { });
            WarScriptLibraryRegistry.RegisterAll(b, b.GlobalDefinitionScope);
            b.LogUnhandledErrors = false;
            using (var ms = new MemoryStream(bytes))
                b.LoadBytecode(ms);
            Assert.IsTrue(b.Run().IsOk);

            var mine = Call(b, "call_local", Get(b, "make_lambda"));
            Assert.IsTrue(mine.IsOk, mine.Error?.ToString());
            Assert.IsTrue(mine.Value.Numeric == 42);
            var heal = Call(b, "call_heal", Get(b, "make_unit"));
            Assert.IsTrue(heal.IsOk, heal.Error?.ToString());

            // b's values are b's, not a's (the file holds no owner).
            AssertForeign(Call(a, "call_local", Get(b, "make_lambda")), "<fun lambda>");
            a.ClearFault();
            AssertForeign(Call(a, "call_heal", Get(b, "make_unit")), "method 'heal'");
        }

        /// <summary>
        /// Program data holds no instance (T3.4, F-VM-3): every lambda
        /// constant, compiled or loaded, is its <see cref="CompiledFunction"/>
        /// alone; the function value is created for the running instance.
        /// </summary>
        [Test]
        public void LambdaConstants_AreProgramData_NotFunctionValues()
        {
            const string source = "fun outer()\n    return fun(x) return fun(y) return y end end\nend\nf = fun(z) return z end\n";
            var (body, definitions) = new WarScriptLanguage("t", source, null, null).CompileWithoutRunning();
            AssertLambdaConstants(body, definitions.GetFunction("outer", 0).Compiled, 3);

            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                BytecodeSerializer.Save(w, body, definitions);
            ms.Position = 0;
            using (var r = new BinaryReader(ms))
            {
                var (loadedBody, loaded) = BytecodeSerializer.Load(r, new WarScriptLanguage("u", "", null, null), null);
                AssertLambdaConstants(loadedBody, loaded.GetFunction("outer", 0).Compiled, 3);
            }
        }

        private static void AssertLambdaConstants(CompiledFunction body, CompiledFunction outer, int expected)
        {
            int found = 0;
            void Walk(CompiledFunction f)
            {
                foreach (var c in f.Chunk.Constants)
                {
                    if (!c.IsNativeObject) continue;
                    Assert.IsInstanceOf<CompiledFunction>(c.Ref, f.Name);
                    found++;
                    Walk((CompiledFunction)c.Ref);
                }
            }
            Walk(body);
            Walk(outer);
            Assert.AreEqual(expected, found);
        }

        [Test]
        public void ALambda_IsOwnedByTheInstanceEvaluatingIt_AndTheSameEachTime()
        {
            var (a, _) = Instance("a", OwnerSource);
            var (b, _) = Instance("b", OwnerSource);
            var fa = Get(a, "make_lambda");
            var fb = Get(b, "make_lambda");
            Assert.AreSame(a, ((FunctionValue)fa.Ref).Owner);
            Assert.AreSame(b, ((FunctionValue)fb.Ref).Owner);
            Assert.AreSame(fa.Ref, Get(a, "make_lambda").Ref, "the same literal is the same value");
        }

        [Test]
        public void FunctionValue_FormatsAsBefore()
        {
            var (a, _) = Instance("a", OwnerSource);
            var lambda = Get(a, "make_lambda");
            Assert.IsInstanceOf<FunctionValue>(lambda.Ref);
            Assert.AreSame(a, ((FunctionValue)lambda.Ref).Owner);
            Assert.AreEqual("<fun lambda>", lambda.ToString());
        }
    }
}
