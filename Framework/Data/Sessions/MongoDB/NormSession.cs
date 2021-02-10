//using System;
//using System.Collections.Generic;
//using System.Linq;
//using Norm.Linq;
//using Norm;
//using Norm.Responses;
//using Norm.Collections;

//namespace Framework.Data.Sessions.MongoDB {
//    public class NormSession : IDbSession
//    {
//        protected readonly IMongo mongo;
//        protected readonly IMongoDatabase db;

//        public NormSession()
//        {
//            //this looks for a connection string in your Web.config - you can override this if you want
//            mongo = Mongo.Create("MongoDB");
//            db = mongo.Database;
//        }

//        public void CommitChanges() {
//            //mongo isn't transactional in this way... it's all firehosed
//        }

//        public void Delete<T>(System.Linq.Expressions.Expression<Func<T, bool>> expression) where T: class, new() {
//            var items = GetAll<T>().Where(expression);
//            foreach (T item in items) {
//                Delete(item);
//                VerifyNoErrors();
//            }
//        }

//        public void Delete<T>(T item) where T: class, new() {
//            db.GetCollection<T>().Delete(item);
//            VerifyNoErrors();
//        }

//        public void DeleteAll<T>() where T: class, new() {
//            db.DropCollection(typeof(T).Name);
//            VerifyNoErrors();
//        }

//        public T GetSingle<T>(System.Linq.Expressions.Expression<Func<T, bool>> expression) where T: class, new() {
//            return db.GetCollection<T>().AsQueryable().Where(expression).SingleOrDefault();
//        }

//        public T GetById<T>(object id) where T : class, new()
//        {
//            throw new NotImplementedException();
//        }

//        public IQueryable<T> GetAll<T>() where T: class, new()
//        {
//            return db.GetCollection<T>().AsQueryable();
//        }

//        public void Add<T>(T item) where T: class, new() {
//            db.GetCollection<T>().Insert(item);
//        }

//        public void Add<T>(IEnumerable<T> items) where T: class, new() {
//            foreach (T item in items) {
//                Add(item);
//            }
//        }

//        public void Update<T>(T item) where T: class, new() {
//            db.GetCollection<T>().Save(item);
//            VerifyNoErrors();
//        }

//        public void Cancel()
//        {
//            throw new NotImplementedException();
//        }

//        //this is just some sugar if you need it.
//        public T MapReduce<T>(string map, string reduce)
//        {
//            MapReduce mr = db.CreateMapReduce();
//            MapReduceResponse response =
//                mr.Execute(new MapReduceOptions(typeof (T).Name)
//                               {
//                                   Map = map,
//                                   Reduce = reduce
//                               });
//            IMongoCollection<MapReduceResult<T>> coll = response.GetCollection<MapReduceResult<T>>();
//            MapReduceResult<T> r = coll.Find().FirstOrDefault();
//            return r.Value;
//        }

//        public void Dispose() {
//            mongo.Dispose(); 
//        }

//        private void VerifyNoErrors()
//        {
//            LastErrorResponse response = db.LastError();
//            if (response == null || response.Code == 0)
//            {
//                return;
//            }

//            string message =
//            string.Format("Error accessing database [{0}]: {1}",
//               response.Code,
//               response.Error);
//            throw new MongoDataException(message, response.Code);
//        } 
//    }
//}