using PackageGuardian.Core.Objects;

namespace PackageGuardian.Core.Storage
{
    /// <summary>
    /// Interface for object storage backend.
    /// </summary>
    public interface IObjectStore
    {
        /// <summary>
        /// Write object to storage and return its object ID.
        /// </summary>
        /// <param name="obj">Object to write</param>
        /// <param name="serializedData">Output: full serialized data (header + payload)</param>
        /// <returns>Object ID (hex string)</returns>
        string WriteObject(PgObject obj, out byte[] serializedData);

        /// <summary>
        /// Stage an object for writing without touching the filesystem yet.
        /// </summary>
        /// <param name="obj">Object to serialize</param>
        /// <returns>Handle describing the staged write</returns>
        StagedObjectWrite StageObject(PgObject obj);

        /// <summary>
        /// Flush a staged object write to disk.
        /// </summary>
        /// <param name="staged">Staged write handle</param>
        /// <returns>Object ID</returns>
        string CommitStagedObject(StagedObjectWrite staged);

        /// <summary>
        /// Read object from storage by ID.
        /// </summary>
        /// <param name="oid">Object ID (hex string)</param>
        /// <returns>Deserialized object</returns>
        PgObject ReadObject(string oid);

        /// <summary>
        /// Check if object exists in storage.
        /// </summary>
        /// <param name="oid">Object ID (hex string)</param>
        /// <returns>True if object exists</returns>
        bool Contains(string oid);
    }
}

