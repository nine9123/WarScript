using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("WarScript.Tests")]
// Headless~/Runner: compiles the golden runner's NUnit-free test sources (plan 9.5).
[assembly: InternalsVisibleTo("WarScript.Runner")]
// Headless~/Fuzz: compiles the same sources to start the runner and read its JSON (plan 9.6).
[assembly: InternalsVisibleTo("WarScript.Fuzz")]
// Tools~/WarScript.Migrate: parses with the v1 frontend for the migration (T3.2, plan 8.2).
[assembly: InternalsVisibleTo("WarScript.Migrate")]
