using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace LumaTherm.App.Controls;

public sealed class AuthoritativeToggleButton : ToggleButton
{
    protected override AutomationPeer OnCreateAutomationPeer() => new AuthoritativeToggleButtonAutomationPeer(this);

    protected override void OnToggle()
    {
        // Runtime snapshots own IsChecked; activation still flows through OnClick to Command.
    }

    internal void InvokeFromAutomation() => base.OnClick();
}

internal sealed class AuthoritativeToggleButtonAutomationPeer(AuthoritativeToggleButton owner)
    : ToggleButtonAutomationPeer(owner), IToggleProvider
{
    ToggleState IToggleProvider.ToggleState => owner.IsChecked switch
    {
        true => ToggleState.On,
        false => ToggleState.Off,
        null => ToggleState.Indeterminate,
    };

    void IToggleProvider.Toggle()
    {
        if (!owner.IsEnabled)
        {
            throw new ElementNotEnabledException();
        }

        owner.InvokeFromAutomation();
    }
}
