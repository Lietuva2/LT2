using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Data.EF.Actions;
using Data.EF.Users;
using Data.EF.Voting;
using Data.Infrastructure.Sessions;
using Data.ViewModels.Account;
using Data.ViewModels.Chat;
using EntityFramework.Caching;
using EntityFramework.Extensions;
using Framework;
using Framework.Lists;
using Framework.Mvc.Lists;
using Framework.Strings;
using Globalization.Resources.Services;

using Services.Caching;
using Services.Classes;
using Services.Infrastructure;
using Services.Session;

namespace Services.ModelServices
{
    public class ChatService : IService
    {
        private IUsersContextFactory usersSessionFactory;
        private IVotingContextFactory votingSessionFactory;
        private Func<INoSqlSession> noSqlSessionFactory;

        private readonly ICache cache;

        public ChatService(
            IUsersContextFactory usersSessionFactory,
            Func<INoSqlSession> mongoDbSessionFactory,
            IVotingContextFactory votingSessionFactory,
            ICache cache)
        {
            this.usersSessionFactory = usersSessionFactory;
            this.noSqlSessionFactory = mongoDbSessionFactory;
            this.votingSessionFactory = votingSessionFactory;
            this.cache = cache;
        }

        private User GetUser(string objectId)
        {
            using (var context = usersSessionFactory.CreateContext())
            {
                return context.Users.Where(u => u.ObjectId == objectId).SingleOrDefault();
            }
        }

        public bool IsUserInGroup(string userId, string groupId)
        {
            using (var context = usersSessionFactory.CreateContext())
            {
                var user = GetUser(userId);
                return IsUserInGroup(user, groupId);
            }
        }

        public bool IsUserInGroup(int userId, string groupId)
        {
            using (var context = usersSessionFactory.CreateContext())
            {
                var user = context.Users.Single(u => u.Id == userId);
                return IsUserInGroup(user, groupId);
            }
        }

        private bool IsUserInGroup(User user, string groupId)
        {
            return user.ChatGroupUsers.Any(room => room.GroupId.Equals(groupId, StringComparison.OrdinalIgnoreCase)) || user.ChatClients.Any(c => c.GroupId == groupId);
        }

        public List<ChatGroupModel> GetUserGroups(string userId)
        {
            using (var context = usersSessionFactory.CreateContext())
            {
                return context.Users.Where(u => u.ObjectId == userId).SelectMany(u => u.ChatGroupUsers).Select(u => new ChatGroupModel(){Id = u.GroupId, Name = u.ChatGroup.Name, Url = u.ChatGroup.Url}).ToList();
            }
        }

        public ChatGroupModel GetChatGroup(string groupId)
        {
            using (var context = usersSessionFactory.CreateContext())
            {
                return context.ChatGroups.Where(u => u.Id == groupId).Select(u => new ChatGroupModel() { Id = u.Id, Name = u.Name, Url = u.Url }).SingleOrDefault();
            }
        }

        public List<ChatUser> GetGroupUsers(string groupId)
        {
            using (var context = usersSessionFactory.CreateContext())
            {
                var groupUsers = context.ChatGroupUsers.Where(g => g.GroupId == groupId).Select(g => new ChatUser()
                    {
                        Id = g.User.ObjectId,
                        Name = g.User.FirstName + " " + g.User.LastName,
                        IsOnline = context.ChatClients.Any(c => c.UserId == g.UserId),
                        DbId = g.UserId
                    }).ToList();

                var clientUsers = context.ChatClients.Where(c => c.GroupId == groupId).Select(g => new ChatUser()
                    {
                        Id = g.User.ObjectId,
                        Name = g.User.FirstName + " " + g.User.LastName,
                        IsOnline = true,
                        DbId = g.UserId
                    }).ToList();

                clientUsers.AddRange(
                    groupUsers.Where(g => !clientUsers.Select(c => c.Id).Contains(g.Id)));

                return clientUsers;
            }
        }

        public void SaveConnectionId(string userId, string groupId, string connectionId, string userAgent)
        {
            using (var context = usersSessionFactory.CreateContext(true))
            {
                var conn = context.ChatClients.SingleOrDefault(c => c.ConnectionId == connectionId);
                if (conn == null)
                {
                    var user = GetUser(userId);
                    user.ChatClients.Add(new ChatClient()
                        {
                            GroupId = groupId,
                            LastActivity = DateTime.Now,
                            UserAgent = userAgent,
                            ConnectionId = connectionId
                        });
                }
                else
                {
                    conn.LastActivity = DateTime.Now;
                }
            }
        }

