using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace WindowsInitializer.Core;

public sealed record InstalledExtension(
    string PackageId,
    string Version,
    string Publisher,
    string InstallPath,
    string ManifestSha256,
    DateTimeOffset InstalledAt,
    ImmutableArray<ExtensionFileEntry> Files,
    ImmutableArray<ExtensionTaskEntry> Tasks);

public sealed class ControlledExtensionInstaller
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ControlledExtensionPackageValidator _validator;

    public ControlledExtensionInstaller(ControlledExtensionPackageValidator? validator = null) =>
        _validator = validator ?? new ControlledExtensionPackageValidator();

    public async Task<InstalledExtension> InstallAsync(
        string packagePath,
        string extensionsRoot,
        IEnumerable<string> protectedRoots,
        int currentBuild,
        string architecture = "x64",
        CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(extensionsRoot)) throw new ArgumentException("Extensions root must be absolute.", nameof(extensionsRoot));
        var root = Path.GetFullPath(extensionsRoot);
        Directory.CreateDirectory(root);
        var staging = Path.Combine(root, ".staging-" + Guid.NewGuid().ToString("N"));
        try
        {
            var validation = await _validator.ValidateAsync(packagePath, staging, protectedRoots, currentBuild, architecture, cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid || validation.Manifest is null)
                throw new ExtensionInstallException(validation.Code, validation.Message);

            var manifest = validation.Manifest;
            var finalPath = Path.Combine(root, manifest.PackageId + "-" + manifest.Version);
            EnsureWithinRoot(finalPath, root);
            await ExtractAsync(packagePath, staging, cancellationToken).ConfigureAwait(false);
            var manifestBytes = await File.ReadAllBytesAsync(Path.Combine(staging, "manifest.json"), cancellationToken).ConfigureAwait(false);
            var installed = new InstalledExtension(manifest.PackageId, manifest.Version, manifest.Publisher, finalPath,
                Convert.ToHexString(SHA256.HashData(manifestBytes)), DateTimeOffset.UtcNow,
                manifest.Files.ToImmutableArray(), (manifest.Tasks ?? []).ToImmutableArray());
            if (Directory.Exists(finalPath))
            {
                var existing = await ReadInstallationAsync(finalPath, cancellationToken).ConfigureAwait(false);
                if (existing is not null && existing.ManifestSha256.Equals(installed.ManifestSha256, StringComparison.OrdinalIgnoreCase)) return existing;
                throw new ExtensionInstallException("ExtensionAlreadyInstalled", "A different extension is already installed at the target path.");
            }
            await WriteRegistrationAsync(Path.Combine(staging, ".installation.json"), installed, cancellationToken).ConfigureAwait(false);
            Directory.Move(staging, finalPath);
            staging = string.Empty;
            return installed;
        }
        finally
        {
            if (!string.IsNullOrEmpty(staging) && Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    public async Task<IReadOnlyList<InstalledExtension>> ListAsync(
        string extensionsRoot,
        int currentBuild = int.MaxValue,
        string architecture = "x64",
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(extensionsRoot);
        if (!Directory.Exists(root)) return [];
        var result = new List<InstalledExtension>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var installation = await ReadInstallationAsync(directory, cancellationToken).ConfigureAwait(false);
            if (installation is null) continue;
            var validation = await _validator.ValidateInstalledAsync(directory, currentBuild, architecture, cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid || validation.Manifest is null) continue;
            var manifest = validation.Manifest;
            if (!manifest.PackageId.Equals(installation.PackageId, StringComparison.OrdinalIgnoreCase) ||
                !manifest.Version.Equals(installation.Version, StringComparison.OrdinalIgnoreCase) ||
                !manifest.Publisher.Equals(installation.Publisher, StringComparison.Ordinal)) continue;
            result.Add(installation with
            {
                Files = manifest.Files.ToImmutableArray(),
                Tasks = (manifest.Tasks ?? []).ToImmutableArray()
            });
        }
        return result.OrderByDescending(item => item.InstalledAt).ToArray();
    }

    private static async Task ExtractAsync(string packagePath, string destination, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(packagePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var relative = entry.FullName.Replace('\\', '/');
            var target = Path.GetFullPath(Path.Combine(destination, relative));
            EnsureWithinRoot(target, destination);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<InstalledExtension?> ReadInstallationAsync(string directory, CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, ".installation.json");
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = File.OpenRead(path);
            var installed = await JsonSerializer.DeserializeAsync<InstalledExtension>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (installed is null || !Path.GetFullPath(installed.InstallPath).Equals(Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase)) return null;
            var manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath)) return null;
            await using var manifestStream = File.OpenRead(manifestPath);
            var manifestHash = Convert.ToHexString(await SHA256.HashDataAsync(manifestStream, cancellationToken).ConfigureAwait(false));
            if (!manifestHash.Equals(installed.ManifestSha256, StringComparison.OrdinalIgnoreCase)) return null;
            foreach (var file in installed.Files)
            {
                var target = Path.GetFullPath(Path.Combine(directory, file.Path));
                try { EnsureWithinRoot(target, directory); }
                catch (ExtensionInstallException) { return null; }
                if (!File.Exists(target)) return null;
                await using var fileStream = File.OpenRead(target);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(fileStream, cancellationToken).ConfigureAwait(false));
                if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) return null;
            }
            return installed;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    private static async Task WriteRegistrationAsync(string path, InstalledExtension installed, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(installed, JsonOptions);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static void EnsureWithinRoot(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new ExtensionInstallException("ExtensionPathUnsafe", "Extension path escapes the isolated install root.");
    }
}

public sealed class ExtensionInstallException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
