namespace ScreenFlow.Domain;

/// <summary>Base type for rule violations the API maps to a typed error code.</summary>
public abstract class DomainException(string message) : Exception(message);

/// <summary>The state machine forbids the requested change (CONTRACT.md §5). Maps to INVALID_TRANSITION.</summary>
public sealed class InvalidTransitionException(string message) : DomainException(message);

/// <summary>A search id that does not belong to the order. Maps to NOT_FOUND.</summary>
public sealed class SearchNotFoundException(Guid orderId, Guid searchId)
    : DomainException($"Search {searchId} was not found on order {orderId}.");
