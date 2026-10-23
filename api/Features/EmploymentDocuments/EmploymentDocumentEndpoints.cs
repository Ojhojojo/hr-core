using HrCore.Features.HrChangeLog;
using HrCore.Features.Staff;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.EmploymentDocuments;

public static class EmploymentDocumentEndpoints
{
    public const int NoteMaxLength = 500;
    public const int FileNameMaxLength = 260;
    public const long MaxBytes = 10 * 1024 * 1024;

    private const string UnsupportedFileError = "Upload a PDF or an image (JPG, PNG or WebP).";

    private static readonly Dictionary<string, EmploymentDocType> DocTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["offer"] = EmploymentDocType.Offer,
        ["contract"] = EmploymentDocType.Contract,
        ["id"] = EmploymentDocType.Id,
        ["other"] = EmploymentDocType.Other,
        ["coe_request_note"] = EmploymentDocType.CoeRequestNote,
    };

    // Extension allowlist. The uploaded bytes must also match this type's magic bytes.
    private static readonly Dictionary<string, string> FileTypesByExtension = new()
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
    };

    public static IEndpointRouteBuilder MapEmploymentDocumentApi(this IEndpointRouteBuilder app)
    {
        var directory = app.MapGroup("/api/employee-directory")
            .WithTags("Employee directory")
            .AddEndpointFilter<StaffAuthorizationFilter>();
        directory.MapGet("/me/documents", ListMine).WithName("ListMyEmploymentDocuments");
        directory.MapGet("/{staffId:guid}/documents", ListForStaff).WithName("ListEmploymentDocuments");
        directory.MapPost("/{staffId:guid}/documents", Upload)
            .DisableAntiforgery()
            .WithName("UploadEmploymentDocument");

        app.MapGet("/api/employee-documents/{id:guid}", Download)
            .WithTags("Employee directory")
            .AddEndpointFilter<StaffAuthorizationFilter>()
            .WithName("DownloadEmploymentDocument");
        return app;
    }

    private static async Task<IResult> ListMine(
        HttpContext http,
        IHrDb db,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null)
        {
            return Forbidden("Staff access required.");
        }

        return Results.Ok(await ListAsync(db, actor.Id, cancellationToken));
    }

    private static async Task<IResult> ListForStaff(
        Guid staffId,
        HttpContext http,
        BranchResolver branches,
        IHrDb db,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null)
        {
            return Forbidden("You cannot open employment documents.");
        }

        if (!HrRoles.IsDesk(actor.Role))
        {
            if (actor.Id != staffId)
            {
                return Forbidden("You cannot open another employee's documents.");
            }

            return Results.Ok(await ListAsync(db, staffId, cancellationToken));
        }

        if (actor.Role is not (StaffRole.Owner or StaffRole.Admin))
        {
            return Forbidden("You cannot open employment documents.");
        }

        if (await FindOnShopAsync(staffId, http, branches, db, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(await ListAsync(db, staffId, cancellationToken));
    }

    private static async Task<IResult> Upload(
        Guid staffId,
        HttpContext http,
        BranchResolver branches,
        IHrDb db,
        IHrFileStore files,
        HrChangeLogService hr,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null || actor.Role is not (StaffRole.Owner or StaffRole.Admin))
        {
            return Forbidden("You cannot upload employment documents.");
        }

        var onShop = await FindOnShopAsync(staffId, http, branches, db, cancellationToken);
        if (onShop is null)
        {
            return Results.NotFound();
        }

        if (!http.Request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "Send a file as multipart form data." });
        }

        var form = await http.Request.ReadFormAsync(cancellationToken);
        var upload = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (upload is null || upload.Length == 0)
        {
            return Results.BadRequest(new { error = "Choose a file." });
        }

        if (upload.Length > MaxBytes)
        {
            return Results.BadRequest(new { error = "File must be 10 MB or smaller." });
        }

        if (!TryParseDocType(form["docType"], out var docType))
        {
            return Results.BadRequest(new { error = "Unknown document type." });
        }

        string? note = null;
        var rawNote = form["note"].ToString();
        if (!string.IsNullOrWhiteSpace(rawNote))
        {
            note = rawNote.Trim();
            if (note.Length > NoteMaxLength)
            {
                return Results.BadRequest(new { error = "Note must be 500 characters or fewer." });
            }
        }

        var fileName = Path.GetFileName(upload.FileName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "document";
        }

        if (fileName.Length > FileNameMaxLength)
        {
            return Results.BadRequest(new { error = "File name must be 260 characters or fewer." });
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!FileTypesByExtension.TryGetValue(ext, out var extensionContentType))
        {
            return Results.BadRequest(new { error = UnsupportedFileError });
        }

        await using var read = upload.OpenReadStream();
        using var buffer = new MemoryStream();
        await read.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0)
        {
            return Results.BadRequest(new { error = "Choose a file." });
        }

        if (buffer.Length > MaxBytes)
        {
            return Results.BadRequest(new { error = "File must be 10 MB or smaller." });
        }

        // The stored type comes from the bytes, not the browser header, and must match the extension.
        var contentType = DetectContentType(buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 12)));
        if (contentType is null || contentType != extensionContentType)
        {
            return Results.BadRequest(new { error = UnsupportedFileError });
        }

        var storagePath = $"{onShop.BranchId}/{staffId}/{Guid.NewGuid():N}{ext}";
        try
        {
            buffer.Position = 0;
            await files.SaveAsync(storagePath, buffer, contentType, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }

        var row = new EmploymentDocument
        {
            Id = Guid.NewGuid(),
            StaffId = staffId,
            BranchId = onShop.BranchId,
            DocType = docType,
            FileName = fileName,
            ContentType = contentType,
            ByteSize = buffer.Length,
            StoragePath = storagePath,
            Note = note,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.EmploymentDocuments.Add(row);
        hr.Record(
            actor.Id,
            staffId,
            HrChangeField.DocumentUpload,
            null,
            row.DocType.ToString(),
            refType: "document",
            refId: row.Id);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/employee-documents/{row.Id}", ToResponse(row));
    }

    private static async Task<IResult> Download(
        Guid id,
        HttpContext http,
        BranchResolver branches,
        IHrDb db,
        IHrFileStore files,
        CancellationToken cancellationToken)
    {
        var row = await db.EmploymentDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (row is null)
        {
            return Results.NotFound();
        }

        var actor = http.GetStaff();
        if (actor is null)
        {
            return Forbidden("You cannot open employment documents.");
        }

        if (!HrRoles.IsDesk(actor.Role))
        {
            if (actor.Id != row.StaffId)
            {
                return Forbidden("You cannot open another employee's documents.");
            }
        }
        else if (actor.Role is StaffRole.Owner or StaffRole.Admin)
        {
            if (await FindOnShopAsync(row.StaffId, http, branches, db, cancellationToken) is null)
            {
                return Results.NotFound();
            }
        }
        else
        {
            return Forbidden("You cannot open employment documents.");
        }

        var stream = await files.OpenAsync(row.StoragePath, cancellationToken);
        return stream is null
            ? Results.NotFound()
            : Results.File(stream, row.ContentType, row.FileName);
    }

    private static async Task<ShopStaff?> FindOnShopAsync(
        Guid staffId,
        HttpContext http,
        BranchResolver branches,
        IHrDb db,
        CancellationToken cancellationToken)
    {
        var branchId = await branches.ResolveOpsAsync(http, cancellationToken);
        var staff = await db.Staff.AsNoTracking()
            .Include(s => s.Branch)
            .FirstOrDefaultAsync(s => s.Id == staffId, cancellationToken);
        if (staff is null || staff.BranchId != branchId)
        {
            return null;
        }

        var branch = await db.Branches.AsNoTracking()
            .FirstAsync(b => b.Id == branchId, cancellationToken);
        if (!string.Equals(branch.Code, BranchCodes.Demo, StringComparison.OrdinalIgnoreCase)
            && string.Equals(staff.Branch?.Code, BranchCodes.Demo, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new ShopStaff(branchId);
    }

    private static async Task<List<EmploymentDocumentResponse>> ListAsync(
        IHrDb db,
        Guid staffId,
        CancellationToken cancellationToken) =>
        await db.EmploymentDocuments.AsNoTracking()
            .Where(d => d.StaffId == staffId)
            .OrderByDescending(d => d.CreatedAtUtc)
            .ThenByDescending(d => d.Id)
            .Select(d => new EmploymentDocumentResponse(
                d.Id,
                d.DocType,
                d.FileName,
                d.ContentType,
                d.ByteSize,
                d.Note,
                d.CreatedAtUtc))
            .ToListAsync(cancellationToken);

    private static bool TryParseDocType(string? raw, out EmploymentDocType docType)
    {
        docType = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return DocTypes.TryGetValue(raw.Trim(), out docType);
    }

    private static string? DetectContentType(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith("%PDF-"u8))
        {
            return "application/pdf";
        }

        if (head.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
        {
            return "image/jpeg";
        }

        if (head.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        if (head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }

    private static EmploymentDocumentResponse ToResponse(EmploymentDocument row) => new(
        row.Id,
        row.DocType,
        row.FileName,
        row.ContentType,
        row.ByteSize,
        row.Note,
        row.CreatedAtUtc);

    private static IResult Forbidden(string error) =>
        Results.Json(new { error }, statusCode: StatusCodes.Status403Forbidden);

    private sealed record ShopStaff(Guid BranchId);
}

public sealed record EmploymentDocumentResponse(
    Guid Id,
    EmploymentDocType DocType,
    string FileName,
    string ContentType,
    long ByteSize,
    string? Note,
    DateTime CreatedAtUtc);
