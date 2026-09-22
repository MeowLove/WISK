using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Wisk.Core;

public sealed record ExtensionFileEntry(string Path, string Sha256, bool Executable = false, string? Signature = null);

public sealed record ExtensionTaskEntry(string TaskId, string ExecutablePath, string Protocol = "stdio-json-v1");

public sealed record ExtensionManifest(
    string PackageId,
    string Version,
    string Publisher,
    string Architecture,
    int MinimumBuild,
    int MaximumBuild,
    string ManifestVersion,
    IReadOnlyList<ExtensionFileEntry> Files,
    IReadOnlyList<string>? Permissions = null,
    IReadOnlyList<ExtensionTaskEntry>? Tasks = null);

public sealed record ExtensionValidationResult(bool IsValid, string Code, string Message, ExtensionManifest? Manifest = null);

public interface IExtensionSignatureVerifier
{
    Task<bool> VerifyAsync(ReadOnlyMemory<byte> manifestBytes, string signature, string publisher, CancellationToken cancellationToken);
    Task<bool> VerifyAssetAsync(string assetPath, ReadOnlyMemory<byte> assetBytes, string publisher, string signature, CancellationToken cancellationToken);
}

public sealed class RejectingExtensionSignatureVerifier : IExtensionSignatureVerifier
{
    public Task<bool> VerifyAsync(ReadOnlyMemory<byte> manifestBytes, string signature, string publisher, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task<bool> VerifyAssetAsync(string assetPath, ReadOnlyMemory<byte> assetBytes, string publisher, string signature, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}

public sealed class PinnedRsaExtensionSignatureVerifier(IReadOnlyDictionary<string, string> publisherPublicKeys) : IExtensionSignatureVerifier
{
    public Task<bool> VerifyAsync(ReadOnlyMemory<byte> manifestBytes, string signature, string publisher, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!publisherPublicKeys.TryGetValue(publisher, out var publicKeyPem)) return Task.FromResult(false);
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            return Task.FromResult(rsa.VerifyData(manifestBytes.Span, Convert.FromBase64String(signature),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or CryptographicException)
        {
            return Task.FromResult(false);
        }
    }

    public Task<bool> VerifyAssetAsync(string assetPath, ReadOnlyMemory<byte> assetBytes, string publisher, string signature, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!publisherPublicKeys.TryGetValue(publisher, out var publicKeyPem)) return Task.FromResult(false);
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            return Task.FromResult(rsa.VerifyData(assetBytes.Span, Convert.FromBase64String(signature),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or CryptographicException)
        {
            return Task.FromResult(false);
        }
    }
}

public sealed class ControlledExtensionPackageValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
    private static readonly HashSet<string> DangerousExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".ps1", ".psm1", ".dll", ".msi", ".msix", ".msixbundle",
        ".appx", ".appxbundle", ".cab", ".msu"
    };
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".ps1", ".psm1", ".dll", ".msi", ".msix", ".msixbundle",
        ".appx", ".appxbundle", ".cab", ".msu", ".zip", ".7z", ".ttf", ".otf", ".lnk",
        ".ico", ".png", ".jpg", ".jpeg", ".json", ".txt", ".xml", ".config", ".reg"
    };
    private static readonly HashSet<string> AllowedPermissions = new(StringComparer.Ordinal)
    {
        "process.execute", "filesystem.package-read", "network.access", "machine.modify",
        "user-profile.modify", "reboot.request"
    };

    private readonly IExtensionSignatureVerifier _signatureVerifier;
    private readonly ISet<string> _publisherAllowList;

    public ControlledExtensionPackageValidator(IExtensionSignatureVerifier? signatureVerifier = null, IEnumerable<string>? publisherAllowList = null)
    {
        _signatureVerifier = signatureVerifier ?? new RejectingExtensionSignatureVerifier();
        _publisherAllowList = new HashSet<string>(publisherAllowList ?? [], StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ExtensionValidationResult> ValidateAsync(
        string packagePath,
        string installRoot,
        IEnumerable<string> protectedRoots,
        int currentBuild,
        string architecture = "x64",
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packagePath)) return Invalid("PackageMissing", "Extension package does not exist.");
        if (!Path.IsPathFullyQualified(installRoot)) return Invalid("ExtensionPathUnsafe", "Install root must be absolute.");

        try
        {
            await using var stream = File.OpenRead(packagePath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var pathResult = ValidateArchivePaths(archive, installRoot, protectedRoots);
            if (pathResult is not null) return pathResult;
            var manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry is null) return Invalid("ManifestInvalid", "manifest.json must use the canonical file name.");
            var signatureEntry = archive.GetEntry("manifest.sig");
            if (signatureEntry is null || signatureEntry.Length is <= 0 or > 16 * 1024)
                return Invalid("ManifestInvalid", "manifest.sig is required and must be no larger than 16 KiB.");
            await using var manifestStream = manifestEntry.Open();
            using var manifestMemory = new MemoryStream();
            await manifestStream.CopyToAsync(manifestMemory, cancellationToken).ConfigureAwait(false);
            var manifestBytes = manifestMemory.ToArray();
            ExtensionManifest? manifest;
            try
            {
                StrictJson.ValidateNoAmbiguousProperties(manifestBytes);
                manifest = JsonSerializer.Deserialize<ExtensionManifest>(manifestBytes, JsonOptions);
            }
            catch (JsonException) { return Invalid("ManifestInvalid", "manifest.json is not valid JSON."); }
            if (manifest is null || !IsCompleteManifest(manifest))
                return Invalid("ManifestInvalid", "Manifest fields are incomplete.");
            string signature;
            await using (var signatureStream = signatureEntry.Open())
            using (var signatureReader = new StreamReader(signatureStream))
                signature = (await signatureReader.ReadToEndAsync(cancellationToken).ConfigureAwait(false)).Trim();
            if (string.IsNullOrWhiteSpace(signature) ||
                !await _signatureVerifier.VerifyAsync(manifestBytes, signature, manifest.Publisher, cancellationToken).ConfigureAwait(false))
                return Invalid("ExtensionNotTrusted", "Extension manifest signature validation failed.");
            if (_publisherAllowList.Count == 0 || !_publisherAllowList.Contains(manifest.Publisher))
                return Invalid("ExtensionNotTrusted", "The extension publisher is not trusted.");
            if (!string.Equals(manifest.Architecture, architecture, StringComparison.OrdinalIgnoreCase) ||
                currentBuild < manifest.MinimumBuild || (manifest.MaximumBuild > 0 && currentBuild > manifest.MaximumBuild))
                return Invalid("ExtensionIncompatible", "Extension is incompatible with this OS or architecture.");

            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifest.Files)
            {
                var normalizedPath = NormalizePackagePath(file.Path);
                if (!listed.Add(normalizedPath)) return Invalid("ExtensionPathUnsafe", "Manifest contains duplicate paths.");
                if (!IsSafeRelativePath(file.Path)) return Invalid("ExtensionPathUnsafe", $"Unsafe manifest path: {file.Path}");
                if (!AllowedExtensions.Contains(Path.GetExtension(file.Path)))
                    return Invalid("ExtensionAssetRejected", $"Extension asset type is not allowed: {file.Path}");
                var entry = archive.GetEntry(normalizedPath);
                if (entry is null) return Invalid("ExtensionHashMismatch", $"Manifest file is missing: {file.Path}");
                if (entry.Length > 100 * 1024 * 1024) return Invalid("ExtensionAssetRejected", "Extension asset exceeds the size limit.");
                await using var entryStream = entry.Open();
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(entryStream, cancellationToken).ConfigureAwait(false));
                if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    return Invalid("ExtensionHashMismatch", $"SHA-256 mismatch: {file.Path}");
                if (DangerousExtensions.Contains(Path.GetExtension(file.Path)))
                {
                    if (!file.Executable || !IsBase64Signature(file.Signature)) return Invalid("ExtensionAssetRejected", $"Executable asset must be marked and signed: {file.Path}");
                    await using var assetStream = entry.Open();
                    using var assetMemory = new MemoryStream();
                    await assetStream.CopyToAsync(assetMemory, cancellationToken).ConfigureAwait(false);
                    if (!await _signatureVerifier.VerifyAssetAsync(file.Path, assetMemory.ToArray(), manifest.Publisher, file.Signature!, cancellationToken).ConfigureAwait(false))
                        return Invalid("ExtensionAssetRejected", $"Executable asset signature validation failed: {file.Path}");
                }
            }
            var taskResult = ValidateTaskDeclarations(manifest);
            if (taskResult is not null) return taskResult;
            var unlisted = archive.Entries
                .Where(entry => !string.IsNullOrEmpty(entry.Name) &&
                                !entry.FullName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) &&
                                !entry.FullName.Equals("manifest.sig", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(entry => !listed.Contains(entry.FullName.Replace('\\', '/')));
            if (unlisted is not null)
                return Invalid("ExtensionHashMismatch", $"Package contains an unlisted file: {unlisted.FullName}");
            return new ExtensionValidationResult(true, "Valid", "Controlled extension package validated.", manifest);
        }
        catch (InvalidDataException exception)
        {
            return Invalid("PackageInvalid", exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Invalid("AccessDenied", exception.Message);
        }
    }

    public async Task<ExtensionValidationResult> ValidateInstalledAsync(
        string installPath,
        int currentBuild,
        string architecture = "x64",
        CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(installPath) || !Directory.Exists(installPath))
            return Invalid("ExtensionPathUnsafe", "Installed extension path is invalid.");
        try
        {
            var root = EnsureTrailingSeparator(Path.GetFullPath(installPath));
            var manifestPath = Path.Combine(root, "manifest.json");
            var signaturePath = Path.Combine(root, "manifest.sig");
            if (!File.Exists(manifestPath) || !File.Exists(signaturePath))
                return Invalid("ManifestInvalid", "Installed extension manifest or signature is missing.");
            var manifestBytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            StrictJson.ValidateNoAmbiguousProperties(manifestBytes);
            var manifest = JsonSerializer.Deserialize<ExtensionManifest>(manifestBytes, JsonOptions);
            if (manifest is null || !IsCompleteManifest(manifest))
                return Invalid("ManifestInvalid", "Installed extension manifest fields are incomplete.");
            var signatureInfo = new FileInfo(signaturePath);
            if (signatureInfo.Length is <= 0 or > 16 * 1024) return Invalid("ManifestInvalid", "Installed extension signature is invalid.");
            var signature = (await File.ReadAllTextAsync(signaturePath, cancellationToken).ConfigureAwait(false)).Trim();
            if (!await _signatureVerifier.VerifyAsync(manifestBytes, signature, manifest.Publisher, cancellationToken).ConfigureAwait(false) ||
                _publisherAllowList.Count == 0 || !_publisherAllowList.Contains(manifest.Publisher))
                return Invalid("ExtensionNotTrusted", "Installed extension signature or publisher is not trusted.");
            if (!string.Equals(manifest.Architecture, architecture, StringComparison.OrdinalIgnoreCase) ||
                currentBuild < manifest.MinimumBuild || manifest.MaximumBuild > 0 && currentBuild > manifest.MaximumBuild)
                return Invalid("ExtensionIncompatible", "Installed extension is incompatible with this OS or architecture.");
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "manifest.json", "manifest.sig", ".installation.json" };
            foreach (var file in manifest.Files)
            {
                var normalizedPath = NormalizePackagePath(file.Path);
                if (!expected.Add(normalizedPath) || !IsSafeRelativePath(file.Path) || !AllowedExtensions.Contains(Path.GetExtension(file.Path)))
                    return Invalid("ExtensionAssetRejected", "Installed extension manifest contains an invalid asset.");
                var assetPath = Path.GetFullPath(Path.Combine(root, normalizedPath));
                if (!assetPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(assetPath))
                    return Invalid("ExtensionPathUnsafe", "Installed extension asset path is unsafe or missing.");
                await using var stream = File.OpenRead(assetPath);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
                if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    return Invalid("ExtensionHashMismatch", "Installed extension asset hash does not match.");
                if (DangerousExtensions.Contains(Path.GetExtension(file.Path)))
                {
                    var bytes = await File.ReadAllBytesAsync(assetPath, cancellationToken).ConfigureAwait(false);
                    if (!file.Executable || !IsBase64Signature(file.Signature) || !await _signatureVerifier.VerifyAssetAsync(file.Path, bytes, manifest.Publisher, file.Signature!, cancellationToken).ConfigureAwait(false))
                        return Invalid("ExtensionAssetRejected", "Installed executable asset signature validation failed.");
                }
            }
            if (Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).Any(path => !expected.Contains(path)))
                return Invalid("ExtensionHashMismatch", "Installed extension contains an unlisted file.");
            var taskResult = ValidateTaskDeclarations(manifest);
            return taskResult ?? new ExtensionValidationResult(true, "Valid", "Installed extension validated.", manifest);
        }
        catch (JsonException) { return Invalid("ManifestInvalid", "Installed extension manifest is not valid JSON."); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Invalid("AccessDenied", exception.Message);
        }
    }

    private static ExtensionValidationResult? ValidateArchivePaths(ZipArchive archive, string installRoot, IEnumerable<string> protectedRoots)
    {
        var installFull = EnsureTrailingSeparator(Path.GetFullPath(installRoot));
        var protectedFull = protectedRoots.Select(path => EnsureTrailingSeparator(Path.GetFullPath(path))).ToArray();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var normalized = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrEmpty(normalized) || normalized.EndsWith('/')) continue;
            if (!IsSafeRelativePath(normalized) || !paths.Add(normalized))
                return Invalid("ExtensionPathUnsafe", $"Unsafe or duplicate package path: {entry.FullName}");
            var destination = Path.GetFullPath(Path.Combine(installRoot, normalized));
            if (!destination.StartsWith(installFull, StringComparison.OrdinalIgnoreCase) || protectedFull.Any(root => destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
                return Invalid("ExtensionPathUnsafe", $"Package escapes the isolated install root: {entry.FullName}");
            var attributes = (entry.ExternalAttributes >> 16) & 0xF000;
            if (attributes == 0xA000) return Invalid("ExtensionPathUnsafe", "Symbolic links are not permitted in extension packages.");
        }
        if (!paths.Contains("manifest.json") || !paths.Contains("manifest.sig"))
            return Invalid("ManifestInvalid", "manifest.json and manifest.sig are required.");
        return null;
    }

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.StartsWith('\\') || Path.IsPathRooted(path) || path.Contains(':')) return false;
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.All(segment => segment is not ("." or "..")) && !path.Contains("//", StringComparison.Ordinal);
    }

    private static bool IsSafeIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');

    private static bool IsBase64Signature(string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature) || signature.Length > 16 * 1024) return false;
        try { return Convert.FromBase64String(signature).Length > 0; }
        catch (FormatException) { return false; }
    }

    private static bool IsCompleteManifest(ExtensionManifest manifest) =>
        manifest.ManifestVersion == "1.0" && IsSafeIdentifier(manifest.PackageId) && IsSafeIdentifier(manifest.Version) &&
        !string.IsNullOrWhiteSpace(manifest.Publisher) && !string.IsNullOrWhiteSpace(manifest.Architecture) &&
        manifest.Permissions is not null && manifest.Files is { Count: > 0 } &&
        !manifest.Permissions.Any(permission => !AllowedPermissions.Contains(permission)) &&
        manifest.Permissions.Distinct(StringComparer.OrdinalIgnoreCase).Count() == manifest.Permissions.Count &&
        (manifest.Tasks is not { Count: > 0 } || manifest.Permissions.Contains("process.execute", StringComparer.Ordinal)) &&
        manifest.MinimumBuild >= 0 && manifest.MaximumBuild >= 0 &&
        (manifest.MaximumBuild == 0 || manifest.MaximumBuild >= manifest.MinimumBuild);

    private static ExtensionValidationResult? ValidateTaskDeclarations(ExtensionManifest manifest)
    {
        var taskIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var task in manifest.Tasks ?? [])
        {
            if (!IsSafeIdentifier(task.TaskId) || !taskIds.Add(task.TaskId) || task.Protocol != "stdio-json-v1")
                return Invalid("ManifestInvalid", "Extension task declarations are invalid or duplicated.");
            if (!IsSafeRelativePath(task.ExecutablePath) || !Path.GetExtension(task.ExecutablePath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                return Invalid("ExtensionAssetRejected", $"Extension task entry point must be an EXE: {task.TaskId}");
            var executable = manifest.Files.FirstOrDefault(file => NormalizePackagePath(file.Path).Equals(NormalizePackagePath(task.ExecutablePath), StringComparison.OrdinalIgnoreCase));
            if (executable is null || !executable.Executable)
                return Invalid("ExtensionAssetRejected", $"Extension task entry point is not a verified executable: {task.TaskId}");
        }
        return null;
    }

    private static string EnsureTrailingSeparator(string path) => path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
    private static string NormalizePackagePath(string path) => path.Replace('\\', '/');
    private static ExtensionValidationResult Invalid(string code, string message) => new(false, code, message);
}
