using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace ScreenFlow.Api.Contracts;

/// <summary>
/// Keyset pagination cursor: the (SubmittedAt, Id) of the last item on the page. Opaque to clients
/// (base64url). Unlike OFFSET paging, the next page costs the same however deep you go, and rows
/// inserted meanwhile don't shift items between pages.
/// </summary>
public readonly record struct OrderCursor(DateTimeOffset SubmittedAt, Guid Id)
{
    public string Encode()
    {
        var raw = string.Create(CultureInfo.InvariantCulture, $"{SubmittedAt.UtcTicks}|{Id:N}");
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(raw));
    }

    public static bool TryDecode(string value, out OrderCursor cursor)
    {
        cursor = default;
        try
        {
            var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(value)).Split('|');
            if (parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && ticks is > 0 and < 3155378975999999999 // DateTime.MaxValue.Ticks
                && Guid.TryParseExact(parts[1], "N", out var id))
            {
                cursor = new OrderCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
                return true;
            }
        }
        catch (FormatException)
        {
            // Not base64url: fall through to "invalid".
        }
        return false;
    }
}
