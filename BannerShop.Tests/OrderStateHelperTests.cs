using BannerShop.Core.Enums;
using BannerShop.Core.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Unit tests for <see cref="OrderStateHelper"/> — the pure per-order-type lifecycle
/// state machine that <c>AdminOrderService.UpdateStateAsync</c>, the AI pipeline
/// (<c>AiGenerationPipeline</c> → CustomerApproval) and <c>DesignRequestService</c>
/// (→ InProduction) all gate real transitions on.
/// </summary>
public class OrderStateHelperTests
{
    // ── ValidSequence ────────────────────────────────────────────────────────

    [Fact]
    public void ValidSequence_CustomBanner_HasNoDesignOrApprovalSteps()
    {
        OrderStateHelper.ValidSequence(OrderType.CustomBanner).Should().Equal(
            OrderState.Draft,
            OrderState.Paid,
            OrderState.InProduction,
            OrderState.Shipped,
            OrderState.Delivered);
    }

    [Fact]
    public void ValidSequence_AiBanner_InsertsCustomerApprovalBeforeProduction()
    {
        OrderStateHelper.ValidSequence(OrderType.AiBanner).Should().Equal(
            OrderState.Draft,
            OrderState.Paid,
            OrderState.CustomerApproval,
            OrderState.InProduction,
            OrderState.Shipped,
            OrderState.Delivered);
    }

    [Fact]
    public void ValidSequence_ManualDesign_InsertsDesignReadyThenCustomerApproval()
    {
        OrderStateHelper.ValidSequence(OrderType.ManualDesign).Should().Equal(
            OrderState.Draft,
            OrderState.Paid,
            OrderState.DesignReady,
            OrderState.CustomerApproval,
            OrderState.InProduction,
            OrderState.Shipped,
            OrderState.Delivered);
    }

    [Fact]
    public void ValidSequence_CreditPack_StopsAtPaid()
    {
        OrderStateHelper.ValidSequence(OrderType.CreditPack).Should().Equal(
            OrderState.Draft,
            OrderState.Paid);
    }

    [Fact]
    public void ValidSequence_UnknownOrderType_FallsBackToCustomBanner()
    {
        // Defensive default branch: an out-of-range enum value maps to the CustomBanner path.
        OrderStateHelper.ValidSequence((OrderType)99)
            .Should().Equal(OrderStateHelper.ValidSequence(OrderType.CustomBanner));
    }

    [Fact]
    public void ValidSequence_EveryType_StartsAtDraft()
    {
        foreach (OrderType type in Enum.GetValues<OrderType>())
            OrderStateHelper.ValidSequence(type)[0].Should().Be(OrderState.Draft);
    }

    // ── IsValidTransition: forward steps ─────────────────────────────────────

    [Theory]
    [InlineData(OrderState.Draft,        OrderState.Paid)]
    [InlineData(OrderState.Paid,         OrderState.InProduction)]
    [InlineData(OrderState.InProduction, OrderState.Shipped)]
    [InlineData(OrderState.Shipped,      OrderState.Delivered)]
    public void IsValidTransition_CustomBanner_AllowsEachForwardStep(OrderState current, OrderState next)
    {
        OrderStateHelper.IsValidTransition(OrderType.CustomBanner, current, next).Should().BeTrue();
    }

    [Theory]
    [InlineData(OrderState.Paid,             OrderState.CustomerApproval)]
    [InlineData(OrderState.CustomerApproval, OrderState.InProduction)]
    public void IsValidTransition_AiBanner_AllowsApprovalStep(OrderState current, OrderState next)
    {
        OrderStateHelper.IsValidTransition(OrderType.AiBanner, current, next).Should().BeTrue();
    }

    [Fact]
    public void IsValidTransition_ManualDesign_AllowsPaidToDesignReady()
    {
        OrderStateHelper.IsValidTransition(
            OrderType.ManualDesign, OrderState.Paid, OrderState.DesignReady).Should().BeTrue();
    }

    // ── IsValidTransition: rejected steps ────────────────────────────────────

