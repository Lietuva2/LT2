using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Options;
using MongoDB.Bson.Serialization.Serializers;

namespace Framework.Infrastructure.Storage
{
    public class MongoObjectId : IComparable<MongoObjectId>, IEquatable<MongoObjectId>
    {
        public MongoObjectId(BsonObjectId sb)
        {
            BsonObjectId = sb;
        }

        public BsonObjectId BsonObjectId { get; private set; }
        public static BsonObjectId Empty {get { return BsonObjectId.Empty; }}

        #region public operators 
        public static implicit operator MongoObjectId(BsonObjectId sb)
        {
            return new MongoObjectId(sb);
        }

        public static implicit operator BsonObjectId(MongoObjectId sb)
        {
            if(sb == null)
            {
                return null;
            }

            return sb.BsonObjectId;
        }

        public static implicit operator MongoObjectId(
            string value
        )
        {
            if(string.IsNullOrEmpty(value))
            {
                return null;
            }

            if (string.IsNullOrEmpty(value.Trim()))
            {
                return null;
            }

            return new BsonObjectId(value);
        }

        public static implicit operator string(
            MongoObjectId value
        )
        {
            if(value.IsNullOrEmpty())
            {
                return null;
            }

            return value.BsonObjectId.ToString();
        }

        //compare to strings
        public static bool operator <(
            string lhs,
            MongoObjectId rhs
        )
        {
            if (object.ReferenceEquals(lhs, null) && object.ReferenceEquals(rhs, null)) { return false; }
            if (object.ReferenceEquals(lhs, null)) { return true; }
            if (object.ReferenceEquals(rhs, null)) { return false; }
            return BsonObjectId.Parse(lhs).CompareTo(rhs.BsonObjectId) < 0;
        }

        public static bool operator <=(
            string lhs,
            MongoObjectId rhs
        )
        {
            if (object.ReferenceEquals(lhs, null) && object.ReferenceEquals(rhs, null)) { return true; }
            if (object.ReferenceEquals(lhs, null)) { return true; }
            if (object.ReferenceEquals(rhs, null)) { return false; }
            return BsonObjectId.Parse(lhs).CompareTo(rhs.BsonObjectId) <= 0;
        }

        public static bool operator !=(
            string lhs,
            MongoObjectId rhs
        )
        {
            return !(BsonObjectId.Parse(lhs) == rhs.BsonObjectId);
        }

        public static bool operator ==(
            string lhs,
            MongoObjectId rhs
        )
        {
            return object.Equals(BsonObjectId.Parse(lhs), rhs.BsonObjectId);
        }

        public static bool operator >(
            string lhs,
            MongoObjectId rhs
        )
        {
            return !(BsonObjectId.Parse(lhs) <= rhs.BsonObjectId);
        }

        public static bool operator >=(
            string lhs,
            MongoObjectId rhs
        )
        {
            return !(BsonObjectId.Parse(lhs) < rhs.BsonObjectId);
        }

        //compare to self

        public static bool operator <(
            MongoObjectId lhs,
            MongoObjectId rhs
        )
        {
            if (object.ReferenceEquals(lhs, null) && object.ReferenceEquals(rhs, null)) { return false; }
            if (object.ReferenceEquals(lhs, null)) { return true; }
            if (object.ReferenceEquals(rhs, null)) { return false; }
            return lhs.CompareTo(rhs) < 0;
        }

        public static bool operator <=(
            MongoObjectId lhs,
            MongoObjectId rhs
        )
        {
            if (object.ReferenceEquals(lhs, null) && object.ReferenceEquals(rhs, null)) { return true; }
            if (object.ReferenceEquals(lhs, null)) { return true; }
            if (object.ReferenceEquals(rhs, null)) { return false; }
            return lhs.CompareTo(rhs) <= 0;
        }

        public static bool operator !=(
            MongoObjectId lhs,
            MongoObjectId rhs
        )
        {
            return !(lhs == rhs);
        }

        public static bool operator ==(
            MongoObjectId lhs,
            MongoObjectId rhs
        )
        {
            if (object.ReferenceEquals(lhs, null) && object.ReferenceEquals(rhs, null)) { return true; }
            if (object.ReferenceEquals(lhs, null) || object.ReferenceEquals(rhs, null)) { return false; }
            return Equals(lhs, rhs);
        }

