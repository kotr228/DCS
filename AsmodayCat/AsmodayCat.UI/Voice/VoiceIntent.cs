using System.Text.RegularExpressions;

namespace AsmodayCat.UI.Voice;

// Understands a spoken yes/no for approvals ("так", "дозволяю" / "ні", "скасуй"…).
// "No" wins when both appear ("так, але ні", "не дозволяю"): denying is the safe choice.
public static class VoiceIntent
{
    private static readonly Regex Deny = new(
        @"(?<!\p{L})(ні|нi|нє|не\s+треба|не\s+можна|не\s+дозволяю|не\s+підтверджую|не\s+роби|відхил\p{L}*|скасу\p{L}*|" +
        @"стоп|відмов\p{L}*|заборон\p{L}*|нет|не\s+надо|не\s+разрешаю|отмен\p{L}*|отклон\p{L}*|запрет\p{L}*|no|deny|cancel|stop|reject)(?!\p{L})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Approve = new(
        @"(?<!\p{L})(так|дозволяю|дозволь|дозволити|підтверджую|підтвердж\p{L}*|можна|давай|згод\p{L}*|виконуй|роби|" +
        @"да|разрешаю|разреши|подтверждаю|выполняй|делай|можно|yes|approve|confirm|ok|окей)(?!\p{L})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // true = approve, false = deny, null = not understood
    public static bool? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (Deny.IsMatch(text)) return false;
        if (Approve.IsMatch(text)) return true;
        return null;
    }
}
