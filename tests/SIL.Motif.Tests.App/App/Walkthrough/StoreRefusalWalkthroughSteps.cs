using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using SIL.Motif.App.Views;

namespace SIL.Motif.Tests.App.Walkthrough;

internal static class StoreRefusalWalkthroughSteps
{
    internal static Expander FindRefusalDetails(this WalkthroughWindow walkthrough)
    {
        var refusal = walkthrough.Find<RefusalBlock>("Refresh refusal");
        return refusal.GetLogicalDescendants().OfType<Expander>().Single(expander =>
            AutomationProperties.GetName(expander) == "Refusal details");
    }

    internal static Button FindDeleteButton(this RefusalBlock refusal) =>
        refusal.GetLogicalDescendants().OfType<Button>().Single(button =>
            AutomationProperties.GetName(button) == "Delete Motif's file for this project and reopen it");

    internal static void ClickDeleteButton(this WalkthroughWindow walkthrough)
    {
        var refusal = walkthrough.Find<RefusalBlock>("Refresh refusal");
        HeadlessClick.Click(walkthrough.Window, refusal.FindDeleteButton(),
            "Delete Motif's file for this project and reopen it");
    }
}
