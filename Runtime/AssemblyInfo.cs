using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("WarScript.Tests")]
// Headless~/Runner: compiles the golden runner's NUnit-free test sources (plan 9.5).
[assembly: InternalsVisibleTo("WarScript.Runner")]
// Headless~/Fuzz: compiles the same sources to start the runner and read its JSON (plan 9.6).
[assembly: InternalsVisibleTo("WarScript.Fuzz")]
