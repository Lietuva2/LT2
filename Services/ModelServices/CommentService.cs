using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Web.Mvc;
using Bus.Commands;
using Data.EF.Voting;
using Data.Enums;
using Data.Infrastructure.Sessions;
using Data.MongoDB;
using Data.MongoDB.Interfaces;
using Data.ViewModels.Base;
using Data.ViewModels.Comments;
using EntityFramework.Extensions;
using Framework;
using Framework.Bus;
using Framework.Enums;
using Framework.Exceptions;
using Framework.Infrastructure;
using Framework.Infrastructure.Logging;
using Framework.Infrastructure.Storage;
using Framework.Lists;
using Framework.Other;
using Framework.Strings;
using Globalization;
using Globalization.Resources.Services;
using MongoDB.Driver.Builders;

using Omu.ValueInjecter;
using Services.Classes;
using Services.Infrastructure;
using Services.Session;
using Comment = Data.MongoDB.Comment;
using Idea = Data.MongoDB.Idea;

namespace Services.ModelServices
{
    public class CommentService : IService
    {
        private readonly IVotingContextFactory votingSessionFactory;
        private readonly Func<INoSqlSession> noSqlSessionFactory;
        private readonly ILogger logger;
        private readonly IBus bus;

        public UserInfo CurrentUser { get { return MembershipSession.GetUser(); } }
        public ActionService ActionService { get { return ServiceLocator.Resolve<ActionService>(); } }
        public UserService UserService { get { return ServiceLocator.Resolve<UserService>(); } }

        public CommentService(IVotingContextFactory votingSessionFactory, Func<INoSqlSession> noSqlSessionFactory, ILogger logger, IBus bus)
        {
            this.votingSessionFactory = votingSessionFactory;
            this.noSqlSessionFactory = noSqlSessionFactory;
            this.logger = logger;
            this.bus = bus;
        }

        public static bool IsComment(ActionTypes actionType)
        {
            return actionType.In(ActionTypes.Commented, ActionTypes.IdeaCommented,
                                   ActionTypes.LikedComment, ActionTypes.IdeaCommentLiked,
                                   ActionTypes.CommentCommented, ActionTypes.CommentCommentLiked,
                                   ActionTypes.UserCommented, ActionTypes.UserCommentLiked);
        }

        public static bool IsNewComment(ActionTypes actionType)
        {
            return actionType.In(ActionTypes.Commented, ActionTypes.IdeaCommented, ActionTypes.CommentCommented, ActionTypes.UserCommented);
        }

        public CommentView GetCommentViewFromComment(string objectId, Data.MongoDB.Comment comment, MongoObjectId parentId, EntryTypes type, string relatedVersion = null)
        {
            var hiddenText = Globalization.Resources.Comments.Resource.CommentHidden;
            var model = new CommentView
            {
                CommentText = comment.IsHidden ? hiddenText : comment.Text.NewLineToHtml().ActivateLinks() ?? string.Empty,
                CommentDate = comment.Date,
                AuthorFullName = comment.UserFullName,
                AuthorObjectId = comment.UserObjectId,
                ForAgainst = comment.PositiveOrNegative,
                ProfilePictureThumbId = UserService.GetProfilePictureThumbId(comment.UserObjectId),
                Liking = new Liking()
                {
                    EntryId = objectId,
                    ParentId = parentId,
                    CommentId = comment.Id,
                    Type = type,
                    LikesCount = GetLikesCountString(comment.SupportingUserIds.Count),
                    IsLikeVisible = !comment.IsHidden && CurrentUser.IsAuthenticated && !comment.SupportingUserIds.Contains(CurrentUser.Id) && comment.UserObjectId != CurrentUser.Id,
                    IsUnlikeVisible = !comment.IsHidden && CurrentUser.IsAuthenticated && comment.SupportingUserIds.Contains(CurrentUser.Id) && comment.UserObjectId != CurrentUser.Id
                },
                Id = comment.Id,
                IsCreatedByCurrentUser = CurrentUser.IsAuthenticated && (comment.UserObjectId == CurrentUser.Id || CurrentUser.Role == UserRoles.Admin),
                IsCommentable = type != EntryTypes.Problem && CurrentUser.IsAuthenticated,
                ParentId = parentId,
                EntryId = objectId,
                VersionId = comment.RelatedVersionId,
                RelatedVersion = relatedVersion,
                EntryType = type,
                IsHidden = comment.IsHidden,
                Number = comment.Number,
                Embed = comment.Embed != null ? new EmbedModel()
                                            {
                                                Url = comment.Embed.Url,
                                                Author_Name = comment.Embed.Author_Name,
                                                Author_Url = comment.Embed.Author_Url,
                                                Cache_Age = comment.Embed.Cache_Age,
                                                Description = comment.Embed.Description,
                                                Height = comment.Embed.Height,
                                                Html = comment.Embed.Type != "link" ? comment.Embed.Html : string.Empty,
                                                Provider_Name = comment.Embed.Provider_Name,
                                                Provider_Url = comment.Embed.Provider_Url,
                                                Thumbnail_Url = comment.Embed.Thumbnail_Url,
                                                Title = comment.Embed.Title,
                                                Type = comment.Embed.Type,
                                                Version = comment.Embed.Version,
                                                Width = comment.Embed.Width
                                            } : null
            };
            if (CurrentUser.IsAuthenticated && parentId == null)
            {
                model.Subscribe = ActionService.IsSubscribed(comment.Id, CurrentUser.DbId.Value);
            }
            //model.Liking.IsCreatedByCurrentUser = CurrentUser.IsAuthenticated && comment.UserObjectId == CurrentUser.Id;

            return model;
        }

