using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Core.ECS.SaveLoad
{
    internal static class MainThreadDispatcher
    {
        private static SynchronizationContext _unityContext;

        [RuntimeInitializeOnLoadMethod]
        private static void Initialize()
        {
            _unityContext = SynchronizationContext.Current;
        }

        public static async Task InvokeAsync(Action action)
        {
            if (SynchronizationContext.Current == _unityContext)
            {
                action();
            }
            else
            {
                var tcs = new TaskCompletionSource<bool>();
                _unityContext.Post(_ =>
                {
                    try
                    {
                        action();
                        tcs.SetResult(true);
                    }
                    catch (Exception e)
                    {
                        tcs.SetException(e);
                    }
                }, null);
                await tcs.Task;
            }
        }
    }
}