namespace PatchPanda.Web.Exceptions;

public class RateLimitException(DateTimeOffset resetsAt) : Exception
{
    public DateTimeOffset ResetsAt { get; } = resetsAt;
}
