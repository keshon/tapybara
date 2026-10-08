namespace Tapybara.Core.Concurrency;

/// <summary>
/// Отмена до точки сохранения; начатая фиксация заканчивается целиком.
/// </summary>
/// <remarks>
/// CAS выбирает победителя между Cancel и Commit без блокировки UI на
/// файловом вводе-выводе. Это не транзакция нескольких файлов при падении
/// процесса; владельцу всё равно нужны атомарные записи и восстановление.
/// </remarks>
public sealed class CancelableOperation : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private int _state;

    /// <summary>Токен вычислительной части; сохранение после Commit уже не отменяется.</summary>
    public CancellationToken Token { get; }

    /// <summary>Отдельная операция на каждое задание, а не на каждую страницу UI.</summary>
    public CancelableOperation() => Token = _cancellation.Token;

    /// <summary>Возвращает false, если фиксация уже началась или отмена уже запрошена.</summary>
    public bool TryCancel()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
        {
            return false;
        }

        _cancellation.Cancel();
        return true;
    }

    /// <summary>Ровно один блок сохранения: отменённая операция в него не войдёт.</summary>
    public void Commit(Action write)
    {
        ArgumentNullException.ThrowIfNull(write);
        int prior = Interlocked.CompareExchange(ref _state, 2, 0);
        if (prior == 1)
        {
            throw new OperationCanceledException(Token);
        }

        if (prior != 0)
        {
            throw new InvalidOperationException("Операция уже зафиксирована или завершена.");
        }

        write();
    }

    /// <summary>Вызывается после завершения работы, не одновременно с TryCancel.</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _state, 3);
        _cancellation.Dispose();
    }
}
