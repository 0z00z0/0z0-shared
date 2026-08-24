using ZeroZero.Config;

namespace ZeroZero.Mqtt;

/// <summary>The settings store over one JSON file. The module owns the file name; the host owns the
/// directory.</summary>
/// <remarks>
/// <para>That split is what lets several applications use the module and each get its own file
/// without any of them knowing the others' layout, and without the module ever composing a product
/// name. The file sits beside the host's own settings file; it is not a section inside it.</para>
/// <para>A host whose configuration is a single document — with cross-field write traps that make a
/// second file the wrong answer — implements <see cref="IMqttSettingsStore"/>'s three members over
/// that document instead and never constructs this class.</para>
/// </remarks>
public sealed class MqttSettingsFile : IMqttSettingsStore, IDisposable
{
    /// <summary>The name the module owns.</summary>
    public const string DefaultFileName = "mqtt.json";

    private readonly SettingsFile<MqttSettings> _file;

    public MqttSettingsFile(SettingsFileOptions options)
    {
        _file = new SettingsFile<MqttSettings>(options);
        _file.Changed += OnFileChanged;
    }

    /// <summary>The whole of the wiring: one line, and the host has a store.</summary>
    public static MqttSettingsFile In(string directory) =>
        new(new SettingsFileOptions(directory, DefaultFileName));

    /// <summary>The file behind the store, for a host that wants to hear about a failed write or to
    /// reload after an external edit.</summary>
    public SettingsFile<MqttSettings> File => _file;

    public string FilePath => _file.FilePath;

    public MqttSettings Read() => _file.Read();

    public void Update(Action<MqttSettings> mutate) => _file.Update(mutate);

    /// <summary>Raised after the stored state changes, outside the file's own lock.</summary>
    public event Action? Changed;

    public void Dispose() => _file.Changed -= OnFileChanged;

    private void OnFileChanged(object? sender, EventArgs e) => Changed?.Invoke();
}
