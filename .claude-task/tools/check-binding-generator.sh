#!/usr/bin/env bash
# Checks Editor/WsBindingGenerator.cs outside Unity: compiles the generator
# with stub UnityEditor/UnityEngine types, generates the bindings of a sample
# [WsModule] (a function, a [WsConst] and a [WsEnum]), compiles the generated
# code against Headless~/Runtime and runs a script that uses them. Prints the
# script's output; exits non-zero if any step fails. Also writes the module's
# native manifest (PROP-30) and compiles the same script against it on an
# instance without the bindings: prints the manifest and "manifest: compiles".
# Then checks that the [WsConst] and [WsEnum] type operands (PROP-28) with the
# bindings and with the manifest alike, and that an enum member is read-only.
#
# The run project is named WarScript.Tests so that it may use the runtime's
# internals: generated [WsConst]/[WsEnum] code calls the internal
# ConstantNames (see the T2.1 review note in Documentation~/WARSCRIPT_V2_PLAN.md).
#
# Usage: .claude-task/tools/check-binding-generator.sh [work dir, default /tmp/wsgen-check]
# Needs Headless~/external/FixPointCS (./Headless~/fetch-fixpointcs.sh).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="${1:-/tmp/wsgen-check}"
rm -rf "$WORK"; mkdir -p "$WORK/gen" "$WORK/run"

cat > "$WORK/Module.cs" <<'EOF'
using FixMath;
using WarScript.Attributes;
namespace Demo
{
    [WsModule("demo")]
    public static partial class DemoModule
    {
        [WsFunction("twice")] public static F64 Twice(F64 a) => a * 2;
        [WsFunction] public static string Label(string text, int width = 3) => text;
        [WsConst] public const int MAX_UNITS = 50;
        [WsEnum] public enum State { Idle, Moving, Attacking = 7 }
    }
}
EOF

cat > "$WORK/gen/gen.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><RollForward>Major</RollForward>
    <LangVersion>9.0</LangVersion><Nullable>disable</Nullable><NoWarn>CS8632</NoWarn>
    <DefineConstants>UNITY_EDITOR</DefineConstants><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
  <ItemGroup><Compile Include="$ROOT/Editor/WsBindingGenerator.cs" /><Compile Include="*.cs" /><Compile Include="../Module.cs" />
    <ProjectReference Include="$ROOT/Headless~/Runtime/WarScript.Runtime.csproj" /></ItemGroup>
</Project>
EOF
cat > "$WORK/gen/Stubs.cs" <<'EOF'
namespace UnityEditor
{
    public class MenuItem : System.Attribute { public MenuItem(string s) { } }
    public static class AssetDatabase
    {
        public static void Refresh() { }
        public static string[] FindAssets(string s) => new string[0];
        public static string GUIDToAssetPath(string s) => s;
    }
}
namespace UnityEngine
{
    public static class Debug { public static void Log(object o) { } public static void LogWarning(object o) { } public static void LogError(object o) { } }
    public static class Application { public static string dataPath => "."; }
}
EOF
cat > "$WORK/gen/Main.cs" <<EOF
using System.Reflection;
class GenMain
{
    static void Main()
    {
        var m = typeof(WarScript.Editor.WsBindingGenerator).GetMethod("GenerateForType", BindingFlags.NonPublic | BindingFlags.Static);
        var t = typeof(Demo.DemoModule);
        var src = (string)m.Invoke(null, new object[] { t, t.GetCustomAttribute<WarScript.Attributes.WsModuleAttribute>() });
        System.IO.File.WriteAllText("$WORK/run/Demo.g.cs", src);
        var entries = new WarScript.Editor.WsBindingGenerator.ManifestEntries();
        typeof(WarScript.Editor.WsBindingGenerator).GetMethod("AddManifestEntries", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { t, entries });
        System.IO.File.WriteAllText("$WORK/run/demo.wsmanifest", entries.Write());
    }
}
EOF

cat > "$WORK/run/run.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><AssemblyName>WarScript.Tests</AssemblyName><TargetFramework>net8.0</TargetFramework>
    <RollForward>Major</RollForward><LangVersion>9.0</LangVersion><Nullable>disable</Nullable><NoWarn>CS8632</NoWarn>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
  <ItemGroup><Compile Include="*.cs" /><Compile Include="../Module.cs" />
    <ProjectReference Include="$ROOT/Headless~/Runtime/WarScript.Runtime.csproj" /></ItemGroup>
</Project>
EOF
cat > "$WORK/run/Main.cs" <<'EOF'
using WarScript;
class RunMain
{
    static void Main()
    {
        var src = string.Join("\n",
            "print twice [21]",
            "print MAX_UNITS",
            "print State :: Moving",
            "print State :: name [7]",
            "print State :: name [State :: Idle]",
            "print State :: name [3]",
            "print State :: names",
            "print State :: count",
            "print label [\"ab\"]",
            "") ;
        var s = new WarScriptLanguage("demo", src, null, (x, m) => System.Console.WriteLine(m));
        Demo.DemoModule.Register(s, s.GlobalDefinitionScope);
        s.Run();

        var text = System.IO.File.ReadAllText(System.IO.Path.Combine(System.AppContext.BaseDirectory, "../../../demo.wsmanifest"));
        System.Console.Write(text);
        var checker = new WarScriptLanguage("demo", src, null, (x, m) => System.Console.WriteLine(m));
        NativeManifest.Parse(text).Register(checker);
        var compiled = checker.Compile();
        System.Console.WriteLine(compiled.Success ? "manifest: compiles" : "manifest: " + compiled.Diagnostics[0]);
        if (!compiled.Success) System.Environment.Exit(1);

        // PROP-28 (T3.8): the host constant and enum are typed, the same with
        // the bindings and with the manifest; enum members are constant.
        var typed = "a = MAX_UNITS - \"x\"\nb = State :: Idle << 1\n";
        foreach (var useManifest in new[] { false, true })
        {
            var t = new WarScriptLanguage("typed", typed, null, (x, m) => { });
            if (useManifest) NativeManifest.Parse(text).Register(t);
            else Demo.DemoModule.Register(t, t.GlobalDefinitionScope);
            var r = t.Compile();
            System.Console.WriteLine((useManifest ? "manifest" : "bindings") + " typed: "
                + string.Join(" | ", System.Linq.Enumerable.Select(r.Diagnostics, d => d.Message)));
            if (r.Success || r.Diagnostics.Count != 2) System.Environment.Exit(1);
        }
        var w = new WarScriptLanguage("write", "s = State\ns :: Idle = 3\n", null, (x, m) => { });
        Demo.DemoModule.Register(w, w.GlobalDefinitionScope);
        w.LogUnhandledErrors = false;
        var wr = w.Run();
        System.Console.WriteLine("enum write: " + wr.Error?.Kind + ": " + wr.Error?.Message);
        if (wr.IsOk) System.Environment.Exit(1);
    }
}
EOF

(cd "$WORK/gen" && dotnet run -v q) >/dev/null
(cd "$WORK/run" && dotnet run -v q)
