using Godot.Bridge;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using System.Reflection;
using System.Runtime.Loader;

namespace BetterConsole;

[ModInitializer(nameof(Init))]
public class Entry
{
    public const string ModId = "BetterConsole";

    private static bool _registeredAssemblyResolver;

    public static void Init()
    {
        RegisterAssemblyResolver();

        var harmony = new Harmony("sts2.reme.betterconsole");
        harmony.PatchAll();

        ScriptManagerBridge.LookupScriptsInAssembly(typeof(Entry).Assembly);
    }

    private static void RegisterAssemblyResolver()
    {
        if (_registeredAssemblyResolver)
            return;

        AssemblyLoadContext.Default.Resolving += ResolveModDependency;
        _registeredAssemblyResolver = true;
    }

    private static Assembly? ResolveModDependency(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        if (assemblyName.Name != "TinyPinyin")
            return null;

        var path = Path.Combine(Path.GetDirectoryName(typeof(Entry).Assembly.Location) ?? "", "TinyPinyin.dll");
        if (!File.Exists(path))
            return null;

        return context.LoadFromAssemblyPath(path);
    }
}
