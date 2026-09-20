using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;

namespace ZeroZero.Brand.WinUI;

/// <summary>
/// A borderless action button in the brand's own shape, placed by a host on its own: the logo's
/// square brackets at either end, a leading symbol, the label in the brand face, and a block caret
/// that blinks while the button is hovered or holds keyboard focus. The host sets
/// <see cref="Label"/>, <see cref="Symbol"/> and <see cref="State"/> and handles
/// <see cref="Click"/>; the button draws the state and owns every animation.
/// <para>
/// It sizes itself to its own text and centres itself in whatever width it is given, unless
/// <see cref="FillsWidth"/> is set, which spreads its brackets to the full width instead. A host
/// wanting several buttons one width puts them in a <see cref="BrandBracketButtonColumn"/>, which
/// measures the widest and sets the rest to match.
/// </para>
/// <para>
/// Motion follows the system's animation setting, read on every change and every tick: with
/// animations off nothing blinks, spins or moves, and the spinner is a still ellipsis.
/// </para>
/// </summary>
public sealed partial class BrandBracketButton : UserControl
{
    /// <summary>How far hover moves each bracket outwards, in effective pixels.</summary>
    private const double HoverSpread = 3;

    /// <summary>How far a press moves each bracket inwards, in effective pixels.</summary>
    private const double PressClose = 2;

    /// <summary>The caret's opacity when it is shown without blinking: pressed, or Attention at rest.</summary>
    private const double DimCaret = 0.4;

    private const string StillSpinner = "…";

    private static readonly string[] SpinnerFrames = ["|", "/", "-", "\\"];
    private static readonly TimeSpan SpinnerStep = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan CaretBlink = TimeSpan.FromMilliseconds(530);
    private static readonly TimeSpan SuccessHold = TimeSpan.FromSeconds(4);

