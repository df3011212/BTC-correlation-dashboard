namespace TradingViewWebhookDashboard.Services;

public static class CorrelationMath
{
    public static double CalculatePearson(IReadOnlyList<decimal> left, IReadOnlyList<decimal> right)
    {
        if (left.Count != right.Count || left.Count < 2)
        {
            return double.NaN;
        }

        var count = left.Count;
        double sumLeft = 0d;
        double sumRight = 0d;
        double sumLeftSquared = 0d;
        double sumRightSquared = 0d;
        double sumProduct = 0d;

        for (var index = 0; index < count; index++)
        {
            var leftValue = decimal.ToDouble(left[index]);
            var rightValue = decimal.ToDouble(right[index]);

            sumLeft += leftValue;
            sumRight += rightValue;
            sumLeftSquared += leftValue * leftValue;
            sumRightSquared += rightValue * rightValue;
            sumProduct += leftValue * rightValue;
        }

        var numerator = (count * sumProduct) - (sumLeft * sumRight);
        var denominatorLeft = (count * sumLeftSquared) - (sumLeft * sumLeft);
        var denominatorRight = (count * sumRightSquared) - (sumRight * sumRight);

        if (denominatorLeft <= 0d || denominatorRight <= 0d)
        {
            return double.NaN;
        }

        return numerator / Math.Sqrt(denominatorLeft * denominatorRight);
    }
}