        public List<string> GetConnectionIds(int id)
        {
            using (var context = usersSessionFactory.CreateContext())
            {
                return context.ChatClients.Where(u => u.UserId == id).Select(u => u.ConnectionId).ToList();
            }
        }

        public List<string> GetConnectionIds(string id)
        {
            using (var context = usersSessionFactory.CreateContext())
            {
                return context.Users.Where(u => u.ObjectId == id).SelectMany(u => u.ChatClients).Select(u => u.ConnectionId).ToList();
            }
        }

        public GroupUser CloseConnection(string connectionId)
        {
            using (var context = usersSessionFactory.CreateContext())
            {
                var client = context.ChatClients.Where(c => c.ConnectionId == connectionId).Select(c => new { Client = c, UserId = c.User.ObjectId }).SingleOrDefault();
                if (client != null)
                {
                    var groupId = client.Client.GroupId;
                    var userId = client.UserId;

                    context.ChatClients.Remove(client.Client);
                    context.SaveChanges();

                    if (!string.IsNullOrEmpty(groupId))
                    {
                        return new GroupUser()
                            {
                                GroupId = groupId,
                                UserId = userId
                            };
                    }
                }
            }

            return null;
        }

        private ChatOpenDialog GetOpenDialog(int userId, int otherUserId)
        {
            using (var context = usersSessionFactory.CreateContext())
            {
                return
                    context.ChatOpenDialogs.SingleOrDefault(d => d.UserId == userId && d.OtherUserId == otherUserId);
            }
        }

        private ChatOpenGroup GetOpenGroup(int userId, string groupId)
        {
            using (var context = usersSessionFactory.CreateContext())
            {
                return
                    context.ChatOpenGroups.SingleOrDefault(d => d.UserId == userId && d.GroupId == groupId);
            }
        }

        public List<ChatUser> GetOpenDialogUsers(int userId)
        {
            using (var context = usersSessionFactory.CreateContext())
            {
                return
                    context.ChatOpenDialogs.Where(d => d.UserId == userId)
                                                                 .Select(d => new ChatUser()
                                                                 {
                                                                     DbId = d.OtherUserId,
                                                                     Name = d.User.FirstName + " " + d.User.LastName,
                                                                     Id = d.User.ObjectId,
                                                                     IsOnline = context.ChatClients.Any(c => c.UserId == d.OtherUserId)
                                                                 }).ToList();
            }
        }

        public List<ChatGroupModel> GetOpenGroups(int userId)
        {
            using (var context = usersSessionFactory.CreateContext())
            {
                return
                    context.ChatOpenGroups.Where(d => d.UserId == userId)
                                                                 .Select(d => new ChatGroupModel()
                                                                 {
                                                                     Id = d.GroupId,
                                                                     Name = d.ChatGroup.Name,
                                                                     Url = d.ChatGroup.Url
                                                                 }).ToList();
            }
        }

        public List<ChatUser> GetOpenDialogConnectionIds(string connectionId)
        {
            using (var context = usersSessionFactory.CreateContext())
            {
                return (from c in context.ChatClients
                        from d in c.User.ChatOpenDialogs
                        from cc in d.User1.ChatClients
                        where c.ConnectionId == connectionId
                        select new ChatUser()
                            {
                                Id = d.User.ObjectId,
                                ConnectionId = cc.ConnectionId
                            }).ToList();
            }
        }

        public void SaveOpenDialog(int userId, int otherUserId, bool both)
        {
            using (var context = usersSessionFactory.CreateContext(true))
            {
                if (GetOpenDialog(userId, otherUserId) == null)
                {
                    context.ChatOpenDialogs.Add(new ChatOpenDialog()
                        {
                            UserId = userId,
                            OtherUserId = otherUserId
                        });
                }

                if (both && GetOpenDialog(otherUserId, userId) == null)
                {
                    context.ChatOpenDialogs.Add(new ChatOpenDialog()
                    {
                        UserId = otherUserId,
                        OtherUserId = userId
                    });
                }
            }
        }

        public void SaveOpenGroup(int userId, string groupId)
        {
            using (var context = usersSessionFactory.CreateContext(true))
            {
                if (GetOpenGroup(userId, groupId) == null)
                {
                    context.ChatOpenGroups.Add(new ChatOpenGroup()
                    {
                        UserId = userId,
                        GroupId = groupId
                    });
                }
            }
        }

