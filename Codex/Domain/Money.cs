namespace Codex.Domain;

public sealed record Currency(string Code, string Symbol, string Name, int MinorUnits)
{
    public static readonly Currency Aud = new("AUD", "$", "Australian Dollar",  2);
    public static readonly Currency Brl = new("BRL", "R$", "Brazilian Real",    2);
    public static readonly Currency Cad = new("CAD", "$", "Canadian Dollar",    2);
    public static readonly Currency Chf = new("CHF", "Fr", "Swiss Franc",       2);
    public static readonly Currency Cny = new("CNY", "¥", "Chinese Yuan",       2);
    public static readonly Currency Eur = new("EUR", "€", "Euro",               2);
    public static readonly Currency Gbp = new("GBP", "£", "British Pound",      2);
    public static readonly Currency Hkd = new("HKD", "$", "Hong Kong Dollar",   2);
    public static readonly Currency Inr = new("INR", "₹", "Indian Rupee",       2);
    public static readonly Currency Jpy = new("JPY", "¥", "Japanese Yen",       0);
    public static readonly Currency Krw = new("KRW", "₩", "South Korean Won",   0);
    public static readonly Currency Mxn = new("MXN", "$", "Mexican Peso",       2);
    public static readonly Currency Nok = new("NOK", "kr", "Norwegian Krone",   2);
    public static readonly Currency Sek = new("SEK", "kr", "Swedish Krona",     2);
    public static readonly Currency Sgd = new("SGD", "$", "Singapore Dollar",   2);
    public static readonly Currency Usd = new("USD", "$", "US Dollar",          2);
    public static readonly Currency Zar = new("ZAR", "R", "South African Rand", 2);

    public static readonly IReadOnlyList<Currency> All = new[] {
        Aud, Brl, Cad, Chf, Cny, Eur, Gbp, Hkd, Inr,
        Jpy, Krw, Mxn, Nok, Sek, Sgd, Usd, Zar,
    };

    public static readonly IReadOnlyDictionary<string, Currency> ByCode =
        All.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);
}

public struct Money
{
    public Money(decimal amount, Currency currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public Money(double amount, Currency currency)
    {
        Amount = (decimal)amount;
        Currency = currency;
    }

    public decimal Amount { get; set; }
    public Currency Currency { get; init; }
    public readonly decimal Rounded()
    {
        return decimal.Round(Amount, Currency.MinorUnits, MidpointRounding.ToEven);
    }
    public readonly override string ToString() => $"{Currency.Symbol} {Rounded()}";
}
