namespace BusinessManagement.Domain.Billing;

public static class ReturnRules
{
    /// <summary>
    /// Pro-rata share of a sold line's amount for a returned quantity, to the paisa. When the return takes back everything
    /// still outstanding on the line, it gets the exact REMAINDER so that all returns together never exceed (or fall short of)
    /// the original line amount, whatever the rounding.
    /// </summary>
    public static decimal Prorate(decimal lineAmount, decimal soldQty, decimal returnQty, decimal alreadyReturnedQty, decimal alreadyReturnedAmount)
    {
        var remainingQty = soldQty - alreadyReturnedQty;
        if (returnQty <= 0 || returnQty > remainingQty) throw new ArgumentOutOfRangeException(nameof(returnQty));
        if (returnQty == remainingQty) return lineAmount - alreadyReturnedAmount;
        return SalesCalculator.Round(lineAmount * returnQty / soldQty);
    }
}
