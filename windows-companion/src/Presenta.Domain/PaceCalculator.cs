namespace Presenta.Domain;

/// <summary>Outcome of a pace evaluation.</summary>
/// <param name="Status">Classification of the pace.</param>
/// <param name="ExpectedSlide">Expected slide at this time (linear interpolation, 1..totalSlides).</param>
/// <param name="Deviation">Actual slide minus expected slide. Negative means behind.</param>
/// <param name="Tolerance">Tolerance in slides used for the classification.</param>
public readonly record struct PaceResult(PaceStatus Status, double ExpectedSlide, double Deviation, int Tolerance);

/// <summary>Pure pace calculations.</summary>
public static class PaceCalculator
{
    /// <summary>
    /// Expected slide is 1 at time zero and totalSlides at the planned duration, interpolated linearly and
    /// clamped afterwards. Within the tolerance (inclusive) the pace is on track.
    /// During the warm-up period the status is <see cref="PaceStatus.NotEnoughData"/>.
    /// </summary>
    public static PaceResult Evaluate(SessionOptions options, int currentSlide, int totalSlides, TimeSpan elapsed, TimeSpan planned)
    {
        var tolerance = options.GetPaceTolerance(totalSlides);
        var expected = ExpectedSlide(totalSlides, elapsed, planned);
        var deviation = currentSlide - expected;

        if (elapsed < options.GetWarmUp(planned))
        {
            return new PaceResult(PaceStatus.NotEnoughData, expected, deviation, tolerance);
        }

        var status = deviation < -tolerance ? PaceStatus.Behind
            : deviation > tolerance ? PaceStatus.TooFast
            : PaceStatus.OnTrack;
        return new PaceResult(status, expected, deviation, tolerance);
    }

    public static double ExpectedSlide(int totalSlides, TimeSpan elapsed, TimeSpan planned)
    {
        if (totalSlides <= 1 || planned <= TimeSpan.Zero)
        {
            return 1;
        }

        var fraction = Math.Clamp(elapsed.TotalMilliseconds / planned.TotalMilliseconds, 0.0, 1.0);
        return 1 + (totalSlides - 1) * fraction;
    }

    /// <summary>Registered slides per minute of elapsed time; 0 below the minimum elapsed time.</summary>
    public static double AverageSlidesPerMinute(SessionOptions options, int registeredSlides, TimeSpan elapsed)
    {
        if (elapsed < options.MinimumElapsedForAveragePace || elapsed <= TimeSpan.Zero)
        {
            return 0;
        }

        return registeredSlides / elapsed.TotalMinutes;
    }
}
