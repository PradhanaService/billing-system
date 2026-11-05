using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Security;

namespace WaterBilling.Services.Admin;

public sealed record ActivityFilter(DateOnly From, DateOnly To, ActivityCategory? Category = null, string? UserName = null, string? Text = null);

public sealed record ActivityRow(long Id, DateTime EventAtUtc, string UserName, string? Role, ActivityCategory Category,
    string Action, string? EntityType, long? EntityId, string? BeforeJson, string? AfterJson, string? Reason);

// Activity log viewer (FR-ADM-07): Admin only, read-only, and every search is itself logged.
public class ActivityLogService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser, TimeProvider clock)
{
    public const int MaxRows = 500;

    public async Task<IReadOnlyList<ActivityRow>> SearchAsync(ActivityFilter f)
    {
        var me = await currentUser.RequireAsync(Permission.ActivityLogView);
        await using var db = await dbFactory.CreateDbContextAsync();

        // Dates are IST business-calendar dates; convert the range to UTC instants.
        var fromUtc = f.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) - Common.IndianTime.Offset;
        var toUtc = f.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) - Common.IndianTime.Offset;
        var query = db.ActivityLogs.Where(a => a.EventAtUtc >= fromUtc && a.EventAtUtc < toUtc);
        if (f.Category is { } c) query = query.Where(a => a.Category == c);
        if (!string.IsNullOrWhiteSpace(f.UserName)) query = query.Where(a => EF.Functions.Like(a.UserName, $"%{f.UserName.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(f.Text))
        {
            var t = $"%{f.Text.Trim()}%";
            query = query.Where(a => EF.Functions.Like(a.Action, t) || EF.Functions.Like(a.AfterJson ?? "", t)
                || EF.Functions.Like(a.BeforeJson ?? "", t) || EF.Functions.Like(a.Reason ?? "", t));
        }
        var rows = await query.OrderByDescending(a => a.Id).Take(MaxRows)
            .Select(a => new ActivityRow(a.Id, a.EventAtUtc, a.UserName, a.Role, a.Category, a.Action, a.EntityType, a.EntityId,
                a.BeforeJson, a.AfterJson, a.Reason))
            .ToListAsync();

        ActivityLogger.Add(db, clock.GetUtcNow().UtcDateTime, me, ActivityCategory.Admin, "ACTIVITY_LOG_VIEWED",
            after: new { From = f.From.ToString("yyyy-MM-dd"), To = f.To.ToString("yyyy-MM-dd"), Category = f.Category?.ToString(), f.UserName, f.Text });
        await db.SaveChangesAsync();
        return rows;
    }
}
