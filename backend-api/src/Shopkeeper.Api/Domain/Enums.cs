namespace Shopkeeper.Api.Domain;

public enum MembershipRole
{
    Owner = 1,
    Staff = 2
}

public enum ItemConditionGrade
{
    A = 1,
    B = 2,
    C = 3
}

public enum ItemType
{
    New = 1,
    Used = 2
}

public enum PaymentMethod
{
    Cash = 1,
    BankTransfer = 2,
    Pos = 3
}

public enum SaleStatus
{
    Completed = 1,
    PartiallyPaid = 2,
    Void = 3
}

public enum CreditStatus
{
    Open = 1,
    Settled = 2,
    Defaulted = 3
}

public enum SyncOperation
{
    Create = 1,
    Update = 2,
    Delete = 3
}

public enum SyncStatus
{
    Accepted = 1,
    Conflict = 2
}
