using System.Threading;

namespace CountdownDisplay;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private const string MutexName = "Local\\CountdownDisplay.SingleInstance";
    private const string ActivationEventName = "Local\\CountdownDisplay.OpenSettings";
    private Mutex? _mutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationWait;
    private bool _ownsMutex;

    public bool TryBecomePrimary(Action onActivationRequested)
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            try
            {
                _ownsMutex = _mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                _ownsMutex = true;
            }
            if (!_ownsMutex) return false;
        }
        else
        {
            _ownsMutex = true;
        }

        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        _activationWait = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, _) => onActivationRequested(),
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);
        return true;
    }

    public static void RequestActivation()
    {
        try
        {
            using var activationEvent = EventWaitHandle.OpenExisting(ActivationEventName);
            activationEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // 主实例正在初始化或刚退出；此时第二个实例直接退出即可。
        }
    }

    public void Dispose()
    {
        _activationWait?.Unregister(null);
        _activationEvent?.Dispose();
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }
}
