using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace WaterBilling.Data;

// Tells open screens (Dashboard, Outstanding) that saved data has changed, so they can reload
// their figures. One instance for the whole app: a save in one browser window updates every
// other open window. Screens only subscribe to the event; they still read data through services.
public sealed class DataChangeNotifier
{
    public event Action? Changed;

    public void Notify() => Changed?.Invoke();
}

// Raises the notification only once data is really saved:
//  - a save inside a transaction (invoice + ledger lines): when the transaction commits, not before,
//    so a screen never reloads while the new rows are still invisible to it;
//  - a save without a transaction (e.g. editing a supplier): right after the save.
public sealed class DataChangeInterceptor(DataChangeNotifier notifier) : DbTransactionInterceptor, ISaveChangesInterceptor
{
    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        notifier.Notify();

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        notifier.Notify();
        return Task.CompletedTask;
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        NotifyIfNoTransaction(eventData.Context);
        return result;
    }

    public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        NotifyIfNoTransaction(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void NotifyIfNoTransaction(DbContext? context)
    {
        if (context?.Database.CurrentTransaction is null) notifier.Notify();
    }
}
