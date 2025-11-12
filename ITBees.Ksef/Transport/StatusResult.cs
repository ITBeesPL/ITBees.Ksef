using ITBees.Ksef.Core;

namespace ITBees.Ksef.Transport;

public sealed record StatusResult(
    string Status,
    string? KsefNumber,
    string? RejectionReason,
    DateTimeOffset? UpoAvailableAt);