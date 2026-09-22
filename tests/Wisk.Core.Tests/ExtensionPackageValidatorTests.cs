using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wisk.Core;
using Xunit;

namespace Wisk.Core.Tests;

public sealed class ExtensionPackageValidatorTests
{
    [Fact]
    public async Task ValidSignedPackagePassesAllChecks()
    {
        var root = CreateTempDirectory();
        var payload = Encoding.UTF8.GetBytes("safe payload");
        var package = CreatePackage(root, "payload.txt", payload, executable: false);
        var validator = new ControlledExtensionPackageValidator(new FakeVerifier(), ["Contoso"]);

        var result = await validator.ValidateAsync(package, Path.Combine(root, "install"), [Path.Combine(root, "state")], 26100);

        Assert.True(result.IsValid, result.Message);
        Assert.Equal("Contoso", result.Manifest!.Publisher);
    }

    [Fact]
    public async Task PinnedRsaSignatureIsVerifiedOverExactManifestBytes()
    {
        var root = CreateTempDirectory();
        var payload = Encoding.UTF8.GetBytes("safe payload");
        var manifest = new ExtensionManifest("demo", "1.0.0", "Contoso", "x64", 26100, 0, "1.0",
            [new ExtensionFileEntry("payload.txt", Convert.ToHexString(SHA256.HashData(payload)))], []);
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var rsa = RSA.Create(2048);
        var signature = Convert.ToBase64String(rsa.SignData(manifestBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var package = CreatePackage(root, "payload.txt", payload, executable: false, manifestBytes, signature);
        var verifier = new PinnedRsaExtensionSignatureVerifier(new Dictionary<string, string> { ["Contoso"] = rsa.ExportSubjectPublicKeyInfoPem() });

        var result = await new ControlledExtensionPackageValidator(verifier, ["Contoso"])
            .ValidateAsync(package, Path.Combine(root, "install"), [], 26100);

        Assert.True(result.IsValid, result.Message);
    }

    [Fact]
    public async Task PinnedRsaRejectsAssetSignatureForDifferentBytes()
    {
        var root = CreateTempDirectory();
        var payload = new byte[] { 1, 2, 3 };
        using var rsa = RSA.Create(2048);
        var wrongAssetSignature = Convert.ToBase64String(rsa.SignData([9, 9, 9], HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var manifest = new ExtensionManifest("demo", "1.0.0", "Contoso", "x64", 26100, 0, "1.0",
            [new ExtensionFileEntry("tool.exe", Convert.ToHexString(SHA256.HashData(payload)), true, wrongAssetSignature)], []);
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var manifestSignature = Convert.ToBase64String(rsa.SignData(manifestBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var package = CreatePackage(root, "tool.exe", payload, executable: true, manifestBytes, manifestSignature);
        var verifier = new PinnedRsaExtensionSignatureVerifier(new Dictionary<string, string> { ["Contoso"] = rsa.ExportSubjectPublicKeyInfoPem() });

        var result = await new ControlledExtensionPackageValidator(verifier, ["Contoso"])
            .ValidateAsync(package, Path.Combine(root, "install"), [], 26100);

        Assert.False(result.IsValid);
        Assert.Equal("ExtensionAssetRejected", result.Code);
    }

    [Fact]
    public async Task ZipSlipIsRejectedBeforeManifestTrust()
    {
        var root = CreateTempDirectory();
        var package = CreatePackage(root, "../escape.txt", Encoding.UTF8.GetBytes("escape"), executable: false);
        var validator = new ControlledExtensionPackageValidator(new FakeVerifier(), ["Contoso"]);

        var result = await validator.ValidateAsync(package, Path.Combine(root, "install"), [], 26100);

        Assert.False(result.IsValid);
        Assert.Equal("ExtensionPathUnsafe", result.Code);
    }

    [Fact]
    public async Task UnsignedExecutableIsRejected()
    {
        var root = CreateTempDirectory();
        var package = CreatePackage(root, "tool.exe", [1, 2, 3], executable: true);
        var validator = new ControlledExtensionPackageValidator(new FakeVerifier(allowAssets: false), ["Contoso"]);

        var result = await validator.ValidateAsync(package, Path.Combine(root, "install"), [], 26100);

        Assert.False(result.IsValid);
        Assert.Equal("ExtensionAssetRejected", result.Code);
    }

    [Fact]
    public async Task SignedExecutableAssetPassesAssetSignatureBoundary()
    {
        var root = CreateTempDirectory();
        var package = CreatePackage(root, "tool.exe", [1, 2, 3], executable: true,
            assetSignature: Convert.ToBase64String([1, 2, 3]));

        var result = await new ControlledExtensionPackageValidator(new FakeVerifier(), ["Contoso"])
            .ValidateAsync(package, Path.Combine(root, "install"), [], 26100);

        Assert.True(result.IsValid, result.Message);
    }

    [Fact]
    public async Task AmbiguousManifestPropertiesAreRejected()
    {
        var root = CreateTempDirectory();
        var package = CreatePackage(root, "payload.txt", Encoding.UTF8.GetBytes("safe payload"), executable: false);
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("manifest.json")!;
            string json;
            using (var reader = new StreamReader(entry.Open()))
                json = reader.ReadToEnd().Replace("\"packageId\":\"demo\"", "\"packageId\":\"demo\",\"PackageId\":\"changed\"", StringComparison.Ordinal);
            entry.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open());
            writer.Write(json);
        }
        var validator = new ControlledExtensionPackageValidator(new FakeVerifier(), ["Contoso"]);

        var result = await validator.ValidateAsync(package, Path.Combine(root, "install"), [], 26100);

        Assert.False(result.IsValid);
        Assert.Equal("ManifestInvalid", result.Code);
    }

    [Fact]
    public async Task UnlistedPackageFileIsRejected()
    {
        var root = CreateTempDirectory();
        var package = CreatePackage(root, "payload.txt", Encoding.UTF8.GetBytes("safe payload"), executable: false);
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Update))
        {
            using var writer = new StreamWriter(archive.CreateEntry("unlisted.txt").Open());
            writer.Write("not covered by the manifest");
        }

        var result = await new ControlledExtensionPackageValidator(new FakeVerifier(), ["Contoso"])
            .ValidateAsync(package, Path.Combine(root, "install"), [], 26100);

        Assert.False(result.IsValid);
        Assert.Equal("ExtensionHashMismatch", result.Code);
    }

    [Fact]
    public async Task UnknownAssetTypeIsRejected()
    {
        var root = CreateTempDirectory();
        var package = CreatePackage(root, "payload.unknown", Encoding.UTF8.GetBytes("payload"), executable: false);

        var result = await new ControlledExtensionPackageValidator(new FakeVerifier(), ["Contoso"])
            .ValidateAsync(package, Path.Combine(root, "install"), [], 26100);

        Assert.False(result.IsValid);
        Assert.Equal("ExtensionAssetRejected", result.Code);
    }

    [Fact]
    public async Task UnknownPermissionIsRejected()
    {
        var root = CreateTempDirectory();
        var payload = Encoding.UTF8.GetBytes("safe payload");
        var manifest = new ExtensionManifest("demo", "1.0.0", "Contoso", "x64", 26100, 0, "1.0",
            [new ExtensionFileEntry("payload.txt", Convert.ToHexString(SHA256.HashData(payload)))], ["arbitrary.capability"]);
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var package = CreatePackage(root, "payload.txt", payload, executable: false, manifestBytes);

        var result = await new ControlledExtensionPackageValidator(new FakeVerifier(), ["Contoso"])
            .ValidateAsync(package, Path.Combine(root, "install"), [], 26100);

        Assert.False(result.IsValid);
        Assert.Equal("ManifestInvalid", result.Code);
    }

    [Fact]
    public async Task UnsafeVersionIsRejectedBeforeInstallation()
    {
        var root = CreateTempDirectory();
        var package = CreatePackage(root, "payload.txt", Encoding.UTF8.GetBytes("safe"), executable: false);
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("manifest.json")!;
            string json;
            using (var reader = new StreamReader(entry.Open())) json = reader.ReadToEnd();
            entry.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open());
            writer.Write(json.Replace("\"version\":\"1.0.0\"", "\"version\":\"../escape\"", StringComparison.Ordinal));
        }

        var result = await new ControlledExtensionPackageValidator(new FakeVerifier(), ["Contoso"])
            .ValidateAsync(package, Path.Combine(root, "install"), [], 26100);

        Assert.False(result.IsValid);
        Assert.Equal("ManifestInvalid", result.Code);
    }

