using System.Collections.Generic;

public static class RacerRegistry
{
    private static readonly List<RacerMotor> racers = new();

    public static IReadOnlyList<RacerMotor> Racers => racers;

    public static void Register(RacerMotor racer)
    {
        if (racer != null && !racers.Contains(racer))
        {
            racers.Add(racer);
        }
    }

    public static void Unregister(RacerMotor racer)
    {
        if (racer != null)
        {
            racers.Remove(racer);
        }
    }
}