#!/usr/bin/env bash
# Checks Editor/WsBindingGenerator.cs outside Unity: compiles the generator
# with stub UnityEditor/UnityEngine types, generates the bindings of a sample
# [WsModule] (a function, a [WsConst] and a [WsEnum]), compiles the generated
# code against Headless~/Runtime and runs a script that uses them. Prints the
# script's output; exits non-zero if any step fails.
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
            "") ;
        var s = new WarScriptLanguage("demo", src, null, (x, m) => System.Console.WriteLine(m));
        Demo.DemoModule.Register(s, s.GlobalDefinitionScope);
        s.Run();
    }
}
EOF

(cd "$WORK/gen" && dotnet run -v q) >/dev/null
(cd "$WORK/run" && dotnet run -v q)
