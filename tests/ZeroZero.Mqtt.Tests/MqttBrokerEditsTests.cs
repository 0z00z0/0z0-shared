using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>Staged-then-applied editing: nothing in the Broker block is live until Apply, an
/// un-applied edit is a state a collapsed group must not be able to hide, and one validation gate
/// serves both buttons.</summary>
public class MqttBrokerEditsTests
{
    private static (MqttBrokerEdits Edits, RecordingSettingsStore Store) Staged(
        Action<MqttSettings>? seed = null)
    {
        var store = new RecordingSettingsStore();
        if (seed is not null) store.Update(seed);
        var edits = new MqttBrokerEdits();
        edits.Load(store.Read());
        return (edits, store);
    }

    // ------------------------------------------------------------------------------------------
    // Nothing takes effect while typing.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void TypingCommitsNothing()
    {
        var (edits, store) = Staged();

        edits.Host = "broker.invalid";
        edits.Touch();

        Assert.Equal(0, store.Writes);
        Assert.Equal("", store.Read().Host);
    }

    [Fact]
    public void AnEditIsVisibleAsAnEditBeforeItIsApplied()
    {
        var (edits, _) = Staged();

        Assert.Equal(MqttEditState.Clean, edits.State);
        Assert.False(edits.IsDirty);

        edits.Host = "broker.invalid";
        edits.Touch();

        Assert.True(edits.IsDirty);
        Assert.Equal(MqttEditState.Edited, edits.State);
    }

    [Fact]
    public void AnEditPutBackWhereItStartedIsNoLongerAnEdit()
    {
        var (edits, _) = Staged(s => s.Host = "broker.invalid");

        edits.Host = "elsewhere.invalid";
        edits.Touch();
        edits.Host = "broker.invalid";
        edits.Touch();

        Assert.False(edits.IsDirty);
        Assert.Equal(MqttEditState.Clean, edits.State);
    }

    [Fact]
    public void ApplyCommitsTheWholeBlockInOneWrite()
    {
        var (edits, store) = Staged();

        edits.Host = " broker.invalid ";
        edits.Username = " user ";
        edits.Password = "secret";
        edits.Transport = MqttTransportMode.WebSocket;
        edits.Encryption = MqttEncryptionMode.On;
        edits.DiscoveryPrefix = "hass";
        edits.SelectPort(8883);

        Assert.True(edits.Apply(store));

        var saved = store.Read();
        Assert.Equal(1, store.Writes);
        Assert.Equal("broker.invalid", saved.Host);
        Assert.Equal("user", saved.Username);
        Assert.Equal("secret", saved.Password);
        Assert.Equal(MqttTransportMode.WebSocket, saved.TransportMode);
        Assert.Equal(MqttEncryptionMode.On, saved.EncryptionMode);
        Assert.Equal("hass", saved.DiscoveryPrefix);
        Assert.Equal(8883, saved.Port);
    }

    [Fact]
    public void ApplyLeavesTheBlockShowingItIsLive()
    {
        var (edits, store) = Staged();

        edits.Host = "broker.invalid";
        edits.Touch();
        edits.Apply(store);

        Assert.False(edits.IsDirty);
        Assert.Equal(MqttEditState.Applied, edits.State);
    }

    [Fact]
    public void AnEditAfterApplyRetractsTheAppliedClaim()
    {
        var (edits, store) = Staged();
        edits.Host = "broker.invalid";
        edits.Apply(store);

        edits.Host = "elsewhere.invalid";
        edits.Touch();

        Assert.Equal(MqttEditState.Edited, edits.State);
    }

    [Fact]
    public void ANoOpTouchAfterApplyDoesNotWithdrawTheAppliedClaim()
    {
        var (edits, store) = Staged();
        edits.Host = "broker.invalid";
        edits.Apply(store);

        edits.Touch();

        Assert.Equal(MqttEditState.Applied, edits.State);
    }

    [Fact]
    public void AStoredPrefixThatIsBlankDoesNotOpenTheBlockAsUnapplied()
    {
        // Comparing the prefix as it would be committed rather than as it is typed would make a
        // store holding a blank prefix read as an edit nobody made.
        var (edits, _) = Staged(s => s.DiscoveryPrefix = "");

        Assert.False(edits.IsDirty);
        Assert.Equal(MqttEditState.Clean, edits.State);
    }

    [Fact]
    public void ClearingThePrefixBoxIsAnEdit()
    {
        var (edits, _) = Staged();

        edits.DiscoveryPrefix = "";
        edits.Touch();

        Assert.True(edits.IsDirty);
        Assert.Equal(MqttEditState.Edited, edits.State);
    }

    [Fact]
    public void ABlankPrefixCommitsTheDefaultRatherThanAnEmptyPrefix()
    {
        // An empty prefix would put every discovery topic at the root of the broker.
        var (edits, store) = Staged();

        edits.DiscoveryPrefix = "   ";
        edits.Apply(store);

        Assert.Equal(MqttSettings.DefaultDiscoveryPrefix, store.Read().DiscoveryPrefix);
    }

