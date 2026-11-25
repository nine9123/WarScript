using System.Collections.Generic;
using NUnit.Framework;
using WarScript;
using WarScript.Context.Definition;
using WarScript.Expression.Value;
using WarScript.Statement;

namespace Tests
{
    /// <summary>
    /// A function (not a method) that shadows a native is a compile error
    /// (v2 plan T3.8, PROP-12, DEC-21), and the class of an <c>is</c> or
    /// <c>as</c> must exist (T3.8). The golden cases are
    /// <c>language/function_shadows_native*.ws</c>,
    /// <c>function_other_arity_than_native.ws</c> and
    /// <c>language/is_*.ws</c>, <c>as_unknown_class.ws</c>.
    /// </summary>
    [TestFixture]
    public class NativeShadowingTests
    {
        private static (WarScriptLanguage script, List<string> output) Script(
            string source, Dictionary<string, string> files = null)
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("main", source,
                files == null ? null : path => files.TryGetValue(path, out var text) ? text : null,
                (s, msg) => output.Add(msg));
            script.LogUnhandledErrors = false;
            script.GlobalDefinitionScope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("twice", new List<string> { "a" }),
                args => WarValue.FromNumeric(args[0].Numeric * 2), "", ""));
            return (script, output);
        }

        [Test]
        public void AFunctionShadowingANative_IsACompileError_AtItsName_AndInstallsNothing()
        {
            var (script, output) = Script(
                "fun helper()\n" +
                "end\n" +
                "fun twice(a)\n" +
                "    return a\n" +
                "end\n");

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            var error = result.Diagnostics[0];
            Assert.AreEqual("Function 'twice' with 1 argument shadows a native function", error.Message);
            Assert.AreEqual(3, error.Line);
            Assert.AreEqual(5, error.Column, "at the function name");
            Assert.IsNull(script.GetFunction("helper", 0));
            Assert.IsInstanceOf<NativeFunctionDefinition>(script.GetFunction("twice", 1).Definition);
            CollectionAssert.IsEmpty(output, "nothing logged");
        }

        [Test]
        public void AFunctionInsideAFunction_ShadowingANative_IsACompileError()
        {
            var (script, _) = Script(
                "fun outer()\n" +
                "    fun twice(a)\n" +
                "        return a\n" +
                "    end\n" +
                "end\n");

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            Assert.AreEqual("Function 'twice' with 1 argument shadows a native function", result.Diagnostics[0].Message);
            Assert.AreEqual(2, result.Diagnostics[0].Line);
        }

        [Test]
        public void AMethodNamedLikeANative_Compiles_AndStillLogs()
        {
            var (script, output) = Script(
                "class C()\n" +
                "    fun twice(a)\n" +
                "        return a\n" +
                "    end\n" +
                "    fun call(a)\n" +
                "        return twice(a)\n" +
                "    end\n" +
                "end\n" +
                "c = new C()\n" +
                "print c.call(3)\n" +
                "print twice(3)\n");

            Assert.IsTrue(script.Run().IsOk);

            CollectionAssert.AreEqual(new[] { "Shadowing native function 'twice'", "3", "6" }, output);
        }

        [Test]
        public void AFunctionOfAnotherArity_Compiles()
        {
            var (script, output) = Script(
                "fun twice(a, b)\n" +
                "    return a + b\n" +
                "end\n" +
                "print twice(3)\n" +
                "print twice(3, 4)\n");

            Assert.IsTrue(script.Run().IsOk);

            CollectionAssert.AreEqual(new[] { "6", "7" }, output);
        }

        [Test]
        public void AnImport_MayTestForAClassOfItsImporter()
        {
            var files = new Dictionary<string, string>
            {
                ["lib"] = "fun is_unit(v)\n    return v is Unit\nend\n",
            };
            var (script, output) = Script(
                "import \"lib\"\n" +
                "class Unit\n" +
                "end\n" +
                "print is_unit(new Unit())\n", files);

            Assert.IsTrue(script.Run().IsOk, script.Fault?.ToString());

            CollectionAssert.AreEqual(new[] { "true" }, output);
        }

        [Test]
        public void AnUnknownClassInAnImport_NamesTheImport()
        {
            var files = new Dictionary<string, string>
            {
                ["lib"] = "fun is_unit(v)\n    return v as Unti\nend\n",
            };
            var (script, _) = Script("import \"lib\"\nclass Unit\nend\n", files);

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            Assert.AreEqual("Class 'Unti' is not defined", result.Diagnostics[0].Message);
            Assert.AreEqual("lib", result.Diagnostics[0].ScriptName);
            Assert.AreEqual(2, result.Diagnostics[0].Line);
            Assert.AreEqual(17, result.Diagnostics[0].Column, "at the class name");
        }

        [Test]
        public void AHostClass_MayBeTested()
        {
            var (script, output) = Script("print 5 is Host\n");
            script.GlobalDefinitionScope.AddClass(new ClassDefinition(
                new ClassDetails("Host", new List<string>()), new List<ClassDetails>(),
                new ClassStatement(script, 0, "Host"), new DefinitionScope(script, null)));

            Assert.IsTrue(script.Run().IsOk, script.Fault?.ToString());

            CollectionAssert.AreEqual(new[] { "false" }, output);
        }
    }
}