        public void CloseDialog(int userId, int otherUserId)
        {
            using (var context = usersSessionFactory.CreateContext(true))
            {
                var dialog = GetOpenDialog(userId, otherUserId);
                if (dialog != null)
                {
                    context.ChatOpenDialogs.Remove(dialog);
                }
            }
        }

        public void CloseGroup(int userId, string groupId)
        {
            using (var context = usersSessionFactory.CreateContext(true))
            {
                var dialog = GetOpenGroup(userId, groupId);
                if (dialog != null)
                {
                    context.ChatOpenGroups.Remove(dialog);
                }
            }
        }

        public List<ChatMessageModel> GetMessages(HistoryType lastDay, int? userId = null, int? otherUserId = null, string groupId = null)
        {
            using (var context = usersSessionFactory.CreateContext())
            {
                if (!string.IsNullOrEmpty(groupId) && otherUserId.HasValue)
                {
                    throw new ArgumentException("Both otherUserId and groupId cannot be set");
                }

                var query = context.ChatMessages.AsQueryable();
                if (userId.HasValue && otherUserId.HasValue)
                {
                    query = query.Where(d => d.UserId == userId && d.OtherUserId == otherUserId || d.UserId == otherUserId && d.OtherUserId == userId);
                }

                if (!string.IsNullOrEmpty(groupId))
                {
                    query = query.Where(d => d.GroupId == groupId);
                }

                
                switch (lastDay)
                {
                    case HistoryType.Today:
                        var today = DateTime.Now.AddHours(-24);
                        query = query.Where(q => q.Date >= today);
                        break;
                        case HistoryType.LastWeek:
                        var week = DateTime.Now.Date.AddDays(-7);
                            query = query.Where(q => q.Date >= week);
                        break;
                        case HistoryType.LastMonth:
                        var month = DateTime.Now.Date.AddMonths(-1);
                            query = query.Where(q => q.Date >= month);
                        break;
                        case HistoryType.All:
                        break;
                        case HistoryType.Unanswered:
                        if (userId.HasValue)
                        {
                            var date = GetUserChatDate(groupId, userId.Value);
                            if (date.HasValue)
                            {
                                query = query.Where(q => q.Date > date);
                            }
                        }
                        break;
                }

                var result =
                    query.OrderByDescending(d => d.Id).Select(d => new ChatMessageModel()
                               {
                                   UserId = d.User.ObjectId,
                                   UserName = d.User.FirstName,
                                   Date = d.Date,
                                   Message = d.Content,
                                   FullName = d.User.FirstName + " " + d.User.LastName
                               }).ToList();

                result.ForEach(r => r.Message = r.Message.ActivateLinks());
                return result;
            }
        }

        public void SaveMessage(string message, int userDbId, int? otherUserDbId = null, string groupId = null)
        {
            using (var context = usersSessionFactory.CreateContext(true))
            {
                context.ChatMessages.Add(new ChatMessage()
                {
                    Content = message.Sanitize(),
                    Date = DateTime.Now,
                    UserId = userDbId,
                    OtherUserId = otherUserDbId,
                    GroupId = groupId,
                    HtmlEncoded = false
                });
            }
        }

        public ChatUser GetChatUser(string userId)
        {
            using (var context = usersSessionFactory.CreateContext())
            {
                return context.Users.Where(u => u.ObjectId == userId).Select(u => new ChatUser()
                    {
                        Id = u.ObjectId,
                        DbId = u.Id,
                        Name = u.FirstName + " " + u.LastName
                    }).SingleOrDefault();
            }
        }

        public void AddUserToGroup(int userDbId, string groupId, string name)
        {
            using (var context = usersSessionFactory.CreateContext(true))
            {
                if (!context.ChatGroupUsers.Any(c => c.UserId == userDbId && c.GroupId == groupId))
                {
                    context.ChatGroupUsers.Add(new ChatGroupUser()
                        {
                            UserId = userDbId,
                            GroupId = groupId
                        });
                }
            }
        }

        public void RemoveUserFromGroup(int userDbId, string groupId)
        {
            using (var context = usersSessionFactory.CreateContext(true))
            {
                var groupUser = context.ChatGroupUsers.SingleOrDefault(c => c.UserId == userDbId && c.GroupId == groupId);
                if (groupUser != null)
                {
                    context.ChatGroupUsers.Remove(groupUser);
                }
            }
        }

        public bool HasOtherClients(string connectionId)
        {
            using (var context = usersSessionFactory.CreateContext())
            {
                return GetOtherClientQuery(connectionId).Any();
            }
        }

