using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Core.ECS.SaveLoad.Converters;
using Cysharp.Threading.Tasks;
using Scellecs.Morpeh;
using UnityEngine;
using UltraLiteDB;
using UnityEngine.Profiling;
using UnityEngine.Scripting;

namespace Core.ECS.SaveLoad
{
    public partial class MorpehEcsSaveLoadService : ISaveLoadService
    {
        public static SaveLoadStatus SaveLoadStatus { get; private set; }

        public static event Action OnWorldLoadingStarted;
        public static event Action OnWorldLoadingFinished;

        [Preserve] private static readonly Dictionary<Type, MethodInfo> SetMethodsCache = new();
        [Preserve] private static readonly Dictionary<Type, object> StashCache = new();

        private static readonly string WorldCollectionId = "world";
        private static readonly string LevelCollectionId = "levels";

        private readonly ISaveMigrationService _saveMigrationService;
        private readonly World _world;
        private readonly SaveLoadParameters _saveLoadParameters;
        private readonly Dictionary<string, Type> _typeMappings = new();
        private readonly LinkedList<Entity> _rawEntities = new();
        private readonly int _maxBatchSize = 50;
        
        private Filter _saveableFilter;
        private bool _isOperationInProgress;
        private string _currentSceneId;

        public string CurrentSceneId
        {
            get => _currentSceneId;
            set => _currentSceneId = value;
        }


        public MorpehEcsSaveLoadService(
            SaveLoadParameters parameters,
            ISaveMigrationService saveMigrationService,
            World world = null)
        {
            _world = world ?? World.Default;
            _saveableFilter = _world.Filter.With<Saveable>().Build();
            _saveLoadParameters = parameters;
            _saveMigrationService = saveMigrationService;

            ConfigureBsonMapper();
        }


        public void RegisterTypeMapping(string oldTypeName, Type newType) =>
            _typeMappings[oldTypeName] = newType;

        public void SaveData(string saveName)
        {
            if (!ValidateOperationInProgress()) return;

            Profiler.BeginSample("SAVE SYSTEM");
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                SaveLoadStatus = SaveLoadStatus.Save;

                var (worldData, levelData) = GatherSaveData(saveName);
                EnsureSaveDirectoryExists();
                WriteWorldAndLevelData(saveName, worldData, levelData);
            }
            catch (Exception ex)
            {
                FailOperation($"Failed to save world", ex);
            }
            finally
            {
                LogResult(sw);
                ResetOperationState();
                Profiler.EndSample();
            }
        }

