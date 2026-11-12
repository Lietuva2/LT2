namespace Lt2.SharedKernel;

/// <summary>Identifiers for our own rows: UUID version 7, time ordered.</summary>
public static class Ids
{
    public static Guid New(TimeProvider clock) => Guid.CreateVersion7(clock.GetUtcNow());
}
