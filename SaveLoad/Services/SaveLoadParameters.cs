namespace Core.ECS.SaveLoad
{
    public class SaveLoadParameters
    {
        public readonly string GameVersion;
        public readonly string SavesPath;
        public readonly string SaveFileExtension;

        public SaveLoadParameters(string gameVersion, string savesPath, string saveFileExtension = "db")
        {
            GameVersion = gameVersion;
            SavesPath = savesPath;
            SaveFileExtension = saveFileExtension;
        }
    }
}