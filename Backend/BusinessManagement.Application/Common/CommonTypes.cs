using FluentValidation;
using FluentValidation.Results;

namespace BusinessManagement.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public static class Paging
{
    public const int MaxPageSize = 100;
    public static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize));
}

public static class Fail
{
    /// <summary>Throws a 400 validation error for a single field (surfaced as ProblemDetails with errors).</summary>
    public static Exception Validation(string property, string message) =>
        new ValidationException([new ValidationFailure(property, message)]);
}

/// <summary>Normalises mobile numbers to digits only: strips +, spaces, dashes, a 91 country prefix or a leading 0.</summary>
public static class MobileNumber
{
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 12 && digits.StartsWith("91", StringComparison.Ordinal)) digits = digits[2..];
        else if (digits.Length == 11 && digits[0] == '0') digits = digits[1..];
        return digits;
    }

    public static bool IsValid(string? input)
    {
        var n = Normalize(input);
        return n.Length is >= 10 and <= 15 && n.All(char.IsAsciiDigit);
    }
}

/// <summary>GSTIN format + checksum validation (15 chars; last char is a mod-36 check digit).</summary>
public static class Gstin
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private static readonly System.Text.RegularExpressions.Regex Pattern =
        new(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][0-9A-Z]Z[0-9A-Z]$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static string Normalize(string? gstin) => (gstin ?? string.Empty).Trim().ToUpperInvariant();

    public static bool IsValid(string? gstin)
    {
        var g = Normalize(gstin);
        if (!Pattern.IsMatch(g)) return false;
        var state = int.Parse(g[..2]);
        if (state is < 1 or > 99) return false;
        var sum = 0;
        for (var i = 0; i < 14; i++)
        {
            var v = Alphabet.IndexOf(g[i]) * (i % 2 == 0 ? 1 : 2);
            sum += v / 36 + v % 36;
        }
        return Alphabet[(36 - sum % 36) % 36] == g[14];
    }

    /// <summary>The first two digits of a GSTIN are the state code.</summary>
    public static string StateCode(string gstin) => Normalize(gstin)[..2];
}
