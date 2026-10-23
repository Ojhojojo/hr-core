using System.Text.Json;

// The smallest host shared HR accepts: an Owner/Admin role enum and the HrRoles and
// StaffAccessFlags answers. A real app (Rakkii, Qurio) provides its own versions of these.
namespace HrCore.Domain
{
    public enum StaffRole
    {
        Owner,
        Admin,
        Worker
    }
}

namespace HrCore.Features.Staff
{
    public static partial class HrRoles
    {
        public static partial bool IsDesk(StaffRole role) => role is StaffRole.Owner or StaffRole.Admin;

        public static partial bool ClocksByDefault(StaffRole role) => role != StaffRole.Owner;

        public static partial bool SeesOnlySelfInStaffList(StaffRole role) => false;

        public static partial StaffRole UnknownPersonRole() => StaffRole.Worker;
    }

    public static partial class StaffAccessFlags
    {
        public static partial void ApplyOnCreate(Domain.Staff staff, IReadOnlyDictionary<string, JsonElement>? requested)
        {
        }

        public static partial void ApplyOnUpdate(Domain.Staff staff, IReadOnlyDictionary<string, JsonElement>? requested)
        {
        }

        public static partial Dictionary<string, object?>? Describe(Domain.Staff staff) => null;
    }
}