        #region DB comments
        public CommentView GetCommentViewFromDb(string commentId, string parentId, UserInfo user)
        {
            using (var session = votingSessionFactory.CreateContext())
            {
                var c = GetCommentDbView(commentId, user);
                return GetCommentView(c, user, parentId);
            }
        }

        public List<CommentView> GetCommentViewFromDb(List<string> commentIds, UserInfo user)
        {
            if (commentIds.Count == 0)
            {
                return new List<CommentView>();
            }

            using (var session = votingSessionFactory.CreateContext(unique: true))
            {
                try
                {
                    if (commentIds.Count > 1)
                    {
                        var dbComments = GetCommentDbView(commentIds, user);
                        List<CommentView> list = new List<CommentView>();
                        foreach (var dbComment in dbComments)
                        {
                            list.Add(GetCommentView(dbComment, user, null, true));
                        }

                        return list;
                    }
                    //sw.Stop();
                    //var single = sw.Elapsed;
                    //sw.Restart();
                    else
                    {
                        var dbComments1 = GetCommentDbViewIds(commentIds, user);
                        List<CommentView> list1 = new List<CommentView>();
                        foreach (var dbComment in dbComments1)
                        {
                            list1.Add(GetCommentView(dbComment, user, null, true));
                        }

                        return list1;
                    }
                }
                finally
                {
                    //logger.Information(string.Format("Comment query execution times: commentsCount - {1}, time = {0}", sw.Elapsed, commentIds.Count));
                }
            }
        }

        public CommentView GetCommentView(BaseCommentDbView dbComment, UserInfo user, string parentId, bool isNewsFeed = false)
        {
            var hiddenText = Globalization.Resources.Comments.Resource.CommentHidden;
            var model = new CommentView
            {
                CommentText = dbComment.IsHidden ? hiddenText : dbComment.Text.NewLineToHtml().ActivateLinks() ?? string.Empty,
                CommentDate = dbComment.Date,
                AuthorFullName = dbComment.UserFullName,
                AuthorObjectId = dbComment.UserObjectId,
                ForAgainst = (ForAgainst)(dbComment.TypeId ?? (int)ForAgainst.Neutral),
                Liking = new Liking()
                {
                    EntryId = dbComment.ObjectId,
                    ParentId = parentId,
                    CommentId = dbComment.Id,
                    Type = (EntryTypes)dbComment.EntryTypeId,
                    LikesCount = GetLikesCountString(dbComment.SupporterCount),
                    IsLikeVisible =
                        user.IsAuthenticated && !dbComment.IsLikedByCurrentUser && dbComment.UserObjectId != user.Id,
                    IsUnlikeVisible = dbComment.IsLikedByCurrentUser && dbComment.UserObjectId != user.Id
                },
                Id = dbComment.Id,
                IsCreatedByCurrentUser =
                    user.IsAuthenticated &&
                    (dbComment.UserObjectId == user.Id || user.Role == UserRoles.Admin),
                IsCommentable = dbComment.EntryTypeId != (int)EntryTypes.Problem && user.IsAuthenticated,
                ParentId = parentId,
                EntryId = dbComment.ObjectId,
                RelatedVersion = null,
                EntryType = (EntryTypes)dbComment.EntryTypeId,
                Comments = new List<CommentView>(),
                IsNewsFeed = isNewsFeed,
                IsHidden = dbComment.IsHidden,
                Subscribe = CurrentUser.IsAuthenticated ? ActionService.IsSubscribed(dbComment.Id, CurrentUser.DbId.Value) : null,
                Number = dbComment.Number,
                ProfilePictureThumbId = UserService.GetProfilePictureThumbId(dbComment.UserObjectId)
            };

            if (dbComment.Embed != null)
            {
                model.Embed.InjectFrom(dbComment.Embed);
            }

            if (dbComment is CommentDbView)
            {
                if (string.IsNullOrEmpty(parentId))
                {
                    var dbComment1 = (dbComment as CommentDbView);
                    if (dbComment1.CommentComments != null)
                    {
                        foreach (var comment in dbComment1.CommentComments)
                        {
                            model.Comments.Add(GetCommentView(comment, user, dbComment.Id));
                        }
                    }
                    if (dbComment1.CommentCommentIds != null)
                    {
                        foreach (var id in dbComment1.CommentCommentIds)
                        {
                            model.Comments.Add(GetCommentViewFromDb(id, dbComment.Id, user));
                        }
                    }
                }
            }
            return model;
        }

