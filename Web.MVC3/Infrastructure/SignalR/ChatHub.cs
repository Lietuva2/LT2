using System;
using System.Threading.Tasks;
using Data.ViewModels.Chat;
using Framework.Strings;
using Microsoft.AspNet.SignalR;
using Services.Caching;
using Services.Classes;
using Services.ModelServices;
using Web.Infrastructure.SignalR;

namespace Web.Infrastructure.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly ChatService service;
        private readonly ICache cache;
        private readonly UserService userService;
        private const string UserListGroupPrefix = "_userList";

        private string UserAgent
        {
            get
            {
                if (Context.Headers != null)
                {
                    return Context.Headers["User-Agent"];
                }
                return null;
            }
        }

        public ChatHub(ChatService service, UserService userService, ICache cache)
        {
            this.service = service;
            this.cache = cache;
            this.userService = userService;
        }

        public void Login(string groupId)
        {
            var userInfo = userService.GetUserInfoByUserName(Context.User.Identity.Name);
            Clients.Caller.Id = userInfo.Id;
            Clients.Caller.DbId = userInfo.DbId;
            Clients.Caller.FullName = userInfo.FullName;
            Clients.Caller.FirstName = userInfo.FirstName;
            var userGroups = service.GetUserGroups(userInfo.Id);

            foreach (var userGroup in userGroups)
            {
                Groups.Add(Context.ConnectionId, userGroup.Id);
            }

            if (!string.IsNullOrEmpty(groupId))
            {
                Groups.Add(Context.ConnectionId, groupId);
                Groups.Add(Context.ConnectionId, groupId + UserListGroupPrefix);
                foreach (var user in service.GetGroupUsers(groupId))
                {
                    if (user.Id != userInfo.Id)
                    {
                        Clients.Caller.addUser(user.Id, user.Name, user.IsOnline);
                    }
                }

                if (!service.IsUserInGroup(userInfo.Id, groupId))
                {
                    Clients.OthersInGroup(groupId + UserListGroupPrefix).addUser(userInfo.Id, userInfo.FullName, true);
                }

                var cnt = service.GetGroupMessageCount(groupId, (int)Clients.Caller.DbId);
                Clients.Caller.groupMessageCount(cnt);
            }

            foreach (var group in userGroups)
            {
                Clients.OthersInGroup(group + UserListGroupPrefix).userConnected(userInfo.Id);
            }

            foreach (var group in service.GetOpenGroups(userInfo.DbId.Value))
            {
                OpenGroupDialogInternal(group.Id, group.Name, group.Url);
            }

            foreach (var otherUser in service.GetOpenDialogUsers(userInfo.DbId.Value))
            {
                var msgs = service.GetMessages(HistoryType.Today, userInfo.DbId.Value, otherUser.DbId);
                Clients.Caller.messageHistory(msgs, otherUser.Id, otherUser.Name, otherUser.IsOnline);

                Clients.User(otherUser.Id).userConnected(userInfo.Id);
                //var connections = service.GetConnectionIds(otherUser.DbId);
                //foreach (var conn in connections)
                //{
                //    Clients.Client(conn).userConnected(userInfo.Id);
                //}
            }

            service.SaveConnectionId(userInfo.Id, groupId, Context.ConnectionId, UserAgent);
        }

        public override System.Threading.Tasks.Task OnConnected()
        {
            return base.OnConnected();
        }

        public override System.Threading.Tasks.Task OnReconnected()
        {
            return base.OnReconnected();
        }

        public override System.Threading.Tasks.Task OnDisconnected(bool stopCalled)
        {
            var openDialogs = service.GetOpenDialogConnectionIds(Context.ConnectionId);
            foreach (var conn in openDialogs)
            {
                Clients.Client(conn.ConnectionId).userDisconnected(conn.Id);
            }

            if (!service.HasOtherClients(Context.ConnectionId))
            {
                foreach (var userGroup in service.GetClientUserGroupIds(Context.ConnectionId))
                {
                    Clients.OthersInGroup(userGroup.GroupId + UserListGroupPrefix).userDisconnected(userGroup.UserId);
                }
            }

            var groupUser = service.CloseConnection(Context.ConnectionId);

            if (groupUser != null)
            {
                if (!service.IsUserInGroup(groupUser.UserId, groupUser.GroupId))
                {
                    Clients.OthersInGroup(groupUser.GroupId + UserListGroupPrefix).removeUser(groupUser.UserId);
                }
            }

            return base.OnDisconnected(stopCalled);
        }

        public void UserIsTyping(string userId)
        {
            Clients.User(userId).userIsTyping(Clients.Caller.Id);
            //var connections = service.GetConnectionIds(userId);
            //foreach (var conn in connections)
            //{
            //    Clients.Client(conn).userIsTyping(Clients.Caller.Id);
            //}
        }

        public void UserNotTyping(string userId)
        {
            Clients.User(userId).userNotTyping(Clients.Caller.Id);
            //var connections = service.GetConnectionIds(userId);
            //foreach (var conn in connections)
            //{
            //    Clients.Client(conn).userNotTyping(Clients.Caller.Id);
            //}
        }

        public void SendMessage(string message, string userId)
        {
            var dbId = userService.GetUserDbId(userId);
            string callerId = Clients.Caller.Id;
            if (userService.IsUserBlocked(dbId.Value, (int) Clients.Caller.DbId))
            {
                Clients.Caller.newMessage(
                    GetMessageModel("Jūs esate užblokuotas ir negalite susisiekti su šiuo asmeniu"),
                    userId);

                return;
            }

            Clients.User(userId)
                .newMessage(GetMessageModel(message), Clients.Caller.Id, Clients.Caller.FullName, false);

            foreach (var otherClientId in service.GetOtherClientIds(Context.ConnectionId))
            {
                Clients.Client(otherClientId)
                       .newMessage(GetMessageModel(message), userId);
            }

            service.SaveOpenDialog((int) Clients.Caller.DbId, dbId.Value, true);
            service.SaveMessage(message, (int) Clients.Caller.DbId, dbId.Value);

            

            //service.SaveOpenDialog((int)Clients.Caller.DbId, dbId.Value, true);
            //service.SaveMessage(message, (int)Clients.Caller.DbId, dbId.Value);


            //if (!dbId.HasValue)
            //{
            //    return;
            //}

            //var connectionIds = service.GetConnectionIds(dbId.Value);

            //if (userService.IsUserBlocked(dbId.Value, (int)Clients.Caller.DbId))
            //{
            //    Clients.Caller.newMessage(GetMessageModel("Jūs esate užblokuotas ir negalite susisiekti su šiuo asmeniu"),
            //               userId);

            //    return;
            //}

            //foreach (var connectionId in connectionIds)
            //{
            //    Clients.Client(connectionId)
            //           .newMessage(GetMessageModel(message), Clients.Caller.Id, Clients.Caller.FullName, false);
            //}

            //foreach (var otherClientId in service.GetOtherClientIds(Context.ConnectionId))
            //{
            //    Clients.Client(otherClientId)
            //           .newMessage(GetMessageModel(message), userId);
            //}

            //service.SaveOpenDialog((int)Clients.Caller.DbId, dbId.Value, true);
            //service.SaveMessage(message, (int)Clients.Caller.DbId, dbId.Value);

        }

        public void OpenDialog(string userId)
        {
            service.SaveOpenDialog((int)Clients.Caller.DbId, userService.GetUserDbId(userId).Value, false);
        }

        public void SendGroupMessage(string message, string groupId, string chatName, string url)
        {
            var group = service.SaveGroup(groupId, url, chatName);
            Clients.OthersInGroup(groupId).newMessage(GetMessageModel(message), groupId, chatName, true, group.Url, Clients.Caller.Id);
            service.SaveMessage(message, (int)Clients.Caller.DbId, groupId: groupId);
            service.AddUserToGroup((int)Clients.Caller.DbId, groupId, chatName);
            service.SaveOpenGroup((int)Clients.Caller.DbId, groupId);
            foreach (var user in service.GetGroupUsers(groupId))
            {
                service.SaveOpenGroup(user.DbId, groupId);
            }
        }

        public void OpenGroupDialog(string groupId, string name, string url)
        {
            var group = service.SaveGroup(groupId, url, name);
            OpenGroupDialogInternal(groupId, name, url);
            service.AddUserToGroup((int)Clients.Caller.DbId, groupId, name);
            service.SaveOpenGroup((int)Clients.Caller.DbId, groupId);
        }

        private void OpenGroupDialogInternal(string groupId, string name, string url)
        {
            if (!string.IsNullOrEmpty(name))
            {
                var msgs = service.GetMessages(HistoryType.Unanswered, groupId: groupId,
                                               userId: (int) Clients.Caller.DbId);
                Clients.Caller.messageHistory(msgs, groupId, name, null, true, url);
            }
        }

        public void CloseDialog(string groupOruserId)
        {
            var dbId = userService.GetUserDbId(groupOruserId);
            if (dbId.HasValue)
            {
                service.CloseDialog((int) Clients.Caller.DbId, dbId.Value);
            }
            else
            {
                service.CloseGroup((int)Clients.Caller.DbId, groupOruserId);
            }
        }

        public void UnsubscribeGroup(string groupId)
        {
            service.RemoveUserFromGroup((int)Clients.Caller.DbId, groupId);
            service.CloseGroup((int)Clients.Caller.DbId, groupId);
            Groups.Remove(Context.ConnectionId, groupId);
        }

        public void GetHistory(string userId, HistoryType type)
        {
            var otherUser = service.GetChatUser(userId);
            var msgs = service.GetMessages(type, (int)Clients.Caller.DbId, otherUser.DbId);
            Clients.Caller.messageHistory(msgs, userId, otherUser.Name, null, false);
        }

        public void GetGroupHistory(string groupId, HistoryType type)
        {
            var msgs = service.GetMessages(type, groupId: groupId);
            var group = service.GetChatGroup(groupId);
            Clients.Caller.messageHistory(msgs, groupId, group.Name, null, true);
        }

        public void StopBlinking()
        {
            var otherClients = service.GetOtherClientIds(Context.ConnectionId);
            foreach (var connId in otherClients)
            {
                Clients.Client(connId).stopBlinking();
            }
        }

        public void StopAllClients()
        {
            var otherClients = service.GetOtherClientIds(Context.ConnectionId);
            foreach (var connId in otherClients)
            {
                Clients.Client(connId).stopClient();
            }
        }

        private ChatMessageModel GetMessageModel(string message)
        {
            return new ChatMessageModel()
            {
                Date = DateTime.Now,
                Message = message.Sanitize().ActivateLinks(),
                UserId = Clients.Caller.Id,
                UserName = Clients.Caller.FirstName,
                FullName = Clients.Caller.FullName
            };
        }
    }
}