    [Fact]
    public async Task TaskEntryPointMustBeADeclaredExecutable()
    {
        var root = CreateTempDirectory();
        var package = CreatePackage(root, "worker.exe", [1, 2, 3], executable: false,
            tasks: [new ExtensionTaskEntry("runtime-ms-bundle", "worker.exe")]);

        var result = await new ControlledExtensionPackageValidator(new FakeVerifier(), ["Contoso"])
            .ValidateAsync(package, Path.Combine(root, "install"), [], 26100);

        Assert.False(result.IsValid);
        Assert.Equal("ExtensionAssetRejected", result.Code);
    }

    private static string CreatePackage(string root, string path, byte[] payload, bool executable, byte[]? manifestBytes = null,
        string signature = "signed", IReadOnlyList<ExtensionTaskEntry>? tasks = null, string? assetSignature = null)
    {
        var packagePath = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry(path.Replace('\\', '/'));
            using var stream = entry.Open();
            stream.Write(payload);
        }
        var permissions = tasks is { Count: > 0 } ? new[] { "process.execute" } : [];
        var manifest = new ExtensionManifest("demo", "1.0.0", "Contoso", "x64", 26100, 0, "1.0",
            [new ExtensionFileEntry(path, Convert.ToHexString(SHA256.HashData(payload)), executable, assetSignature)], permissions, tasks);
        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Update))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
                writer.Write(Encoding.UTF8.GetString(manifestBytes ?? JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            using var signatureWriter = new StreamWriter(archive.CreateEntry("manifest.sig").Open());
            signatureWriter.Write(signature);
        }
        return packagePath;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "WISK wisk-extension-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeVerifier(bool allowAssets = true) : IExtensionSignatureVerifier
    {
        public Task<bool> VerifyAsync(ReadOnlyMemory<byte> manifestBytes, string signature, string publisher, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> VerifyAssetAsync(string assetPath, ReadOnlyMemory<byte> assetBytes, string publisher, string signature, CancellationToken cancellationToken) => Task.FromResult(allowAssets);
    }
}
