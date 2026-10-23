using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.SettingsLog;

/// <summary>Area keys for <see cref="SettingsChangeLog"/>. Company-wide areas always carry a null BranchId.</summary>
public static class SettingsArea
{
    public const string ShopDesk = "shop_desk";
    public const string ShopPayment = "shop_payment";
    public const string BankAccount = "bank_account";
    public const string Company = "company";
    public const string Branch = "branch";
    public const string Service = "service";
    public const string MessageTemplate = "message_template";
    public const string PartGrade = "part_grade";
    public const string PartCategory = "part_category";
    public const string WorkCategory = "work_category";
    public const string ExpenseCategory = "expense_category";
    public const string ChecklistTemplate = "checklist_template";
    public const string InsuranceRates = "insurance_rates";
    public const string Demo = "demo";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        ShopDesk, ShopPayment, BankAccount, Company, Branch, Service, MessageTemplate,
        PartGrade, PartCategory, WorkCategory, ExpenseCategory, ChecklistTemplate, InsuranceRates, Demo
    };
}

public static class SettingsChangeLogHttpExtensions
{
    /// <summary>The bound staff id for a settings write, or null on the unauthenticated test path.</summary>
    public static Guid? ActorStaffId(this HttpContext http) => http.GetStaff()?.Id;
}

/// <summary>One stored row as the API returns it, with the actor's display name.</summary>
public sealed record SettingsChangeRow(
    Guid Id,
    Guid? BranchId,
    string Area,
    string Field,
    string? OldValue,
    string? NewValue,
    Guid? ChangedByStaffId,
    string? ChangedByName,
    DateTime ChangedAtUtc);

/// <summary>Newest first.</summary>
public sealed record SettingsChangeListResponse(IReadOnlyList<SettingsChangeRow> Rows);

/// <summary>Latest change for one field: who and when, with the values it moved between.</summary>
public sealed record SettingsLatestChange(
    string? ChangedBy,
    DateTime ChangedAtUtc,
    string? OldValue,
    string? NewValue);

/// <summary>Shape: area → field → latest change, for one shop (or company-wide when BranchId is null).</summary>
public sealed record SettingsLatestResponse(
    Guid? BranchId,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, SettingsLatestChange>> Areas);

/// <summary>
/// Writes and reads the settings change trail. Callers stage rows in the same unit of work as the
/// settings change, so a row is saved only if the change is saved. Recording an unchanged value is a no-op.
/// </summary>
public sealed class SettingsChangeLogService(IHrDb db)
{
    public const int MaxFieldLength = 80;
    public const int MaxValueLength = 500;
    public const int DefaultTake = 100;
    public const int MaxTake = 500;

    /// <summary>
    /// Stages one row. The comparison uses the full values; only the stored copy is clipped,
    /// so a change past the 500-character mark is still recorded.
    /// </summary>
    /// <param name="force">Record even when the raw values are equal (a replaced file, or a masked value).</param>
    public void Record(
        Guid? actorStaffId,
        Guid? branchId,
        string area,
        string field,
        string? oldValue,
        string? newValue,
        bool force = false)
    {
        if (!force && string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        db.SettingsChangeLogs.Add(new SettingsChangeLog
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            Area = area,
            Field = Clip(field, MaxFieldLength)!,
            OldValue = Clip(oldValue, MaxValueLength),
            NewValue = Clip(newValue, MaxValueLength),
            ChangedByStaffId = actorStaffId,
            ChangedAtUtc = DateTime.UtcNow
        });
    }

    /// <summary>Stages one row per tuple whose old and new values differ.</summary>
    public void RecordChanges(
        Guid? actorStaffId,
        Guid? branchId,
        string area,
        IEnumerable<(string Field, string? Old, string? New)> changes)
    {
        foreach (var (field, oldValue, newValue) in changes)
        {
            Record(actorStaffId, branchId, area, field, oldValue, newValue);
        }
    }

    /// <summary>Stages one row per field of a row that was just created. Every old value is null.</summary>
    public void RecordCreated(
        Guid? actorStaffId,
        Guid? branchId,
        string area,
        string key,
        IEnumerable<(string Field, string? Value)> values) =>
        RecordChanges(
            actorStaffId,
            branchId,
            area,
            values.Select(value => ($"{key}.{value.Field}", (string?)null, value.Value)));

    /// <summary>Stages one row per field that differs between two snapshots of the same row.</summary>
    public void RecordUpdated(
        Guid? actorStaffId,
        Guid? branchId,
        string area,
        string key,
        IReadOnlyList<(string Field, string? Value)> before,
        IReadOnlyList<(string Field, string? Value)> after) =>
        RecordChanges(
            actorStaffId,
            branchId,
            area,
            Diff(before, after).Select(change => ($"{key}.{change.Field}", change.Old, change.New)));

