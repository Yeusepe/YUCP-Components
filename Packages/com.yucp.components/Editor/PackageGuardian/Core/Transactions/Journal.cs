using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PackageGuardian.Core.Transactions
{
    /// <summary>
    /// Write-ahead log for atomic ref updates.
    /// Ensures crash safety during ref operations.
    /// </summary>
    public sealed class Journal
    {
        private readonly string _journalPath;
        private readonly object _lock = new object();

        public Journal(string repositoryRoot)
        {
            if (string.IsNullOrWhiteSpace(repositoryRoot))
                throw new ArgumentNullException(nameof(repositoryRoot));
            
            string pgDir = Path.Combine(repositoryRoot, ".pg");
            _journalPath = Path.Combine(pgDir, "journal.log");
        }

        /// <summary>
        /// Begin a transaction by writing the operation to the journal.
        /// </summary>
        public void BeginTransaction(string refName, string newValue, string description)
        {
            lock (_lock)
            {
                var entry = new JournalEntry
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Operation = "update-ref",
                    RefName = refName,
                    NewValue = newValue,
                    Description = description
                };
                
                AppendEntry(entry);
            }
        }

        /// <summary>
        /// Commit a transaction by marking it complete.
        /// </summary>
        public void CommitTransaction(string refName)
        {
            lock (_lock)
            {
                var entry = new JournalEntry
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Operation = "commit",
                    RefName = refName
                };
                
                AppendEntry(entry);
            }
        }

        /// <summary>
        /// Clear the journal after successful operations.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                if (File.Exists(_journalPath))
                {
                    File.Delete(_journalPath);
                }
            }
        }

        /// <summary>
        /// Recover from journal by replaying incomplete transactions.
        /// Returns list of refs that were recovered.
        /// </summary>
        public List<RecoveredRef> Recover()
        {
            lock (_lock)
            {
                var recovered = new List<RecoveredRef>();
                
                if (!File.Exists(_journalPath))
                    return recovered;
                
                var pendingUpdates = new Dictionary<string, JournalEntry>();
                
                foreach (string line in File.ReadAllLines(_journalPath))
                {
                    var entry = ParseEntry(line);
                    if (entry == null)
                        continue;
                    
                    switch (entry.Operation)
                    {
                        case "update-ref":
                            pendingUpdates[entry.RefName] = entry;
                            break;
                        
                        case "commit":
                            // Transaction completed successfully, remove from pending
                            pendingUpdates.Remove(entry.RefName);
                            break;
                    }
                }
                
                // Any remaining pending updates need to be completed
                foreach (var kvp in pendingUpdates)
                {
                    recovered.Add(new RecoveredRef
                    {
                        RefName = kvp.Value.RefName,
                        NewValue = kvp.Value.NewValue,
                        Description = kvp.Value.Description
                    });
                }
                
                return recovered;
            }
        }

        private void AppendEntry(JournalEntry entry)
        {
            string line = SerializeEntry(entry);
            File.AppendAllText(_journalPath, line + "\n", Encoding.UTF8);
        }

        private string SerializeEntry(JournalEntry entry)
        {
            var sb = new StringBuilder();
            sb.Append(entry.Timestamp);
            sb.Append('|');
            sb.Append(entry.Operation);
            sb.Append('|');
            sb.Append(entry.RefName ?? "");
            sb.Append('|');
            sb.Append(entry.NewValue ?? "");
            sb.Append('|');
            sb.Append(entry.Description ?? "");
            return sb.ToString();
        }

        private JournalEntry ParseEntry(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;
            
            var parts = line.Split('|');
            if (parts.Length < 5)
                return null;
            
            try
            {
                return new JournalEntry
                {
                    Timestamp = long.Parse(parts[0]),
                    Operation = parts[1],
                    RefName = parts[2],
                    NewValue = parts[3],
                    Description = parts[4]
                };
            }
            catch
            {
                return null;
            }
        }

        private class JournalEntry
        {
            public long Timestamp { get; set; }
            public string Operation { get; set; }
            public string RefName { get; set; }
            public string NewValue { get; set; }
            public string Description { get; set; }
        }

        public class RecoveredRef
        {
            public string RefName { get; set; }
            public string NewValue { get; set; }
            public string Description { get; set; }
        }
    }
}

