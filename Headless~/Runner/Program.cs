using System;
using System.Globalization;
using System.IO;
using Tests.Golden;

namespace WarScript.Runner
{
    /// <summary>
    /// Runs one golden script file once and prints the result as one JSON
    /// object (plan 9.5). The golden runner starts it for '# isolate' files,
    /// so a script that kills the process kills only this child.
    ///
    ///   WarScript.Runner &lt;file.ws|file.wsbc&gt; [--mode source|bytecode] [--budget n]
    ///                    [--memory-budget n] [--culture name|invariant]
    ///
    /// A <c>.wsbc</c> file is bytecode, loaded as it is (the fuzzer's bytecode
    /// mode, plan 9.6; --mode does not apply): a file the loader rejects with
    /// InvalidDataException reports status "rejected".
    /// Budgets default to the file's '# budget:' / '# memory-budget:' directives
    /// (a .wsbc file has none: the GoldenFile defaults).
    /// Exit codes: 0 the script completed (with any script status), 2 an
    /// invariant was violated, 3 a C# exception escaped, 1 bad arguments.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string path = null;
            var mode = GoldenMode.Source;
            var culture = "";
            int? budget = null;
            long? memoryBudget = null;

            try
            {
                for (var i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--mode":
                            mode = (GoldenMode)Enum.Parse(typeof(GoldenMode), Value(args, ref i), ignoreCase: true);
                            break;
                        case "--budget":
                            budget = int.Parse(Value(args, ref i), CultureInfo.InvariantCulture);
                            break;
                        case "--memory-budget":
                            memoryBudget = long.Parse(Value(args, ref i), CultureInfo.InvariantCulture);
                            break;
                        case "--culture":
                            culture = Value(args, ref i);
                            if (culture == "invariant") culture = "";
                            break;
                        default:
                            if (path != null || args[i].StartsWith("--", StringComparison.Ordinal))
                                throw new ArgumentException("unexpected argument " + args[i]);
                            path = args[i];
                            break;
                    }
                }
                if (path == null)
                    throw new ArgumentException("no script file given");
            }
            catch (System.Exception e) when (e is ArgumentException || e is FormatException || e is OverflowException)
            {
                Console.Error.WriteLine(e.Message);
                Console.Error.WriteLine("usage: WarScript.Runner <file.ws> [--mode source|bytecode] [--budget n] " +
                                        "[--memory-budget n] [--culture name|invariant]");
                return 1;
            }

            var full = Path.GetFullPath(path);
            var file = full.EndsWith(".wsbc", StringComparison.OrdinalIgnoreCase)
                ? GoldenFile.LoadBytecode(full)
                : GoldenFile.Load(Path.GetDirectoryName(full), Path.GetFileName(full));
            if (budget.HasValue) file.Budget = budget.Value;
            if (memoryBudget.HasValue) file.MemoryBudget = memoryBudget.Value;

            var run = GoldenRun.Execute(file, mode, culture);

            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.Out.WriteLine(IsolatedRun.ToJson(run));
            Console.Out.Flush();

            if (run.EscapedException != null) return IsolatedRun.ExitHostFault;
            if (run.InvariantViolation != null) return IsolatedRun.ExitInvariantViolated;
            return IsolatedRun.ExitCompleted;
        }

        private static string Value(string[] args, ref int i)
        {
            if (i + 1 >= args.Length)
                throw new ArgumentException(args[i] + " needs a value");
            return args[++i];
        }
    }
}
