using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using WarScript;

namespace Tests
{
    /// <summary>
    /// Pins serialization and hot-reload behavior that the format/docs promise
    /// but no test previously exercised:
    ///   - lambda constants (NativeObject(CompiledFunction)) survive a
    ///     SaveBytecode/LoadBytecode round-trip (format v1 feature),
    ///   - const and enum definitions survive a round-trip,
    ///   - default-parameter functions keep all registered arities,
    ///   - ConstantNames is cleared by Reload()/LoadBytecode(), so a former
    ///     const name becomes assignable again (documented invariant),
    ///   - reloading a class with a different property layout leaves old
    ///     instances intact with their old properties.
    /// </summary>
    [TestFixture]
    public class SerializationAndReloadEdgeTests
    {
        /// <summary>Save `source`'s bytecode and load it into a fresh instance.</summary>
        private static (WarScriptLanguage script, List<string> output) RoundTrip(string source)
        {
            var (original, _) = TestHelper.Run("roundtrip_src", source);
            var ms = new MemoryStream();
            original.SaveBytecode(ms);
            ms.Position = 0;

            var output = new List<string>();
            var loaded = new WarScriptLanguage("roundtrip_dst", "", null, (s, m) => output.Add(m));
            loaded.LoadBytecode(ms);
            loaded.Run();
            return (loaded, output);
        }

        // ────────────────────────────────────────────────
        //  Lambda constants
        // ────────────────────────────────────────────────

        [Test]
        public void Lambdas_SurviveSerializationRoundTrip()
        {
            var (script, output) = RoundTrip(@"
double = fun [x] return x * 2 end
fun use_lambda [n]
    f = fun [x] return x + 100 end
    return f [n]
end
fun show []
    print use_lambda [5]
    print double [21]
end
");
            var show = script.GetFunction("show", 0);
            Assert.IsNotNull(show, "function using lambdas should survive the round-trip");
            script.Call(show);

            Assert.AreEqual("105", output[0]); // local lambda constant
            Assert.AreEqual("42", output[1]);  // global lambda variable (top-level ran on load)
            Assert.IsFalse(script.ExceptionContext.IsRaised());
        }

        // ────────────────────────────────────────────────
        //  const / enum
        // ────────────────────────────────────────────────

        [Test]
        public void ConstAndEnum_SurviveSerializationRoundTrip()
        {
            var (script, output) = RoundTrip(@"
const MAX = 5
enum Color
    RED
    GREEN = 7
end
fun show []
    print MAX
    print Color :: GREEN
    print Color :: name [7]
    print Color :: count
end
");
            script.Call(script.GetFunction("show", 0));

            Assert.AreEqual("5", output[0]);
            Assert.AreEqual("7", output[1]);
            Assert.AreEqual("GREEN", output[2]);
            Assert.AreEqual("2", output[3]);
        }

        [Test]
        public void LoadBytecode_ClearsConstantNames_SubsequentReloadCanReassign()
        {
            var (original, _) = TestHelper.Run("const_src", "const MAX = 5\nfun get [] return MAX end\n");
            var ms = new MemoryStream();
            original.SaveBytecode(ms);
            ms.Position = 0;

            var output = new List<string>();
            var loaded = new WarScriptLanguage("const_dst", "", null, (s, m) => output.Add(m));
            loaded.LoadBytecode(ms);
            loaded.Run();

            // ConstantNames was cleared by LoadBytecode, so a reloaded script
            // may freely assign the name that used to be a const.
            loaded.Reload("MAX = 99\nprint \"reassigned \" + MAX\n");
            loaded.Run();

            Assert.AreEqual("reassigned 99", output[0]);
        }

        // ────────────────────────────────────────────────
        //  Default-parameter multi-arity
        // ────────────────────────────────────────────────

        [Test]
        public void DefaultParameterArities_SurviveSerializationRoundTrip()
        {
            var (script, output) = RoundTrip(@"
fun greet [name, greeting = ""Hello""]
    return greeting + "", "" + name
end
fun show []
    print greet [""Bob""]
    print greet [""Ann"", ""Yo""]
end
");
            Assert.IsTrue(script.HasFunction("greet", 1), "1-arg arity should survive");
            Assert.IsTrue(script.HasFunction("greet", 2), "2-arg arity should survive");

            script.Call(script.GetFunction("show", 0));
            Assert.AreEqual("Hello, Bob", output[0]);
            Assert.AreEqual("Yo, Ann", output[1]);
        }

        // ────────────────────────────────────────────────
        //  Reload
        // ────────────────────────────────────────────────

        [Test]
        public void Reload_ClearsConstantNames_FormerConstBecomesAssignable()
        {
            var (script, output) = TestHelper.Run("reload_const", "const MAX = 5\nprint MAX\n");
            Assert.AreEqual("5", output[0]);

            script.Reload("MAX = 10\nprint \"reloaded \" + MAX\n");
            script.Run();

            Assert.AreEqual("reloaded 10", output[1]);
        }

        [Test]
        public void Reload_ChangedClassLayout_OldInstancesKeepOldProperties()
        {
            var (script, output) = TestHelper.Run("reload_layout", @"
class P [x, y]
end
g = new P [1, 2]
");
            // Redefine P with a completely different property layout. The old
            // instance in `g` was built against the old layout and keeps it.
            script.Reload(@"
class P [a, b, c]
    fun sum [] return this :: a + this :: b + this :: c end
end
fun probe []
    print g :: x
end
");
            script.Call(script.GetFunction("probe", 0));

            Assert.IsFalse(script.ExceptionContext.IsRaised());
            Assert.AreEqual("1", output[0]);
        }
    }
}
