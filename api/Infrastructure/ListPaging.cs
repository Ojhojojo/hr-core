namespace HrCore.Infrastructure;

public static class ListPaging
{
    public const int DefaultTake = 50;
    public const int MaxTake = 200;

    public static (int Skip, int Take) Clamp(int? skip, int? take)
    {
        var s = skip.GetValueOrDefault();
        if (s < 0)
        {
            s = 0;
        }

        var t = take ?? DefaultTake;
        if (t < 1)
        {
            t = DefaultTake;
        }

        if (t > MaxTake)
        {
            t = MaxTake;
        }

        return (s, t);
    }
}
