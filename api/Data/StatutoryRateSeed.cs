using HrCore.Features.Payroll;

namespace HrCore.Data;

/// <summary>
/// Fixed ids and current-pack values seeded when <see cref="HrCoreFeatures.StatutoryRates"/> is on.
/// A host's additive migration copies these rows. Changing a government table later is a data
/// update, not a code release.
/// </summary>
public static class StatutoryRateSeed
{
    public static readonly Guid Sss2024Id = Guid.Parse("11111111-aaaa-4111-8111-000000000001");
    public static readonly Guid Sss2025Id = Guid.Parse("11111111-aaaa-4111-8111-000000000002");
    public static readonly Guid PhilHealth2024Id = Guid.Parse("22222222-aaaa-4222-8222-000000000001");
    public static readonly Guid PagIbig2024JanId = Guid.Parse("33333333-aaaa-4333-8333-000000000001");
    public static readonly Guid PagIbig2024FebId = Guid.Parse("33333333-aaaa-4333-8333-000000000002");
    public static readonly Guid Wht2024Id = Guid.Parse("44444444-aaaa-4444-8444-000000000001");

    public static readonly Guid WhtBracket1Id = Guid.Parse("55555555-aaaa-4555-8555-000000000001");
    public static readonly Guid WhtBracket2Id = Guid.Parse("55555555-aaaa-4555-8555-000000000002");
    public static readonly Guid WhtBracket3Id = Guid.Parse("55555555-aaaa-4555-8555-000000000003");
    public static readonly Guid WhtBracket4Id = Guid.Parse("55555555-aaaa-4555-8555-000000000004");
    public static readonly Guid WhtBracket5Id = Guid.Parse("55555555-aaaa-4555-8555-000000000005");

    public static readonly DateOnly From2024 = new(2024, 1, 1);
    public static readonly DateOnly PagIbigCapRaisedOn = new(2024, 2, 1);
    public static readonly DateOnly From2025 = new(2025, 1, 1);

    public static StatutoryRate[] Rates() =>
    [
        new()
        {
            Id = Sss2024Id,
            Kind = StatutoryRateKind.Sss,
            EffectiveFrom = From2024,
            MscFloor = StatutoryPack.SssMscFloor2024,
            MscCeiling = StatutoryPack.SssMscCeiling2024,
            MscStep = StatutoryPack.SssMscStep,
            EmployeeRate = StatutoryPack.SssEmployeeRate2024,
            EmployerRate = StatutoryPack.SssEmployerRate2024,
            EcLow = StatutoryPack.SssEcLow,
            EcHigh = StatutoryPack.SssEcHigh,
            EcMscCeilingForLow = StatutoryPack.SssEcMscCeilingForLow
        },
        new()
        {
            Id = Sss2025Id,
            Kind = StatutoryRateKind.Sss,
            EffectiveFrom = From2025,
            MscFloor = StatutoryPack.SssMscFloor2025,
            MscCeiling = StatutoryPack.SssMscCeiling2025,
            MscStep = StatutoryPack.SssMscStep,
            EmployeeRate = StatutoryPack.SssEmployeeRate2025,
            EmployerRate = StatutoryPack.SssEmployerRate2025,
            EcLow = StatutoryPack.SssEcLow,
            EcHigh = StatutoryPack.SssEcHigh,
            EcMscCeilingForLow = StatutoryPack.SssEcMscCeilingForLow
        },
        new()
        {
            Id = PhilHealth2024Id,
            Kind = StatutoryRateKind.PhilHealth,
            EffectiveFrom = From2024,
            Floor = StatutoryPack.PhilHealthFloor,
            Ceiling = StatutoryPack.PhilHealthCeiling,
            EmployeeRate = StatutoryPack.PhilHealthEmployeeRate2024,
            EmployerRate = StatutoryPack.PhilHealthEmployerRate2024
        },
        new()
        {
            Id = PagIbig2024JanId,
            Kind = StatutoryRateKind.PagIbig,
            EffectiveFrom = From2024,
            SmallSalary = StatutoryPack.PagIbigSmallSalary,
            RateSmall = StatutoryPack.PagIbigRateSmall,
            Rate = StatutoryPack.PagIbigRate,
            Cap = StatutoryPack.PagIbigCapBeforeRaise,
            EmployerRate = StatutoryPack.PagIbigEmployerRate
        },
        new()
        {
            Id = PagIbig2024FebId,
            Kind = StatutoryRateKind.PagIbig,
            EffectiveFrom = PagIbigCapRaisedOn,
            SmallSalary = StatutoryPack.PagIbigSmallSalary,
            RateSmall = StatutoryPack.PagIbigRateSmall,
            Rate = StatutoryPack.PagIbigRate,
            Cap = StatutoryPack.PagIbigCap,
            EmployerRate = StatutoryPack.PagIbigEmployerRate
        },
        new()
        {
            Id = Wht2024Id,
            Kind = StatutoryRateKind.Wht,
            EffectiveFrom = From2024,
            ExemptMonthly = StatutoryPack.WhtExemptMonthly
        }
    ];

    public static StatutoryTaxBracket[] Brackets() =>
    [
        new()
        {
            Id = WhtBracket1Id,
            StatutoryRateId = Wht2024Id,
            SortOrder = 1,
            UpTo = 33_333m,
            BaseTax = 0m,
            Rate = 0.20m,
            ExcessOver = StatutoryPack.WhtExemptMonthly
        },
        new()
        {
            Id = WhtBracket2Id,
            StatutoryRateId = Wht2024Id,
            SortOrder = 2,
            UpTo = 66_667m,
            BaseTax = 2_500m,
            Rate = 0.25m,
            ExcessOver = 33_333m
        },
        new()
        {
            Id = WhtBracket3Id,
            StatutoryRateId = Wht2024Id,
            SortOrder = 3,
            UpTo = 166_667m,
            BaseTax = 10_833.33m,
            Rate = 0.30m,
            ExcessOver = 66_667m
        },
        new()
        {
            Id = WhtBracket4Id,
            StatutoryRateId = Wht2024Id,
            SortOrder = 4,
            UpTo = 666_667m,
            BaseTax = 40_833.33m,
            Rate = 0.32m,
            ExcessOver = 166_667m
        },
        new()
        {
            Id = WhtBracket5Id,
            StatutoryRateId = Wht2024Id,
            SortOrder = 5,
            UpTo = null,
            BaseTax = 200_833.33m,
            Rate = 0.35m,
            ExcessOver = 666_667m
        }
    ];
}