        public List<string> GetOtherClientIds(string connectionId)
        {
            using (var context = usersSessionFactory.CreateContext())
            {
                return GetOtherClientQuery(connectionId).Select(c => c.ConnectionId).ToList();
            }
        }

        public IQueryable<ChatClient> GetOtherClientQuery(string connectionId)
        {
            using (var context = usersSessionFactory.CreateContext())
            {
                return from c in context.ChatClients.Where(cc => cc.ConnectionId == connectionId)
                            from u in c.User.ChatClients.Where(uu => uu.ConnectionId != connectionId)
                            select u;
            }
        }

        public List<GroupUser> GetClientUserGroupIds(string connectionId)
        {
            using (var context = usersSessionFactory.CreateContext())
            {
                return (from c in context.ChatClients
                        from u in c.User.ChatGroupUsers
                        where c.ConnectionId == connectionId
                        select new GroupUser()
                            {
                                GroupId = u.GroupId,
                                UserId = c.User.ObjectId
                            }).ToList();
            }
        }

        public ChatGroup SaveGroup(string groupId, string url, string name)
        {
            using (var context = usersSessionFactory.CreateContext(true))
            {
                var group = context.ChatGroups.SingleOrDefault(g => g.Id == groupId);
                if (group == null)
                {
                    group = new ChatGroup()
                        {
                            Id = groupId,
                            Url = url
                        };
                    context.ChatGroups.Add(group);
                }

                
                group.Name = name;
                SetGroupProperties(group);

                return group;
            }
        }

        private void SetGroupProperties(ChatGroup group)
        {
            using (var context = votingSessionFactory.CreateContext())
            {
                var idea = context.Ideas.Where(i => i.Id == group.Id).SingleOrDefault();
                if (idea != null)
                {
                    group.IsPrivate = idea.IsPrivate;
                    group.OrganizationId = idea.OrganizationId;
                }

                var issue = context.Issues.Where(i => i.ObjectId == group.Id).SingleOrDefault();
                if (issue != null)
                {
                    group.IsPrivate = issue.IsPrivateToOrganization;
                    group.OrganizationId = issue.OrganizationId;
                }
            }
        }

        public void ClearClients()
        {
            using (var context = usersSessionFactory.CreateContext(true))
            {
                context.ChatClients.Delete();
            }
        }

        public int GetGroupMessageCount(string groupId, int userId)
        {
            using (var context = usersSessionFactory.CreateContext(true))
            {
                var date = GetUserChatDate(groupId, userId);
                var query = context.ChatMessages.Where(c => c.GroupId == groupId);
                if (date.HasValue)
                {
                    query = query.Where(c => c.Date > date);
                }
                return query.Count();
            }
        }

        private DateTime? GetUserChatDate(string groupId, int userId)
        {
            using (var context = usersSessionFactory.CreateContext(true))
            {
                return
                    context.ChatMessages.Where(c => c.GroupId == groupId && c.UserId == userId)
                           .Max(c => (DateTime?) c.Date);
            }
        }

        public ChatIndexModel GetIndexModel()
        {
            using (var context = usersSessionFactory.CreateContext())
            {
                var userId = MembershipSession.GetUser().DbId;
                var orgIds = MembershipSession.GetUser().OrganizationIds;
                var model = new ChatIndexModel();
                model.Groups =
                    context.ChatGroups.Where(c => c.ChatMessages.Any() && (!c.IsPrivate || orgIds.Contains(c.OrganizationId))).OrderByDescending(m => m.ChatMessages.Max(cm => cm.Date))
                    .Select(g => new ChatGroupModel()
                        {
                            Id = g.Id,
                            Name = g.Name,
                            Url = g.Url,
                            MessageCount = g.ChatMessages.Count(),
                            Date = g.ChatMessages.Max(m => m.Date),
                            Users = g.ChatGroupUsers.Where(u => u.UserId != userId).Select(u => new ChatUser()
                                {
                                    Id = u.User.ObjectId,
                                    DbId = u.User.Id,
                                    Name = u.User.FirstName + " " + u.User.LastName,
                                    IsOnline = u.User.ChatClients.Any()
                                })
                        }).Take(20).ToList();

                model.Users = context.ChatClients.Where(c => c.User.Id != userId).Select(c => new ChatUser()
                    {
                        Id = c.User.ObjectId,
                        DbId = c.User.Id,
                        Name = c.User.FirstName + " " + c.User.LastName,
                        IsOnline = true
                    }).Distinct().ToList();

                return model;
            }
        }
    }
}
