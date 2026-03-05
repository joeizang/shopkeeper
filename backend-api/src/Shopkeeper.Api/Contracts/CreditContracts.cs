using Shopkeeper.Api.Domain;

namespace Shopkeeper.Api.Contracts;

public sealed record CreditAccountView(
    Guid Id,
    Guid SaleId,
    DateTime DueDateUtc,
    decimal OutstandingAmount,
    CreditStatus Status);

public sealed record CreditRepaymentRequest(decimal Amount, PaymentMethod Method, string? Reference, string? Notes);
