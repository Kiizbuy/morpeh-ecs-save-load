namespace Core.ECS.SaveLoad
{
    public static class SaveMigrationServiceExtensions
    {
        public static SaveMigrationBuilder<T> RegisterMigrationFor<T>(this ISaveMigrationService service) 
            where T : ISaveableData
        {
            return new SaveMigrationBuilder<T>(service);
        }
    }
}