using Codex.Domain;

Console.WriteLine("CODEX\n");

foreach (var cur in Currency.All)
{
    var rand = new Random();
    var whole = rand.Next(Int32.MaxValue);
    var frac = (decimal)rand.NextDouble();
    frac = decimal.Round(frac, 3);
    var amt = whole + frac;
    var money = new Money(amt, cur);
    Console.WriteLine($"{money} is rounded. Non-rounded is {money.Amount}. {money.Currency.Name} has {money.Currency.MinorUnits} minor units.");
}

// CODEX
//
// $ 1045854876.61 is rounded.	    Non-rounded is 1045854876.613.	Australian Dollar has 2 minor units.
// R$ 1652107942.16 is rounded.	    Non-rounded is 1652107942.159.	Brazilian Real has 2 minor units.
// $ 1972781994.69 is rounded.	    Non-rounded is 1972781994.692.	Canadian Dollar has 2 minor units.
// Fr 1769913437.32 is rounded.	    Non-rounded is 1769913437.318.	Swiss Franc has 2 minor units.
// ¥ 12140443.75 is rounded.	    Non-rounded is 12140443.748.	Chinese Yuan has 2 minor units.
// € 78004995.26 is rounded.	    Non-rounded is 78004995.256.	Euro has 2 minor units.
// £ 1842634875.76 is rounded.	    Non-rounded is 1842634875.761.	British Pound has 2 minor units.
// $ 783458843.87 is rounded.	    Non-rounded is 783458843.867.	Hong Kong Dollar has 2 minor units.
// ₹ 1874686628.54 is rounded.	    Non-rounded is 1874686628.541.	Indian Rupee has 2 minor units.
// ¥ 299638036 is rounded.	        Non-rounded is 299638035.719.	Japanese Yen has 0 minor units.
// ₩ 2001528333 is rounded.	        Non-rounded is 2001528333.328.	South Korean Won has 0 minor units.
// $ 455542418.05 is rounded.	    Non-rounded is 455542418.054.	Mexican Peso has 2 minor units.
// kr 938641650.13 is rounded.	    Non-rounded is 938641650.129.	Norwegian Krone has 2 minor units.
// kr 1603207659.53 is rounded.	    Non-rounded is 1603207659.534.	Swedish Krona has 2 minor units.
// $ 74702435.00 is rounded.	    Non-rounded is 74702435.005.	Singapore Dollar has 2 minor units.
// $ 1933261026.16 is rounded.	    Non-rounded is 1933261026.157.	US Dollar has 2 minor units.
// R 890022032.68 is rounded.	    Non-rounded is 890022032.683.	South African Rand has 2 minor units.

// Notes
// Singapore Dollar rounds 0.005 -> 0.00 because round-to-even strategy is being used.
// Tabs added manually for readability.