        public async UniTask SaveDataAsync(string saveName, Action<float> progressCallback = null)
        {
            if (!ValidateOperationInProgress()) return;

            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                SaveLoadStatus = SaveLoadStatus.Save;
                ReportProgress(progressCallback, 0.1f);

                var (worldData, levelData) = GatherSaveData(saveName);
                ReportProgress(progressCallback, 0.8f);

                await UniTask.Run(() =>
                {
                    EnsureSaveDirectoryExists();
                    WriteWorldAndLevelData(saveName, worldData, levelData);
                });

                ReportProgress(progressCallback, 1f);
            }
            catch (Exception ex)
            {
                FailOperation($"Failed to save world", ex);
                throw;
            }
            finally
            {
                LogResult(sw);
                _world.Commit();
                ResetOperationState();
            }
        }

        public void LoadData(string saveName)
        {
            if (!ValidateOperationInProgress()) 
                return;

            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                SaveLoadStatus = SaveLoadStatus.Load;

                var (worldData, levelData) = ReadWorldAndLevelData(saveName);
                ClearCurrentWorld();
                OnWorldLoadingStarted?.Invoke();

                LoadEntityCollection(worldData.persistentEntities);

                if (levelData != null)
                {
                    LoadEntityCollection(levelData.Entities, levelData.LevelId);
                }
            }
            catch (Exception ex)
            {
                FailOperation($"Critical load error", ex);
                _world.Dispose();
                throw;
            }
            finally
            {
                OnWorldLoadingFinished?.Invoke();
                LogResult(sw);
                ResetOperationState();
            }
        }

        public async UniTask LoadDataAsync(string saveName, Action<float> progressCallback = null)
        {
            if (!ValidateOperationInProgress()) return;

            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                SaveLoadStatus = SaveLoadStatus.Load;
                ReportProgress(progressCallback, 0.1f);

                var (worldData, levelData) = await UniTask.Run(() => ReadWorldAndLevelData(saveName));
                ReportProgress(progressCallback, 0.3f);

                ClearCurrentWorld();

                OnWorldLoadingStarted?.Invoke();
                ReportProgress(progressCallback, 0.4f);

                await LoadEntityCollectionAsync(worldData.persistentEntities,
                    progressCallback,
                    startProgress: 0.4f,
                    endProgress: 0.7f);

                if (levelData != null)
                {
                    await LoadEntityCollectionAsync(levelData.Entities,
                        progressCallback,
                        startProgress: 0.7f,
                        endProgress: 1f,
                        levelId: levelData.LevelId);
                }

                ReportProgress(progressCallback, 1f);
            }
            catch (Exception ex)
            {
                FailOperation($"Critical load error", ex);
                _world.Dispose();
                throw;
            }
            finally
            {
                OnWorldLoadingFinished?.Invoke();
                LogResult(sw);
                _world.Commit();
                ResetOperationState();
            }
        }

        public bool HasLevelData(string saveName, string levelId)
        {
            var dbPath = GetSavePath(saveName);
            using var db = CreateDatabase(dbPath);
            var levelColl = db.GetCollection<LevelSaveData>(LevelCollectionId);
            return levelColl.Exists(Query.EQ(nameof(LevelSaveData.LevelId), levelId));
        }

        public void LoadLevelData(string saveName, string levelId)
        {
            if (!ValidateOperationInProgress()) 
                return;

            try
            {
                var dbPath = GetSavePath(saveName);
                using var db = CreateDatabase(dbPath);

                var levelColl = db.GetCollection<LevelSaveData>(LevelCollectionId);
                var levelData = levelColl.FindOne(Query.EQ(nameof(LevelSaveData.LevelId), levelId));

                if (levelData == null) return;

                foreach (var entity in _saveableFilter)
                {
                    if (entity.Has<SceneScoped>() &&
                        entity.GetComponent<SceneScoped>().SceneId == levelId)
                    {
                        _world.RemoveEntity(entity);
                    }
                }

                LoadEntityCollection(levelData.Entities, levelId);
            }
            finally
            {
                _isOperationInProgress = false;
            }
        }

        private bool ValidateOperationInProgress()
        {
            if (_isOperationInProgress)
            {
                Debug.LogWarning("Another save/load operation is already in progress. Aborting.");
                return false;
            }

            _isOperationInProgress = true;
            return true;
        }

        private void ResetOperationState()
        {
            SaveLoadStatus = SaveLoadStatus.None;
            _isOperationInProgress = false;
        }

        private void FailOperation(string message, Exception ex)
        {
            SaveLoadStatus = SaveLoadStatus.Error;
            Debug.LogError(message);
            Debug.LogException(ex);
        }

        private void ReportProgress(Action<float> callback, float progress) 
            => callback?.Invoke(progress);

        private void LogResult(System.Diagnostics.Stopwatch sw)
        {
            Debug.Log(_saveLoadParameters.SavesPath);
            Debug.Log($"Operation completed in {sw.Elapsed.TotalSeconds:F3} seconds.");
        }

        private void EnsureSaveDirectoryExists()
        {
            if (!Directory.Exists(_saveLoadParameters.SavesPath))
                Directory.CreateDirectory(_saveLoadParameters.SavesPath);
        }

        private UltraLiteDatabase CreateDatabase(string dbPath) => new UltraLiteDatabase(dbPath);

        private string GetSavePath(string saveName) =>
            Path.Combine(_saveLoadParameters.SavesPath,
                $"{saveName}.{_saveLoadParameters.SaveFileExtension}");

        private (WorldSaveData worldData, Dictionary<string, LevelSaveData> levelsData) GatherSaveData(string saveName)
        {
            var worldData = new WorldSaveData
            {
                persistentEntities = new List<EntitySaveData>(), 
                SaveId = saveName
            };
            var levelsData = new Dictionary<string, LevelSaveData>();

            _rawEntities.Clear();
            foreach (var entity in _saveableFilter)
                _rawEntities.AddLast(entity);

            foreach (var entity in _rawEntities)
            {
                var entityData = SaveEntity(entity);
                if (entityData == null) 
                    continue;

                if (entity.Has<SceneScoped>())
                {
                    var sceneId = entity.GetComponent<SceneScoped>().SceneId;
                    if (!levelsData.TryGetValue(sceneId, out var level))
                    {
                        level = new LevelSaveData {LevelId = sceneId, Entities = new List<EntitySaveData>()};
                        levelsData[sceneId] = level;
                    }

                    level.Entities.Add(entityData);
                }
                else
                {
                    worldData.persistentEntities.Add(entityData);
                }
            }

            return (worldData, levelsData);
        }

        private void WriteWorldAndLevelData(
            string saveName,
            WorldSaveData worldData,
            Dictionary<string, LevelSaveData> levelDataDict)
        {
            var dbPath = GetSavePath(saveName);
            using var db = CreateDatabase(dbPath);

            var worldColl = db.GetCollection<WorldSaveData>(WorldCollectionId);
            worldColl.Delete(Query.All());
            worldColl.Upsert(worldData);

            var levelColl = db.GetCollection<LevelSaveData>(LevelCollectionId);

            levelColl.Delete(Query.EQ(nameof(LevelSaveData.LevelId), _currentSceneId ?? "DEFAULT"));
            levelColl.Upsert(levelDataDict.Values);
        }

        private EntitySaveData SaveEntity(Entity entity)
        {
            var entityData = new EntitySaveData
            {
                Components = new List<ComponentSaveData>()
            };

            TrySaveComponents(entity, entityData.Components);

            return entityData.Components.Count > 0 ? entityData : null;
        }

        partial void TrySaveComponents(Entity entity, List<ComponentSaveData> componentList);

        [Preserve]
        private bool TrySaveComponent<T>(Entity entity, ICollection<ComponentSaveData> componentsList)
            where T : struct, ISaveableComponent
        {
            var stash = _world.GetStash<T>();
            ref var data = ref stash.Get(entity, out var exist);

            if (!exist) return false;

            if (data is IPreSaveDataHandler handler && handler.OnPreSave(entity, _world))
                _world.Commit();

            try
            {
                var bsonDoc = BsonMapper.Global.ToDocument(data);
                componentsList.Add(new ComponentSaveData
                {
                    FullTypeName = typeof(T).FullName,
                    OriginalVersion = _saveLoadParameters.GameVersion,
                    BsonData = bsonDoc
                });
                return true;
            }
            catch (Exception ex)
            {
                SaveLoadStatus = SaveLoadStatus.Error;
                Debug.LogError($"Failed to save component {typeof(T).Name}: {ex}");
                return false;
            }
        }

        private (WorldSaveData worldData, LevelSaveData levelData) ReadWorldAndLevelData(string saveName)
        {
            var dbPath = GetSavePath(saveName);
            using var db = CreateDatabase(dbPath);

            var worldColl = db.GetCollection<WorldSaveData>(WorldCollectionId);
            var worldData = worldColl.FindOne(Query.All());

            var levelColl = db.GetCollection<LevelSaveData>(LevelCollectionId);
            var levelData = levelColl.FindOne(Query.EQ(nameof(LevelSaveData.LevelId),
                _currentSceneId ?? "DEFAULT"));

            return (worldData, levelData);
        }

        private void ClearCurrentWorld()
        {
            foreach (var entity in _saveableFilter)
                _world.RemoveEntity(entity);
        }

        private void LoadEntityCollection(List<EntitySaveData> entities, string levelId = null)
        {
            foreach (var entityData in entities)
            {
                if (LoadEntity(entityData, out var loadedEntity))
                {
                    if (!string.IsNullOrEmpty(levelId))
                        loadedEntity.SetComponent(new SceneScoped {SceneId = levelId});
                }
            }
        }

        private async UniTask LoadEntityCollectionAsync(
            List<EntitySaveData> entities,
            Action<float> progressCallback,
            float startProgress,
            float endProgress,
            string levelId = null)
        {
            var total = entities.Count;
            for (int i = 0; i < total; i++)
            {
                var entityData = entities[i];
                if (LoadEntity(entityData, out var loadedEntity) && !string.IsNullOrEmpty(levelId))
                {
                    loadedEntity.SetComponent(new SceneScoped {SceneId = levelId});
                }

                if (i % _maxBatchSize == 0)
                {
                    var prog = Mathf.Lerp(startProgress, endProgress, i / (float) total);
                    ReportProgress(progressCallback, prog);
                    await UniTask.Yield();
                }
            }
        }

        private bool LoadEntity(EntitySaveData entityData, out Entity loadedEntity)
        {
            loadedEntity = default;
            var entity = _world.CreateEntity();
            var success = true;
            var handlers = new List<IPostLoadDataHandler>();

            foreach (var componentData in entityData.Components)
            {
                try
                {
                    var type = ResolveComponentType(componentData.FullTypeName);
                    if (type == null)
                    {
                        success = false;
                        continue;
                    }

                    var migratedDoc = _saveMigrationService.ApplyMigrations(
                        type.FullName,
                        componentData.OriginalVersion ?? "0.0.1",
                        _saveLoadParameters.GameVersion,
                        componentData.BsonData);

                    var component = BsonMapper.Global.ToObject(type, migratedDoc);
                    if (component == null)
                    {
                        Debug.LogError($"Failed to deserialize {componentData.FullTypeName}");
                        success = false;
                        continue;
                    }

                    if (component is IPostLoadDataHandler postLoad) 
                        handlers.Add(postLoad);
                    
                    AddComponentToEntity(entity, component, type);
                }
                catch (Exception ex)
                {
                    SaveLoadStatus = SaveLoadStatus.Error;
                    Debug.LogError($"Failed to load component {componentData.FullTypeName}: {ex}");
                    success = false;
                }
            }

            foreach (var handler in handlers.OrderBy(h => h.Priority))
            {
                try
                {
                    handler.OnPostLoad(entity, _world);
                }
                catch (Exception ex)
                {
                    SaveLoadStatus = SaveLoadStatus.Error;
                    Debug.LogError($"Post-load failed for {handler.GetType()}: {ex}");
                    success = false;
                    break;
                }
            }

            if (!success)
            {
                _world.RemoveEntity(entity);
                return false;
            }

            entity.SetComponent(new Saveable());
            entity.SetComponent(new WasLoadedFromSave());

            loadedEntity = entity;
            return true;
        }

        private Type ResolveComponentType(string typeName)
        {
            var type = Type.GetType(typeName);
            if (type != null) 
                return type;

            if (_typeMappings.TryGetValue(typeName, out var fallback))
                return fallback;

            Debug.LogError($"Type {typeName} not found and no fallback mapping exists");
            return null;
        }

        private void AddComponentToEntity(Entity entity, object component, Type componentType)
        {
            try
            {
                if (!StashCache.TryGetValue(componentType, out var stash))
                {
                    var getStashMethod = typeof(WorldStashExtensions)
                        .GetMethod(nameof(WorldStashExtensions.GetStash))
                        .MakeGenericMethod(componentType);
                    stash = getStashMethod.Invoke(null, new object[] {_world});
                    StashCache[componentType] = stash;
                }

                if (!SetMethodsCache.TryGetValue(componentType, out var setMethod))
                {
                    setMethod = stash.GetType().GetMethod("Set", new[]
                    {
                        typeof(Entity),
                        componentType.MakeByRefType()
                    });
                    SetMethodsCache[componentType] = setMethod;
                }

                var parameters = new object[] {entity, component};
                setMethod.Invoke(stash, parameters);
            }
            catch (Exception ex)
            {
                SaveLoadStatus = SaveLoadStatus.Error;
                Debug.LogError($"Failed to add component {componentType.Name} to entity: {ex}");
                throw;
            }
        }

        private void ConfigureBsonMapper()
        {
            BsonTypeConverters.RegisterAllBuiltinConverters();
            BsonMapper.Global.IncludeFields = true;
            BsonMapper.Global.SerializeNullValues = true;
        }

        [Serializable]
        public class WorldSaveData
        {
            [BsonId] public string SaveId = Guid.NewGuid().ToString();
            public List<EntitySaveData> persistentEntities = new();
        }

        [Serializable]
        public class LevelSaveData
        {
            [BsonId] public string Id = Guid.NewGuid().ToString();
            public string LevelId;
            public List<EntitySaveData> Entities = new();
        }

        [Serializable]
        public class EntitySaveData
        {
            public List<ComponentSaveData> Components = new();
        }

        [Serializable]
        public class ComponentSaveData
        {
            public string FullTypeName;
            public string OriginalVersion;
            public BsonDocument BsonData;
        }
    }
}