        public CommentDbView GetCommentDbView(string commentId, UserInfo user)
        {
            using (var session = votingSessionFactory.CreateContext())
            {
                return session.Comments.Where(cc => cc.Id == commentId).
                    Select(GetCommentDbViewFromDbCommentIds(user)).SingleOrDefault();
            }
        }

        public List<CommentDbView> GetCommentDbView(List<string> commentIds, UserInfo user)
        {
            using (var session = votingSessionFactory.CreateContext())
            {
                return session.Comments.Where(cc => commentIds.Contains(cc.Id)).
                    Select(GetCommentDbViewFromDbComment(user)).ToList();
            }
        }

        public List<CommentDbView> GetCommentDbViewIds(List<string> commentIds, UserInfo user)
        {
            using (var session = votingSessionFactory.CreateContext())
            {
                return session.Comments.Where(cc => commentIds.Contains(cc.Id)).
                    Select(GetCommentDbViewFromDbCommentIds(user)).ToList();
            }
        }

        public Expression<Func<Data.EF.Voting.Comment, CommentDbView>> GetCommentDbViewFromDbComment(UserInfo user)
        {
            return cc => new CommentDbView
                {
                    Id = cc.Id,
                    Text = cc.Text,
                    Date = cc.Date,
                    UserObjectId = cc.UserObjectId,
                    UserFullName = cc.UserFullName,
                    TypeId = cc.TypeId,
                    EntryTypeId = cc.EntryTypeId,
                    ObjectId = cc.ObjectId,
                    SupporterCount = cc.CommentSupporters.Count,
                    IsLikedByCurrentUser =
                        user.IsAuthenticated &&
                        cc.CommentSupporters.Any(s => s.UserId == user.Id),
                    IsHidden = cc.IsHidden,
                    Embed = cc.Embed,
                    Number = cc.Number,
                    CommentComments = cc.Comments1.OrderBy(c1 => c1.Date).Select(cc1 => new CommentCommentDbView
                    {
                        Id = cc1.Id,
                        Text = cc1.Text,
                        Date = cc1.Date,
                        UserObjectId = cc1.UserObjectId,
                        UserFullName = cc1.UserFullName,
                        TypeId = cc1.TypeId,
                        EntryTypeId = cc1.EntryTypeId,
                        ObjectId = cc1.ObjectId,
                        SupporterCount = cc1.CommentSupporters.Count,
                        IsLikedByCurrentUser =
                            user.IsAuthenticated &&
                            cc1.CommentSupporters.Any(s => s.UserId == user.Id),
                        Embed = cc1.Embed,
                        Number = cc1.Number
                    })
                };
        }