    /// <summary>The symbol a button shows unless the host names another.</summary>
    public const string DefaultSymbol = ">";

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(BrandBracketButton),
        new PropertyMetadata(string.Empty, (d, _) => ((BrandBracketButton)d).ApplyLabel()));

    public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(
        nameof(Symbol), typeof(string), typeof(BrandBracketButton),
        new PropertyMetadata(DefaultSymbol, (d, _) => ((BrandBracketButton)d).ApplySymbol()));

    public static readonly DependencyProperty FillsWidthProperty = DependencyProperty.Register(
        nameof(FillsWidth), typeof(bool), typeof(BrandBracketButton),
        new PropertyMetadata(false, (d, _) => ((BrandBracketButton)d).ApplyFillsWidth()));

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(BrandBracketButtonState), typeof(BrandBracketButton),
        new PropertyMetadata(BrandBracketButtonState.Rest, (d, _) => ((BrandBracketButton)d).ApplyState()));

    private readonly UISettings _uiSettings = new();
    private readonly DispatcherQueueTimer _spinnerTimer;
    private readonly DispatcherQueueTimer _caretTimer;
    private readonly DispatcherQueueTimer _successTimer;

    /// <summary>Set between Loaded and Unloaded; nothing animates or counts down off screen.</summary>
    private bool _onScreen;

    private bool _moving;
    private bool _breathing;

    /// <summary>The outward offset the move storyboard last sent the brackets to.</summary>
    private double _bracketOffset;

    private int _spinnerFrame;
    private bool _caretOn;

    /// <summary>Raised when the button is pressed — by pointer, Enter or Space — in every state.</summary>
    public event RoutedEventHandler? Click;

    /// <summary>The text after the symbol, and the button's accessible name.</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>
    /// The character before the label — a cross for a cancel, an arrow for a download, whatever the
    /// act is. One plain character in the brand face rather than a picture, so it takes the theme
    /// and the scaling the rest of the button takes. A chevron where the host names none.
    /// </summary>
    /// <remarks>
    /// Only <see cref="BrandBracketButtonState.Rest"/> and
    /// <see cref="BrandBracketButtonState.Attention"/> show it: the spinner and the slashed zero
    /// take its place while <see cref="BrandBracketButtonState.Busy"/> and
    /// <see cref="BrandBracketButtonState.Success"/> are showing.
    /// </remarks>
    public string Symbol
    {
        get => (string)GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    /// <summary>
    /// Whether the button spreads its brackets to the whole width it is given rather than sizing to
    /// its own text and centring in it. Off, so a button placed on its own is unchanged; a
    /// <see cref="BrandBracketButtonColumn"/> turns it on for the buttons it holds. The label and
    /// the symbol stay centred between the brackets either way.
    /// </summary>
    public bool FillsWidth
    {
        get => (bool)GetValue(FillsWidthProperty);
        set => SetValue(FillsWidthProperty, value);
    }

    /// <summary>What the button shows. <see cref="BrandBracketButtonState.Success"/> returns to
    /// <see cref="BrandBracketButtonState.Rest"/> by itself after four seconds; every other state
    /// holds until the host sets another.</summary>
    public BrandBracketButtonState State
    {
        get => (BrandBracketButtonState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public BrandBracketButton()
    {
        InitializeComponent();

        _spinnerTimer = CreateTimer(SpinnerStep, repeating: true, OnSpinnerTick);
        _caretTimer = CreateTimer(CaretBlink, repeating: true, OnCaretTick);
        _successTimer = CreateTimer(SuccessHold, repeating: false, OnSuccessElapsed);

        Surface.Click += (_, e) => Click?.Invoke(this, e);

        // Hover, press and keyboard focus each change what the brackets and the caret do.
        Surface.RegisterPropertyChangedCallback(ButtonBase.IsPointerOverProperty, (_, _) => UpdateMotion());
        Surface.RegisterPropertyChangedCallback(ButtonBase.IsPressedProperty, (_, _) => UpdateMotion());
        Surface.GotFocus += (_, _) => UpdateMotion();
        Surface.LostFocus += (_, _) => UpdateMotion();

        Loaded += (_, _) =>
        {
            _onScreen = true;
            ApplyState();
        };
        Unloaded += (_, _) =>
        {
            _onScreen = false;
            StopEverything();
        };

        ApplyLabel();
        ApplySymbol();
        ApplyFillsWidth();
    }

    private bool AnimationsEnabled => _uiSettings.AnimationsEnabled;

    private DispatcherQueueTimer CreateTimer(TimeSpan interval, bool repeating, Action tick)
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = interval;
        timer.IsRepeating = repeating;
        timer.Tick += (_, _) => tick();
        return timer;
    }

    private void ApplyLabel()
    {
        string label = Label ?? string.Empty;
        LabelText.Text = label;
        AutomationProperties.SetName(Surface, label);
    }

    private void ApplySymbol() => SymbolText.Text = Symbol ?? DefaultSymbol;

    private void ApplyFillsWidth() =>
        Surface.HorizontalAlignment = FillsWidth ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;

    private void ApplyState()
    {
        VisualStateManager.GoToState(this, State.ToString(), false);

        _successTimer.Stop();
        if (_onScreen && State == BrandBracketButtonState.Success) _successTimer.Start();

        UpdateSpinner();
        UpdateMotion();
    }

    private void OnSuccessElapsed()
    {
        if (State == BrandBracketButtonState.Success) State = BrandBracketButtonState.Rest;
    }

    private void UpdateSpinner()
    {
        if (!_onScreen || State != BrandBracketButtonState.Busy)
        {
            _spinnerTimer.Stop();
            return;
        }

        if (!AnimationsEnabled)
        {
            _spinnerTimer.Stop();
            Spinner.Text = StillSpinner;
            return;
        }

        Spinner.Text = SpinnerFrames[_spinnerFrame];
        if (!_spinnerTimer.IsRunning) _spinnerTimer.Start();
    }

    private void OnSpinnerTick()
    {
        // The setting can change while the work runs; the next tick is where that is noticed.
        if (!AnimationsEnabled || State != BrandBracketButtonState.Busy)
        {
            UpdateSpinner();
            UpdateMotion();
            return;
        }

        _spinnerFrame = (_spinnerFrame + 1) % SpinnerFrames.Length;
        Spinner.Text = SpinnerFrames[_spinnerFrame];
    }

    /// <summary>Brings the brackets and the caret into line with the state, the pointer, the
    /// keyboard focus and the system's animation setting.</summary>
    private void UpdateMotion()
    {
        bool motion = _onScreen && AnimationsEnabled;
        bool pressed = Surface.IsPressed;
        bool hovered = Surface.IsPointerOver;
        bool keyboardFocus = Surface.FocusState == FocusState.Keyboard;

        if (!motion) StopBrackets();
        else if (State == BrandBracketButtonState.Busy) Breathe();
        else MoveBrackets(pressed ? -PressClose : hovered ? HoverSpread : 0);

        // The caret belongs to Rest and Attention only; work running or a success being reported
        // shows none.
        bool caretState = State is BrandBracketButtonState.Rest or BrandBracketButtonState.Attention;

        if (!caretState) SetCaret(0);
        else if (pressed) SetCaret(DimCaret);
        else if (hovered || keyboardFocus)
        {
            if (motion) StartBlink();
            else SetCaret(1);
        }
        else SetCaret(State == BrandBracketButtonState.Attention ? DimCaret : 0);
    }

    private void SetCaret(double opacity)
    {
        _caretTimer.Stop();
        Caret.Opacity = opacity;
    }

    private void StartBlink()
    {
        if (_caretTimer.IsRunning) return;
        _caretOn = true;
        Caret.Opacity = 1;
        _caretTimer.Start();
    }

    private void OnCaretTick()
    {
        if (!AnimationsEnabled)
        {
            UpdateMotion();
            return;
        }

        _caretOn = !_caretOn;
        Caret.Opacity = _caretOn ? 1 : 0;
    }

    /// <summary>Sends the brackets to an outward offset: positive spreads them, negative closes them.</summary>
    private void MoveBrackets(double outward)
    {
        if (_moving && outward == _bracketOffset) return;

        // Breathing stops back at rest, so a move out of it starts from there.
        double from = _moving ? _bracketOffset : 0;
        StopBrackets();

        var move = (Storyboard)Resources["MoveBrackets"];
        var left = (DoubleAnimation)move.Children[0];
        var right = (DoubleAnimation)move.Children[1];
        left.From = -from;
        left.To = -outward;
        right.From = from;
        right.To = outward;
        move.Begin();

        _moving = true;
        _bracketOffset = outward;
    }

    private void Breathe()
    {
        if (_breathing) return;
        StopBrackets();
        ((Storyboard)Resources["BreatheBrackets"]).Begin();
        _breathing = true;
    }

    /// <summary>Stops both storyboards, which returns the brackets to where the markup puts them.</summary>
    private void StopBrackets()
    {
        ((Storyboard)Resources["MoveBrackets"]).Stop();
        ((Storyboard)Resources["BreatheBrackets"]).Stop();
        _moving = false;
        _breathing = false;
        _bracketOffset = 0;
    }

    private void StopEverything()
    {
        _spinnerTimer.Stop();
        _caretTimer.Stop();
        _successTimer.Stop();
        StopBrackets();
    }
}
