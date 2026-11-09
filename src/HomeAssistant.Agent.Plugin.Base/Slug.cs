using System.Security.Cryptography;
using System.Text;

namespace HomeAssistant.Agent.Plugin.Base;

// Turns a name the user typed into an id Home Assistant accepts. Discovery
// topics — and therefore unique ids and device ids — may only contain
// [a-zA-Z0-9_-]; anything else makes Home Assistant ignore the entity without
// a word. "My Laptop" -> "my_laptop", "Телеграм" -> "telegram".
public static class Slug
{
    // Russian and Ukrainian letters, transliterated the way people would type
    // them in Latin; other scripts fall back to the hash below.
    private static readonly Dictionary<char, string> Transliteration = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['ґ'] = "g", ['д'] = "d",
        ['е'] = "e", ['ё'] = "yo", ['є'] = "ye", ['ж'] = "zh", ['з'] = "z", ['и'] = "i",
        ['і'] = "i", ['ї'] = "yi", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m",
        ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t",
        ['у'] = "u", ['ф'] = "f", ['х'] = "kh", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh",
        ['щ'] = "shch", ['ъ'] = "", ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "yu",
        ['я'] = "ya",
    };

    // Lowercase ASCII letters and digits, runs of anything else collapsed into
    // one separator. A name with nothing usable left (only emoji, say) gets a
    // short hash of itself, so it is still stable and still distinct from
    // other names; an empty name gets the fallback.
    public static string ToObjectId(string? value, char separator = '_', string fallback = "item")
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var builder = new StringBuilder(value.Length);
        var pendingSeparator = false;

        void Append(string part)
        {
            if (pendingSeparator && builder.Length > 0)
                builder.Append(separator);

            builder.Append(part);
            pendingSeparator = false;
        }

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
                Append(character.ToString());
            else if (Transliteration.TryGetValue(character, out var latin))
            {
                if (latin.Length > 0)
                    Append(latin);
            }
            else
                pendingSeparator = true;
        }

        if (builder.Length > 0)
            return builder.ToString();

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return "x" + Convert.ToHexStringLower(hash)[..8];
    }
}