    /// <summary>Fields for a sortable, activatable list row (part grades, categories).</summary>
    public static IReadOnlyList<(string Field, string? Value)> ListRow(string name, int sortOrder, bool active) =>
    [
        ("name", name),
        ("sort_order", Show(sortOrder)),
        ("active", Show(active)),
    ];

    /// <summary>
    /// Compares two snapshots taken with the same field list, before and after a change.
    /// Returns only the fields whose values differ.
    /// </summary>
    public static List<(string Field, string? Old, string? New)> Diff(
        IReadOnlyList<(string Field, string? Value)> before,
        IReadOnlyList<(string Field, string? Value)> after)
    {
        var prior = before.ToDictionary(item => item.Field, item => item.Value, StringComparer.Ordinal);
        var changes = new List<(string Field, string? Old, string? New)>();
        foreach (var (field, value) in after)
        {
            if (!prior.TryGetValue(field, out var old))
            {
                continue;
            }

            if (!string.Equals(old, value, StringComparison.Ordinal))
            {
                changes.Add((field, old, value));
            }
        }

        return changes;
    }

    /// <summary>Formats a value for the log: invariant numbers, HH:mm times, enum names, true/false.</summary>
    public static string? Show(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? "true" : "false",
        decimal number => number.ToString("0.########", CultureInfo.InvariantCulture),
        double number => number.ToString("0.########", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("HH:mm", CultureInfo.InvariantCulture),
        Enum enumValue => enumValue.ToString(),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    /// <summary>Keeps the last four characters of an account number, for example <c>****5678</c>.</summary>
    public static string? MaskAccount(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= 4
            ? new string('*', value.Length)
            : new string('*', value.Length - 4) + value[^4..];
    }

    public static string? Clip(string? value, int max)
    {
        if (value is null || value.Length <= max)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, max - 1), "…");
    }

    /// <summary>
    /// Newest first. A franchise owner passes <paramref name="branchId"/> as their own shop and gets
    /// company-wide rows too; pass <paramref name="includeCompany"/> to add them.
    /// </summary>
    public async Task<SettingsChangeListResponse> ListAsync(
        Guid? branchId,
        bool includeCompany,
        string? area,
        int take,
        CancellationToken cancellationToken)
    {
        var query = db.SettingsChangeLogs.AsNoTracking().AsQueryable();
        if (branchId is Guid shop)
        {
            query = includeCompany
                ? query.Where(row => row.BranchId == shop || row.BranchId == null)
                : query.Where(row => row.BranchId == shop);
        }
        else if (!includeCompany)
        {
            query = query.Where(row => row.BranchId == null);
        }

        if (!string.IsNullOrWhiteSpace(area))
        {
            query = query.Where(row => row.Area == area);
        }

        var rows = await query
            .Include(row => row.ChangedByStaff)
            .ThenInclude(staff => staff!.Profile)
            .OrderByDescending(row => row.ChangedAtUtc)
            .ThenByDescending(row => row.Id)
            .Take(Math.Clamp(take, 1, MaxTake))
            .ToListAsync(cancellationToken);

        return new SettingsChangeListResponse(rows.Select(ToRow).ToList());
    }

    /// <summary>
    /// Newest row per area and field, for one shop. Company-wide rows are returned when
    /// <paramref name="branchId"/> is null.
    /// </summary>
    public async Task<SettingsLatestResponse> LatestAsync(Guid? branchId, CancellationToken cancellationToken)
    {
        var query = db.SettingsChangeLogs.AsNoTracking()
            .Where(row => row.BranchId == branchId);

        var rows = await query
            .Include(row => row.ChangedByStaff)
            .ThenInclude(staff => staff!.Profile)
            .OrderByDescending(row => row.ChangedAtUtc)
            .ThenByDescending(row => row.Id)
            .ToListAsync(cancellationToken);

        var areas = new Dictionary<string, IReadOnlyDictionary<string, SettingsLatestChange>>(StringComparer.Ordinal);
        foreach (var group in rows.GroupBy(row => row.Area, StringComparer.Ordinal))
        {
            var fields = new Dictionary<string, SettingsLatestChange>(StringComparer.Ordinal);
            foreach (var row in group)
            {
                // Rows are newest first, so the first row seen for a field is its latest change.
                fields.TryAdd(row.Field, new SettingsLatestChange(
                    row.ChangedByStaff?.Profile.DisplayName,
                    row.ChangedAtUtc,
                    row.OldValue,
                    row.NewValue));
            }

            areas[group.Key] = fields;
        }

        return new SettingsLatestResponse(branchId, areas);
    }

    private static SettingsChangeRow ToRow(SettingsChangeLog row) => new(
        row.Id,
        row.BranchId,
        row.Area,
        row.Field,
        row.OldValue,
        row.NewValue,
        row.ChangedByStaffId,
        row.ChangedByStaff?.Profile.DisplayName,
        row.ChangedAtUtc);
}
