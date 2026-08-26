using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

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
/// <see cref="MqttPanelSetup"/>, and every string of the module's own comes from
/// <see cref="MqttStrings"/>.</para>
/// </remarks>
public sealed partial class MqttSettingsPanel : UserControl
{
    private MqttPanelSetup? _setup;
    private MqttStrings _strings = MqttStrings.Default;
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

    private readonly Dictionary<string, ToggleSwitch> _groupToggles = [];

    private CancellationTokenSource? _probeCts;

    // The two relative ages have to move while nothing at all is happening: a panel left open showing
    // "just now" must reach "2 minutes ago" without a publish to trigger it. Twenty seconds is inside
    // the coarsest thing the wording can show, and the tick stops the moment the page is off screen
    // so no consuming application pays for a timer behind a page nobody is looking at.
    private readonly DispatcherTimer _ageTick = new() { Interval = TimeSpan.FromSeconds(20) };

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
    public void Initialise(MqttPanelSetup setup)
    {
        _setup = setup ?? throw new ArgumentNullException(nameof(setup));

        _strings = new MqttStrings(setup.Strings ?? MqttResourceStrings.Instance);
        _text = new MqttPanelText(_strings);
        _edits = new MqttBrokerEdits(_strings);
        _probe = new MqttProbeSession(_text);

        ApplyText();
        ApplyHostText(setup);
        BuildGroupRows(setup);
        Reload();
    }

