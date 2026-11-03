using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PepperDash.Essentials.Plugins.Extron.Mgp.Tests;

public static class AssemblyFixture
{
    private static readonly Lazy<MetadataLoadContext> LazyContext = new(CreateContext);
    private static readonly Lazy<Assembly> LazyAssembly = new(LoadPluginAssembly);

    private static string Configuration
    {
        get
        {
            // Derive from test output path: tests/bin/{Configuration}/net10.0/
            var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var parts = baseDir.Split(Path.DirectorySeparatorChar);
            return parts[^2]; // TFM folder is last, Configuration is second-to-last
        }
    }

    private static string PluginDllPath =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            // OutputPath in the plugin csproj is the flat "bin\$(Configuration)\" (no "4Series"
            // segment), and TargetFramework is net8 - the output folder mirrors it verbatim.
            "src", "bin", Configuration, "net8",
            "epi-extron-mgp.4Series.dll"));

    private static string PluginOutputDir => Path.GetDirectoryName(PluginDllPath)!;

    public static MetadataLoadContext Context => LazyContext.Value;
    public static Assembly PluginAssembly => LazyAssembly.Value;

    private static MetadataLoadContext CreateContext()
    {
        var dllByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Fail clearly if the plugin hasn't been built yet, rather than letting
        // Directory.GetFiles throw a less actionable DirectoryNotFoundException below.
        if (!File.Exists(PluginDllPath))
            throw new FileNotFoundException(
                $"Plugin DLL not found at '{PluginDllPath}'. Build the plugin first.", PluginDllPath);

        // Priority 1: Plugin output dir (correct versions win)
        foreach (var dll in Directory.GetFiles(PluginOutputDir, "*.dll"))
            dllByName[Path.GetFileName(dll)] = dll;

        var nugetDir = ResolveNugetPackagesDir();

        // Priority 2: project.assets.json resolution for the plugin's actual NuGet dependency
        // graph (Crestron SDK, PepperDash.*, Serilog, SSH.NET, ...). NOT deps.json - plugin
        // csprojs put <ExcludeAssets>runtime</ExcludeAssets> on the PepperDashEssentials
        // reference (Essentials itself provides those DLLs on the processor at runtime), which
        // makes the plugin's own deps.json omit dependencies entirely. project.assets.json (in
        // src/obj/) has the full pre-exclusion dependency graph and is present after any
        // restore/build.
        var assetsJsonPath = Path.Combine(SourceDirectory, "obj", "project.assets.json");
        if (!File.Exists(assetsJsonPath))
            throw new FileNotFoundException(
                $"'{assetsJsonPath}' not found. Restore/build the plugin first.", assetsJsonPath);

        // Parsed once and shared with both resolvers below - they're consumed synchronously
        // within this using scope, so the underlying buffer stays valid for both.
        using var assetsDoc = JsonDocument.Parse(File.ReadAllBytes(assetsJsonPath));
        var assetsRoot = assetsDoc.RootElement;

        foreach (var path in ResolveProjectAssetsCompileAssemblies(assetsRoot, nugetDir))
            dllByName.TryAdd(Path.GetFileName(path), path);

        // Priority 3 (lowest): the host .NET runtime directory (System.Private.CoreLib,
        // System.Runtime, plus the mscorlib/netstandard forwarding facades). The plugin targets
        // net8 and xUnit runs on a newer runtime (net10.0 here); a net8 assembly's references to
        // System.Runtime 8.0.0.0 etc. resolve against the host's same-named, higher-versioned
        // assemblies, which carry the same type identities - unlike the old net472 build, no
        // separate reference-assembly pack is needed. Lowest priority so the plugin's own NuGet
        // closure always wins on a name collision.
        foreach (var dll in Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
            dllByName.TryAdd(Path.GetFileName(dll), dll);

        return new MetadataLoadContext(new PathAssemblyResolver(dllByName.Values));
    }

    private static string ResolveNugetPackagesDir()
    {
        // Honor NUGET_PACKAGES (common in CI / enterprise setups); fall back to the default.
        var nugetDir = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(nugetDir))
            return nugetDir;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages");
    }

    private static IEnumerable<string> ResolveProjectAssetsCompileAssemblies(JsonElement assetsRoot, string nugetDir)
    {
        if (!assetsRoot.TryGetProperty("targets", out var targets))
            yield break;

        // Target keys are "<tfm>" or "<tfm>/<rid>". A RID-specific restore emits BOTH, and the
        // RID target's libraries carry only runtime assets - picking it by enumeration order
        // would drop compile-time references. Prefer the RID-less key, fall back to the first,
        // and never hard-code the TFM (net8 here, but this shouldn't assume that either).
        var allTargets = targets.EnumerateObject().ToList();
        var target = allTargets.FirstOrDefault(t => !t.Name.Contains('/'));
        if (target.Value.ValueKind != JsonValueKind.Object)
            target = allTargets.FirstOrDefault();
        if (target.Value.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var lib in target.Value.EnumerateObject())
        {
            // Library key is "PackageId/Version"
            var slash = lib.Name.LastIndexOf('/');
            if (slash < 0) continue;
            var packageId = lib.Name[..slash].ToLowerInvariant();
            var version = lib.Name[(slash + 1)..];

            if (!lib.Value.TryGetProperty("compile", out var assets) &&
                !lib.Value.TryGetProperty("runtime", out assets))
                continue;

            foreach (var asset in assets.EnumerateObject())
            {
                // "_._" is NuGet's placeholder for "no asset of this kind" - skip it.
                if (!asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    continue;

                var dllPath = Path.Combine(nugetDir, packageId, version,
                    asset.Name.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(dllPath))
                    yield return dllPath;
            }
        }
    }

    private static Assembly LoadPluginAssembly()
    {
        if (!File.Exists(PluginDllPath))
            throw new FileNotFoundException(
                $"Plugin DLL not found at '{PluginDllPath}'. Build the plugin first.", PluginDllPath);
        return Context.LoadFromAssemblyPath(PluginDllPath);
    }

    // Walks the FULL base-type chain - handles factories that derive through an intermediate
    // base, not just direct subclasses. A direct-BaseType-only check would silently miss those.
    public static List<Type> FindFactoryTypes(string baseTypePrefix = "EssentialsPluginDeviceFactory")
    {
        return PluginAssembly.GetTypes()
            .Where(t => !t.IsAbstract && InheritsFromFactory(t, baseTypePrefix))
            .ToList();
    }

    private static bool InheritsFromFactory(Type type, string prefix)
    {
        for (var b = type.BaseType; b != null; b = b.BaseType)
            if (b.IsGenericType && b.GetGenericTypeDefinition().Name.StartsWith(prefix))
                return true;
        return false;
    }

    public static string SourceDirectory =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "src"));

    // Read every source file once, then search in memory - tests call this per factory/config, so
    // re-enumerating/re-reading src/*.cs on every call is avoidable IO.
    private static readonly Lazy<string[]> AllSourceContents = new(() =>
        Directory.GetFiles(SourceDirectory, "*.cs", SearchOption.AllDirectories)
            // Skip build output (src/bin, src/obj) so we don't read generated .cs or match generated code.
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText)
            .ToArray());

    public static string? FindSourceForClass(string className) =>
        AllSourceContents.Value.FirstOrDefault(content => DeclaresClass(content, className));

    // Match "class <name>" only when <name> ends on a non-identifier boundary, so a prefix
    // like "Foo" does not match "class FooBar". Avoids a regex (keeps the helper allocation-light).
    private static bool DeclaresClass(string content, string className)
    {
        var needle = "class " + className;
        for (var i = content.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = content.IndexOf(needle, i + 1, StringComparison.Ordinal))
        {
            var after = i + needle.Length;
            var next = after < content.Length ? content[after] : ' ';
            if (!char.IsLetterOrDigit(next) && next != '_')
                return true;
        }
        return false;
    }
}
