using System;

namespace Framework.Data.Sessions.MongoDB
{
    public class MongoDataException : Exception
    {
        public int ResponseCode { get; set; }
        public MongoDataException(string message, int responseCode) : base(message)
        {
            this.ResponseCode = responseCode;
        }
    }
}
