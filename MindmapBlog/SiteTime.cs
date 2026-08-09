using System.Globalization;
using System.Runtime.CompilerServices;

namespace MindmapBlog;

/// <summary>
/// 站点展示用时间：固定为东八区（Asia/Shanghai），避免 CI（UTC）或服务器时区导致少 8 小时。
/// </summary>
internal static class SiteTime
{
    public static readonly TimeZoneInfo China = ResolveChinaTimeZone();

    public static DateTimeOffset ToChina(DateTimeOffset dto) =>
        TimeZoneInfo.ConvertTime(dto, China);

    public static DateTime ToChinaDateTime(DateTimeOffset dto) =>
        ToChina(dto).DateTime;

    public static string Format(DateTimeOffset dto, string format) =>
        ToChina(dto).ToString(format, CultureInfo.InvariantCulture);

    private static TimeZoneInfo ResolveChinaTimeZone()
    {
        foreach (var id in new[] { "Asia/Shanghai", "China Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // try next
            }
            catch (InvalidTimeZoneException)
            {
                // try next
            }
        }

        // 兜底：固定 UTC+8（无 DST）
        return TimeZoneInfo.CreateCustomTimeZone(
            "UTC+08",
            TimeSpan.FromHours(8),
            "China Standard Time",
            "China Standard Time");
    }
}

/// <summary>把 <see cref="DateTimeOffset.ToLocalTime"/> 换成东八区展示。</summary>
internal static class SiteTimeExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DateTimeOffset ToSiteLocal(this DateTimeOffset dto) => SiteTime.ToChina(dto);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DateTime ToSiteLocalDateTime(this DateTimeOffset dto) => SiteTime.ToChinaDateTime(dto);
}
