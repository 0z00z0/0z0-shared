using System.Threading.Channels;
using MQTTnet;
using ZeroZero.Primitives;

namespace ZeroZero.Mqtt;

/// <summary>The inbound path of one <see cref="MqttConnection"/>: the receive callback, the command
/// routing, and the single worker that runs what was accepted.</summary>
internal sealed class MqttInbound : IDisposable
{
    private readonly MqttConnectionSetup _setup;
    private readonly ILogSink _log;
    private readonly MqttCommandRouter _router;
    private readonly MqttActivity _activity;
    private readonly Func<string> _deviceId;
    private readonly Func<IReadOnlyList<MqttSubscription>> _subscriptions;
    private readonly Func<string, CancellationToken, Task> _clearRetained;

    // Drained on a dedicated worker off the MQTT receive callback, which must return promptly. Single
    // reader: one command's read-modify-write must finish before the next starts.
    private readonly Channel<(string Source, Func<CancellationToken, Task> Run)> _work =
        Channel.CreateUnbounded<(string, Func<CancellationToken, Task>)>(
            new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _workerStop = new();
    private readonly Task _worker;

    // Command topics this connection has just emptied. The broker delivers the clear back with an
    // empty payload and the retain flag down: the same bytes as an empty command, a value for a text.
    private readonly Lock _selfClearedLock = new();
    private readonly HashSet<string> _selfCleared = new(StringComparer.Ordinal);

    /// <param name="clearRetained">Publishes an empty retained payload on a topic.</param>
    public MqttInbound(
        MqttConnectionSetup setup, MqttCommandRouter router, MqttActivity activity,
        Func<string> deviceId, Func<IReadOnlyList<MqttSubscription>> subscriptions,
        Func<string, CancellationToken, Task> clearRetained)
    {
        _setup = setup;
        _log = setup.Log;
        _router = router;
        _activity = activity;
        _deviceId = deviceId;
        _subscriptions = subscriptions;
        _clearRetained = clearRetained;
        _worker = Task.Run(ProcessWorkAsync);
    }

    /// <summary>Inbound handler. Runs on the MQTT receive callback, so it only judges and enqueues.
    /// Never throws — the MQTT loop must survive a bad payload — and never logs the payload.</summary>
    public Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            var message = new MqttInboundMessage(
                e.ApplicationMessage.Topic,
                e.ApplicationMessage.ConvertPayloadToString() ?? "",
                e.ApplicationMessage.Retain);

            if (RouteCommand(message)) return Task.CompletedTask;

            foreach (var subscription in _subscriptions())
                if (subscription.Matches(message.Topic))
                    Enqueue($"Subscription:{subscription.TopicFilter}",
                            ct => subscription.Handler(message, ct));
        }
        catch (Exception ex) { _log.Error($"MqttConnection.OnMessage: {MqttProbe.Describe(ex)}", null); }
        return Task.CompletedTask;
    }

    /// <summary>True when the message belonged to the command subtree, whatever became of it.</summary>
    private bool RouteCommand(MqttInboundMessage message)
    {
        // The echo of this connection's own retained-clear. It arrives with the retain flag down and
        // an empty payload, and an entity that takes an empty string as a value would act on it.
        if (message.Payload.Length == 0 && TakeSelfCleared(message.Topic)) return true;

        var routing = _router.Route(
            _setup.TopicRoot, _deviceId(), message.Topic, message.Retained, message.Payload);

        if (routing.EntityId is not { } entityId) return false;

        // A command is an event, not state. Left retained it is redelivered and refused on every
        // reconnect for ever, and only whoever put it there could otherwise clear it.
        if (routing.Verdict.Outcome == MqttCommandOutcome.Retained) ClearRetainedCommand(message.Topic);

        if (routing.Verdict is { Outcome: MqttCommandOutcome.Accepted, Run: { } run })
        {
            // On acceptance, not completion: the status line answers "is the broker reaching us".
            _activity.RecordCommand(entityId);
            Enqueue($"Command:{entityId}", run);
            return true;
        }

        // Not acted on. The facts are the module's; the wording is the entity's, carried verbatim.
        _setup.CommandRefused?.Invoke(new MqttCommandRefusal(
            DateTimeOffset.UtcNow, entityId, routing.Verdict.Outcome, routing.Verdict.Detail));
        return true;
    }

    /// <summary>Empties a command topic something left retained, and notes that the clear's own echo
    /// is to be ignored.</summary>
    private void ClearRetainedCommand(string topic)
    {
        lock (_selfClearedLock) _selfCleared.Add(topic);
        Enqueue($"ClearRetained:{topic}", ct => _clearRetained(topic, ct));
    }

    /// <summary>Whether an empty payload on this topic is this connection's own clear coming back.
    /// Taken rather than read: the note stands for one message.</summary>
    private bool TakeSelfCleared(string topic)
    {
        lock (_selfClearedLock) return _selfCleared.Remove(topic);
    }

    private void Enqueue(string source, Func<CancellationToken, Task> run) =>
        _work.Writer.TryWrite((source, run));   // unbounded and non-blocking; the worker drains in order

    /// <summary>Drains the queue one item at a time, so a blocking read-modify-write completes before
    /// the next starts. Ends when the writer is completed.</summary>
    private async Task ProcessWorkAsync()
    {
        await foreach (var (source, run) in _work.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                // Any republish happens through the application's own change signal.
                await run(_workerStop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_workerStop.IsCancellationRequested) { break; }
            catch (Exception ex) { _log.Error($"MqttConnection.{source}: {MqttProbe.Describe(ex)}", null); }
        }
    }

    public void Complete() => _work.Writer.TryComplete();

    public void StopWorker()
    {
        _workerStop.Cancel();
        try { _worker.Wait(TimeSpan.FromSeconds(2)); } catch { /* a handler outliving the budget */ }
    }

    public void Dispose() => _workerStop.Dispose();
}