        public static bool operator >(
            MongoObjectId lhs,
            MongoObjectId rhs
        )
        {
            return !(lhs <= rhs);
        }

        public static bool operator >=(
            MongoObjectId lhs,
            MongoObjectId rhs
        )
        {
            return !(lhs < rhs);
        }

        #endregion

        public int CompareTo(MongoObjectId other)
        {
            return BsonObjectId.CompareTo(other.BsonObjectId);
        }

        public bool Equals(MongoObjectId other)
        {
            return BsonObjectId.Equals(other.BsonObjectId);
        }

        public static bool Equals(MongoObjectId one, MongoObjectId other)
        {
            return one.BsonObjectId.Equals(other.BsonObjectId);
        }

        public override bool Equals(object obj)
        {
            return BsonObjectId.Equals(obj);
        }

        public override string ToString()
        {
            return BsonObjectId.ToString();
        }

        public int CompareTo(BsonValue other)
        {
            return BsonObjectId.CompareTo(other);
        }
    }

    public class MongoObjectIdGenerator : IIdGenerator
    {
        #region private static fields
        private static MongoObjectIdGenerator instance = new MongoObjectIdGenerator();
        #endregion

        #region constructors
        public MongoObjectIdGenerator()
        {
        }
        #endregion

        #region public static properties
        public static MongoObjectIdGenerator Instance
        {
            get { return instance; }
        }
        #endregion

        public static void Register()
        {
            BsonSerializer.RegisterIdGenerator(typeof(MongoObjectId), instance);
        }

        #region public methods
        public object GenerateId()
        {
            return (MongoObjectId)BsonObjectId.GenerateNewId();
        }

        public object GenerateId(object container, object document)
        {
            return (MongoObjectId)BsonObjectId.GenerateNewId();
        }

        public bool IsEmpty(
            object id
        )
        {
            return id == null || ((MongoObjectId)id).BsonObjectId == BsonObjectId.Empty;
        }
        #endregion
}

    public class MongoObjectIdSerializer : BsonBaseSerializer
    {
        #region private static fields
        private static MongoObjectIdSerializer instance = new MongoObjectIdSerializer();
        #endregion

        #region constructors
        public MongoObjectIdSerializer()
        {
        }
        #endregion

        #region public static properties
        public static MongoObjectIdSerializer Instance
        {
            get { return instance; }
        }
        #endregion

        #region public static methods
        public static void Register() {
            BsonSerializer.RegisterSerializer(typeof(MongoObjectId), instance);
        }
        #endregion

        #region public methods
        public override object Deserialize(
            BsonReader bsonReader,
            Type nominalType,
            Type actualType,
            IBsonSerializationOptions options
        ) {
            BsonType bsonType = bsonReader.CurrentBsonType;
            switch (bsonType) {
                case BsonType.ObjectId:
                    int timestamp;
                    int machine;
                    short pid;
                    int increment;
                    bsonReader.ReadObjectId(out timestamp, out machine, out pid, out increment);
                    return new MongoObjectId(new BsonObjectId(timestamp, machine, pid, increment));
                case BsonType.String:
                    return new MongoObjectId(BsonObjectId.Parse(bsonReader.ReadString()));
                case BsonType.Null:
                    bsonReader.ReadNull();
                    return null;
                default:
                    var message = string.Format("Cannot deserialize ObjectId from BsonType: {0}", bsonType);
                    throw new FormatException(message);
            }
        }

        public override void Serialize(
            BsonWriter bsonWriter,
            Type nominalType,
            object value,
            IBsonSerializationOptions options
        )
        {
            if(value == null)
            {
                bsonWriter.WriteNull();
                return;
            }

            var objectId = ((MongoObjectId)value).BsonObjectId;
            var representation = (options == null) ? BsonType.ObjectId : ((RepresentationSerializationOptions) options).Representation;
            switch (representation) {
                case BsonType.ObjectId:
                    bsonWriter.WriteObjectId(objectId.Timestamp, objectId.Machine, objectId.Pid, objectId.Increment);
                    break;
                case BsonType.String:
                    bsonWriter.WriteString(objectId.ToString());
                    break;
                default:
                    throw new BsonInternalException("Unexpected representation");
            }
        }
        #endregion
    }
}