        private Expression<Func<Data.EF.Voting.Comment, CommentDbView>> GetCommentDbViewFromDbCommentIds(UserInfo user)
        {
            return cc => new CommentDbView
            {
                Id = cc.Id,
                Text = cc.Text,
                Date = cc.Date,
                UserObjectId = cc.UserObjectId,
                UserFullName = cc.UserFullName,
                TypeId = cc.TypeId,
                EntryTypeId = cc.EntryTypeId,
                ObjectId = cc.ObjectId,
                SupporterCount = cc.CommentSupporters.Count,
                IsLikedByCurrentUser =
                    user.IsAuthenticated &&
                    cc.CommentSupporters.Any(s => s.UserId == user.Id),
                IsHidden = cc.IsHidden,
                CommentCommentIds = cc.Comments1.OrderBy(c1 => c1.Date).Select(c1 => c1.Id),
                Embed = cc.Embed,
                Number = cc.Number
            };
        }

        public void UpdateComments(string objectId, List<Data.MongoDB.Comment> comments, EntryTypes type)
        {
            using (var votingSession = votingSessionFactory.CreateContext(true))
            {
                var dbComments = (from c in votingSession.Comments
                                  where c.ObjectId == objectId && c.ParentCommentId == null
                                  select c).ToList();

                var deletedCommentIds =
                    dbComments.Select(c => c.Id).Except(comments.Select(c => c.Id.ToString())).ToList();
                foreach (var deletedCommentId in deletedCommentIds)
                {
                    var deletedComment = dbComments.Single(c => c.Id == deletedCommentId);
                    foreach (var comment in deletedComment.Comments1.ToList())
                    {
                        votingSession.Comments.Remove(comment);
                    }

                    votingSession.Comments.Remove(deletedComment);
                    if (deletedComment.EmbedId.HasValue)
                    {
                        votingSession.Embeds.Delete(v => v.Id == deletedComment.EmbedId);
                    }
                }

                foreach (var comment in comments)
                {
                    var dbComment = dbComments.SingleOrDefault(c => c.Id == comment.Id.ToString());

                    if (dbComment == null)
                    {
                        dbComment = new Data.EF.Voting.Comment()
                            {
                                Id = comment.Id.ToString()
                            };
                        votingSession.Comments.Add(dbComment);

                        if (type == EntryTypes.Issue)
                        {
                            dbComment.IssueComments.Add(new IssueComment()
                                {
                                    IssueId = objectId,
                                    UserObjectId = comment.UserObjectId,
                                    TypeId = (int)comment.PositiveOrNegative
                                });
                        }
                        else if (type == EntryTypes.Idea)
                        {
                            dbComment.IdeaComments.Add(new IdeaComment()
                                {
                                    IdeaId = objectId,
                                    UserObjectId = comment.UserObjectId,
                                    TypeId = (int)comment.PositiveOrNegative
                                });
                        }
                        else if (type == EntryTypes.Problem)
                        {
                            dbComment.ProblemComments.Add(new ProblemComment()
                                {
                                    ProblemId = objectId,
                                    UserObjectId = comment.UserObjectId
                                });
                        }
                        else if (type == EntryTypes.User)
                        {
                            dbComment.UserComments.Add(new UserComment()
                                {
                                    UserId = objectId,
                                    UserObjectId = comment.UserObjectId,
                                    TypeId = (int)comment.PositiveOrNegative
                                });
                        }
                    }

                    dbComment.Text = comment.Text;
                    dbComment.TypeId = (int)comment.PositiveOrNegative;
                    dbComment.UserObjectId = comment.UserObjectId;
                    dbComment.Date = comment.Date.Truncate();
                    dbComment.EntryTypeId = (int)type;
                    dbComment.ObjectId = objectId;
                    dbComment.UserFullName = comment.UserFullName;
                    dbComment.IsHidden = comment.IsHidden;
                    dbComment.Number = comment.Number ?? string.Empty;
                    if (comment.Embed != null)
                    {
                        var embed = votingSession.Embeds.SingleOrDefault(e => e.Id == dbComment.EmbedId);
                        dbComment.Embed = embed ?? new Data.EF.Voting.Embed();

                        dbComment.Embed.Author_Name = comment.Embed.Author_Name;
                        dbComment.Embed.Author_Url = comment.Embed.Author_Url;
                        dbComment.Embed.Cache_Age = comment.Embed.Cache_Age;
                        dbComment.Embed.Description = comment.Embed.Description;
                        dbComment.Embed.Height = comment.Embed.Height;
                        dbComment.Embed.Html = comment.Embed.Html;
                        dbComment.Embed.Provider_Name = comment.Embed.Provider_Name;
                        dbComment.Embed.Provider_Url = comment.Embed.Provider_Url;
                        dbComment.Embed.Thumbnail_Url = comment.Embed.Thumbnail_Url;
                        dbComment.Embed.Title = comment.Embed.Title;
                        dbComment.Embed.Type = comment.Embed.Type;
                        dbComment.Embed.Url = comment.Embed.Url;
                        dbComment.Embed.Version = comment.Embed.Version;
                        dbComment.Embed.Width = comment.Embed.Width;
                    }

                    var dbSupporters =
                        votingSession.CommentSupporters.Where(c => c.CommentId == dbComment.Id).
                            ToList();
                    var deletedSupporters =
                        dbSupporters.Select(s => s.UserId).Except(comment.SupportingUserIds.Select(s => s.ToString())).ToList();

                    if (deletedSupporters.Any())
                    {
                        votingSession.CommentSupporters.Delete(
                            c => deletedSupporters.Contains(c.UserId) && c.CommentId == dbComment.Id);
                    }
                    //dbSupporters.Where(s => deletedSupporters.Contains(s.UserId)).ToList().ForEach(a => votingSession.CommentSupporters.Remove(a));

                    foreach (var id in comment.SupportingUserIds)
                    {
                        if (!dbSupporters.Any(s => s.UserId == id))
                        {
                            dbComment.CommentSupporters.Add(new CommentSupporter()
                                {
                                    UserId = id
                                });
                        }
                    }

                    var dbCommentComments = (from c in votingSession.Comments
                                             where c.ParentCommentId == dbComment.Id
                                             select c).ToList();

                    var deletedCommentCommentIds =
                        dbCommentComments.Select(c => c.Id).Except(comment.Comments.Select(c => c.Id.ToString())).ToList
                            ();
                    foreach (var deletedCommentId in deletedCommentCommentIds)
                    {
                        var deletedComment = dbCommentComments.Single(c => c.Id == deletedCommentId);
                        votingSession.Comments.Remove(deletedComment);

                        if (deletedComment.EmbedId.HasValue)
                        {
                            votingSession.Embeds.Delete(v => v.Id == deletedComment.EmbedId);
                        }
                    }

                    foreach (var cComment in comment.Comments)
                    {
                        var dbCommentComment = dbCommentComments.SingleOrDefault(c => c.Id == cComment.Id);

                        if (dbCommentComment == null)
                        {
                            dbCommentComment = new Data.EF.Voting.Comment()
                                {
                                    Id = cComment.Id,
                                    ParentCommentId = comment.Id
                                };

                            dbComment.Comments1.Add(dbCommentComment);
                        }

                        dbCommentComment.Text = cComment.Text;
                        dbCommentComment.TypeId = null;
                        dbCommentComment.UserObjectId = cComment.UserObjectId;
                        dbCommentComment.Date = cComment.Date.Truncate();
                        dbCommentComment.EntryTypeId = (int)type;
                        dbCommentComment.ObjectId = objectId;
                        dbCommentComment.UserFullName = cComment.UserFullName;
                        dbCommentComment.Number = cComment.Number ?? string.Empty;
                        if (cComment.Embed != null)
                        {
                            var embed = votingSession.Embeds.SingleOrDefault(e => e.Id == dbCommentComment.EmbedId);
                            dbCommentComment.Embed = embed ?? new Data.EF.Voting.Embed();

                            dbCommentComment.Embed.Author_Name = cComment.Embed.Author_Name;
                            dbCommentComment.Embed.Author_Url = cComment.Embed.Author_Url;
                            dbCommentComment.Embed.Cache_Age = cComment.Embed.Cache_Age;
                            dbCommentComment.Embed.Description = cComment.Embed.Description;
                            dbCommentComment.Embed.Height = cComment.Embed.Height;
                            dbCommentComment.Embed.Html = cComment.Embed.Html;
                            dbCommentComment.Embed.Provider_Name = cComment.Embed.Provider_Name;
                            dbCommentComment.Embed.Provider_Url = cComment.Embed.Provider_Url;
                            dbCommentComment.Embed.Thumbnail_Url = cComment.Embed.Thumbnail_Url;
                            dbCommentComment.Embed.Title = cComment.Embed.Title;
                            dbCommentComment.Embed.Type = cComment.Embed.Type;
                            dbCommentComment.Embed.Url = cComment.Embed.Url;
                            dbCommentComment.Embed.Version = cComment.Embed.Version;
                            dbCommentComment.Embed.Width = cComment.Embed.Width;
                        }

                        var dbcSupporters =
                            votingSession.CommentSupporters.Where(
                                c => c.CommentId == dbCommentComment.Id).ToList();

                        var deletedCSupporters =
                            dbcSupporters.Select(s => s.UserId).Except(
                                cComment.SupportingUserIds.Select(s => s.ToString())).ToList();
                        if (deletedCSupporters.Any())
                        {
                            votingSession.CommentSupporters.Delete(
                                c => deletedCSupporters.Contains(c.UserId) && c.CommentId == dbCommentComment.Id);
                        }

                        foreach (var id in cComment.SupportingUserIds)
                        {
                            if (!dbcSupporters.Any(s => s.UserId == id))
                            {
                                dbCommentComment.CommentSupporters.Add(new CommentSupporter()
                                    {
                                        UserId = id
                                    });
                            }
                        }
                    }
                }
            }
        }
        #endregion

