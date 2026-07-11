namespace NovaTechCRM.Domain.ValueObjects;

// Value object — immutable, equality by value not reference
// Added in v2.3 — older code still uses raw decimal everywhere
// TODO: migrate Order.TotalAmount and Invoice.TotalAmount to use this (NOVA-60)
public readonly struct Money : IEquatable<Money>, IComparable<Money>
{
    public decimal Amount { get; }
    public string Currency { get; }

    public static readonly Money Zero = new(0, "USD");

    public Money(decimal amount, string currency = "USD")
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency cannot be empty.", nameof(currency));

        Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        Currency = currency.ToUpperInvariant();
    }

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    public Money ApplyDiscount(decimal discountPercent)
    {
        if (discountPercent < 0 || discountPercent > 100)
            throw new ArgumentOutOfRangeException(nameof(discountPercent));
        return new Money(Amount * (1 - discountPercent), Currency);
    }

    public bool IsPositive => Amount > 0;
    public bool IsZero => Amount == 0;
    public bool IsNegative => Amount < 0;

    private void EnsureSameCurrency(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException(
                $"Cannot operate on Money values with different currencies: {Currency} vs {other.Currency}");
    }

    public static Money operator +(Money a, Money b) => a.Add(b);
    public static Money operator -(Money a, Money b) => a.Subtract(b);
    public static Money operator *(Money a, decimal factor) => a.Multiply(factor);
    public static bool operator ==(Money a, Money b) => a.Equals(b);
    public static bool operator !=(Money a, Money b) => !a.Equals(b);
    public static bool operator >(Money a, Money b) => a.CompareTo(b) > 0;
    public static bool operator <(Money a, Money b) => a.CompareTo(b) < 0;
    public static bool operator >=(Money a, Money b) => a.CompareTo(b) >= 0;
    public static bool operator <=(Money a, Money b) => a.CompareTo(b) <= 0;

    public int CompareTo(Money other)
    {
        EnsureSameCurrency(other);
        return Amount.CompareTo(other.Amount);
    }

    public bool Equals(Money other) =>
        Amount == other.Amount && Currency == other.Currency;

    public override bool Equals(object? obj) =>
        obj is Money other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Amount, Currency);

    public override string ToString() => $"{Amount:F2} {Currency}";

    // parse from string like "49.99 USD" — used in import jobs
    public static Money Parse(string value)
    {
        var parts = value.Trim().Split(' ');
        if (parts.Length != 2)
            throw new FormatException($"Cannot parse Money from '{value}'");
        return new Money(decimal.Parse(parts[0]), parts[1]);
    }
}
