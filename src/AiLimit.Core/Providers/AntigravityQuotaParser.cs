using System.Text.Json;

namespace AiLimit.Core.Providers;

internal static class AntigravityQuotaParser
{
    public static IReadOnlyList<AntigravityQuotaBucket> ParseQuotaBuckets(string json)
    {
        using var document = JsonDocument.Parse(json);
        var buckets = new List<AntigravityQuotaBucket>();
        CollectQuotaBuckets(document.RootElement, buckets);
        return buckets;
    }

    public static string? ExtractProjectId(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("cloudaicompanionProject", out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            if (value.ValueKind == JsonValueKind.Object
                && value.TryGetProperty("id", out var id))
            {
                return id.GetString();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    public static string? ExtractAccountKey(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return ExtractAccountKey(document.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractAccountKey(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("userStatus") && property.Value.ValueKind == JsonValueKind.Object)
                {
                    var accountKey = ReadAccountKey(property.Value);
                    if (!string.IsNullOrWhiteSpace(accountKey))
                    {
                        return accountKey;
                    }
                }

                var nested = ExtractAccountKey(property.Value);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = ExtractAccountKey(item);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string? ReadAccountKey(JsonElement userStatus)
    {
        foreach (var name in new[] { "email", "userEmail", "accountEmail" })
        {
            if (userStatus.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return $"email:{value.GetString()!.Trim().ToLowerInvariant()}";
            }
        }

        foreach (var name in new[] { "userId", "gaiaId", "id" })
        {
            if (userStatus.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return $"id:{value.GetString()!.Trim()}";
            }
        }

        return null;
    }

    private static void CollectQuotaBuckets(JsonElement element, List<AntigravityQuotaBucket> buckets)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            TryAddQuotaBucket(element, buckets);
            var handledModelMap = TryAddModelMapQuotaBuckets(element, buckets);
            foreach (var property in element.EnumerateObject())
            {
                if (handledModelMap && property.NameEquals("models"))
                {
                    continue;
                }

                CollectQuotaBuckets(property.Value, buckets);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectQuotaBuckets(item, buckets);
            }
        }
    }

    private static void TryAddQuotaBucket(JsonElement element, List<AntigravityQuotaBucket> buckets)
    {
        var quotaSource = element;
        if (element.TryGetProperty("quotaInfo", out var quotaInfo))
        {
            quotaSource = quotaInfo;
        }

        if (!quotaSource.TryGetProperty("remainingFraction", out var remainingFraction))
        {
            return;
        }

        var model = ReadModelId(element);
        if (string.IsNullOrWhiteSpace(model)
            || !IsTrackedModel(model)
            || !TryGetDouble(remainingFraction, out var fraction))
        {
            return;
        }

        DateTimeOffset? resetAt = null;
        if (quotaSource.TryGetProperty("resetTime", out var resetTime)
            && DateTimeOffset.TryParse(resetTime.GetString(), out var parsedReset))
        {
            resetAt = parsedReset;
        }

        buckets.Add(new AntigravityQuotaBucket(
            model,
            Math.Clamp((int)Math.Round(fraction * 100, MidpointRounding.AwayFromZero), 0, 100),
            resetAt));
    }

    private static bool TryAddModelMapQuotaBuckets(JsonElement element, List<AntigravityQuotaBucket> buckets)
    {
        if (!element.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var model in models.EnumerateObject())
        {
            if (!model.Value.TryGetProperty("quotaInfo", out var quotaInfo)
                || !quotaInfo.TryGetProperty("remainingFraction", out var remainingFraction)
                || !TryGetDouble(remainingFraction, out var fraction))
            {
                continue;
            }

            DateTimeOffset? resetAt = null;
            if (quotaInfo.TryGetProperty("resetTime", out var resetTime)
                && DateTimeOffset.TryParse(resetTime.GetString(), out var parsedReset))
            {
                resetAt = parsedReset;
            }

            var label = model.Value.TryGetProperty("displayName", out var displayName)
                ? displayName.GetString()
                : null;
            var modelName = string.IsNullOrWhiteSpace(label) ? model.Name : label!;
            if (!IsTrackedModel(model.Name) && !IsTrackedModel(modelName))
            {
                continue;
            }

            buckets.Add(new AntigravityQuotaBucket(
                modelName,
                Math.Clamp((int)Math.Round(fraction * 100, MidpointRounding.AwayFromZero), 0, 100),
                resetAt));
        }

        return true;
    }

    internal static bool IsTrackedModel(string value)
    {
        return value.StartsWith("gemini", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("claude", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("gpt", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("image", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("imagen", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadModelId(JsonElement element)
    {
        if (element.TryGetProperty("modelId", out var modelId))
        {
            return modelId.GetString();
        }

        if (element.TryGetProperty("label", out var label))
        {
            return label.GetString();
        }

        if (element.TryGetProperty("displayName", out var displayName))
        {
            return displayName.GetString();
        }

        if (element.TryGetProperty("modelOrAlias", out var modelOrAlias)
            && modelOrAlias.TryGetProperty("model", out var model))
        {
            return model.GetString();
        }

        return null;
    }

    private static bool TryGetDouble(JsonElement value, out double result)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.TryGetDouble(out result);
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return double.TryParse(value.GetString(), out result);
        }

        result = 0;
        return false;
    }
}