    // ------------------------------------------------------------------------------------------
    // One validation gate, both buttons.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void AutomaticAndAnOfferedPortAreAlwaysUsable()
    {
        var (edits, _) = Staged();

        Assert.True(edits.Validate().Usable);
        edits.SelectPort(1883);
        Assert.True(edits.Validate().Usable);
    }

    [Theory]
    [InlineData("70000")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1 883")]
    [InlineData("1,883")]
    [InlineData("18a3")]
    public void AnUnusableTypedPortRefusesTheBlockAndSaysWhy(string typed)
    {
        var (edits, _) = Staged();
        edits.PortMode = MqttPortMode.Custom;
        edits.TypedPort = typed;

        var validation = edits.Validate();

        Assert.False(validation.Usable);
        Assert.False(string.IsNullOrWhiteSpace(validation.Message));
    }

    [Fact]
    public void AnUntypedCustomPortIsNotYetAMistakeButIsNotYetAPortEither()
    {
        var (edits, _) = Staged();
        edits.PortMode = MqttPortMode.Custom;
        edits.TypedPort = "";

        var validation = edits.Validate();

        Assert.Null(validation.Message);
        Assert.False(validation.Usable);
    }

    [Fact]
    public void AnInvalidPortIsRefusedRatherThanCollapsingToAutomatic()
    {
        // The defect this replaces: Apply refused the port while Test silently swept every
        // candidate, so a green result vouched for a configuration that could not be applied.
        var (edits, store) = Staged();
        edits.Host = "broker.invalid";
        edits.PortMode = MqttPortMode.Custom;
        edits.TypedPort = "70000";

        Assert.False(edits.Validate().Usable);
        Assert.False(edits.Apply(store));
        Assert.Equal(0, store.Writes);
        // Null here is exactly what would have been probed as Automatic; the gate is what stops it.
        Assert.Null(edits.Port);
    }

    [Fact]
    public void AValidTypedPortOffTheOfferedListIsAccepted()
    {
        var (edits, store) = Staged();
        edits.PortMode = MqttPortMode.Custom;
        edits.TypedPort = " 1884 ";

        Assert.True(edits.Validate().Usable);
        Assert.Equal(1884, edits.Port);
        Assert.True(edits.Apply(store));
        Assert.Equal(1884, store.Read().Port);
    }

    [Theory]
    [InlineData(null, MqttPortMode.Automatic)]
    [InlineData(1883, MqttPortMode.Offered)]
    [InlineData(1884, MqttPortMode.Custom)]
    public void ASavedPortLandsOnTheRightThirdOfTheSelection(int? saved, MqttPortMode expected)
    {
        var (edits, _) = Staged();

        edits.SelectPort(saved);

        Assert.Equal(expected, edits.PortMode);
        Assert.Equal(saved, edits.Port);
    }

    // ------------------------------------------------------------------------------------------
    // Re-reading the store must not discard what is being typed.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void ReloadKeepsAnEditedFieldAndTakesTheStoresValueForAnUntouchedOne()
    {
        var (edits, store) = Staged(s => { s.Host = "broker.invalid"; s.Username = "user"; });

        edits.Host = "typed.invalid";
        edits.Touch();
        store.Update(s => s.Username = "someone-else");
        edits.Reload(store.Read());

        Assert.Equal("typed.invalid", edits.Host);
        Assert.Equal("someone-else", edits.Username);
        Assert.Equal(MqttEditState.Edited, edits.State);
    }

    [Fact]
    public void ReloadAgreeingWithTheEditRetiresTheEdit()
    {
        var (edits, store) = Staged();

        edits.Host = "broker.invalid";
        edits.Touch();
        store.Update(s => s.Host = "broker.invalid");
        edits.Reload(store.Read());

        Assert.False(edits.IsDirty);
        Assert.Equal(MqttEditState.Clean, edits.State);
    }

    [Fact]
    public void LoadIsTheExplicitDiscardThatReloadIsNot()
    {
        var (edits, store) = Staged();

        edits.Host = "typed.invalid";
        edits.Touch();
        edits.Load(store.Read());

        Assert.Equal("", edits.Host);
        Assert.Equal(MqttEditState.Clean, edits.State);
    }

    [Fact]
    public void AStagedBlockReadsAsAnEndpointRequestWithoutThePassword()
    {
        var (edits, _) = Staged();
        edits.Host = " broker.invalid ";
        edits.Username = " user ";
        edits.Password = "secret";
        edits.SelectPort(8883);
        edits.Transport = MqttTransportMode.Tcp;

        var request = edits.Request;

        Assert.Equal("broker.invalid", request.Host);
        Assert.Equal("user", request.Username);
        Assert.Equal(8883, request.Port);
        Assert.Equal(MqttTransportMode.Tcp, request.Transport);
        Assert.DoesNotContain("secret", request.ToString());
    }
}
