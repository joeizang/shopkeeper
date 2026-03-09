using NodaTime;

namespace Shopkeeper.Api.Contracts;

public sealed record CreateExpenseRequest(
    string Title,
    string Category,
    decimal Amount,
    Instant ExpenseDateUtc,
    string? Notes);

public sealed record UpdateExpenseRequest(
    string? Title,
    string? Category,
    decimal? Amount,
    Instant? ExpenseDateUtc,
    string? Notes,
    string? RowVersionBase64);

public sealed record ExpenseView(
    Guid Id,
    string Title,
    string Category,
    decimal Amount,
    Instant ExpenseDateUtc,
    string? Notes,
    Instant CreatedAtUtc,
    string RowVersionBase64);