        public string GetLikesCountString(int supportingUserCount)
        {
            return GlobalizedSentences.GetSupportingUsersString(supportingUserCount);
        }

        public ExpandableList<CommentView> GetCommentsMostSupported(ICommentable entity, int pageNumber, ForAgainst? posOrNeg = null)
        {
            var comments = new List<CommentView>();
            var query = entity.Comments.AsEnumerable();
            if (posOrNeg.HasValue)
            {
                query = query.Where(c => c.PositiveOrNegative == posOrNeg);
            }

            foreach (var comment in query.OrderByDescending(c => c.SupportingUserIds.Count * 2 + c.Comments.Sum(cc => cc.SupportingUserIds.Count)).ThenByDescending(c => c.Date).GetExpandablePage(pageNumber, CustomAppSettings.PageSizeComment))
            {
                comments.Add(GetCommentViewWithComments(entity, comment));
            }

            return new ExpandableList<CommentView>(comments, CustomAppSettings.PageSizeComment);
        }

        public ExpandableList<CommentView> GetCommentsMostRecent(ICommentable entity, int pageNumber, ForAgainst? posOrNeg = null, bool orderResultAsc = false)
        {
            var comments = new List<CommentView>();
            var query = entity.Comments.AsEnumerable();
            if (posOrNeg.HasValue)
            {
                query = query.Where(c => c.PositiveOrNegative == posOrNeg);
            }

            foreach (var comment in query.OrderByDescending(c => c.Date).GetExpandablePage(pageNumber, CustomAppSettings.PageSizeComment))
            {
                comments.Add(GetCommentViewWithComments(entity, comment));
            }

            var result = new ExpandableList<CommentView>(comments, CustomAppSettings.PageSizeComment);

            if (orderResultAsc)
            {
                result.List = result.List.OrderBy(m => m.CommentDate).ToList();
            }

            return result;
        }

