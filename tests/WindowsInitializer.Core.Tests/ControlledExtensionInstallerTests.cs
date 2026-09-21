using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WindowsInitializer.Core;
using Xunit;

namespace WindowsInitializer.Core.Tests;

public sealed class ControlledExtensionInstallerTests
{
    [Fact]
    public async Task ValidPackageInstallsAtomicallyAndListsRegistration()
    {
        var root = Path.Combine(Path.GetTempPath(), "windows-initializer-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var package = CreatePackage(root, "payload.txt", Encoding.UTF8.GetBytes("safe"));
            var installer = new ControlledExtensionInstaller(new ControlledExtensionPackageValidator(new AllowingVerifier(), ["Contoso"]));

            var installed = await installer.InstallAsync(package, Path.Combine(root, "extensions"), [], 26100);

            Assert.True(File.Exists(Path.Combine(installed.InstallPath, "payload.txt")));
            Assert.Single(await installer.ListAsync(Path.Combine(root, "extensions")));
            await File.WriteAllTextAsync(Path.Combine(installed.InstallPath, "payload.txt"), "tampered");
            Assert.Empty(await installer.ListAsync(Path.Combine(root, "extensions")));
            Assert.DoesNotContain(".staging-", Directory.EnumerateDirectories(Path.Combine(root, "extensions")).Select(Path.GetFileName));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task InvalidPackageLeavesNoInstalledDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "windows-initializer-install-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var package = Path.Combine(root, "bad.zip");
            using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(archive.CreateEntry("../escape.txt").Open());
                writer.Write("bad");
            }
            var installer = new ControlledExtensionInstaller(new ControlledExtensionPackageValidator(new AllowingVerifier(), ["Contoso"]));

            await Assert.ThrowsAsync<ExtensionInstallException>(() => installer.InstallAsync(package, Path.Combine(root, "extensions"), [], 26100));
            Assert.False(Directory.Exists(Path.Combine(root, "extensions", "demo-1.0.0")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreatePackage(string root, string path, byte[] payload)
    {
        var package = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
        var manifest = new ExtensionManifest("demo", "1.0.0", "Contoso", "x64", 26100, 0, "1.0",
            [new ExtensionFileEntry(path, Convert.ToHexString(SHA256.HashData(payload)))], []);
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            using (var payloadStream = archive.CreateEntry(path).Open())
                payloadStream.Write(payload);
            using (var manifestWriter = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
                manifestWriter.Write(JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            using var signatureWriter = new StreamWriter(archive.CreateEntry("manifest.sig").Open());
            signatureWriter.Write("signed");
        }
        return package;
    }

    private sealed class AllowingVerifier : IExtensionSignatureVerifier
    {
        public Task<bool> VerifyAsync(ReadOnlyMemory<byte> manifestBytes, string signature, string publisher, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> VerifyAssetAsync(string assetPath, ReadOnlyMemory<byte> assetBytes, string publisher, string signature, CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
