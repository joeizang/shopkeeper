using NodaTime;
using Shopkeeper.Api.Domain;

namespace Shopkeeper.Api.Contracts;

public sealed record CreditAccountView(
    Guid Id,
    Guid SaleId,
    Instant DueDateUtc,
    decimal OutstandingAmount,
    CreditStatus Status);

public sealed record CreditRepaymentView(
    Guid Id,
    decimal Amount,
    PaymentMethod Method,
    string? Reference,
    string? Notes,
    Instant CreatedAtUtc);

public sealed record CreditDetailResponse(
    CreditAccountView Account,
    IReadOnlyList<CreditRepaymentView> Repayments);

public sealed record CreditRepaymentRequest(decimal Amount, PaymentMethod Method, string? Reference, string? Notes, string? ClientRequestId = null);