        public ExpandableList<CommentView> GetCommentsByVersion(ICommentable entity, MongoObjectId versionId, int pageNumber, ForAgainst? posOrNeg = null)
        {
            var comments = new List<CommentView>();
            var query = entity.Comments.AsEnumerable();
            if (posOrNeg.HasValue)
            {
                query = query.Where(c => c.PositiveOrNegative == posOrNeg);
            }

            foreach (var comment in query.Where(c => c.RelatedVersionId == versionId).OrderByDescending(c => c.SupportingUserIds.Count).ThenByDescending(c => c.Date).GetExpandablePage(pageNumber, CustomAppSettings.PageSizeComment))
            {
                comments.Add(GetCommentViewWithComments(entity, comment));
            }

            return new ExpandableList<CommentView>(comments, CustomAppSettings.PageSizeComment);
        }

        private CommentView GetCommentViewWithComments(ICommentable entity, Comment comment, string parentId = null)
        {
            var commentView = GetCommentViewFromComment(entity.Id, comment, parentId, entity.EntryType,
                                                            entity.GetRelatedVersionNumber(comment.RelatedVersionId));
            foreach (var cComment in comment.Comments.OrderBy(c => c.Date))
            {
                var cCommentView = GetCommentViewFromComment(entity.Id, cComment, comment.Id, entity.EntryType);
                commentView.Comments.Add(cCommentView);
            }

            return commentView;
        }

