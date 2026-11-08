using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

// Read metadata only: never load or execute a game/IL2CPP assembly.
if (args.Length != 2) throw new ArgumentException("Usage: ProductionSurfaceAudit <assembly> <output.json>");
using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var reader = pe.GetMetadataReader();
var provider = new Names();
var inventory = reader.TypeDefinitions.Select(h => provider.GetTypeFromDefinition(reader, h, 0))
    .Where(n => n.StartsWith("Il2CppScheduleOne.", StringComparison.Ordinal)).Order().ToArray();
var pattern = new Regex("Mushroom|Shroom|Inocculate|Cauldron|Chemistry|LabOven|OvenCook|BrickPress|SpawnStation|StationRecipe|CookableModule|LiquidContainer|UseOven|PourInto|DryingRack|MixingStation|GrowContainer|GrowingPot|HarvestPlant|HarvestPotBehaviour|PlantHarvestable|WeedPlant|CocaPlant|Botanist|Handler|MoveItemBehaviour|MoveItemData|PlantData|PotConfiguration|NPCInventory|TransitRoute|ITransitEntity|ProductItemInstance|QualityItemInstance|StorableItemDefinition|Pot|Plant$|PotData|Recycler|Launder|VendingMachine|HarvestMushroom|ApplyShroom|InocculateGrainBag|SewerMushroom|StorageEntity|PlaceableStorage|BedItem|EmployeeHome|DeadDrop|SupplierStash|CashSlot|CashInstance|ItemSlot|DeliveryVehicle|PackagingStation|MushroomSpawnStation", RegexOptions.IgnoreCase);
var types = new SortedDictionary<string, object>();
foreach (var handle in reader.TypeDefinitions)
{
    var type = reader.GetTypeDefinition(handle);
    var name = provider.GetTypeFromDefinition(reader, handle, 0);
    if (!name.StartsWith("Il2CppScheduleOne.", StringComparison.Ordinal) || !pattern.IsMatch(name)) continue;
    var methods = type.GetMethods().Select(h => reader.GetMethodDefinition(h)).Select(m =>
    {
        var s = m.DecodeSignature(provider, null);
        return $"{s.ReturnType} {reader.GetString(m.Name)}({string.Join(",", s.ParameterTypes)})";
    }).Where(n => !n.Contains("Pointer") && !n.Contains(".cctor")).Order().ToArray();
    var properties = type.GetProperties().Select(h => reader.GetPropertyDefinition(h)).Select(p =>
        p.DecodeSignature(provider, null).ReturnType + " " + reader.GetString(p.Name)).Order().ToArray();
    var fields = type.GetFields().Select(h => reader.GetFieldDefinition(h)).Where(f => !reader.GetString(f.Name).StartsWith("Native"))
        .Select(f => f.DecodeSignature(provider, null) + " " + reader.GetString(f.Name)).Order().ToArray();
    var interfaces = type.GetInterfaceImplementations()
        .Select(h => provider.Entity(reader, reader.GetInterfaceImplementation(h).Interface))
        .Order().ToArray();
    types[name] = new { BaseType = provider.Entity(reader, type.BaseType), Interfaces = interfaces, Fields = fields, Properties = properties, Methods = methods };
}
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
File.WriteAllText(args[1], JsonSerializer.Serialize(new { Assembly = Path.GetFullPath(args[0]), Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0]))), Inventory = inventory, Types = types }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{inventory.Length} game types; {types.Count} focused types: {args[1]}");

sealed class Names : ISignatureTypeProvider<string, object?>
{
    public string Entity(MetadataReader r, EntityHandle h) => h.Kind switch { HandleKind.TypeDefinition => GetTypeFromDefinition(r, (TypeDefinitionHandle)h, 0), HandleKind.TypeReference => GetTypeFromReference(r, (TypeReferenceHandle)h, 0), HandleKind.TypeSpecification => GetTypeFromSpecification(r, null, (TypeSpecificationHandle)h, 0), _ => "" };
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte kind) { var t = r.GetTypeDefinition(h); return t.GetDeclaringType().IsNil ? Join(r.GetString(t.Namespace), r.GetString(t.Name)) : GetTypeFromDefinition(r, t.GetDeclaringType(), kind) + "+" + r.GetString(t.Name); }
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte kind) { var t = r.GetTypeReference(h); return Join(r.GetString(t.Namespace), r.GetString(t.Name)); }
    public string GetTypeFromSpecification(MetadataReader r, object? c, TypeSpecificationHandle h, byte kind) => r.GetTypeSpecification(h).DecodeSignature(this, c);
    public string GetArrayType(string t, ArrayShape s) => t + "[" + new string(',', s.Rank - 1) + "]";
    public string GetByReferenceType(string t) => t + "&";
    public string GetFunctionPointerType(MethodSignature<string> s) => "fn:" + s.ReturnType;
    public string GetGenericInstantiation(string t, ImmutableArray<string> a) => t + "<" + string.Join(",", a) + ">";
    public string GetGenericMethodParameter(object? c, int i) => "!!" + i;
    public string GetGenericTypeParameter(object? c, int i) => "!" + i;
    public string GetModifiedType(string m, string t, bool required) => t;
    public string GetPinnedType(string t) => t;
    public string GetPointerType(string t) => t + "*";
    public string GetPrimitiveType(PrimitiveTypeCode t) => t.ToString();
    public string GetSZArrayType(string t) => t + "[]";
    static string Join(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;
}
