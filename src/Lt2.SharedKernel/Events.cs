namespace Lt2.SharedKernel;

/// <summary>Something that happened inside one module.</summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}

/// <summary>An event another module may react to. Written to the outbox in the same transaction as the change.</summary>
public interface IIntegrationEvent
{
    Guid Id { get; }

    DateTimeOffset OccurredAt { get; }
}
