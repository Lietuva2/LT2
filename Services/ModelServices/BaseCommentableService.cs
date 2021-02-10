using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bus.Commands;
using Data.Enums;
using Data.MongoDB.Interfaces;
using Data.ViewModels.Base;
using Data.ViewModels.Comments;
using Framework.Infrastructure.Storage;
using Framework.Lists;

using Services.Session;

namespace Services.ModelServices
{
    public class BaseCommentableService
    {
        protected readonly CommentService commentService;

        public BaseCommentableService(
            
            CommentService commentService)
        {
            this.commentService = commentService;
        }
        
        public UserInfo CurrentUser { get { return MembershipSession.GetUser(); } }

        protected virtual ICommentable GetEntity(MongoObjectId id)
        {
            throw new NotImplementedException();
        }

        protected virtual void SendCommentCommand(ICommentable entity, ActionTypes actionType, CommentView comment)
        {
        }

        protected virtual ActionTypes GetAddNewCommentActionType()
        {
            return ActionTypes.Commented;
        }

        protected virtual ActionTypes GetLikeCommentActionType()
        {
            return ActionTypes.LikedComment;
        }

        #region comments
        public ExpandableList<CommentView> GetCommentsMostSupported(MongoObjectId id, int pageNumber, ForAgainst? posOrNeg = null)
        {
            var entity = GetEntity(id);
            return commentService.GetCommentsMostSupported(entity, pageNumber, posOrNeg);
        }

        public ExpandableList<CommentView> GetCommentsMostRecent(MongoObjectId id, int pageNumber, ForAgainst? posOrNeg = null, bool orderResultAsc = false)
        {
            var entity = GetEntity(id);
            return commentService.GetCommentsMostRecent(entity, pageNumber, posOrNeg, orderResultAsc);
        }

        public ExpandableList<CommentView> GetCommentsByVersion(MongoObjectId id, MongoObjectId versionId, int pageNumber, ForAgainst? posOrNeg = null)
        {
            var entity = GetEntity(id);
            return commentService.GetCommentsByVersion(entity, versionId, pageNumber, posOrNeg);
        }

        public virtual CommentView AddNewComment(MongoObjectId id, string text, EmbedModel embed, ForAgainst forAgainst = ForAgainst.Neutral, MongoObjectId versionId = null)
        {
            var entity = GetEntity(id);
            var comment = commentService.AddNewComment(entity, forAgainst, text, embed, versionId);
            SendCommentCommand(entity, GetAddNewCommentActionType(), comment);
            return comment;
        }

        public CommentView AddNewCommentToComment(MongoObjectId id, MongoObjectId commentId, string text, EmbedModel embed)
        {
            var entity = GetEntity(id);
            var comment = commentService.AddNewCommentToComment(entity, commentId, text, embed);
            SendCommentCommand(entity, ActionTypes.CommentCommented, comment);
            return comment;
        }

        public Liking LikeComment(MongoObjectId id, MongoObjectId commentId, MongoObjectId parentCommentId)
        {
            var entity = GetEntity(id);
            var comment = commentService.LikeComment(entity, commentId, parentCommentId);
            SendCommentCommand(entity, string.IsNullOrEmpty(parentCommentId) ? GetLikeCommentActionType() : ActionTypes.CommentCommentLiked, comment);
            return comment.Liking;
        }

        public bool DeleteComment(MongoObjectId id, MongoObjectId commentId)
        {
            var entity = GetEntity(id);
            return commentService.DeleteComment(entity, commentId);
        }

        public bool DeleteCommentComment(MongoObjectId id, MongoObjectId commentId, MongoObjectId commentCommentId)
        {
            var entity = GetEntity(id);
            return commentService.DeleteCommentComment(entity, commentId, commentCommentId);
        }
        
        public Liking UndoLikeComment(MongoObjectId id, MongoObjectId commentId, MongoObjectId parentCommentId)
        {
            var entity = GetEntity(id);
            return commentService.UndoLikeComment(entity, commentId, parentCommentId);
        }

        public SimpleListContainerModel GetCommentSupporters(int pageNumber, string id, string commentId, string parentId)
        {
            var entity = GetEntity(id);
            return commentService.GetCommentSupporters(pageNumber, entity, commentId, parentId);
        }

        public CommentView HideComment(string id, string commentId, string parentId)
        {
            var entity = GetEntity(id);
            return commentService.HideComment(entity, commentId, parentId);
        }

        public CommentView ShowComment(string id, string commentId, string parentId)
        {
            var entity = GetEntity(id);
            return commentService.ShowComment(entity, commentId, parentId);
        }
        #endregion
    }
}
