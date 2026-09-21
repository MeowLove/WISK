using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsInitializer.Core;

public sealed record TrustedExtensionPublisher(string Id, string RsaPublicKeyPem);
public sealed record ExtensionTrustDocument(string SchemaVersion, ImmutableArray<TrustedExtensionPublisher?> Publishers);

public static class ExtensionTrustPolicy
{
    private const string ResourceSuffix = "Resources.trusted-publishers.v2.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static IReadOnlyDictionary<string, string> LoadEmbedded() => LoadFromAssembly(typeof(ExtensionTrustPolicy).Assembly);

    public static ControlledExtensionPackageValidator CreateValidator()
    {
        var publishers = LoadEmbedded();
        return new ControlledExtensionPackageValidator(new PinnedRsaExtensionSignatureVerifier(publishers), publishers.Keys);
    }

    public static IReadOnlyDictionary<string, string> Deserialize(ReadOnlyMemory<byte> json)
    {
        StrictJson.ValidateNoAmbiguousProperties(json);
        var document = JsonSerializer.Deserialize<ExtensionTrustDocument>(json.Span, JsonOptions)
            ?? throw new InvalidOperationException("Embedded extension trust policy is empty.");
        if (document.SchemaVersion != "2.0" || document.Publishers.IsDefault)
            throw new InvalidOperationException("Embedded extension trust policy has an unsupported schema.");
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var publisher in document.Publishers)
        {
            if (publisher is null || string.IsNullOrWhiteSpace(publisher.Id) || publisher.Id.Length > 256 ||
                string.IsNullOrWhiteSpace(publisher.RsaPublicKeyPem) || !result.TryAdd(publisher.Id, publisher.RsaPublicKeyPem))
                throw new InvalidOperationException("Embedded extension trust policy contains an invalid publisher.");
        }
        return result;
    }

    private static IReadOnlyDictionary<string, string> LoadFromAssembly(Assembly assembly)
    {
        var resourceName = assembly.GetManifestResourceNames().SingleOrDefault(name => name.EndsWith(ResourceSuffix, StringComparison.Ordinal));
        if (resourceName is null) throw new InvalidOperationException("Embedded extension trust policy is missing.");
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Embedded extension trust policy cannot be opened.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Deserialize(memory.ToArray());
    }
}

public static class ExtensionPaths
{
    public static string UserRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsInitializer", "extensions");
}
