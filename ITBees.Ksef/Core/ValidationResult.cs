namespace ITBees.Ksef.Core;

public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors);