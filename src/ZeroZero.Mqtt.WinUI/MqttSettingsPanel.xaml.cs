using System.Globalization;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using ZeroZero.Controls.WinUI;

namespace ZeroZero.Mqtt.WinUI;

/// <summary>
/// The MQTT settings panel: master switch, live status, device identity, a staged broker block, and
/// one row per application-declared publish group. Built on Community Toolkit SettingsControls so it
/// matches the pages beside it in a host's settings window.
/// </summary>
/// <remarks>
/// <para>Three commit models, by control. Immediate for the master switch, the device name and the
/// group toggles. Batched behind Apply for everything that reconfigures the connection, so it
/// reconciles once per edit session rather than once per keystroke. And neither for the device id,
/// which has a confirmation of its own, because renaming every entity must not be a side effect of
/// editing a host.</para>
/// <para>Nothing staged is hidden by a collapsed group: the Broker section's unapplied marker sits
/// beside its heading rather than inside the expander, and the two controls outside that expander
/// commit on their own.</para>
/// <para>The panel knows no application's subject matter. Everything domain-shaped arrives through
/// <see cref="MqttPanelSetup"/>, and every text of the module's own comes from its resource files,
/// behind the host's localizer.</para>
/// </remarks>
public sealed partial class MqttSettingsPanel : UserControl
{
    // The application's own resource map, asked first when the host supplies no localizer.
    private static readonly Lazy<IStringLocalizer> ApplicationResources =
        new(() => new ResourceMapStringLocalizer("ZeroZero.Mqtt.WinUI"));

    private MqttPanelSetup? _setup;
    private IStringLocalizer _strings = MqttPanelText.Default.Strings;
    private MqttPanelText _text = MqttPanelText.Default;
    private MqttBrokerEdits _edits = new();
    private MqttProbeSession _probe = new();

    // Suppresses the change handlers while control values are written from code, so a programmatic
    // assignment cannot queue a commit nobody asked for. One flag is safe: every writer is
    // synchronous.
    private bool _updating;

    // Set by Cancel(). Background callbacks started before it still marshal back, and touching a
    // torn-down XAML tree throws, so RunOnUi drops them instead.
    private bool _closed;

    // True while a manual publish is in flight. A second click is dropped rather than queued.
    private bool _publishing;

    private bool _removing;

    private readonly Dictionary<string, ToggleSwitch> _groupToggles = [];

    private CancellationTokenSource? _probeCts;

    // Moves the two ages on while nothing happens; runs only while the page is on screen.
    private readonly DispatcherTimer _ageTick = new() { Interval = TimeSpan.FromSeconds(20) };

    // Stops following the connection's state; set while the page is on screen.
    private Action? _unfollowState;

    public MqttSettingsPanel()
    {
        EnsureThemeResources();
        InitializeComponent();

        _ageTick.Tick += (_, _) => RefreshActivityTexts();

        Loaded += (_, _) => UpdateAgeTick();
        Unloaded += (_, _) => UpdateAgeTick();
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => UpdateAgeTick());

