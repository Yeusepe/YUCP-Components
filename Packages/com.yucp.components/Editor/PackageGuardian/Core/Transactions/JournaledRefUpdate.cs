using System;

namespace PackageGuardian.Core.Transactions
{
    /// <summary>
    /// Transaction wrapper for atomic ref updates with journal protection.
    /// </summary>
    public sealed class JournaledRefUpdate : IDisposable
    {
        private readonly Journal _journal;
        private readonly string _refName;
        private readonly Action _updateAction;
        private bool _committed;
        private bool _disposed;

        public JournaledRefUpdate(Journal journal, string refName, string newValue, string description, Action updateAction)
        {
            _journal = journal ?? throw new ArgumentNullException(nameof(journal));
            _refName = refName ?? throw new ArgumentNullException(nameof(refName));
            _updateAction = updateAction ?? throw new ArgumentNullException(nameof(updateAction));
            
            // Write to journal
            _journal.BeginTransaction(refName, newValue, description);
        }

        /// <summary>
        /// Execute the ref update and commit the transaction.
        /// </summary>
        public void Commit()
        {
            if (_committed)
                throw new InvalidOperationException("Transaction already committed");
            
            // Perform the actual update
            _updateAction();
            
            // Mark as committed in journal
            _journal.CommitTransaction(_refName);
            
            _committed = true;
        }

        /// <summary>
        /// Rollback is implicit - if Commit() is not called, the journal entry remains
        /// and will be recovered on next repository open.
        /// </summary>
        public void Rollback()
        {
            // In this implementation, rollback is a no-op
            // The journal entry will remain and can be recovered
            _committed = false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            
            if (!_committed)
            {
                // Transaction was not committed, treat as rollback
                Rollback();
            }
            
            _disposed = true;
        }
    }
}

