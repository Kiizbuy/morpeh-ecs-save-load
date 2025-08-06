using System;
using System.Collections.Generic;
using System.Linq;
using UltraLiteDB;

namespace Core.ECS.SaveLoad
{
    public class SaveMigrationBuilder<T> where T : ISaveableData
    {
        private readonly ISaveMigrationService _migrationService;
        private string _currentFromVersion;
        private string _currentToVersion;

        public SaveMigrationBuilder(ISaveMigrationService migrationService)
        {
            _migrationService = migrationService;
        }

        public SaveMigrationBuilder<T> FromVersion(string version)
        {
            _currentFromVersion = version;
            return this;
        }

        public SaveMigrationBuilder<T> ToVersion(string version)
        {
            _currentToVersion = version;
            return this;
        }

        public SaveMigrationBuilder<T> RenameField(string oldFieldName, string newFieldName)
        {
            return WithMigration(doc =>
            {
                if (doc.ContainsKey(oldFieldName))
                {
                    doc[newFieldName] = doc[oldFieldName];
                    doc.Remove(oldFieldName);
                }
                return doc;
            });
        }

        public SaveMigrationBuilder<T> ConvertField(string fieldName, Func<BsonValue, BsonValue> converter)
        {
            return WithMigration(doc =>
            {
                if (doc.ContainsKey(fieldName))
                {
                    doc[fieldName] = converter(doc[fieldName]);
                }
                return doc;
            });
        }

        public SaveMigrationBuilder<T> AddField(string fieldName, Func<BsonDocument, BsonValue> valueFactory)
        {
            return WithMigration(doc =>
            {
                if (!doc.ContainsKey(fieldName))
                {
                    doc[fieldName] = valueFactory(doc);
                }
                return doc;
            });
        }

        public SaveMigrationBuilder<T> RemoveField(string fieldName)
        {
            return WithMigration(doc =>
            {
                doc.Remove(fieldName);
                return doc;
            });
        }

        public SaveMigrationBuilder<T> ValidateTypeChange(Action<BsonDocument> validator)
        {
            return WithMigration(doc =>
            {
                validator(doc);
                return doc;
            });
        }

        public SaveMigrationBuilder<T> ChangeDataType(string oldTypeName, Func<BsonDocument, BsonDocument> typeConverter)
        {
            return WithMigration(doc =>
            {
                doc["__original_type"] = oldTypeName;
                return typeConverter(doc);
            });
        }

        public SaveMigrationBuilder<T> ReplaceFieldValue<TValue>(
            string fieldName, 
            TValue oldValue, 
            TValue newValue,
            IEqualityComparer<TValue> comparer = null)
        {
            return WithMigration(doc =>
            {
                if (doc.ContainsKey(fieldName))
                {
                    var currentValue = BsonMapper.Global.ToObject<TValue>(doc[fieldName].AsDocument);
                    var equalityComparer = comparer ?? EqualityComparer<TValue>.Default;

                    if (equalityComparer.Equals(currentValue, oldValue))
                    {
                        doc[fieldName] = BsonMapper.Global.ToDocument(newValue);
                    }
                }
                return doc;
            });
        }

        public SaveMigrationBuilder<T> ReplaceFieldValue<TValue>(
            string fieldName,
            Func<TValue, bool> shouldReplace,
            Func<TValue, TValue> newValueFactory)
        {
            return WithMigration(doc =>
            {
                if (doc.ContainsKey(fieldName))
                {
                    var currentValue = BsonMapper.Global.ToObject<TValue>(doc[fieldName].AsDocument);
                    if (shouldReplace(currentValue))
                    {
                        doc[fieldName] = BsonMapper.Global.ToDocument(newValueFactory(currentValue));
                    }
                }
                return doc;
            });
        }

        public SaveMigrationBuilder<T> ReplaceFieldValue<TFrom, TTo>(
            string fieldName,
            Func<TFrom, TTo> converter)
        {
            return WithMigration(doc =>
            {
                if (doc.ContainsKey(fieldName))
                {
                    var currentValue = BsonMapper.Global.ToObject<TFrom>(doc[fieldName].AsDocument);
                    doc[fieldName] = BsonMapper.Global.ToDocument(converter(currentValue));
                }
                return doc;
            });
        }

        public SaveMigrationBuilder<T> ReplaceNestedFieldValue<TValue>(
            string fieldPath,
            TValue oldValue,
            TValue newValue)
        {
            return WithMigration(doc =>
            {
                var pathParts = fieldPath.Split('.');
                var current = doc;
                
                for (int i = 0; i < pathParts.Length - 1; i++)
                {
                    if (current.ContainsKey(pathParts[i]) && current[pathParts[i]].IsDocument)
                    {
                        current = current[pathParts[i]].AsDocument;
                    }
                    else
                    {
                        return doc;
                    }
                }
                
                var fieldName = pathParts.Last();
                if (current.ContainsKey(fieldName))
                {
                    var currentValue = BsonMapper.Global.ToObject<TValue>(current[fieldName].AsDocument);
                    if (EqualityComparer<TValue>.Default.Equals(currentValue, oldValue))
                    {
                        current[fieldName] = BsonMapper.Global.ToDocument(newValue);
                    }
                }
                
                return doc;
            });
        }

        public SaveMigrationBuilder<T> WithMigration(Func<BsonDocument, BsonDocument> migration)
        {
            if (string.IsNullOrEmpty(_currentFromVersion) || string.IsNullOrEmpty(_currentToVersion))
            {
                throw new InvalidOperationException("FromVersion and ToVersion must be set before adding migration");
            }

            _migrationService.RegisterMigration<T>(_currentFromVersion, _currentToVersion, migration);
            return this;
        }
        
        public SaveMigrationBuilder<T> WithMigrations(
            params Func<SaveMigrationBuilder<T>, SaveMigrationBuilder<T>>[] migrations)
        {
            return migrations.Aggregate(this, (b, m) => m(b));
        }
    }
}