        ApplyText();
        BuildPortCombo();
        BuildModeCombos();
    }

    /// <summary>Hands the panel everything it needs and does the first read-back. Called once, from
    /// the host, on the UI thread.</summary>
    /// <exception cref="ArgumentException">A setup built without the connection leaves
    /// <see cref="MqttPanelSetup.Activity"/>, <see cref="MqttPanelSetup.ConnectionState"/> or
    /// <see cref="MqttPanelSetup.PublishNow"/> unset.</exception>
    public void Initialise(MqttPanelSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        setup.EnsureConnectionMembers(nameof(setup));
        _setup = setup;

        var host = setup.Strings ?? ApplicationResources.Value;
        _text = new MqttPanelText(host);
        _strings = _text.Strings;
        _edits = new MqttBrokerEdits(host);
        _probe = new MqttProbeSession(_text);

        ApplyText();
        ApplyHostText(setup);
        BuildGroupRows(setup);
        Reload();
        UpdateAgeTick();
    }

    /// <summary>Re-reads everything the store holds, without discarding what is being typed. A field
    /// that has been edited keeps the edit; one that has not takes whatever the store now says.</summary>
    /// <remarks>Safe to call whenever the host re-shows the page.</remarks>
    /// <exception cref="InvalidOperationException"><see cref="Initialise"/> has not been called. The
    /// panel has no store to read until it is.</exception>
    public void Reload()
    {
        // Loud: a quiet return leaves a blank panel with no trail back to the ordering mistake.
        if (_setup is not { } setup)
            throw new InvalidOperationException(
                "MqttSettingsPanel.Initialise must be called before Reload.");

        var saved = setup.Settings.Read();
        _edits.Reload(saved);

        WithUpdatingSuppressed(() =>
        {
            EnabledToggle.IsOn = saved.Enabled;
            PushBrokerFields();

            // Blank means "use the machine-derived default", so show it as a placeholder rather than
            // pre-filling it — an untouched field must keep meaning "default".
            DeviceNameBox.PlaceholderText = setup.ResolvedDefaultDeviceName;
            SetText(DeviceNameBox, saved.DeviceName);

            foreach (var (key, on) in MqttPublishRows.States(setup.Groups))
                if (_groupToggles.TryGetValue(key, out var toggle)) toggle.IsOn = on;
        });

        RefreshDeviceIdText();
        RefreshDetailVisibility();
        RefreshEditIndicators();
        RefreshStatus();
    }

    /// <summary>Re-reads only what the live connection decides. What a host calls when the panel comes
    /// back on screen with nothing having been edited.</summary>
    public void Refresh()
    {
        RefreshStatus();
        UpdateAgeTick();
    }

    /// <summary>Abandons anything in flight and stops the panel touching its controls again. Call on
    /// window close — an in-flight probe outlives the window by up to its budget. Final, so a host
    /// that keeps the page for the next visit calls it from the window's close and never on leaving
    /// the page: the next visit would find a dead panel.</summary>
    public void Cancel()
    {
        _closed = true;
        UpdateAgeTick();
        CancelProbe();
    }

    /// <summary>Whether the Broker group is open. Held by the control, so it survives everything that
    /// only flips visibility — switching settings sections, alt-tabbing, a re-read of the store — and
    /// resets only when the panel itself is rebuilt. The same lifetime as a staged edit, so the two
    /// cannot disagree about what is on screen.</summary>
    public bool BrokerExpanded
    {
        get => BrokerExpander.IsExpanded;
        set => BrokerExpander.IsExpanded = value;
    }

    /// <summary>Whether the publish group list is open. Same lifetime as <see cref="BrokerExpanded"/>.</summary>
    public bool PublishExpanded
    {
        get => PublishExpander.IsExpanded;
        set => PublishExpander.IsExpanded = value;
    }

    /// <summary>Discards every staged broker edit and takes the store's values instead. The explicit
    /// reset a host offers behind its own control; nothing else on the panel throws work away.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Initialise"/> has not been called. The
    /// panel has no store to read until it is.</exception>
    public void Revert()
    {
        // Reads the same store as Reload, so it carries the same ordering requirement; one of the
        // two throwing while the other returned quietly would be a difference with no meaning.
        if (_setup is not { } setup)
            throw new InvalidOperationException(
                "MqttSettingsPanel.Initialise must be called before Revert.");

        _edits.Load(setup.Settings.Read());
        WithUpdatingSuppressed(PushBrokerFields);
        _probe.Clear();
        // A probe in flight is about the values just thrown away.
        CancelProbe();
        RefreshStatus();
    }

    // ---------------------------------------------------------------------------------------------
    // Text. Every string the module owns is resolved once here, so the markup carries none and a
    // translation reaches static labels and composed sentences through the same lookup.
    // ---------------------------------------------------------------------------------------------

    private void ApplyText()
    {
        PublishCard.Header = _strings["TitlePublishSwitch"].Value;
        PublishDescriptionText.Text = _strings["DescPublishSwitch"].Value;
        SetInfo(PublishInfoIcon, _strings["SubjectPublishSwitch"].Value,
                WithModuleVersion(_strings["InfoPublishSwitch"].Value));
        EnabledToggle.OnContent = _strings["ToggleOn"].Value;
        EnabledToggle.OffContent = _strings["ToggleOff"].Value;

        SetHeader(StatusHeader, "HeadingStatus", "SubjectStatus", _strings["InfoStatus"].Value);
        ConnectionLabel.Text = _strings["RowConnection"].Value;
        SetInfo(ConnectionInfoIcon, _strings["SubjectConnection"].Value, _strings["InfoConnection"].Value);
        BrokerInUseLabel.Text = _strings["RowBrokerInUse"].Value;
        SetInfo(BrokerInUseInfoIcon, _strings["SubjectBrokerInUse"].Value, _strings["InfoBrokerInUse"].Value);
        LastPublishLabel.Text = _strings["RowLastPublish"].Value;
        PublishNowBtn.Content = _strings["ButtonPublishNow"].Value;
        SetInfo(LastPublishInfoIcon, _strings["SubjectLastPublish"].Value, _strings["InfoLastPublish"].Value);
        LastCommandLabel.Text = _strings["RowLastCommand"].Value;
        SetInfo(LastCommandInfoIcon, _strings["SubjectLastCommand"].Value, _strings["InfoLastCommand"].Value);

        SetHeader(DeviceHeader, "HeadingDevice", "SubjectDevice", _strings["InfoDevice"].Value);
        DeviceNameCard.Header = _strings["RowDeviceName"].Value;
        DeviceNameDescription.Text = _strings["DescDeviceName"].Value;
        SetInfo(DeviceNameInfoIcon, _strings["SubjectDeviceName"].Value, _strings["InfoDeviceName"].Value);
        DeviceNameSavedText.Text = _strings["Saved"].Value;
        DeviceIdCard.Header = _strings["RowDeviceId"].Value;
        DeviceIdDescription.Text = _strings["DescDeviceId"].Value;
        SetInfo(DeviceIdInfoIcon, _strings["SubjectDeviceId"].Value,
                _strings["InfoDeviceId", MqttIdentity.MaxLength].Value);
        ChangeDeviceIdBtn.Content = _strings["ButtonChangeDeviceId"].Value;

        SetHeader(BrokerHeader, "HeadingBroker", "SubjectBroker",
                  $"{_strings["InfoBroker"].Value}\n\n{_strings["InfoDetectedMark"].Value}");
        BrokerDirtyText.Text = _strings["NotApplied"].Value;
        // The two expander summaries are not set here: they are composed from live state, in
        // RefreshSummaries, exactly as the Status rows are.

        HostCard.Header = _strings["RowHost"].Value;
        SetInfo(HostInfoIcon, _strings["SubjectHost"].Value, _strings["InfoHost"].Value);
        HostBox.PlaceholderText = _strings["PlaceholderHost"].Value;
        PortCard.Header = _strings["RowPort"].Value;
        PortDescription.Text = _strings["DescPort"].Value;
        SetInfo(PortInfoIcon, _strings["SubjectPort"].Value, _strings["InfoPort"].Value);
        PortCustomBox.PlaceholderText = _strings["PlaceholderPort"].Value;
        TransportCard.Header = _strings["RowTransport"].Value;
        TransportDescription.Text = _strings["DescTransport"].Value;
        SetInfo(TransportInfoIcon, _strings["SubjectTransport"].Value, _strings["InfoTransport"].Value);
        EncryptionCard.Header = _strings["RowEncryption"].Value;
        EncryptionDescription.Text = _strings["DescEncryption"].Value;
        SetInfo(EncryptionInfoIcon, _strings["SubjectEncryption"].Value, _strings["InfoEncryption"].Value);
        TrustCard.Header = _strings["RowCertificateTrust"].Value;
        TrustDescription.Text = _strings["DescCertificateTrust"].Value;
        SetInfo(TrustInfoIcon, _strings["SubjectCertificateTrust"].Value, _strings["InfoCertificateTrust"].Value);
        UsernameCard.Header = _strings["RowUsername"].Value;
        UsernameDescription.Text = _strings["DescUsername"].Value;
        SetInfo(UsernameInfoIcon, _strings["SubjectUsername"].Value, _strings["InfoUsername"].Value);
        PasswordCard.Header = _strings["RowPassword"].Value;
        PasswordDescription.Text = _strings["DescPassword"].Value;
        SetInfo(PasswordInfoIcon, _strings["SubjectPassword"].Value, _strings["InfoPassword"].Value);
        PrefixCard.Header = _strings["RowDiscoveryPrefix"].Value;
        PrefixDescription.Text = _strings["DescDiscoveryPrefix"].Value;
        SetInfo(PrefixInfoIcon, _strings["SubjectDiscoveryPrefix"].Value, _strings["InfoDiscoveryPrefix"].Value);

        ApplyCard.Header = _strings["RowApply"].Value;
        ApplyDescription.Text = _strings["DescApply"].Value;
        ApplyBtn.Content = _strings["ButtonApply"].Value;
        TestBtn.Content = _strings["ButtonTest"].Value;
        AppliedText.Text = _strings["Applied"].Value;
        SetInfo(TestInfoIcon, _strings["SubjectTest"].Value, _strings["InfoTest"].Value);

        // The bubble text is the host's, set in ApplyHostText.
        PublishHeader.Heading = _strings["HeadingPublish"].Value;
        PublishHeader.InfoSubject = _strings["SubjectPublishGroups"].Value;

        RemovalHeader.Heading = _strings["HeadingRemoval"].Value;
        RemovalCard.Header = _strings["RowRemoval"].Value;
        RemovalDescription.Text = _strings["DescRemoval"].Value;
        RemoveDeviceBtn.Content = _strings["ButtonRemoveDevice"].Value;
    }

    /// <summary>The strings the host owns. What an application publishes is the one thing the module
    /// cannot write, including how to describe the publish section as a whole.</summary>
    private void ApplyHostText(MqttPanelSetup setup)
    {
        // A blank host string keeps the module's text: blanks on screen help nobody.
        if (setup.PublishTitle is { } title && !string.IsNullOrWhiteSpace(title)) PublishCard.Header = title;
        if (setup.PublishDescription is { } description && !string.IsNullOrWhiteSpace(description))
            PublishDescriptionText.Text = description;
        if (setup.PublishInfo is { } info && !string.IsNullOrWhiteSpace(info))
            SetInfo(PublishInfoIcon, _strings["SubjectPublishSwitch"].Value, WithModuleVersion(info));

        PublishHeader.Info = setup.PublishGroupsInfo is { } groupsInfo && !string.IsNullOrWhiteSpace(groupsInfo)
            ? groupsInfo
            : _strings["InfoPublishGroups"].Value;
    }

    private static void SetInfo(InfoIcon icon, string subject, string info)
    {
        icon.Subject = subject;
        icon.Info = info;
    }

    private void SetHeader(SettingsSectionHeader header, string headingKey, string subjectKey, string info)
    {
        header.Heading = _strings[headingKey].Value;
        header.InfoSubject = _strings[subjectKey].Value;
        header.Info = info;
    }

    /// <summary>The publish switch's text with the module build in force appended.</summary>
    /// <remarks>Here rather than in an About box, which nobody opens while chasing a broker. Rides the
    /// host's <see cref="MqttPanelSetup.PublishInfo"/> too, so supplying one does not drop it.</remarks>
    private string WithModuleVersion(string info) =>
        $"{info}\n\n{_strings["ModuleVersion", MqttModule.Version].Value}";

    // ---------------------------------------------------------------------------------------------
    // The master switch.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Hides the four detail sections while publishing is off. Hidden, not disabled: a greyed
    /// panel still invites reading, and none of it has an answer until the feature is on.</summary>
    private void RefreshDetailVisibility()
    {
        DetailPanel.Visibility = EnabledToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        bool removal = _setup?.OfferRemoval == true
                    && (EnabledToggle.IsOn || RemovalResultText.Visibility == Visibility.Visible);
        RemovalPanel.Visibility = removal ? Visibility.Visible : Visibility.Collapsed;
        RefreshPublishNowEnabled();
    }

    // Enabled applies immediately: it is not one of the batched broker fields.
    private void OnEnabledToggled(object sender, RoutedEventArgs e)
    {
        if (_updating || _setup is not { } setup) return;

        bool on = EnabledToggle.IsOn;
        setup.Settings.Update(s => s.Enabled = on);
        RemovalResultText.Visibility = Visibility.Collapsed;   // about a removal this flip supersedes
        RefreshDetailVisibility();
        // Switching publishing on is not a broker setting changing, so it probes nothing; switching
        // off must abandon a probe already in flight, because off means no network at all.
        if (!on) CancelProbe();
        setup.ConnectionChanged();   // exactly one reconnect attempt for this toggle flip
        RefreshStatus();
    }

    // ---------------------------------------------------------------------------------------------
    // Device identity. Neither control here rides the Apply batch.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Retires the saved marker the moment the name moves again: it vouches for what is
    /// stored, and what is stored is no longer what is on screen.</summary>
    private void OnDeviceNameEdited(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        DeviceNameSavedText.Visibility = Visibility.Collapsed;
    }

    /// <summary>Commits the device name on its own. It sits above the Broker group, and a commit path
    /// that lived inside that group would lose the edit of anyone who never expanded it.</summary>
    private void OnDeviceNameCommitted(object sender, RoutedEventArgs e) => CommitDeviceName();

    private void OnDeviceNameKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        CommitDeviceName();
    }

    private void CommitDeviceName()
    {
        if (_updating || _closed || _setup is not { } setup) return;

        string name = DeviceNameBox.Text?.Trim() ?? "";
        if (name == setup.Settings.Read().DeviceName) return;

        setup.Settings.Update(s => s.DeviceName = name);
        setup.ConnectionChanged();   // the name is published on the device document
        DeviceNameSavedText.Visibility = Visibility.Visible;
    }

    /// <summary>The id actually published under, override or machine-derived default.</summary>
    private string EffectiveDeviceId() => _setup is { } setup
        ? MqttIdentity.Effective(setup.Settings.Read().DeviceId, setup.TopicRoot, Environment.MachineName)
        : "";

    private void RefreshDeviceIdText() => DeviceIdText.Text = EffectiveDeviceId();

    /// <summary>Renaming every published entity is not a side effect of an edit, so the id changes
    /// only through its own dialogue.</summary>
    private async void OnChangeDeviceIdClicked(object sender, RoutedEventArgs e)
    {
        if (_setup is not { } setup) return;

        try
        {
            string? newId = await DeviceIdDialog.AskAsync(this, setup, _strings, EffectiveDeviceId(),
                StyleFor("MqttResultStyle"), StyleFor("MqttResultErrorStyle"));
            if (newId is null || _closed) return;

            setup.Settings.Update(s => s.DeviceId = newId);
            // The same callback Apply uses: the publisher evicts the superseded identity's retained
            // topics before republishing discovery under the new one, which is what makes the
            // dialogue's promise about the old entities true.
            setup.ConnectionChanged();
            RefreshDeviceIdText();
            RefreshStatus();
        }
        catch (Exception ex) { Log("MqttSettingsPanel.OnChangeDeviceIdClicked", ex); }
    }

    // ---------------------------------------------------------------------------------------------
    // The staged broker block.
    // ---------------------------------------------------------------------------------------------

    private void BuildPortCombo()
    {
        PortCombo.Items.Clear();
        PortCombo.Items.Add(new ComboBoxItem { Content = _strings["OptionAutomatic"].Value });
        foreach (int port in MqttEndpointPlan.OfferedPorts)
            PortCombo.Items.Add(new ComboBoxItem { Content = port.ToString(CultureInfo.InvariantCulture) });
        PortCombo.Items.Add(new ComboBoxItem { Content = _strings["PortCustom"].Value });
    }

    /// <summary>The transport, encryption and certificate-trust dropdowns. Their order is the enum's,
    /// so the read-back is positional and there is no lookup table to disagree with either end.</summary>
    private void BuildModeCombos()
    {
        TransportCombo.Items.Clear();
        TransportCombo.Items.Add(new ComboBoxItem { Content = _strings["OptionAutomatic"].Value });
        TransportCombo.Items.Add(new ComboBoxItem { Content = _strings["TransportTcp"].Value });
        TransportCombo.Items.Add(new ComboBoxItem { Content = _strings["TransportWebSocket"].Value });

        EncryptionCombo.Items.Clear();
        EncryptionCombo.Items.Add(new ComboBoxItem { Content = _strings["OptionAutomatic"].Value });
        EncryptionCombo.Items.Add(new ComboBoxItem { Content = _strings["ToggleOn"].Value });
        EncryptionCombo.Items.Add(new ComboBoxItem { Content = _strings["ToggleOff"].Value });

        TrustCombo.Items.Clear();
        TrustCombo.Items.Add(new ComboBoxItem { Content = _strings["TrustSystem"].Value });
        TrustCombo.Items.Add(new ComboBoxItem { Content = _strings["TrustAcceptAny"].Value });
    }

    private int PortCustomIndex => PortCombo.Items.Count - 1;

    /// <summary>Writes the staged block onto the controls. Only where a value differs, so a re-read
    /// cannot move a caret in a box nobody touched.</summary>
    private void PushBrokerFields()
    {
        SetText(HostBox, _edits.Host);
        SetText(UsernameBox, _edits.Username);
        if (PasswordBox.Password != _edits.Password) PasswordBox.Password = _edits.Password;
        SetText(PrefixBox, _edits.DiscoveryPrefix);
        SetText(PortCustomBox, _edits.TypedPort);

        TransportCombo.SelectedIndex = (int)_edits.Transport;
        EncryptionCombo.SelectedIndex = (int)_edits.Encryption;
        TrustCombo.SelectedIndex = (int)_edits.TrustMode;
        PortCombo.SelectedIndex = _edits.PortMode switch
        {
            MqttPortMode.Offered => IndexOfOfferedPort(_edits.OfferedPort) + 1,
            MqttPortMode.Custom => PortCustomIndex,
            _ => 0,
        };
    }

    private static void SetText(TextBox box, string value)
    {
        if (box.Text != value) box.Text = value;
    }

    private static int IndexOfOfferedPort(int port)
    {
        var offered = MqttEndpointPlan.OfferedPorts;
        for (int i = 0; i < offered.Count; i++) if (offered[i] == port) return i;
        return -1;
    }

    /// <summary>Reads every control back into the staged block. One direction at a time: the controls
    /// are the truth while the user is typing, the block is the truth on every read-back.</summary>
    private void PullBrokerFields()
    {
        _edits.Host = HostBox.Text ?? "";
        _edits.Username = UsernameBox.Text ?? "";
        _edits.Password = PasswordBox.Password ?? "";
        _edits.DiscoveryPrefix = PrefixBox.Text ?? "";
        _edits.TypedPort = PortCustomBox.Text ?? "";
        _edits.Transport = TransportCombo.SelectedIndex < 0
            ? MqttTransportMode.Auto : (MqttTransportMode)TransportCombo.SelectedIndex;
        _edits.Encryption = EncryptionCombo.SelectedIndex < 0
            ? MqttEncryptionMode.Auto : (MqttEncryptionMode)EncryptionCombo.SelectedIndex;
        _edits.TrustMode = TrustCombo.SelectedIndex < 0
            ? MqttCertificateTrustMode.System : (MqttCertificateTrustMode)TrustCombo.SelectedIndex;

        int index = PortCombo.SelectedIndex;
        if (index <= 0) _edits.PortMode = MqttPortMode.Automatic;
        else if (index <= MqttEndpointPlan.OfferedPorts.Count)
        {
            _edits.PortMode = MqttPortMode.Offered;
            _edits.OfferedPort = MqttEndpointPlan.OfferedPorts[index - 1];
        }
        else _edits.PortMode = MqttPortMode.Custom;
    }

    private void OnBrokerFieldEdited(object sender, RoutedEventArgs e) => BrokerFieldMoved();

    private void OnBrokerSelectionChanged(object sender, SelectionChangedEventArgs e) => BrokerFieldMoved();

    private void OnPortSelectionChanged(object sender, SelectionChangedEventArgs e) => BrokerFieldMoved();

    /// <summary>Every staged edit lands here: the block is re-read, the indicators are recomputed,
    /// and the answer under the buttons is dropped because it is about values since retyped.</summary>
    /// <remarks>No network follows. Editing a field is not one of the things that starts a check, so
    /// the promise that showing this page touches no broker holds while it is being typed into as
    /// well; a check in flight from an earlier button press is abandoned, because its answer is about
    /// values that have since moved.</remarks>
    private void BrokerFieldMoved()
    {
        if (_updating) return;

        PullBrokerFields();
        _edits.Touch();
        _probe.Clear();
        CancelProbe();
        RefreshEditIndicators();
    }

    /// <summary>Shows the typed-port box only for the last entry, keeps the applied indicator honest,
    /// and gates both buttons on the one validation answer.</summary>
    private void RefreshEditIndicators()
    {
        PortCustomBox.Visibility = _edits.PortMode == MqttPortMode.Custom
            ? Visibility.Visible : Visibility.Collapsed;

        var port = _edits.ValidatePort();
        PortErrorText.Text = port.Message ?? "";
        PortErrorText.Visibility = port.Message is null ? Visibility.Collapsed : Visibility.Visible;

        RefreshTrustFields();

        var validation = _edits.Validate();

        // One gate, both buttons. A green test that vouched for a configuration Apply would refuse is
        // worse than no test at all.
        ApplyBtn.IsEnabled = validation.Usable;
        TestBtn.IsEnabled = validation.Usable && !_probe.Busy;

        AppliedText.Visibility = _edits.State == MqttEditState.Applied ? Visibility.Visible : Visibility.Collapsed;
        // Outside the expander, so a collapsed group cannot hide an edit that is not live.
        BrokerDirtyText.Visibility = _edits.State == MqttEditState.Edited ? Visibility.Visible : Visibility.Collapsed;

        TestResultText.Text = _probe.Line;
        TestResultText.Style = StyleFor(_probe.IsFailure ? "MqttResultErrorStyle" : "MqttResultStyle");
        TestResultText.Visibility = _probe.HasLine ? Visibility.Visible : Visibility.Collapsed;

        TestProgress.IsActive = _probe.Busy;
        TestProgress.Visibility = _probe.Busy ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Puts what accepting any certificate gives up on the line below the dropdown, and
    /// nothing there otherwise.</summary>
    private void RefreshTrustFields()
    {
        bool acceptAny = _edits.TrustMode == MqttCertificateTrustMode.AcceptAny;
        TrustNoteText.Text = acceptAny ? _strings["TrustAcceptAnyNote"].Value : "";
        TrustNoteText.Visibility = acceptAny ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Commits the whole staged block at once, so the connection reconnects per Apply click
    /// rather than per keystroke. The device id is not in the batch — renaming every entity must not
    /// be a side effect of editing a host.</summary>
    private void OnApplyClicked(object sender, RoutedEventArgs e)
    {
        if (_setup is not { } setup) return;

        PullBrokerFields();
        // An out-of-range port is refused, never rounded into range and never collapsed to Automatic.
        if (!_edits.Apply(setup.Settings)) { RefreshEditIndicators(); return; }

        WithUpdatingSuppressed(PushBrokerFields);
        setup.ConnectionChanged();   // exactly one reconnect attempt for this Apply click
        RefreshEditIndicators();
        RefreshStatus();
        // Apply is one of the three things that ask for a probe. Its own throwaway connection, not
        // the one the reconnect above makes: the panel reports what it measured itself.
        StartProbe(MqttProbeTrigger.Apply);
    }

    // ---------------------------------------------------------------------------------------------
    // Status.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Writes one Status row: the trimmed line on screen, the whole string on the hover tip,
    /// and the tier. The single writer for the block, because its shape only holds if every value
    /// goes on the same way — one line, and an error that is a colour and nothing else.</summary>
    private void SetStatusValue(TextBlock target, string text, bool error = false)
    {
        target.Text = text;
        ToolTipService.SetToolTip(target, text);
        target.Style = StyleFor(error ? "MqttStatusErrorStyle" : "MqttStatusValueStyle");
    }

    /// <summary>A style from the panel's own resources. Styles are theme-neutral objects — the
    /// ThemeResource inside a setter resolves where the style is applied — so switching tiers this
    /// way keeps a subtree with its own RequestedTheme painting the right way round.</summary>
    private Microsoft.UI.Xaml.Style? StyleFor(string key) =>
        Resources.TryGetValue(key, out object? value) ? value as Microsoft.UI.Xaml.Style : null;

    /// <summary>The saved broker values as the pure plan reads them — what the live connection is
    /// using, as against what is staged in the boxes. The two differ until Apply.</summary>
    private MqttEndpointRequest SavedRequest()
    {
        if (_setup is not { } setup) return new("", "", null, MqttTransportMode.Auto);
        var s = setup.Settings.Read();
        return new(s.Host, s.Username, s.Port, s.TransportMode, s.EncryptionMode);
    }

    private MqttEndpointMemory? Memory() => _setup?.RecallEndpoint?.Invoke();

    private void RefreshStatus()
    {
        if (_setup is not { } setup) return;

        var request = SavedRequest();
        var memory = Memory();
        var state = setup.ConnectionState();
        SetStatusValue(DetectText, _text.Connection(request, memory, state, _probe.Busy));
        SetStatusValue(BrokerStatusText, _text.DescribeBroker(request, memory));
        RefreshSummaries(setup, request, memory, state);
        RefreshActivityTexts();
    }

    /// <summary>The line each closed section shows about itself. Composed by the module from live
    /// state, on the same seam as the Status rows — the panel assembles no sentence of its own, so
    /// what a collapsed summary says is testable without a window.</summary>
    private void RefreshSummaries(
        MqttPanelSetup setup, MqttEndpointRequest request, MqttEndpointMemory? memory,
        MqttConnectionState state)
    {
        var trust = setup.Settings.Read().CertificateTrust.Mode;
        SetBrokerSummary(_text.SummariseBroker(request, memory, state, trust));
        SetSummary(PublishExpanderDescription, _text.SummarisePublish(MqttPublishRows.Tally(setup.Groups)));
    }

    /// <summary>The closed Broker line with a magnifier after each value found rather than set, on
    /// the line and on its hover tip alike; a screen reader hears the mark's name in its place.</summary>
    private void SetBrokerSummary(MqttBrokerSummary summary)
    {
        string markName = _strings["NameDetectedValue"].Value;
        FillMarked(BrokerExpanderDescription, summary, markName);

        var tip = new RichTextBlock { Style = StyleFor("MqttHintRichStyle") };
        FillMarked(tip, summary, markName);
        ToolTipService.SetToolTip(BrokerExpanderDescription, tip);

        string spoken = summary.Text;
        foreach (int at in summary.DetectedAt.Reverse()) spoken = spoken.Insert(at, " " + markName);
        AutomationProperties.SetName(BrokerExpanderDescription, spoken);
    }

    private void FillMarked(RichTextBlock target, MqttBrokerSummary summary, string markName)
    {
        var line = new Paragraph();
        int from = 0;
        foreach (int at in summary.DetectedAt)
        {
            line.Inlines.Add(new Run { Text = summary.Text[from..at] });
            var mark = new Microsoft.UI.Xaml.Shapes.Path
            {
                Style = StyleFor("MqttDetectedMarkStyle"),
                Data = new GeometryGroup
                {
                    Children =
                    {
                        new EllipseGeometry { Center = new(4, 4), RadiusX = 3, RadiusY = 3 },
                        new LineGeometry { StartPoint = new(6.2, 6.2), EndPoint = new(9.2, 9.2) },
                    },
                },
            };
            AutomationProperties.SetName(mark, markName);
            line.Inlines.Add(new InlineUIContainer { Child = mark });
            from = at;
        }
        line.Inlines.Add(new Run { Text = summary.Text[from..] });

        target.Blocks.Clear();
        target.Blocks.Add(line);
    }

    /// <summary>Writes one collapsed summary: the line on screen, and the whole string on the hover
    /// tip, so a summary wider than the section it sits in is still readable.</summary>
    private static void SetSummary(TextBlock target, string text)
    {
        target.Text = text;
        ToolTipService.SetToolTip(target, text);
    }

    /// <summary>The two relative ages, and the button beside them. Re-read on the tick as well as on
    /// every event, because an age moves on with nothing having happened.</summary>
    private void RefreshActivityTexts()
    {
        if (_setup is not { } setup) return;

        var now = DateTimeOffset.UtcNow;
        SetStatusValue(LastPublishText, _text.DescribeLastPublish(setup.Activity.LastPublish, now));
        SetStatusValue(LastCommandText,
            _text.DescribeLastCommand(setup.Activity.LastCommand, now, setup.CommandLabel));
        // The link comes and goes without the panel hearing about it, so the button's state is
        // re-read wherever the facts beside it are.
        RefreshPublishNowEnabled();
    }

    /// <summary>Runs the age tick, and follows the connection's state, only while the page is on
    /// screen. A settings page nobody is looking at must not cost a consuming application a timer, and
    /// a long-lived connection must not hold a panel that has gone.</summary>
    private void UpdateAgeTick()
    {
        bool wanted = !_closed && IsLoaded && Visibility == Visibility.Visible;
        if (wanted && !_ageTick.IsEnabled) _ageTick.Start();
        else if (!wanted && _ageTick.IsEnabled) _ageTick.Stop();

        if (wanted && _unfollowState is null && _setup is { } setup)
        {
            _unfollowState = setup.FollowConnectionState(() => RunOnUi(RefreshStatus));
            RefreshStatus();   // a change while not followed was missed
        }
        else if (!wanted && _unfollowState is { } unfollow)
        {
            _unfollowState = null;
            unfollow();
        }
    }

    /// <summary>Live only when there is somewhere to publish to — the feature on, a broker connected,
    /// and nothing already in flight. The removal button follows the same rule.</summary>
    private void RefreshPublishNowEnabled()
    {
        bool live = EnabledToggle.IsOn && _setup?.ConnectionState() == MqttConnectionState.Connected;
        PublishNowBtn.IsEnabled = live && !_publishing;
        RemoveDeviceBtn.IsEnabled = live && !_removing;
    }

    /// <summary>Republishes the current state on demand; no config topic is rewritten. Awaited on the
    /// UI thread: a raw dispatcher callback that throws ends the process as a stowed exception.</summary>
    private async void OnPublishNowClicked(object sender, RoutedEventArgs e)
    {
        if (_publishing || _setup is not { } setup) return;

        _publishing = true;
        try
        {
            RefreshPublishNowEnabled();
            bool sent = await setup.PublishNow();
            if (_closed) return;   // panel went while the publish was in flight

            RefreshActivityTexts();
            if (!sent) SetStatusValue(LastPublishText, _strings["PublishFailed"].Value, error: true);
        }
        catch (Exception ex) { Log("MqttSettingsPanel.OnPublishNowClicked", ex); }
        finally
        {
            _publishing = false;
            if (!_closed) RefreshPublishNowEnabled();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The probe.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Tests the staged broker values — whatever is in the fields, applied or not, since the
    /// point of the button is to check before committing. It commits nothing at all, which is what
    /// its own info text says.</summary>
    private void OnTestConnectionClicked(object sender, RoutedEventArgs e)
    {
        if (_probe.Busy) return;   // a second click while one runs is dropped, not queued
        PullBrokerFields();
        StartProbe(MqttProbeTrigger.TestConnection);
    }

    /// <summary>Abandons any probe in flight and frees the controls at once rather than at the end of
    /// a budget nobody is waiting for.</summary>
    private void CancelProbe()
    {
        _probeCts?.Cancel();
        _probeCts = null;
        _probe.Abandon();
        if (!_closed) RefreshEditIndicators();
    }

    /// <summary>Probes for the broker's endpoint if this trigger warrants one, and reports whatever
    /// happens either way. Both callers are a button press; there is no path in from showing the
    /// panel, from editing a field or from a timer.</summary>
    private void StartProbe(MqttProbeTrigger trigger)
    {
        if (_setup is null) return;

        CancelProbe();

        var target = ProbeTarget(_edits.Request);
        switch (_probe.Decide(trigger, _edits.Validate(), EnabledToggle.IsOn, target))
        {
            case MqttProbeDecision.Run:
                RunProbe(target);
                break;
            case MqttProbeDecision.Unusable:
                RefreshEditIndicators();
                break;
            default:
                RefreshEditIndicators();
                RefreshStatus();
                break;
        }
    }

    /// <summary>The staged values as a probe tries them.</summary>
    private MqttProbeTarget ProbeTarget(MqttEndpointRequest request) => new(
        Host: request.Host,
        Port: request.Port,
        Username: request.Username,
        Password: _edits.Password,
        ClientId: MqttProbe.ProbeClientId(EffectiveDeviceId()),
        Transport: request.Transport,
        Encryption: request.Encryption,
        // Read, never written. A test that recorded where the broker answered would change the sweep
        // order of the live connection, which is precisely what it promises not to do.
        Memory: Memory(),
        // The trust in the field rather than the saved one, like every other value here: a test that
        // vouched for trust the connection will not use answers about nothing.
        CertificateTrust: _edits.Trust);

    /// <summary>async void, and guarded whole: nothing may escape into the dispatcher. Never blocks
    /// the UI thread — every stage of the sweep is a socket wait, and the only work back here is the
    /// progress line and the result.</summary>
    private async void RunProbe(MqttProbeTarget target)
    {
        if (_setup is null) return;

        var cts = new CancellationTokenSource();
        _probeCts = cts;
        long token = _probe.Start(target);
        RefreshEditIndicators();
        RefreshStatus();

        try
        {
            // The sweep's churn is the only visible evidence of several seconds of work, so every
            // candidate replaces the line and nothing is debounced. Reported from whichever thread
            // the probe resumed on, so it is bounced through RunOnUi rather than trusted to land
            // here; the session drops anything from a run that has been superseded.
            var progress = new Progress<MqttSearchProgress>(p => RunOnUi(() =>
            {
                _probe.Report(token, p);
                RefreshEditIndicators();
            }));

            var report = await MqttProbe.RunAsync(target, cts.Token, progress);
            if (_closed) return;

            _probe.Settle(token, report);
            RefreshEditIndicators();
            RefreshStatus();
        }
        catch (Exception ex) { Log("MqttSettingsPanel.RunProbe", ex); }
        finally
        {
            // Unconditional, and keyed on nothing: a cancelled probe with no successor still frees
            // the button and stops the spinner here.
            _probe.Finish(token);
            if (ReferenceEquals(_probeCts, cts)) _probeCts = null;
            cts.Dispose();
            if (!_closed) { RefreshEditIndicators(); RefreshStatus(); }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Publish groups. Declared by the consuming application, rendered here.
    // ---------------------------------------------------------------------------------------------

    /// <summary>One SettingsCard per declared group, built once. The panel renders the application's
    /// group vocabulary — label, description, info — and invents none of it. A declared group gets a
    /// row whether or not it currently has entities.</summary>
    private void BuildGroupRows(MqttPanelSetup setup)
    {
        PublishExpander.Items.Clear();
        _groupToggles.Clear();

        foreach (var row in MqttPublishRows.Build(setup.Groups))
        {
            var toggle = new ToggleSwitch
            {
                OnContent = _strings["ToggleOn"].Value,
                OffContent = _strings["ToggleOff"].Value,
                IsOn = row.On,
                Tag = row.Key,
            };
            toggle.Toggled += OnGroupToggled;
            _groupToggles[row.Key] = toggle;

            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (row.HasInfo)
                content.Children.Add(new InfoIcon
                {
                    Subject = _strings["SubjectPublishGroup", row.Label].Value,
                    Info = row.Info,
                });
            content.Children.Add(toggle);

            var card = new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Header = row.Label,
                Content = content,
            };
            if (row.HasDescription) card.Description = row.Description;
            PublishExpander.Items.Add(card);
        }
    }

    /// <summary>Commits the group that moved and re-announces. Unlike the broker fields these apply
    /// immediately: the change is a publish, not a reconnect, so there is nothing to batch.</summary>
    private void OnGroupToggled(object sender, RoutedEventArgs e)
    {
        if (_updating || _setup is not { } setup) return;
        if (sender is not ToggleSwitch { Tag: string key } toggle) return;

        setup.Groups.Set(key, toggle.IsOn);
        setup.PublishSetChanged();
        // The count in the closed section's summary is one of the facts this toggle just changed.
        RefreshStatus();
    }

    // ---------------------------------------------------------------------------------------------
    // Removing the published device. Offered only when the application asks for it.
    // ---------------------------------------------------------------------------------------------

    private async void OnRemoveDeviceClicked(object sender, RoutedEventArgs e)
    {
        if (_removing || _setup is not { RemoveDevice: { } remove } setup) return;

        _removing = true;
        try
        {
            RefreshPublishNowEnabled();
            if (!await ConfirmRemovalAsync() || _closed) return;

            ShowRemovalResult("RemovalRunning", error: false);
            bool removed = await remove(CancellationToken.None);
            if (_closed) return;

            ShowRemovalResult(removed ? "RemovalDone" : "RemovalNoConnection", error: !removed);
            if (removed)
            {
                setup.Settings.Update(s => s.Enabled = false);
                WithUpdatingSuppressed(() => EnabledToggle.IsOn = false);
                CancelProbe();
                RefreshDetailVisibility();
                setup.ConnectionChanged();
                RefreshStatus();
            }
        }
        catch (Exception ex)
        {
            Log("MqttSettingsPanel.OnRemoveDeviceClicked", ex);
            if (!_closed) ShowRemovalResult("RemovalFailed", error: true);
        }
        finally
        {
            _removing = false;
            if (!_closed) RefreshPublishNowEnabled();
        }
    }

    private async Task<bool> ConfirmRemovalAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = _strings["RemovalTitle"].Value,
            Content = new TextBlock { Text = _strings["RemovalWarning"].Value, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = _strings["RemovalConfirm"].Value,
            CloseButtonText = _strings["RemovalCancel"].Value,
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void ShowRemovalResult(string key, bool error)
    {
        RemovalResultText.Text = _strings[key].Value;
        RemovalResultText.Style = StyleFor(error ? "MqttResultErrorStyle" : "MqttResultStyle");
        RemovalResultText.Visibility = Visibility.Visible;
    }

    // ---------------------------------------------------------------------------------------------
    // Panel-local plumbing.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Merges the module's theme defaults into the application's own resources rather than
    /// into this control's, so a key a host declares for itself wins: a dictionary's own entries
    /// outrank the ones it merges, and the host's are its own.</summary>
    private static void EnsureThemeResources()
    {
        if (Application.Current is not { } app) return;

        const string Source = "ms-appx:///ZeroZero.Mqtt.WinUI/Themes/MqttPanelResources.xaml";
        foreach (var merged in app.Resources.MergedDictionaries)
            if (merged.Source?.ToString() == Source) return;

        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(Source) });
    }

    /// <summary>Marshals <paramref name="action"/> onto the panel's UI thread. An unhandled exception
    /// inside a raw <see cref="DispatcherQueue"/> callback is a stowed exception that tears the whole
    /// process down, so every callback that can run off a background task goes through here.</summary>
    private void RunOnUi(Action action)
    {
        try
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_closed) return;   // panel already torn down — a stale callback has nothing to update
                try { action(); }
                catch (Exception ex) { Log("MqttSettingsPanel.RunOnUi", ex); }
            });
        }
        catch (Exception ex) { Log("MqttSettingsPanel.RunOnUi enqueue", ex); }
    }

    /// <summary>Raises the updating guard around a batch of programmatic control assignments, lowering
    /// it in a <c>finally</c> — a hand-written pair leaves the flag stuck true if an assignment
    /// throws, silently disabling every later commit in the panel.</summary>
    private void WithUpdatingSuppressed(Action apply)
    {
        _updating = true;
        try { apply(); }
        finally { _updating = false; }
    }

    private void Log(string source, Exception ex) => _setup?.Log.Error(source, ex);
}
