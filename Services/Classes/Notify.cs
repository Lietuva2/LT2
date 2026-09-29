using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Data.EF.Actions;
using Data.Enums;
using Data.Infrastructure.Sessions;
using Action = System.Action;

namespace Services.Classes
{
    public class UserNotifier
    {
        private IEnumerable<int> notifyUserIds;
        private ActionsEntities actionsEntities;
        private Data.EF.Actions.Action action;
        private int userId;

        public UserNotifier(ActionsEntities actionsEntities, int userId)
        {
            this.actionsEntities = actionsEntities;
            this.userId = userId;
        }

        public UserNotifier(ActionsEntities actionsEntities, Data.EF.Actions.Action action, int userId) :
            this(actionsEntities, action, userId, new List<int>())
        {
        }

        public UserNotifier(ActionsEntities actionsEntities, Data.EF.Actions.Action action, int userId, List<int> notifyUserIds)
        {
            this.notifyUserIds = notifyUserIds;
            this.actionsEntities = actionsEntities;
            this.action = action;
            this.userId = userId;
        }

        public UserNotifier NotifyInterestedUsers()
        {
            notifyUserIds = notifyUserIds.Union(from u in actionsEntities.UserInterestingUsers
                                where u.InterestingUsersId == userId
                                select u.InterestedUsersId);
            return this;
        }

        public UserNotifier NotifyOrganizationUsers()
        {
            if (action != null && !string.IsNullOrEmpty(action.OrganizationId))
            {
                notifyUserIds = notifyUserIds.Union(from u in actionsEntities.UserInterestingOrganizations
                                                    where u.OrganizationId == action.OrganizationId
                                                    select u.UserId);
            }

            return this;
        }

        public UserNotifier NotifyOrganizationMembers()
        {
            if (action != null && !string.IsNullOrEmpty(action.OrganizationId))
            {
                notifyUserIds = notifyUserIds.Union(from u in actionsEntities.UserInterestingOrganizations
                                                    where u.OrganizationId == action.OrganizationId && u.IsMember && u.IsConfirmed
                                                    select u.UserId);
            }

            return this;
        }

        public UserNotifier NotifyCategoryUsers(int[] categoryIds)
        {
            notifyUserIds = notifyUserIds.Union(from c in actionsEntities.InterestingCategories
                                  where categoryIds.Contains(c.CategoryId)
                                  select c.UserId);
            return this;
        }

        public UserNotifier NotifyObjectUsers(string objectId)
        {
            notifyUserIds = notifyUserIds.Union(GetObjectUsers(objectId));
            return this;
        }

        public IEnumerable<int> GetObjectUsers(string objectId)
        {
            return from a in actionsEntities.Actions
                   where
                       a.UserId != userId && a.ObjectId == objectId && a.ActionTypeId != (short) ActionTypes.IssueViewed &&
                       a.ActionTypeId != (short) ActionTypes.IdeaViewed
                   select a.UserId;
        }

        public void Notify()
        {
            Notify(true);
        }

        public void Notify(bool excludeUser)
        {
            if (excludeUser)
            {
                notifyUserIds = notifyUserIds.Where(u => u != userId);
            }
            notifyUserIds = notifyUserIds.Distinct().ToList();
            foreach (var interestedUserId in notifyUserIds)
            {
                var notification = new Notification
                {
                    Action = action,
                    IsRead = false,
                    UserId = interestedUserId
                };

                actionsEntities.Notifications.Add(notification);
            }
        }

        public IEnumerable<int> GetNotifyUserIds()
        {
            return notifyUserIds;
        }
    }
}
