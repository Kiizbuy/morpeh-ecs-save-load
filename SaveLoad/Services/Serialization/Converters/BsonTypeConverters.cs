using System;
using System.Collections.Generic;
using UnityEngine;
using UltraLiteDB;
using UnityEngine.AddressableAssets;
using UnityEngine.Scripting;

namespace Core.ECS.SaveLoad.Converters
{
    [Preserve]
    public static class BsonTypeConverters
    {
        [Preserve]
        private static readonly
            Dictionary<Type, (Func<object, BsonValue> serialize, Func<BsonValue, object> deserialize)>
            CustomConverters = new()
            {
                {typeof(Vector3), (SerializeVector3, DeserializeVector3)},
                {typeof(Vector2), (SerializeVector2, DeserializeVector2)},
                {typeof(Vector3Int), (SerializeVector3Int, DeserializeVector3Int)},
                {typeof(Vector2Int), (SerializeVector2Int, DeserializeVector2Int)},
                {typeof(Quaternion), (SerializeQuaternion, DeserializeQuaternion)},
                {typeof(Rect), (SerializeRect, DeserializeRect)},
                {typeof(Color), (SerializeColor, DeserializeColor)},
                {typeof(Color32), (SerializeColor32, DeserializeColor32)},
                {typeof(LayerMask), (SerializeLayerMask, DeserializeLayerMask)},
                {typeof(AssetReference), (SerializeAssetReference, DeserializeAssetReference)}
            };

        [Preserve]
        public static void RegisterConverter<T>(
            Func<T, BsonValue> serialize,
            Func<BsonValue, T> deserialize)
        {
            CustomConverters[typeof(T)] = (
                o => serialize((T) o),
                b => deserialize(b)
            );
        }

        [Preserve]
        public static void RegisterAllBuiltinConverters()
        {
            foreach (var converter in CustomConverters)
            {
                BsonMapper.Global.RegisterType(
                    converter.Key,
                    converter.Value.serialize,
                    converter.Value.deserialize
                );
            }
        }

        #region Built-in Converters

        [Preserve]
        private static BsonValue SerializeVector3(object obj)
        {
            var v = (Vector3) obj;
            return new BsonArray {v.x, v.y, v.z};
        }

        [Preserve]
        private static object DeserializeVector3(BsonValue bson)
        {
            var arr = bson.AsArray;
            return new Vector3(
                (float) arr[0].AsDouble,
                (float) arr[1].AsDouble,
                (float) arr[2].AsDouble);
        }

        [Preserve]
        private static BsonValue SerializeVector2(object obj)
        {
            var v = (Vector2) obj;
            return new BsonArray {v.x, v.y,};
        }

        [Preserve]
        private static object DeserializeVector2(BsonValue bson)
        {
            var arr = bson.AsArray;
            return new Vector2(
                (float) arr[0].AsDouble,
                (float) arr[1].AsDouble);
        }

        [Preserve]
        private static BsonValue SerializeVector3Int(object obj)
        {
            var v = (Vector3Int) obj;
            return new BsonArray {v.x, v.y, v.z};
        }

        [Preserve]
        private static object DeserializeVector3Int(BsonValue bson)
        {
            var arr = bson.AsArray;
            return new Vector3Int(
                arr[0].AsInt32,
                arr[1].AsInt32,
                arr[2].AsInt32);
        }

        [Preserve]
        private static BsonValue SerializeVector2Int(object obj)
        {
            var v = (Vector2Int) obj;
            return new BsonArray {v.x, v.y};
        }

        [Preserve]
        private static object DeserializeVector2Int(BsonValue bson)
        {
            var arr = bson.AsArray;

            return new Vector2Int(
                arr[0].AsInt32,
                arr[1].AsInt32);
        }

        [Preserve]
        private static BsonValue SerializeQuaternion(object obj)
        {
            var q = (Quaternion) obj;
            return new BsonArray {q.x, q.y, q.z, q.w};
        }

        [Preserve]
        private static object DeserializeQuaternion(BsonValue bson)
        {
            var arr = bson.AsArray;

            return new Quaternion(
                (float) arr[0].AsDouble,
                (float) arr[1].AsDouble,
                (float) arr[2].AsDouble,
                (float) arr[3].AsDouble);
        }

        [Preserve]
        private static BsonValue SerializeRect(object obj)
        {
            var r = (Rect) obj;
            return new BsonArray {r.x, r.y, r.width, r.height};
        }

        [Preserve]
        private static object DeserializeRect(BsonValue bson)
        {
            var arr = bson.AsArray;

            return new Rect(
                (float) arr[0].AsDouble,
                (float) arr[1].AsDouble,
                (float) arr[2].AsDouble,
                (float) arr[3].AsDouble);
        }

        [Preserve]
        private static BsonValue SerializeColor(object obj)
        {
            var c = (Color) obj;
            return new BsonArray {c.r, c.g, c.b, c.a};
        }

        [Preserve]
        private static object DeserializeColor(BsonValue bson)
        {
            var arr = bson.AsArray;

            return new Color(
                (float) arr[0].AsDouble,
                (float) arr[1].AsDouble,
                (float) arr[2].AsDouble,
                (float) arr[3].AsDouble);
        }

        [Preserve]
        private static BsonValue SerializeColor32(object obj)
        {
            var c = (Color32) obj;

            return new BsonDocument
            {
                ["r"] = (int) c.r,
                ["g"] = (int) c.g,
                ["b"] = (int) c.b,
                ["a"] = (int) c.a
            };
        }

        [Preserve]
        private static object DeserializeColor32(BsonValue bson)
        {
            return new Color32(
                (byte) bson["r"].AsInt32,
                (byte) bson["g"].AsInt32,
                (byte) bson["b"].AsInt32,
                (byte) bson["a"].AsInt32);
        }

        [Preserve]
        private static BsonValue SerializeLayerMask(object obj)
        {
            var mask = (LayerMask) obj;
            return new BsonValue(mask.value);
        }

        [Preserve]
        private static object DeserializeLayerMask(BsonValue bson)
        {
            return new LayerMask {value = bson.AsInt32};
        }

        [Preserve]
        private static BsonValue SerializeAssetReference(object obj)
        {
            var ar = (AssetReference) obj;
            return new BsonDocument
            {
                ["guid"] = ar.RuntimeKey.ToString()
            };
        }

        [Preserve]
        private static object DeserializeAssetReference(BsonValue bson)
        {
            return new AssetReference(bson["guid"].AsString);
        }

        #endregion
    }
}