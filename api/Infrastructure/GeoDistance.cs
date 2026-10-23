namespace HrCore.Infrastructure;

public static class GeoDistance
{
    /// <summary>
    /// Missing GPS (both coordinates null) or a point outside the shop radius.
    /// Invalid coordinates and a missing shop pin still throw, same as <see cref="EnsureInside"/>.
    /// </summary>
    public static bool IsOffsite(Branch branch, double? latitude, double? longitude)
    {
        if (latitude is null && longitude is null)
        {
            return true;
        }

        if (latitude is not double lat || longitude is not double lng)
        {
            throw new InvalidOperationException("Send both latitude and longitude.");
        }

        if (!double.IsFinite(lat) || !double.IsFinite(lng) || lat is < -90 or > 90 || lng is < -180 or > 180)
        {
            throw new InvalidOperationException("That location is not valid.");
        }

        if (branch.Latitude is not double pinLat || branch.Longitude is not double pinLng)
        {
            throw new InvalidOperationException(
                "Set the shop map pin in Settings before clocking in from a phone.");
        }

        var metres = Metres(pinLat, pinLng, lat, lng);
        var radius = branch.ClockRadiusMeters > 0
            ? branch.ClockRadiusMeters
            : Branch.DefaultClockRadiusMeters;
        return metres > radius;
    }

    /// <summary>
    /// Phone self-punch must send a point inside the shop pin and radius.
    /// Missing coordinates are rejected. Desk kiosk and punch-for do not call this.
    /// </summary>
    public static void EnsureInside(
        Branch branch,
        double? latitude,
        double? longitude,
        string missingGpsMessage = "Turn on location to clock in from your phone.",
        string action = "clock-in")
    {
        if (latitude is null && longitude is null)
        {
            throw new InvalidOperationException(missingGpsMessage);
        }

        if (latitude is not double lat || longitude is not double lng)
        {
            throw new InvalidOperationException("Send both latitude and longitude.");
        }

        if (!double.IsFinite(lat) || !double.IsFinite(lng) || lat is < -90 or > 90 || lng is < -180 or > 180)
        {
            throw new InvalidOperationException("That location is not valid.");
        }

        if (branch.Latitude is not double pinLat || branch.Longitude is not double pinLng)
        {
            throw new InvalidOperationException(
                "Set the shop map pin in Settings before clocking in from a phone.");
        }

        var metres = Metres(pinLat, pinLng, lat, lng);
        var radius = branch.ClockRadiusMeters > 0
            ? branch.ClockRadiusMeters
            : Branch.DefaultClockRadiusMeters;
        if (metres > radius)
        {
            throw new InvalidOperationException(
                $"You're not at the shop ({Math.Round(metres)} m away). Phone {action} must be within {radius} m.");
        }
    }

    public static double Metres(double lat1, double lon1, double lat2, double lon2)
    {
        const double earth = 6_371_000d;
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earth * c;
    }

    private static double ToRad(double degrees) => degrees * Math.PI / 180d;
}