    [Fact]
    public void IsValidTransition_CustomBanner_RejectsSkippingProduction()
    {
        // CustomBanner has no CustomerApproval state — Paid must go straight to InProduction.
        OrderStateHelper.IsValidTransition(
            OrderType.CustomBanner, OrderState.Paid, OrderState.CustomerApproval).Should().BeFalse();
    }

    [Fact]
    public void IsValidTransition_AiBanner_RejectsSkippingCustomerApproval()
    {
        // AI must pass through CustomerApproval — Paid → InProduction is not a single valid step.
        OrderStateHelper.IsValidTransition(
            OrderType.AiBanner, OrderState.Paid, OrderState.InProduction).Should().BeFalse();
    }

    [Fact]
    public void IsValidTransition_RejectsBackwardStep()
    {
        OrderStateHelper.IsValidTransition(
            OrderType.CustomBanner, OrderState.Shipped, OrderState.InProduction).Should().BeFalse();
    }

    [Fact]
    public void IsValidTransition_RejectsStayingInSameState()
    {
        OrderStateHelper.IsValidTransition(
            OrderType.CustomBanner, OrderState.Paid, OrderState.Paid).Should().BeFalse();
    }

    [Fact]
    public void IsValidTransition_FromTerminalDelivered_RejectsEverything()
    {
        foreach (OrderState next in Enum.GetValues<OrderState>())
            OrderStateHelper.IsValidTransition(OrderType.CustomBanner, OrderState.Delivered, next)
                .Should().BeFalse($"Delivered is terminal but allowed -> {next}");
    }

    // ── IsValidTransition: cancellation ──────────────────────────────────────

    [Theory]
    [InlineData(OrderState.Draft)]
    [InlineData(OrderState.Paid)]
    [InlineData(OrderState.CustomerApproval)]
    [InlineData(OrderState.InProduction)]
    [InlineData(OrderState.Shipped)]
    public void IsValidTransition_CancelAllowedFromAnyNonTerminalState(OrderState current)
    {
        OrderStateHelper.IsValidTransition(OrderType.AiBanner, current, OrderState.Cancelled)
            .Should().BeTrue();
    }

    [Fact]
    public void IsValidTransition_CancelRejectedFromDelivered()
    {
        OrderStateHelper.IsValidTransition(
            OrderType.CustomBanner, OrderState.Delivered, OrderState.Cancelled).Should().BeFalse();
    }

    [Fact]
    public void IsValidTransition_CancelRejectedFromCancelled()
    {
        // Already cancelled — the guard excludes current == Cancelled so no double-cancel.
        OrderStateHelper.IsValidTransition(
            OrderType.CustomBanner, OrderState.Cancelled, OrderState.Cancelled).Should().BeFalse();
    }

    [Fact]
    public void IsValidTransition_CreditPack_AllowsOnlyDraftToPaidAndCancel()
    {
        OrderStateHelper.IsValidTransition(
            OrderType.CreditPack, OrderState.Draft, OrderState.Paid).Should().BeTrue();
        // No production lifecycle for credit packs.
        OrderStateHelper.IsValidTransition(
            OrderType.CreditPack, OrderState.Paid, OrderState.InProduction).Should().BeFalse();
        // But cancelling a not-yet-paid pack is still allowed.
        OrderStateHelper.IsValidTransition(
            OrderType.CreditPack, OrderState.Draft, OrderState.Cancelled).Should().BeTrue();
    }

    // ── Whole-sequence walk ──────────────────────────────────────────────────

    [Theory]
    [InlineData(OrderType.CustomBanner)]
    [InlineData(OrderType.AiBanner)]
    [InlineData(OrderType.ManualDesign)]
    [InlineData(OrderType.CreditPack)]
    public void IsValidTransition_WalkingTheFullSequence_EveryAdjacentPairIsValid(OrderType type)
    {
        var seq = OrderStateHelper.ValidSequence(type);
        for (var i = 0; i < seq.Count - 1; i++)
            OrderStateHelper.IsValidTransition(type, seq[i], seq[i + 1])
                .Should().BeTrue($"{seq[i]} -> {seq[i + 1]} is adjacent in the {type} sequence");
    }
}
