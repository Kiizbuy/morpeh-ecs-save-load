using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

namespace Core.ECS.SaveLoad
{
    public interface ISaveLoadService
    {
        string CurrentSceneId { get; set; }
        
        public void SaveData(string saveName);
        public void LoadData(string saveName);
        UniTask SaveDataAsync(string saveName, Action<float> progressCallback = null);
        UniTask LoadDataAsync(string saveName, Action<float> progressCallback = null);
        public void RegisterTypeMapping(string oldTypeName, Type newType);
        bool HasLevelData(string saveName, string levelId);
        void LoadLevelData(string saveName, string levelId);
    }
}