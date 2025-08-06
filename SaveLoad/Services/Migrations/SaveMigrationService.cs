using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UltraLiteDB;
using UnityEngine;

namespace Core.ECS.SaveLoad
{
    public class SaveMigrationService : ISaveMigrationService
    {
        private readonly ConcurrentDictionary<string, List<IMigration>> _migrations = new();
        private readonly object _migrationLock = new object();

        public void RegisterMigration<T>(string fromVersion, string toVersion,
            Func<BsonDocument, BsonDocument> migration) where T : ISaveableData
        {
            if (migration == null)
                throw new ArgumentNullException(nameof(migration));

            if (!IsValidVersionString(fromVersion))
                throw new ArgumentException($"Invalid fromVersion: {fromVersion}", nameof(fromVersion));

            if (!IsValidVersionString(toVersion))
                throw new ArgumentException($"Invalid toVersion: {toVersion}", nameof(toVersion));

            var fromVer = new Version(fromVersion);
            var toVer   = new Version(toVersion);

            if (fromVer >= toVer)
                throw new ArgumentException($"fromVersion ({fromVersion}) must be less than toVersion ({toVersion})");

            var typeName = typeof(T).FullName ??
                throw new InvalidOperationException($"Type {typeof(T)} has no full name");

            lock (_migrationLock)
            {
                var migrationList = _migrations.GetOrAdd(typeName, _ => new List<IMigration>());

                foreach (var existing in migrationList)
                {
                    if (existing.FromVersion == fromVer && existing.ToVersion == toVer)
                    {
                        throw new InvalidOperationException(
                            $"Migration for {typeName} ({fromVersion}->{toVersion}) already registered");
                    }
                }

                migrationList.Add(new Migration<T>
                {
                    FromVersion = fromVer,
                    ToVersion   = toVer,
                    Migrate     = migration
                });

                migrationList.Sort(CompareMigrationsByFromVersion);
                _migrations[typeName] = migrationList;
            }
        }

        public BsonDocument ApplyMigrations(
            string typeName,
            string originalVersion,
            string currentVersion,
            BsonDocument data)
        {
            if (data == null)
                return null;

            lock (_migrationLock)
            {
                if (!_migrations.TryGetValue(typeName, out var allMigrations) ||
                    allMigrations == null ||
                    allMigrations.Count == 0)
                {
                    return data;
                }

                if (!TryParseVersion(originalVersion, out var fromVer))
                    throw new MigrationException(typeName,
                        $"Invalid original version: {originalVersion}");

                if (!TryParseVersion(currentVersion, out var toVer))
                    throw new MigrationException(typeName,
                        $"Invalid current version: {currentVersion}");

                var applicableMigrations = new List<IMigration>();
                foreach (var mig in allMigrations)
                {
                    if (mig.FromVersion >= fromVer && mig.ToVersion <= toVer)
                        applicableMigrations.Add(mig);
                }

                if (applicableMigrations.Count == 0)
                {
                    Debug.LogWarning(
                        $"No applicable migrations for {typeName} (v{originalVersion}->v{currentVersion})");
                    return data;
                }

                try
                {
                    var result = data;
                    foreach (var migration in applicableMigrations)
                    {
                        Debug.Log($"Applying migration {typeName} v{migration.FromVersion}->v{migration.ToVersion}");
                        var migrated = migration.Migrate(result);
                        if (migrated != null)
                            result = migrated;
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    throw new MigrationException(typeName,
                        $"Failed to apply migrations for {typeName}", ex);
                }
            }
        }

        private static bool IsValidVersionString(string version) => Version.TryParse(version, out _);

        private static bool TryParseVersion(string version, out Version result) =>
            Version.TryParse(version, out result);

        private static int CompareMigrationsByFromVersion(IMigration a, IMigration b) =>
            a.FromVersion.CompareTo(b.FromVersion);

        private interface IMigration
        {
            Version FromVersion { get; }
            Version ToVersion   { get; }
            BsonDocument Migrate(BsonDocument data);
        }

        private class Migration<T> : IMigration where T : ISaveableData
        {
            public Version FromVersion { get; set; }
            public Version ToVersion   { get; set; }
            public Func<BsonDocument, BsonDocument> Migrate { get; set; }

            BsonDocument IMigration.Migrate(BsonDocument data) => Migrate(data);
        }
    }

    internal sealed class MigrationException : Exception
    {
        public MigrationException(string dataType, string message)
            : base($"[{dataType}] {message}")
        {
        }

        public MigrationException(string dataType, string message, Exception inner)
            : base($"[{dataType}] {message}", inner)
        {
        }
    }
}