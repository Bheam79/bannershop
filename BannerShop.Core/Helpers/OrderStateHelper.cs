using BannerShop.Core.Enums;

namespace BannerShop.Core.Helpers;

/// <summary>
/// Pure helper that defines the valid lifecycle state sequences per order type.
/// </summary>
public static class OrderStateHelper
{
    // Pre-built sequences — one per OrderType.
    private static readonly IReadOnlyList<OrderState> CustomBannerSequence = new[]
    {
        OrderState.Draft,
        OrderState.Paid,
        OrderState.InProduction,
        OrderState.Shipped,
        OrderState.Delivered
    };

    private static readonly IReadOnlyList<OrderState> AiBannerSequence = new[]
    {
        OrderState.Draft,
        OrderState.Paid,
        OrderState.CustomerApproval,
        OrderState.InProduction,
        OrderState.Shipped,
        OrderState.Delivered
    };

    private static readonly IReadOnlyList<OrderState> ManualDesignSequence = new[]
    {
        OrderState.Draft,
        OrderState.Paid,
        OrderState.DesignReady,
        OrderState.CustomerApproval,
        OrderState.InProduction,
        OrderState.Shipped,
        OrderState.Delivered
    };

    /// <summary>
    /// Returns the ordered list of <see cref="OrderState"/> values that are valid for
    /// the given <paramref name="orderType"/>, from initial to terminal.
    /// </summary>
    public static IReadOnlyList<OrderState> ValidSequence(OrderType orderType) => orderType switch
    {
        OrderType.CustomBanner  => CustomBannerSequence,
        OrderType.AiBanner      => AiBannerSequence,
        OrderType.ManualDesign  => ManualDesignSequence,
        _                       => CustomBannerSequence
    };

    /// <summary>
    /// Returns <c>true</c> when <paramref name="next"/> is the immediate successor of
    /// <paramref name="current"/> in the sequence for <paramref name="orderType"/>.
    /// <see cref="OrderState.Cancelled"/> is always a valid next state from any non-terminal state.
    /// </summary>
    public static bool IsValidTransition(OrderType orderType, OrderState current, OrderState next)
    {
        // Cancellation is always allowed from any non-terminal (non-Delivered, non-Cancelled) state.
        if (next == OrderState.Cancelled
            && current != OrderState.Delivered
            && current != OrderState.Cancelled)
        {
            return true;
        }

        var seq = ValidSequence(orderType);
        for (var i = 0; i < seq.Count - 1; i++)
        {
            if (seq[i] == current)
                return seq[i + 1] == next;
        }
        return false;
    }
}
