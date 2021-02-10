using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using Bus.Commands;
using Data.EF.Actions;
using Data.Infrastructure.Sessions;
using Data.ViewModels.Voting;
using EntityFramework.Extensions;
using Framework;
using Framework.Bus;
using Services.Caching;
using Services.Session;

namespace Services.ModelServices
{
    public class CategoryService : IService
    {
        private readonly IActionsContextFactory actionSessionFactory;

        public IBus Bus { get; set; }
        public UserInfo CurrentUser { get { return MembershipSession.GetUser(); } }

        public CategoryService(
            IActionsContextFactory actionSessionFactory,
            IBus bus)
        {
            this.actionSessionFactory = actionSessionFactory;
            Bus = bus;
        }

        public IEnumerable<TextValue> GetCategories()
        {
            using (var actionSession = actionSessionFactory.CreateContext())
            {
                var langCode = CurrentUser.LanguageCode;
                var cats = (from c in actionSession.Categories
                            join ct in actionSession.Categories_Translation.Where(t => t.LanguageCode == langCode) on c.Id equals ct.CategoryId into ps
                            from ct in ps.DefaultIfEmpty() 
                            select new TextValue { ValueInt = c.Id, Text = ct != null ? ct.Name : c.Name }).FromCache(tags: new []{langCode});

                return cats.OrderBy(c => c.Text);
            }
        }

        public IList<CategorySelectModel> GetMyCategoriesModel()
        {
            var myCategoryIds = GetMyCategoryIds(false);
            return (from c in GetCategories()
                    select new CategorySelectModel()
                               {
                                   CategoryId = (short)c.ValueInt,
                                   CategoryName = c.Text,
                                   IsSelected = myCategoryIds.Contains(c.ValueInt)
                               }).ToList();
        }

        public string GetCategoryName(int id)
        {
            return (from c in GetCategories()
                    where c.ValueInt == id
                    select c.Text).SingleOrDefault();
        }

        public string GetMyCategoryNames()
        {
            if(!CurrentUser.DbId.HasValue)
            {
                return null;
            }

            return GetUserCategoryNames(CurrentUser.DbId.Value);
        }

        public string GetUserCategoryNames(int userDbId)
        {
            using (var actionSession = actionSessionFactory.CreateContext())
            {
                return string.Join(", ", (from ic in actionSession.InterestingCategories.Where(a => a.UserId == userDbId).ToList()
                                          join c in GetCategories() on ic.CategoryId equals c.ValueInt
                                          orderby c.Text
                                          select c.Text).ToList());
            }
        }

        public IList<CategorySelectModel> GetUserCategoriesModel(int userDbId)
        {
            using (var actionSession = actionSessionFactory.CreateContext())
            {
                return (from ic in actionSession.InterestingCategories.Where(a => a.UserId == userDbId).ToList()
                        join c in GetCategories() on ic.CategoryId equals c.ValueInt
                        orderby c.Text
                        select new CategorySelectModel()
                        {
                            CategoryId = (short)ic.CategoryId,
                            CategoryName = c.Text
                        }).ToList();
            }
        }

        public int GetUserCategoriesCount(int userDbId)
        {
            using (var actionSession = actionSessionFactory.CreateContext())
            {
                return actionSession.InterestingCategories.Count(a => a.UserId == userDbId);
            }
        }

        public bool CheckIfCategoryIsLikedByUser(int categoryId, int userId)
        {
            using (var actionSession = actionSessionFactory.CreateContext())
            {
                return
                    actionSession.InterestingCategories.Where(
                        ic => ic.CategoryId == categoryId && ic.UserId == userId).Any();
            }
        }

        public List<int> GetMyCategoryIds(bool getAllIfEmpty = true)
        {
            return GetMyCategoryIds(CurrentUser, getAllIfEmpty);
        }

        public List<int> GetMyCategoryIds(UserInfo user, bool getAllIfEmpty = true)
        {
            if (user.SelectedCategoryIds != null && user.SelectedCategoryIds.Any())
            {
                return user.SelectedCategoryIds;
            }

            using (var actionSession = actionSessionFactory.CreateContext(unique:true))
            {
                var cats = actionSession.InterestingCategories.Where(ic => ic.UserId == user.DbId).
                    Select(ic => ic.CategoryId).Cast<int>().ToList();
                if (!cats.Any() && getAllIfEmpty)
                {
                    cats = GetCategories().Select(c => c.ValueInt).ToList();
                }
                else
                {
                    user.SelectedCategoryIds = cats;
                }

                return cats;
            }
        }

        public void SaveMyCategories(IEnumerable<int> categoryIds)
        {
            if (categoryIds == null)
            {
                categoryIds = new List<int>();
            }

            var newCategories = new List<short>();
            using (var actionsSession = actionSessionFactory.CreateContext(true))
            {
                var likedCategories = GetMyCategoryIds(false);
                foreach (var id in categoryIds)
                {
                    if (!likedCategories.Contains(id) && id > 0)
                    {
                        var categoryLike = new Data.EF.Actions.InterestingCategory
                                               {
                                                   CategoryId = (short)id,
                                                   UserId = CurrentUser.DbId.Value
                                               };
                        actionsSession.InterestingCategories.Add(categoryLike);
                        newCategories.Add((short)id);
                    }
                }


                foreach (var catId in likedCategories.Except(categoryIds.Cast<int>()))
                {
                    actionsSession.InterestingCategories.Delete(ic => ic.CategoryId == catId && ic.UserId == CurrentUser.DbId);
                }
            }

            CurrentUser.SelectedCategoryIds = null;
            CurrentUser.SelectedCategoryIds = GetMyCategoryIds(false);

            Bus.Send(new LikedCategoriesCommand
            {
                CategoryIds = newCategories,
                UserDbId = CurrentUser.DbId.Value
            });
        }
    }
}
