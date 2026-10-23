namespace HrCore.Domain;

public sealed class EmploymentDocument
{
    public Guid Id { get; set; }
    public Guid StaffId { get; set; }
    public Staff Staff { get; set; } = null!;
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public EmploymentDocType DocType { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long ByteSize { get; set; }
    public string StoragePath { get; set; } = "";
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
