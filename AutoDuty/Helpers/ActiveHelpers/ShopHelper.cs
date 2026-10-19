namespace AutoDuty.Helpers;

using Configurations;
using Dalamud.Plugin.Services;
using IPC;

public class ShopHelper : ActiveHelperBase<ShopHelper, ShopLoopActionConfig>
{
    public override string Name        { get; } = nameof(ShopHelper);
    public override string DisplayNameKey { get; } = "LoopActions.Shop.Name";

    public override string[]? Commands           { get; init; } = ["shop"];
    public override string?   CommandDescription { get; init; } = "Executes shopping";

    internal override void Start()
    {
        if (!Knightshopper_IPCSubsriber.IsEnabled)
            return;

        base.Start();
    }

    protected override void HelperUpdate(IFramework framework)
    {
        if (!base.UpdateBase())
            return;

        switch (Knightshopper_IPCSubsriber.GetStatus())
        {
            case Knightshopper_IPCSubsriber.PurchaseState.Unknown:
                if (!Knightshopper_IPCSubsriber.StartShopping(this.ActionConfig.Currency))
                    this.Stop();
                return;
            case Knightshopper_IPCSubsriber.PurchaseState.Running:
                break;
            case Knightshopper_IPCSubsriber.PurchaseState.Failed:
            case Knightshopper_IPCSubsriber.PurchaseState.Cancelled:
            case Knightshopper_IPCSubsriber.PurchaseState.CancellationRequested:
            case Knightshopper_IPCSubsriber.PurchaseState.Succeeded:
                this.Stop();
                return;
            default:
                break;
        }
    }
}