namespace Shopkeeper.Api.Contracts;

public sealed record PagedResponse<T>(
    int Total,
    int Page,
    int Limit,
    IReadOnlyList<T> Items)
{
    public int TotalPages => Limit > 0 ? (int)Math.Ceiling((double)Total / Limit) : 0;
}
