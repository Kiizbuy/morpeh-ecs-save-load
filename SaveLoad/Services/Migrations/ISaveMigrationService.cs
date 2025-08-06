using System;
using UltraLiteDB;

namespace Core.ECS.SaveLoad
{
    public interface ISaveMigrationService
    {
        void RegisterMigration<T>(string fromVersion, string toVersion,
            Func<BsonDocument, BsonDocument> migration) where T : ISaveableData;

        BsonDocument ApplyMigrations(
            string typeName,
            string originalVersion,
            string currentVersion,
            BsonDocument data);
    }
}