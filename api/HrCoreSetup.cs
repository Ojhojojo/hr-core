using HrCore.Features.Announcements;
using HrCore.Features.Attendance;
using HrCore.Features.EmployeeDirectory;
using HrCore.Features.EmploymentDocuments;
using HrCore.Features.HrChangeLog;
using HrCore.Features.Leave;
using HrCore.Features.Payroll;
using HrCore.Features.SettingsLog;
using HrCore.Features.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HrCore;

/// <summary>Wires shared HR into a host app. Call both from Program.cs.</summary>
public static class HrCoreSetup
{
    /// <summary>
    /// Registers HR services. <typeparamref name="TDb"/> is the app's DbContext; it must implement
    /// <see cref="IHrDb"/> and call <see cref="HrModel.Configure"/>. The app also registers
    /// <see cref="IStaffInvite"/>, <see cref="IFileStore"/>, <see cref="IHrFileStore"/> and
    /// <see cref="DemoBranchOptions"/>; <see cref="IStaffWorkHooks"/> is optional.
    /// </summary>
    public static IServiceCollection AddHrCore<TDb>(this IServiceCollection services)
        where TDb : DbContext, IHrDb
    {
        services.AddScoped<IHrDb>(sp => sp.GetRequiredService<TDb>());
        services.AddScoped<StaffAuthorizationFilter>();
        services.AddScoped<BranchResolver>();
        services.AddScoped<SettingsChangeLogService>();
        services.AddScoped<HrChangeLogService>();
        services.AddScoped<AnnouncementService>();
        services.AddScoped<AttendanceService>();
        services.AddScoped<OvertimeService>();
        services.AddScoped<PayrollService>();
        services.AddScoped<PayrollRateService>();
        services.AddScoped<PayrollRunService>();
        services.AddScoped<Bir2316Service>();
        services.TryAddScoped<IStaffWorkHooks, NoStaffWorkHooks>();
        return services;
    }

    /// <summary>Maps the HR API routes (/api/staff, /api/employee-directory, /api/leave, ...).</summary>
    public static IEndpointRouteBuilder MapHrCore(this IEndpointRouteBuilder app)
    {
        app.MapStaffApi();
        app.MapEmployeeDirectoryApi();
        app.MapEmploymentDocumentApi();
        app.MapLeaveApi();
        app.MapAttendanceApi();
        app.MapPayrollApi();
        app.MapAnnouncementApi();
        app.MapHrChangeLogApi();
        return app;
    }
}
