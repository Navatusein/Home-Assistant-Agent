using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace HomeAssistant.Agent.Core.Logging;

// Keeps credentials out of the log. Debug/Trace output includes whole protocol
// frames (the Discord RPC dumps its AUTHENTICATE command with the access
// token), and the diagnostics bundle includes Settings.json — both pass
// through here, so raising the log level to chase a bug never leaks a secret.
//
// Two shapes are covered: a property whose name says it is a secret, and a
// secret embedded in a string value as JSON ("access_token": "...") or as
// key=value.
public sealed partial class SecretMasker : ILogEventEnricher
{
    public const string Mask = "***";

    private static readonly string[] SecretWords = ["password", "secret", "token"];

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        List<LogEventProperty>? replacements = null;

        foreach (var (name, value) in logEvent.Properties)
        {
            var masked = IsSecretName(name) ? new ScalarValue(Mask) : MaskValue(value);

            if (!ReferenceEquals(masked, value))
                (replacements ??= []).Add(new LogEventProperty(name, masked));
        }

        if (replacements == null)
            return;

        foreach (var property in replacements)
            logEvent.AddOrUpdateProperty(property);
    }

    public static bool IsSecretName(string name)
    {
        foreach (var word in SecretWords)
        {
            if (name.Contains(word, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // Masks secrets inside free text. Cheap for the common case: the regex
    // only runs when one of the trigger words appears at all.
    public static string MaskText(string text)
    {
        if (!MayContainSecret(text))
            return text;

        text = JsonSecretRegex().Replace(text, match => $"{match.Groups["key"].Value}\"{Mask}\"");
        return KeyValueSecretRegex().Replace(text, match => $"{match.Groups["key"].Value}{Mask}");
    }

    private static LogEventPropertyValue MaskValue(LogEventPropertyValue value)
    {
        switch (value)
        {
            case ScalarValue { Value: string text }:
                var masked = MaskText(text);
                return ReferenceEquals(masked, text) ? value : new ScalarValue(masked);

            case StructureValue structure:
                var changed = false;
                var properties = new List<LogEventProperty>(structure.Properties.Count);

                foreach (var property in structure.Properties)
                {
                    var inner = IsSecretName(property.Name) ? new ScalarValue(Mask) : MaskValue(property.Value);
                    changed |= !ReferenceEquals(inner, property.Value);
                    properties.Add(new LogEventProperty(property.Name, inner));
                }

                return changed ? new StructureValue(properties, structure.TypeTag) : value;

            default:
                return value;
        }
    }

    private static bool MayContainSecret(string text)
    {
        foreach (var word in SecretWords)
        {
            if (text.Contains(word, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // "acc ess_token": "abc"  /  "client_secret":"abc"  /  "Password": "abc"
    [GeneratedRegex(@"(?<key>""[^""]*(?:password|secret|token)[^""]*""\s*:\s*)""(?:[^""\\]|\\.)*""", RegexOptions.IgnoreCase)]
    private static partial Regex JsonSecretRegex();

    // access_token=abc  (form bodies, query strings; stops at whitespace, &, ;
    // or a quote). Only "=": a colon also appears in plain prose such as
    // "tokens: 5", and JSON — the colon case that matters — has its own rule.
    [GeneratedRegex(@"(?<key>\b\w*(?:password|secret|token)\w*\s*=\s*)(?![""\s])[^\s&,;""]+", RegexOptions.IgnoreCase)]
    private static partial Regex KeyValueSecretRegex();
}
