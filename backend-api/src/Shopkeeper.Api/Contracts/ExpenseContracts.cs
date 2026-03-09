namespace Shopkeeper.Api.Contracts;

public sealed record CreateExpenseRequest(
    string Title,
    string Category,
    decimal Amount,
    DateTime ExpenseDateUtc,
    string? Notes);

public sealed record UpdateExpenseRequest(
    string? Title,
    string? Category,
    decimal? Amount,
    DateTime? ExpenseDateUtc,
    string? Notes,
    string? RowVersionBase64);

public sealed record ExpenseView(
    Guid Id,
    string Title,
    string Category,
    decimal Amount,
    DateTime ExpenseDateUtc,
    string? Notes,
    DateTime CreatedAtUtc,
    string RowVersionBase64);
