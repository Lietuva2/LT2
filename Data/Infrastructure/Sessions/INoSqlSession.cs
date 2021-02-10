using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using Data.ViewModels.Account;
using Data.ViewModels.Base;
using Framework.Data.Sessions;
using Framework.Infrastructure.Storage;
using Framework.Infrastructure.Storage;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Data.Infrastructure.Sessions
{
    public interface INoSqlSession : IDbSession
    {
        void UpdateById<T>(MongoObjectId id, IMongoUpdate update);

        void UpdateOne<T>(IMongoQuery find, IMongoUpdate update);

        T FindByIdAndModify<T>(MongoObjectId id, IMongoUpdate update);

        T FindAndModify<T>(IMongoQuery find, IMongoUpdate update);

        IQueryable<T> GetAllIn<T>(string prop, params BsonValue[] inSet);

        IQueryable<T> GetAllNotIn<T>(string prop, params BsonValue[] inSet);

        void CreateIndex<T>(string name, bool isUnique, params string[] keys);

        MongoObjectId SaveFile(string filename, byte[] fileBytes, string contentType);

        FileViewModel GetFile<T>(MongoObjectId id);

        MongoCollection<T> GetMongoCollection<T>();

        MongoCursor<T> Find<T>(IMongoQuery find);
    }
}