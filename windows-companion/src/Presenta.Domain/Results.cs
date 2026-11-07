namespace Presenta.Domain;

/// <summary>Result of an operation that returns no value. No exceptions are used for expected failures.</summary>
public readonly record struct OperationResult(SessionErrorCode? Error)
{
    public bool Succeeded => Error is null;

    public static OperationResult Success => new(null);

    public static OperationResult Failure(SessionErrorCode error) => new(error);
}

/// <summary>Result of an operation that returns a value on success.</summary>
public readonly record struct Result<T>(T? Value, SessionErrorCode? Error)
{
    public bool Succeeded => Error is null;

    public static Result<T> Success(T value) => new(value, null);

    public static Result<T> Failure(SessionErrorCode error) => new(default, error);
}

/// <summary>
/// Result of a slide command. <see cref="CounterChanged"/> tells the caller whether the slide counter actually moved;
/// the companion sends a key to the presentation only when it did.
/// </summary>
public sealed record SlideResult(bool Succeeded, bool CounterChanged, int CurrentSlide, SessionErrorCode? Error)
{
    public static SlideResult Ok(bool changed, int currentSlide) => new(true, changed, currentSlide, null);

    public static SlideResult Failure(SessionErrorCode error, int currentSlide) => new(false, false, currentSlide, error);
}
