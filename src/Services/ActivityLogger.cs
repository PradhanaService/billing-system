using System.Text.Json;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Security;

namespace WaterBilling.Services;

// Adds an activity log row to the SAME DbContext as the change it describes,
// so both are saved together or not at all (NFR-AUD-01).
public static class ActivityLogger
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static void Add(
        AppDbContext db, DateTime nowUtc, UserInfo? user, ActivityCategory category, string action,
        string? entityType = null, long? entityId = null,
        object? before = null, object? after = null, string? reason = null,
        string? userNameIfUnknown = null)
    {
        db.ActivityLogs.Add(new ActivityLog
        {
            EventAtUtc = nowUtc,
            UserId = user?.Id,
            UserName = user?.UserName ?? Truncate(userNameIfUnknown ?? "(unknown)", 50),
            Role = user?.Role.ToString(),
            Category = category,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before, Json),
            AfterJson = after is null ? null : JsonSerializer.Serialize(after, Json),
            Reason = reason,
        });
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
