using MongoDB.Bson;
using MongoDB.Driver;

namespace Framework.Infrastructure.Storage
{
    public static class MongoExtenssions
    {
        public static MongoCollection<T> GetCollection<T>(this MongoDatabase db)
        {
            return db.GetCollection<T>(typeof (T).Name);
        }

        public static bool IsNullOrEmpty(this ObjectId id)
        {
            return id.Equals(ObjectId.Empty);
        }

        public static bool IsNullOrEmpty(this BsonObjectId id)
        {
            return id == null || id.Equals(BsonObjectId.Empty);
        }

        public static bool IsNullOrEmpty(this MongoObjectId id)
        {
            return id == null || id.Equals(BsonObjectId.Empty);
        }
    }
}
