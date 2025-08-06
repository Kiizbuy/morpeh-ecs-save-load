using Scellecs.Morpeh;

namespace Core.ECS.SaveLoad.Systems
{
    public class SaveLoadCleanupSystem : ICleanupSystem
    {
        public World World { get; set; }

        private Filter _saveOperationsFilter;
        private Filter _loadOperationsFilter;
        
        public void OnAwake()
        {
            _saveOperationsFilter = World.Filter.With<SaveOperation>().Build();
            _loadOperationsFilter = World.Filter.With<LoadOperation>().Build();
        }

        public void OnUpdate(float deltaTime)
        {
            foreach (var entity in _saveOperationsFilter)
            {
                World.RemoveEntity(entity);
            }
            
            foreach (var entity in _loadOperationsFilter)
            {
                World.RemoveEntity(entity);
            }
        }

        public void Dispose()
        {
        }
    }
}