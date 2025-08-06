using Cysharp.Threading.Tasks;
using Scellecs.Morpeh;

namespace Core.ECS.SaveLoad.Systems
{
    /// <summary>
    /// That class - it's example how to work with save-load service
    /// </summary>
    public class SaveLoadSystem : ISystem
    {
        public World World { get; set; }

        private Filter _saveOperationsFilter;
        private Filter _loadOperationsFilter;
        private Stash<SaveOperation> _saveOperationStash;
        private Stash<LoadOperation> _loadOpeationStash;

        private ISaveLoadService _ecsSaveLoadService;

        public SaveLoadSystem(ISaveLoadService saveLoadService)
        {
            _ecsSaveLoadService = saveLoadService;
        }

        public void OnAwake()
        {
            _saveOperationStash = World.GetStash<SaveOperation>();
            _loadOpeationStash = World.GetStash<LoadOperation>();
            
            _saveOperationsFilter = World.Filter.With<SaveOperation>().Build();
            _loadOperationsFilter = World.Filter.With<LoadOperation>().Build();
        }

        public void OnUpdate(float deltaTime)
        {
            foreach (var entity in _saveOperationsFilter)
            {
                ref var operation = ref _saveOperationStash.Get(entity);
                _ecsSaveLoadService.SaveDataAsync(operation.SaveId).Forget();
            }
            
            foreach (var entity in _loadOperationsFilter)
            {
                ref var operation = ref _loadOpeationStash.Get(entity);
                _ecsSaveLoadService.LoadDataAsync(operation.SaveId).Forget();
            }
        }

        public void Dispose()
        {
        }
    }
}