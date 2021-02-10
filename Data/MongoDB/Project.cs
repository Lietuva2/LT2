using System;
using System.Collections.Generic;
using System.ComponentModel;
using Data.Enums;
using Framework.Infrastructure.Storage;
using Hyper.ComponentModel;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.MongoDB
{
    [TypeDescriptionProvider(typeof(HyperTypeDescriptionProvider))]
    public class Project
    {
        public MongoObjectId Id { get; set; }
        public MongoObjectId IdeaId { get; set; }
        
        public List<ProjectMember> ProjectMembers { get; set; }
        public List<ProjectMember> UnconfirmedMembers { get; set; }
        
        public List<ToDo> ToDos { get; set; }
        public List<MileStone> MileStones { get; set; }
        public bool IsPrivate { get; set; }

        public Project()
        {
            ToDos = new List<ToDo>();
            MileStones = new List<MileStone>();
            ProjectMembers = new List<ProjectMember>();
            UnconfirmedMembers = new List<ProjectMember>();
        }
    }
}