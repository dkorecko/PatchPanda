namespace PatchPanda.Web.Exceptions;

public class RateLimitException(DateTimeOffset resetsAt, int limit) : Exception
{
    public DateTimeOffset ResetsAt { get; } = resetsAt;

    public int Limit { get; } = limit;
}
