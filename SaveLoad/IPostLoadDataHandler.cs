using Newtonsoft.Json;
using Scellecs.Morpeh;
using UltraLiteDB;

namespace Core.ECS.SaveLoad
{
    public interface IPostLoadDataHandler
    {
        [BsonIgnore]
        public int Priority { get; }

        void OnPostLoad(Entity entity, World world);
    }
}