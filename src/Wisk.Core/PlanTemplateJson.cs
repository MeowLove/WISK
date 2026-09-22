using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Wisk.Contracts;

namespace Wisk.Core;

public static class PlanTemplateJson
{
    public const string SchemaVersion = "1.0";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(PlanTemplateDocument template, Catalog catalog)
    {
        Validate(template, catalog);
        return JsonSerializer.Serialize(template, Options);
    }

    public static PlanTemplateDocument Deserialize(string json, Catalog catalog)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ProfileValidationException("Plan template JSON is empty.");
        PlanTemplateDocument? template;
        try
        {
            StrictJson.ValidateNoAmbiguousProperties(json);
            template = JsonSerializer.Deserialize<PlanTemplateDocument>(json, Options);
        }
        catch (JsonException exception)
        {
            throw new ProfileValidationException($"Plan template JSON is invalid: {exception.Message}");
        }
        Validate(template, catalog);
        return template! with { Items = template!.Items.ToImmutableArray() };
    }

    public static void Validate(PlanTemplateDocument? template, Catalog catalog)
    {
        if (template is null) throw new ProfileValidationException("Plan template is missing.");
        if (!template.SchemaVersion.Equals(SchemaVersion, StringComparison.Ordinal))
            throw new ProfileValidationException($"Unsupported plan template schema '{template.SchemaVersion}'.");
        if (!Regex.IsMatch(template.TemplateId ?? string.Empty, "^[A-Za-z0-9._-]{1,80}$"))
            throw new ProfileValidationException("Plan template ID is invalid.");
        if (string.IsNullOrWhiteSpace(template.Name) || template.Name.Length > 100)
            throw new ProfileValidationException("Plan template name must contain 1-100 characters.");
        if (template.Items.IsDefaultOrEmpty || template.Items.Length > 200)
            throw new ProfileValidationException("Plan template must contain 1-200 items.");
        var policyIssue = ProfileDocumentValidator.ValidateExecutionPolicy(template.Policy);
        if (policyIssue is not null) throw new ProfileValidationException(policyIssue.Message);

        var seenSingleTasks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in template.Items)
        {
            if (catalog.Find(item.TaskId) is null) throw new ProfileValidationException($"Unknown task: {item.TaskId}");
            if (!item.TaskId.Equals("accounts-local", StringComparison.OrdinalIgnoreCase) && !seenSingleTasks.Add(item.TaskId))
                throw new ProfileValidationException($"Task '{item.TaskId}' cannot appear more than once in a plan template.");
            ValidateValue(item, catalog);
        }
    }

    private static void ValidateValue(PlanTemplateItem item, Catalog catalog)
    {
        if (ConfigurableRegistrySettingCatalog.Find(item.TaskId) is { } registrySetting)
        {
            if (!registrySetting.IsValidState(item.Value))
                throw new ProfileValidationException($"Task '{item.TaskId}' requires an explicit enabled, disabled, or default template value.");
            return;
        }
        if (item.Value is null) return;
        if (catalog.Find(item.TaskId)?.Kind == TaskKind.Winget && item.Value is "install" or "upgrade") return;
        var valid = item.TaskId.ToLowerInvariant() switch
        {
            "computer-name" => Regex.IsMatch(item.Value, "^[A-Za-z0-9-]{1,15}$"),
            "device-setup-region" => Regex.IsMatch(item.Value, "^\\d{1,4}$"),
            "language-ui-preference" => LanguagePreferenceCatalog.IsValid(item.Value),
            "font-supplements-cjk-indic-europe" => FontSupplementCatalog.IsValidSelection(item.Value),
            "setting-windows-update-mode" => item.Value is "default" or "notify-download" or "auto-notify-install",
            "setting-power-plan" => item.Value is "balanced" or "high-performance" or "power-saver" or "ultimate-performance",
            "setting-sleep-timeouts" => IsTimeoutTemplateValue(item.Value),
            _ => false
        };
        if (!valid) throw new ProfileValidationException($"Task '{item.TaskId}' contains an unsupported or invalid template value.");
    }

    private static bool IsTimeoutTemplateValue(string value)
    {
        var parts = value.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4) return false;
        var names = new[] { "ac", "dc", "displayAc", "displayDc" };
        for (var index = 0; index < parts.Length; index++)
        {
            var pair = parts[index].Split('=', 2);
            if (pair.Length != 2 || !pair[0].Equals(names[index], StringComparison.Ordinal) ||
                !int.TryParse(pair[1], out var minutes) || minutes is < 0 or > 1440) return false;
        }
        return true;
    }
}
