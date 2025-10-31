namespace PackageGuardian.Core.Repository
{
    /// <summary>
    /// Options for checkout operations.
    /// </summary>
    public sealed record CheckoutOptions
    {
        /// <summary>
        /// Overwrite existing files.
        /// </summary>
        public bool Overwrite { get; init; }
        
        /// <summary>
        /// Clean untracked files in target.
        /// </summary>
        public bool CleanUntracked { get; init; }
        
        /// <summary>
        /// Verify file integrity after checkout.
        /// </summary>
        public bool VerifyIntegrity { get; init; }

        public CheckoutOptions(bool overwrite = true, bool cleanUntracked = false, bool verifyIntegrity = true)
        {
            Overwrite = overwrite;
            CleanUntracked = cleanUntracked;
            VerifyIntegrity = verifyIntegrity;
        }

        public static CheckoutOptions Default => new CheckoutOptions();
    }
}

