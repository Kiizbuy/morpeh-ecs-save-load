using Scellecs.Morpeh;

namespace Core.ECS.SaveLoad
{
    public interface IPreSaveDataHandler
    {
        bool OnPreSave(Entity entity, World world);
    }
}