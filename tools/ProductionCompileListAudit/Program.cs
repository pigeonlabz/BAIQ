using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

if (args.Length != 3)
    throw new ArgumentException("Usage: ProductionCompileListAudit <project-root> <configuration> <built-dll>");

var root = Path.GetFullPath(args[0]);
var configuration = args[1];
var assemblyPath = Path.GetFullPath(args[2]);
var projectPath = Path.Combine(root, "src", "InventoryExpanded", "InventoryExpanded.csproj");
if (!File.Exists(projectPath)) throw new FileNotFoundException("Project file not found", projectPath);
if (!File.Exists(assemblyPath)) throw new FileNotFoundException("Built production assembly not found", assemblyPath);

var project = XDocument.Load(projectPath);
var compilePaths = project.Descendants().Where(e => e.Name.LocalName == "Compile")
    .Select(e => (string?)e.Attribute("Include"))
    .Where(path => !string.IsNullOrWhiteSpace(path))
    .Select(path => Normalize(path!))
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

var excludedDeferredSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "Patches/PackagingMk2DiagnosticsPatch.cs",
    "Patches/PackagingMk2SafetyPatch.cs"
};

var candidateSources = new List<(string RelativePath, string Namespace, string[] TypeNames)>();
foreach (var folder in new[] { "Cheats", "Patches" })
{
    var directory = Path.Combine(root, "src", "InventoryExpanded", folder);
    foreach (var file in Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly))
    {
        var name = Path.GetFileName(file);
        if (!System.Text.RegularExpressions.Regex.IsMatch(name,
                "(Production|Yield|Capacity|TemporaryCapacityOverrideScopes|CashRoutingPolicy|MultiplayerBackpackPersistence)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            continue;
        var relative = folder + "/" + name;
        if (!excludedDeferredSources.Contains(relative))
        {
            var source = File.ReadAllText(file);
            var typeNames = System.Text.RegularExpressions.Regex.Matches(source,
                    @"(?m)^(?:(?:public|internal|private|protected)[ \t]+)?(?:(?:static|sealed|readonly|abstract|partial)[ \t]+)*(?:class|struct|record(?:[ \t]+(?:struct|class))?)[ \t]+([A-Za-z_]\w*)(?:[ \t]*<([^>{}]+)>)?",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant)
                .Select(match =>
                {
                    var typeName = match.Groups[1].Value;
                    if (!match.Groups[2].Success) return typeName;
                    var genericArity = match.Groups[2].Value.Count(character => character == ',') + 1;
                    return typeName + "`" + genericArity;
                })
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (typeNames.Length == 0)
                throw new InvalidOperationException("No type declaration found in production source: " + relative);
            candidateSources.Add((relative, "Backpack." + folder, typeNames));
        }
    }
}

var missingCompileEntries = candidateSources.Where(source => !compilePaths.Contains(source.RelativePath)).ToArray();
if (missingCompileEntries.Length > 0)
    throw new InvalidOperationException("Active production sources missing from explicit Compile list: "
        + string.Join(", ", missingCompileEntries.Select(s => s.RelativePath)));

foreach (var deferred in excludedDeferredSources)
{
    if (compilePaths.Contains(deferred))
        throw new InvalidOperationException("Deferred packaging adapter is in the public compile list: " + deferred);
}

using var stream = File.OpenRead(assemblyPath);
using var pe = new PEReader(stream);
var reader = pe.GetMetadataReader();
var emittedTypes = reader.TypeDefinitions.Select(handle =>
{
    var type = reader.GetTypeDefinition(handle);
    var ns = reader.GetString(type.Namespace);
    var name = reader.GetString(type.Name);
    return ns.Length == 0 ? name : ns + "." + name;
}).ToHashSet(StringComparer.Ordinal);

var missingTypes = candidateSources.SelectMany(source => source.TypeNames
        .Where(typeName => !emittedTypes.Contains(source.Namespace + "." + typeName))
        .Select(typeName => source.Namespace + "." + typeName))
    .ToArray();
if (missingTypes.Length > 0)
    throw new InvalidOperationException("Active production source types missing from built assembly: "
        + string.Join(", ", missingTypes));

var forbiddenDiagnosticTypes = new[]
{
    "Backpack.Patches.BackpackPersistenceDiagnostics",
    "Backpack.Patches.PlayerRequestSaveDiagnosticsPatch",
    "Backpack.Patches.PlayerManagerSaveDiagnosticsPatch",
    "Backpack.Patches.PlayerWriteDataDiagnosticsPatch",
    "Backpack.Patches.FishNetStorageAuthorizationTrace",
    "Backpack.Patches.FishNetSetStoredInstanceReaderDiagnosticsPatch",
    "Backpack.Patches.FishNetSetItemSlotQuantityReaderDiagnosticsPatch",
    "Backpack.Patches.FishNetStoredInstanceMutationDiagnosticsPatch",
    "Backpack.Patches.FishNetQuantityMutationDiagnosticsPatch"
};
var includedDiagnosticTypes = forbiddenDiagnosticTypes.Where(emittedTypes.Contains).ToArray();
if (includedDiagnosticTypes.Length > 0)
    throw new InvalidOperationException("Development-only diagnostic types are present in the production DLL: "
        + string.Join(", ", includedDiagnosticTypes));

Console.WriteLine($"{configuration}: {candidateSources.Count} active production sources match the explicit compile list and built DLL.");
Console.WriteLine($"{configuration}: Mk1 Packaging throughput policy is compiled; {excludedDeferredSources.Count} Mk2-only diagnostic/safety sources remain excluded.");
Console.WriteLine($"{configuration}: {forbiddenDiagnosticTypes.Length} diagnostic type names are absent from the production DLL.");

static string Normalize(string path) => path.Replace('\\', '/').TrimStart('.','/');