    /// <summary>Re-reads everything the store holds, without discarding what is being typed. A field
    /// that has been edited keeps the edit; one that has not takes whatever the store now says.</summary>
    /// <remarks>A host may call this whenever it re-shows the page. Losing a typed host to a
    /// re-invocation of the settings window — with the group still open and the text simply gone —
    /// is the failure this signature exists to make impossible.</remarks>
    /// <exception cref="InvalidOperationException"><see cref="Initialise"/> has not been called. The
    /// panel has no store to read until it is.</exception>
    public void Reload()
    {
        // Loud rather than silent: there is nothing to read before the store arrives, and a quiet
        // return turns a host calling this from a general refresh step into a blank panel with no
        // trail back to the ordering that caused it.
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
    /// window close, and when the host navigates away from the page holding the panel — an in-flight
    /// probe outlives the window by up to its budget.</summary>
    public void Cancel()
    {
        _closed = true;
        _ageTick.Stop();
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
        RefreshEditIndicators();
        RefreshStatus();
    }

    // ---------------------------------------------------------------------------------------------
    // Text. Every string the module owns is resolved once here, so the markup carries none and a
    // translation reaches static labels and composed sentences through the same lookup.
    // ---------------------------------------------------------------------------------------------

    private void ApplyText()
    {
        PublishCard.Header = _strings.Get("TitlePublishSwitch");
        PublishDescriptionText.Text = _strings.Get("DescPublishSwitch");
        SetInfo(PublishInfoIcon, _strings.Get("SubjectPublishSwitch"),
                WithModuleVersion(_strings.Get("InfoPublishSwitch")));
        EnabledToggle.OnContent = _strings.Get("ToggleOn");
        EnabledToggle.OffContent = _strings.Get("ToggleOff");

        StatusHeading.Text = _strings.Get("HeadingStatus");
        SetInfo(StatusInfoIcon, _strings.Get("SubjectStatus"), _strings.Get("InfoStatus"));
        ConnectionLabel.Text = _strings.Get("RowConnection");
        SetInfo(ConnectionInfoIcon, _strings.Get("SubjectConnection"), _strings.Get("InfoConnection"));
        BrokerInUseLabel.Text = _strings.Get("RowBrokerInUse");
        SetInfo(BrokerInUseInfoIcon, _strings.Get("SubjectBrokerInUse"), _strings.Get("InfoBrokerInUse"));
        LastPublishLabel.Text = _strings.Get("RowLastPublish");
        PublishNowBtn.Content = _strings.Get("ButtonPublishNow");
        SetInfo(LastPublishInfoIcon, _strings.Get("SubjectLastPublish"), _strings.Get("InfoLastPublish"));
        LastCommandLabel.Text = _strings.Get("RowLastCommand");
        SetInfo(LastCommandInfoIcon, _strings.Get("SubjectLastCommand"), _strings.Get("InfoLastCommand"));

        DeviceHeading.Text = _strings.Get("HeadingDevice");
        SetInfo(DeviceInfoIcon, _strings.Get("SubjectDevice"), _strings.Get("InfoDevice"));
        DeviceNameCard.Header = _strings.Get("RowDeviceName");
        DeviceNameDescription.Text = _strings.Get("DescDeviceName");
        SetInfo(DeviceNameInfoIcon, _strings.Get("SubjectDeviceName"), _strings.Get("InfoDeviceName"));
        DeviceNameSavedText.Text = _strings.Get("Saved");
        DeviceIdCard.Header = _strings.Get("RowDeviceId");
        DeviceIdDescription.Text = _strings.Get("DescDeviceId");
        SetInfo(DeviceIdInfoIcon, _strings.Get("SubjectDeviceId"),
                _strings.Format("InfoDeviceId", MqttIdentity.MaxLength));
        ChangeDeviceIdBtn.Content = _strings.Get("ButtonChangeDeviceId");

        BrokerHeading.Text = _strings.Get("HeadingBroker");
        SetInfo(BrokerInfoIcon, _strings.Get("SubjectBroker"), _strings.Get("InfoBroker"));
        BrokerDirtyText.Text = _strings.Get("NotApplied");
        // The two expander summaries are not set here: they are composed from live state, in
        // RefreshSummaries, exactly as the Status rows are.

        HostCard.Header = _strings.Get("RowHost");
        SetInfo(HostInfoIcon, _strings.Get("SubjectHost"), _strings.Get("InfoHost"));
        HostBox.PlaceholderText = _strings.Get("PlaceholderHost");
        PortCard.Header = _strings.Get("RowPort");
        PortDescription.Text = _strings.Get("DescPort");
        SetInfo(PortInfoIcon, _strings.Get("SubjectPort"), _strings.Get("InfoPort"));
        PortCustomBox.PlaceholderText = _strings.Get("PlaceholderPort");
        TransportCard.Header = _strings.Get("RowTransport");
        TransportDescription.Text = _strings.Get("DescTransport");
        SetInfo(TransportInfoIcon, _strings.Get("SubjectTransport"), _strings.Get("InfoTransport"));
        EncryptionCard.Header = _strings.Get("RowEncryption");
        EncryptionDescription.Text = _strings.Get("DescEncryption");
        SetInfo(EncryptionInfoIcon, _strings.Get("SubjectEncryption"), _strings.Get("InfoEncryption"));
        UsernameCard.Header = _strings.Get("RowUsername");
        UsernameDescription.Text = _strings.Get("DescUsername");
        SetInfo(UsernameInfoIcon, _strings.Get("SubjectUsername"), _strings.Get("InfoUsername"));
        PasswordCard.Header = _strings.Get("RowPassword");
        PasswordDescription.Text = _strings.Get("DescPassword");
        SetInfo(PasswordInfoIcon, _strings.Get("SubjectPassword"), _strings.Get("InfoPassword"));
        PrefixCard.Header = _strings.Get("RowDiscoveryPrefix");
        PrefixDescription.Text = _strings.Get("DescDiscoveryPrefix");
        SetInfo(PrefixInfoIcon, _strings.Get("SubjectDiscoveryPrefix"), _strings.Get("InfoDiscoveryPrefix"));

        ApplyCard.Header = _strings.Get("RowApply");
        ApplyDescription.Text = _strings.Get("DescApply");
        ApplyBtn.Content = _strings.Get("ButtonApply");
        TestBtn.Content = _strings.Get("ButtonTest");
        AppliedText.Text = _strings.Get("Applied");
        SetInfo(TestInfoIcon, _strings.Get("SubjectTest"), _strings.Get("InfoTest"));

        PublishHeading.Text = _strings.Get("HeadingPublish");
    }

    /// <summary>The strings the host owns. What an application publishes is the one thing the module
    /// cannot write, including how to describe the publish section as a whole.</summary>
    private void ApplyHostText(MqttPanelSetup setup)
    {
        if (setup.PublishTitle is { Length: > 0 } title) PublishCard.Header = title;
        if (setup.PublishDescription is { Length: > 0 } description) PublishDescriptionText.Text = description;
        if (setup.PublishInfo is { Length: > 0 } info)
            SetInfo(PublishInfoIcon, _strings.Get("SubjectPublishSwitch"), WithModuleVersion(info));

        // No fallback here on purpose: an icon opening on nothing is worse than no icon.
        bool hasGroupsInfo = !string.IsNullOrWhiteSpace(setup.PublishGroupsInfo);
        if (hasGroupsInfo)
            SetInfo(PublishGroupsInfoIcon, _strings.Get("SubjectPublishGroups"), setup.PublishGroupsInfo!);
        PublishGroupsInfoIcon.Visibility = hasGroupsInfo ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SetInfo(Brand.WinUI.InfoIcon icon, string subject, string info)
    {
        icon.Subject = subject;
        icon.Info = info;
    }

    /// <summary>The publish switch's text with the module build in force appended.</summary>
    /// <remarks>Version information conventionally lives in an About box, and nobody opens one while
    /// chasing a broker that will not connect. This icon is in front of the reader at the moment the
    /// question arises, so a consumer that has done nothing at all still reports which build produced
    /// the behaviour being described. It rides whatever text is in force, the host's included, so
    /// supplying <see cref="MqttPanelSetup.PublishInfo"/> does not silently drop it.</remarks>
    private string WithModuleVersion(string info) =>
        $"{info}\n\n{_strings.Format("ModuleVersion", MqttModule.Version)}";

    // ---------------------------------------------------------------------------------------------
    // The master switch.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Hides the four detail sections while publishing is off. Hidden, not disabled: a greyed
    /// panel still invites reading, and none of it has an answer until the feature is on.</summary>
    private void RefreshDetailVisibility()
    {
        DetailPanel.Visibility = EnabledToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        RefreshPublishNowEnabled();
    }

    // Enabled applies immediately: it is not one of the batched broker fields.
    private void OnEnabledToggled(object sender, RoutedEventArgs e)
    {
        if (_updating || _setup is not { } setup) return;

        bool on = EnabledToggle.IsOn;
        setup.Settings.Update(s => s.Enabled = on);
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

    /// <summary>The device ID's own confirmation dialogue. The id is the <c>unique_id</c> stem, the
    /// device identifier and every topic segment, so changing it renames every published entity —
    /// hence the separate dialogue, gated on a valid different id and an acknowledgement.</summary>
    private async void OnChangeDeviceIdClicked(object sender, RoutedEventArgs e)
    {
        if (_setup is not { } setup) return;

        try
        {
            string current = EffectiveDeviceId();

            var idBox = new TextBox
            {
                Text = setup.Settings.Read().DeviceId,
                PlaceholderText = MqttIdentity.Default(setup.TopicRoot, Environment.MachineName),
            };
            var errorText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
                Style = StyleFor("MqttResultErrorStyle"),
            };
            // The id is sanitised to [a-z0-9_] before it reaches a topic, so echo what will be
            // published — otherwise "Office ThinkPad" silently becomes something else.
            // The panel's own secondary tier rather than an opacity: an opacity is a tone no theme
            // key can reach, so a host that rebrands the panel cannot rebrand these two lines.
            var previewText = new TextBlock { Style = StyleFor("MqttResultStyle") };
            // Names the mechanism rather than one application's consequence of it: a consumer with no
            // automations still has dashboards, history or nothing at all, and the module cannot know
            // which. A host with a sharper consequence supplies it beside this.
            var ack = new CheckBox { Content = _strings.Get("DeviceIdAcknowledge") };

            var body = new StackPanel { Spacing = 8, Width = 420 };
            body.Children.Add(new TextBlock
            {
                Text = _strings.Format("DeviceIdCurrent", current),
                TextWrapping = TextWrapping.Wrap,
            });
            body.Children.Add(new TextBlock
            {
                Text = _strings.Get("DeviceIdNew"),
                Style = StyleFor("MqttResultStyle"),
            });
            body.Children.Add(idBox);
            body.Children.Add(previewText);
            body.Children.Add(errorText);
            body.Children.Add(new TextBlock
            {
                Text = _strings.Get("DeviceIdWarning"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
            if (setup.DeviceIdConsequence is { Length: > 0 } consequence)
                body.Children.Add(new TextBlock { Text = consequence, TextWrapping = TextWrapping.Wrap });
            body.Children.Add(ack);

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                // The dialogue is rooted beside the page rather than inside it, so it does not
                // inherit a RequestedTheme the host set on the subtree holding the panel.
                RequestedTheme = ActualTheme,
                Title = _strings.Get("DeviceIdTitle"),
                Content = body,
                PrimaryButtonText = _strings.Get("DeviceIdConfirm"),
                CloseButtonText = _strings.Get("DeviceIdCancel"),
                DefaultButton = ContentDialogButton.Close,
                IsPrimaryButtonEnabled = false,
            };

            // The primary button is the only gate, so re-derive it from scratch on every edit rather
            // than tracking a "was valid" flag that can go stale.
            void Revalidate()
            {
                string raw = idBox.Text ?? "";
                string? error = MqttIdentity.Validate(raw, _strings);
                string candidate = MqttIdentity.Effective(raw, setup.TopicRoot, Environment.MachineName);

                errorText.Text = error ?? "";
                errorText.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
                previewText.Text = error is null ? _strings.Format("DeviceIdPreview", candidate) : "";

                dialog.IsPrimaryButtonEnabled = error is null && candidate != current && ack.IsChecked == true;
            }

            idBox.TextChanged += (_, _) => Revalidate();
            ack.Checked += (_, _) => Revalidate();
            ack.Unchecked += (_, _) => Revalidate();
            Revalidate();

            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            if (_closed) return;

            // Store the sanitised form: the card above shows the effective id and the two must not
            // disagree. Blank stays blank — that is the "use the machine default" sentinel.
            string entered = (idBox.Text ?? "").Trim();
            string newId = entered.Length == 0 ? "" : MqttIdentity.Normalise(entered);

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
        PortCombo.Items.Add(new ComboBoxItem { Content = _strings.Get("OptionAutomatic") });
        foreach (int port in MqttEndpointPlan.OfferedPorts)
            PortCombo.Items.Add(new ComboBoxItem { Content = port.ToString(CultureInfo.InvariantCulture) });
        PortCombo.Items.Add(new ComboBoxItem { Content = _strings.Get("PortCustom") });
    }

    /// <summary>The transport and encryption dropdowns. Their order is the enum's, so the read-back is
    /// positional and there is no lookup table to disagree with either end.</summary>
    private void BuildModeCombos()
    {
        TransportCombo.Items.Clear();
        TransportCombo.Items.Add(new ComboBoxItem { Content = _strings.Get("OptionAutomatic") });
        TransportCombo.Items.Add(new ComboBoxItem { Content = _strings.Get("TransportTcp") });
        TransportCombo.Items.Add(new ComboBoxItem { Content = _strings.Get("TransportWebSocket") });

        EncryptionCombo.Items.Clear();
        EncryptionCombo.Items.Add(new ComboBoxItem { Content = _strings.Get("OptionAutomatic") });
        EncryptionCombo.Items.Add(new ComboBoxItem { Content = _strings.Get("ToggleOn") });
        EncryptionCombo.Items.Add(new ComboBoxItem { Content = _strings.Get("ToggleOff") });
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

        var validation = _edits.Validate();
        PortErrorText.Text = validation.Message ?? "";
        PortErrorText.Visibility = validation.Message is null ? Visibility.Collapsed : Visibility.Visible;

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
        SetSummary(BrokerExpanderDescription, _text.SummariseBroker(request, memory, state));
        SetSummary(PublishExpanderDescription, _text.SummarisePublish(MqttPublishRows.Tally(setup.Groups)));
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

    /// <summary>Runs the age tick only while the page is on screen. A settings page nobody is looking
    /// at must not cost a consuming application a timer.</summary>
    private void UpdateAgeTick()
    {
        bool wanted = !_closed && IsLoaded && Visibility == Visibility.Visible;
        if (wanted && !_ageTick.IsEnabled) _ageTick.Start();
        else if (!wanted && _ageTick.IsEnabled) _ageTick.Stop();
    }

    /// <summary>Live only when there is somewhere to publish to — the feature on, a broker connected,
    /// and nothing already in flight.</summary>
    private void RefreshPublishNowEnabled() =>
        PublishNowBtn.IsEnabled = !_publishing && EnabledToggle.IsOn
                               && _setup?.ConnectionState() == MqttConnectionState.Connected;

    /// <summary>Republishes the current state on demand. State only: what the entities already are,
    /// never a fresh announcement, so no retained config topic is rewritten. Awaited on the UI thread,
    /// which is where the continuation resumes — no raw dispatcher callback, whose unhandled exception
    /// would take the process down as a stowed exception.</summary>
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
            if (!sent) SetStatusValue(LastPublishText, _strings.Get("PublishFailed"), error: true);
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

        var validation = _edits.Validate();
        var request = _edits.Request;

        // One gate for the button and for Apply, and a refusal is still an answer: an action that
        // appears to do nothing at all is the worse failure.
        if (!validation.Usable)
        {
            if (trigger == MqttProbeTrigger.TestConnection)
                _probe.Refuse(validation.Message ?? _strings.Format("PortNotANumber",
                    MqttBrokerEdits.PortMin, MqttBrokerEdits.PortMax));
            RefreshEditIndicators();
            return;
        }

        if (!MqttEndpointPlan.ShouldProbe(trigger, EnabledToggle.IsOn, request.Host))
        {
            if (trigger == MqttProbeTrigger.TestConnection) _probe.Refuse(_strings.Get("ReportNoHost"));
            RefreshEditIndicators();
            RefreshStatus();
            return;
        }

        RunProbe(request);
    }

    /// <summary>async void, and guarded whole: nothing may escape into the dispatcher. Never blocks
    /// the UI thread — every stage of the sweep is a socket wait, and the only work back here is the
    /// progress line and the result.</summary>
    private async void RunProbe(MqttEndpointRequest request)
    {
        if (_setup is not { } setup) return;

        var cts = new CancellationTokenSource();
        _probeCts = cts;
        long token = _probe.Start();
        RefreshEditIndicators();
        RefreshStatus();

        try
        {
            var saved = setup.Settings.Read();
            var target = new MqttProbeTarget(
                Host: request.Host,
                Port: request.Port,
                Username: request.Username,
                Password: _edits.Password,
                ClientId: MqttProbe.ProbeClientId(EffectiveDeviceId()),
                Transport: request.Transport,
                Encryption: request.Encryption,
                // Read, never written. A test that recorded where the broker answered would change
                // the sweep order of the live connection, which is precisely what it promises not to
                // do.
                Memory: Memory(),
                // Under the same trust the connection uses, or a probe passes where the link fails.
                CertificateTrust: saved.CertificateTrust);

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
            // Unconditional, and keyed on nothing. The identity check this replaces did not run for a
            // cancelled probe with no successor, which left the button disabled and the spinner
            // turning until the window closed.
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
                OnContent = _strings.Get("ToggleOn"),
                OffContent = _strings.Get("ToggleOff"),
                IsOn = row.On,
                Tag = row.Key,
            };
            toggle.Toggled += OnGroupToggled;
            _groupToggles[row.Key] = toggle;

            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (row.HasInfo)
                content.Children.Add(new Brand.WinUI.InfoIcon { Subject = row.InfoSubject, Info = row.Info });
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
