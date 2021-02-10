using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Data.Enums;
using Data.MongoDB.Interfaces;
using Framework.Infrastructure.Storage;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.MongoDB
{
    public class User : ICommentable
    {
        private Settings _settings;

        public MongoObjectId Id { get; set; }
        public int DbId { get; set; }
        [BsonIgnore]
        public string FullName { get { return FirstName + " " + LastName; } }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string UserName { get; set; }
        public string Email { get; set; }
        public EmploymentStatus EmploymentStatus { get; set; }
        public string Country { get; set; }
        public string City { get; set; }
        public int? CityId { get; set; }
        public string Municipality { get; set; }
        public int? MunicipalityId { get; set; }
        public string OriginCity { get; set; }
        public int? OriginCityId { get; set; }
        public string OriginMunicipality { get; set; }
        public int? OriginMunicipalityId { get; set; }
        public string OriginCountry { get; set; }
        public string PostCode { get; set; }
        public List<WorkPosition> Positions { get; set; }
        public List<Education> Educations { get; set; }
        public List<Website> WebSites { get; set; }
        public string Interests { get; set; }
        public string Groups { get; set; }
        public string Awards { get; set; }
        public List<PhoneNumber> PhoneNumbers { get; set; }
        public string Adress { get; set; }
        public DateTime? BirthDate { get; set; }
        public MaritalStatus MaritalStatus { get; set; }
        public string PoliticalViews { get; set; }
        public List<PoliticalParty> MemberOfParties { get; set; }
        public string Nationality { get; set; }
        public string Citizenship { get; set; }
        public string ShortLink { get; set; }
        public string PersonCode { get; set; }
        public int LastNumber { get; set; }
        public DateTime LastActivityDate { get; set; }
        public DateTime? LastMailSendDate { get; set; }
        public DateTime? LastNewsLetterSendDate { get; set; }
        
        private List<Comment> _comments;
        public List<Comment> Comments
        {
            get
            {
                if (_comments == null)
                {
                    _comments = new List<Comment>();
                }

                return _comments;
            }
            set { _comments = value; }
        }

        public DateTime? ModificationDate { get; set; }

        public EntryTypes EntryType
        {
            get { return EntryTypes.User; }
        }

        public Settings Settings
        {
            get
            {
                if (_settings == null)
                {
                    _settings = new Settings();
                }

                return _settings;
            }
            set { _settings = value; }
        }

        public DateTime MemberSince { get; set; }
        public string Summary { get; set; }
        public string Specialties { get; set; }
        
        [BsonIgnore]
        public string CurrentLocation
        {
            get
            {
                string place = Country;
                if(!string.IsNullOrEmpty(Municipality))
                {
                    place = Municipality + ", " + place;
                }

                if(!string.IsNullOrEmpty(City))
                {
                    place = City + ", " + place;
                }

                return place;
            }
        }
        [BsonIgnore]
        public string OriginLocation
        {
            get
            {
                string place = OriginCountry;
                if (!string.IsNullOrEmpty(OriginMunicipality))
                {
                    place = OriginMunicipality + ", " + place;
                }

                if (!string.IsNullOrEmpty(OriginCity))
                {
                    place = OriginCity + ", " + place;
                }

                return place;
            }
        }

        public MongoObjectId ProfilePictureId { get; set; }
        public MongoObjectId ProfilePictureThumbId { get; set; }
        public List<MongoObjectId> ProfilePictureHistory { get; set; }

        public string GetRelatedVersionNumber(string versionId)
        {
            return null;
        }

        public User()
        {
            Educations = new List<Education>();
            Positions = new List<WorkPosition>();
            MemberOfParties = new List<PoliticalParty>();
            PhoneNumbers = new List<PhoneNumber>();
            WebSites = new List<Website>();
            ProfilePictureHistory = new List<MongoObjectId>();
            Settings = new Settings();
            Comments = new List<Comment>();
            Id = BsonObjectId.GenerateNewId();
        }
    }
}