        public void UpdateEntity(ICommentable entity)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                entity.ModificationDate = DateTime.Now;
                noSqlSession.Update(entity);
            }
        }

        public virtual CommentView AddNewComment(ICommentable entity, ForAgainst forAgainst, string text, EmbedModel embed, MongoObjectId versionId = null)
        {
            if (CurrentUser.RequireUniqueAuthentication && !CurrentUser.IsUnique)
            {
                throw new UserNotUniqueException();
            }

            if (entity.Id == CurrentUser.Id)
            {
                forAgainst = ForAgainst.Neutral;
            }

            entity.LastNumber++;
            var comment = new Comment
                              {
                                  Date = DateTime.Now,
                                  Text = text,
                                  UserFullName = CurrentUser.FullName,
                                  UserObjectId = CurrentUser.Id,
                                  RelatedVersionId = versionId,
                                  PositiveOrNegative = forAgainst,
                                  Number = entity.LastNumber.ToString() + "."
                              };

            if (embed != null && !embed.IsEmpty)
            {
                comment.Embed = new Data.MongoDB.Embed();
                comment.Embed.InjectFrom(embed);

                comment.Embed.Title = comment.Embed.Title.Sanitize();
                comment.Embed.Description = comment.Embed.Description.Sanitize();
            }
            entity.Comments.Add(comment);
            UpdateEntity(entity);

            var model = GetCommentViewFromComment(entity.Id, comment, null, entity.EntryType,
                                             entity.GetRelatedVersionNumber(comment.RelatedVersionId));


            model.SubscribeMain = ActionService.Subscribe(entity.Id, CurrentUser.DbId.Value, entity.EntryType);
            model.Subscribe = ActionService.Subscribe(comment.Id, CurrentUser.DbId.Value);
            
            return model;
        }

        public bool DeleteComment(ICommentable entity, MongoObjectId commentId)
        {
            if (entity == null)
            {
                return false;
            }

            var comment = entity.Comments.Where(c => c.Id == commentId).SingleOrDefault();

            if (comment == null || (comment.UserObjectId != CurrentUser.Id && CurrentUser.Role != UserRoles.Admin))
            {
                return false;
            }

            entity.Comments.Remove(comment);
            UpdateEntity(entity);

            bus.Send(new CommentDeletedCommand()
            {
                CommentId = commentId,
                EntryType = entity.EntryType,
                UserObjectId = CurrentUser.Id
            });

            return true;
        }

        public bool DeleteCommentComment(ICommentable entity, MongoObjectId commentId, MongoObjectId commentCommentId)
        {
            if (entity == null)
            {
                return false;
            }

            var comment = entity.Comments.Where(c => c.Id == commentId).SingleOrDefault();

            if (comment == null)
            {
                return false;
            }

            var commentComment = comment.Comments.Where(c => c.Id == commentCommentId).SingleOrDefault();

            if (commentComment == null || (commentComment.UserObjectId != CurrentUser.Id && CurrentUser.Role != UserRoles.Admin))
            {
                return false;
            }

            comment.Comments.Remove(commentComment);
            UpdateEntity(entity);

            bus.Send(new CommentDeletedCommand()
            {
                CommentId = commentCommentId,
                EntryType = entity.EntryType,
                UserObjectId = CurrentUser.Id
            });

            return true;
        }

        private Comment GetComment(ICommentable entity, MongoObjectId commentId)
        {
            return entity.Comments.Where(c => c.Id == commentId).SingleOrDefault();
        }

        public CommentView AddNewCommentToComment(ICommentable entity, MongoObjectId commentId, string text, EmbedModel embed)
        {
            if (CurrentUser.RequireUniqueAuthentication && !CurrentUser.IsUnique)
            {
                throw new UserNotUniqueException();
            }

            var comment = GetComment(entity, commentId);
            if (comment == null)
            {
                return null;
            }
            comment.LastNumber++;
            var cComment = new Comment
            {
                Date = DateTime.Now,
                Text = text,
                UserFullName = CurrentUser.FullName,
                UserObjectId = CurrentUser.Id,
                Number = comment.Number + comment.LastNumber
            };

            if (embed != null && !embed.IsEmpty)
            {
                cComment.Embed = new Data.MongoDB.Embed();
                cComment.Embed.InjectFrom(embed);
                cComment.Embed.Title = cComment.Embed.Title.Sanitize();
                cComment.Embed.Description = cComment.Embed.Description.Sanitize();
            }

            comment.Comments.Add(cComment);

            UpdateEntity(entity);

            var model = GetCommentViewFromComment(entity.Id, cComment, commentId, entity.EntryType,
                                                            entity.GetRelatedVersionNumber(comment.RelatedVersionId));

            model.SubscribeMain = ActionService.Subscribe(entity.Id, CurrentUser.DbId.Value, entity.EntryType);
            model.Subscribe = ActionService.Subscribe(comment.Id, CurrentUser.DbId.Value);

            return model;
        }

        private Comment GetComment(ICommentable entity, MongoObjectId commentId, MongoObjectId parentCommentId)
        {
            if (!string.IsNullOrEmpty(parentCommentId))
            {
                var parentComment = GetComment(entity, parentCommentId);
                return parentComment.Comments.SingleOrDefault(c => c.Id == commentId);
            }

            return GetComment(entity, commentId);
        }

        public CommentView LikeComment(ICommentable entity, MongoObjectId commentId, MongoObjectId parentCommentId)
        {
            Comment comment = GetComment(entity, commentId, parentCommentId);

            if (comment == null || comment.SupportingUserIds.Contains(CurrentUser.Id))
            {
                return null;
            }

            comment.SupportingUserIds.Add(CurrentUser.Id);

            UpdateEntity(entity);

            return GetCommentViewFromComment(entity.Id, comment, parentCommentId, entity.EntryType);
        }

        public Liking UndoLikeComment(ICommentable entity, MongoObjectId commentId, MongoObjectId parentCommentId)
        {
            Comment comment, parentComment = null;
            if (parentCommentId != null)
            {
                parentComment = GetComment(entity, parentCommentId);
                comment = parentComment.Comments.Where(c => c.Id == commentId).SingleOrDefault();
            }
            else
            {
                comment = GetComment(entity, commentId);
            }

            if (!comment.SupportingUserIds.Contains(CurrentUser.Id))
            {
                return null;
            }

            comment.SupportingUserIds.Remove(CurrentUser.Id);

            UpdateEntity(entity);
            var command = new CommentUnlikedCommand
                              {
                                  ObjectId = entity.Id,
                                  CommentId = commentId,
                                  EntryType = entity.EntryType,
                                  UserObjectId = CurrentUser.Id
                              };
            bus.Send(command);

            return GetCommentViewFromComment(entity.Id, comment, parentCommentId, entity.EntryType).Liking;
        }

        public SimpleListContainerModel GetCommentSupporters(int pageNumber, ICommentable entity, string commentId, string parentId)
        {
            Comment comment;
            if (!string.IsNullOrEmpty(parentId))
            {
                var parent = GetComment(entity, parentId);
                comment = parent.Comments.Where(c => c.Id == commentId).SingleOrDefault();
            }
            else
            {
                comment = GetComment(entity, commentId);
            }

            if (comment == null)
            {
                return new SimpleListContainerModel();
            }
            var result = comment.SupportingUserIds.Select(
            i => new SimpleListModel
            {
                Id = i,
                Subject = GetUserFullName(i)
            }).OrderByDescending(i => i.Subject)
            .GetExpandablePage(pageNumber, CustomAppSettings.PageSizeList).ToList();
            result.ForEach(r => r.Type = EntryTypes.User);
            var simpleList = new SimpleListContainerModel();
            simpleList.List = new ExpandableList<SimpleListModel>(result, CustomAppSettings.PageSizeList);
            return simpleList;
        }

        private string GetUserFullName(MongoObjectId id)
        {
            using (var session = noSqlSessionFactory())
            {
                var user = session.Find<Data.MongoDB.User>(Query.EQ("_id", id)).SetFields("FirstName", "LastName").SingleOrDefault();
                if (user != null)
                {
                    return user.FullName;
                }

                return string.Empty;
            }
        }

        public int GetCommentsCount(ICommentable entity, ForAgainst forAgainst)
        {
            return entity.Comments.Count(c => c.PositiveOrNegative == forAgainst);
        }

        public CommentView HideComment(ICommentable entity, string commentId, string parentId)
        {
            Comment comment = GetComment(entity, commentId, parentId);
            comment.IsHidden = true;
            UpdateEntity(entity);
            return GetCommentViewWithComments(entity, comment, parentId);
        }

        public CommentView ShowComment(ICommentable entity, string commentId, string parentId)
        {
            Comment comment = GetComment(entity, commentId, parentId);
            comment.IsHidden = false;
            UpdateEntity(entity);
            return GetCommentViewWithComments(entity, comment, parentId);
        }
    }
}
